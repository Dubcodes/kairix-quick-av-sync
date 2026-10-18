using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Kairix.QuickAVSync.Models;
using Kairix.QuickAVSync.Services;

namespace Kairix.QuickAVSync.DetectorFixtureRunner;

internal static class FixtureProcesses
{
    public static void RequireTools()
    {
        foreach (var command in new[] { "ffprobe", "ffmpeg" })
        {
            using var process = Start(command, ["-version"], redirectOutput: true);
            process.StandardOutput.ReadLine();
            process.WaitForExit();
            if (process.ExitCode != 0) throw new InvalidOperationException($"Required development tool is unavailable: {command}");
        }
    }

    public static async Task<MediaInventory> ProbeAsync(string path)
    {
        var arguments = new[]
        {
            "-v", "error", "-show_entries",
            "format=duration:stream=index,codec_type,codec_name,width,height,pix_fmt,field_order,r_frame_rate,avg_frame_rate,time_base,sample_rate,channels,channel_layout",
            "-of", "json", path
        };
        using var process = Start("ffprobe", arguments, redirectOutput: true);
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0) throw new InvalidOperationException($"ffprobe failed for {path}: {error}");
        using var document = JsonDocument.Parse(output);
        var streams = document.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        var video = streams.Single(stream => stream.GetProperty("codec_type").GetString() == "video");
        var audio = streams.Single(stream => stream.GetProperty("codec_type").GetString() == "audio");
        var format = document.RootElement.GetProperty("format");
        return new(
            Path.GetFileName(path),
            double.Parse(format.GetProperty("duration").GetString()!, CultureInfo.InvariantCulture),
            Text(video, "codec_name"), Number(video, "width"), Number(video, "height"), Text(video, "pix_fmt"), Text(video, "field_order"),
            Text(video, "r_frame_rate"), Text(video, "avg_frame_rate"), Text(video, "time_base"),
            Text(audio, "codec_name"), int.Parse(Text(audio, "sample_rate"), CultureInfo.InvariantCulture), Number(audio, "channels"),
            Text(audio, "channel_layout"), Text(audio, "time_base"));
    }

    public static async Task<ClipAudioAnalysis> AnalyzeAudioAsync(string path, int sampleRate, int channels)
    {
        if (channels != 2) throw new InvalidOperationException($"Expected stereo fixture audio: {path}");
        var arguments = new[] { "-v", "error", "-i", path, "-map", "0:a:0", "-f", "f32le", "-acodec", "pcm_f32le", "-ar", sampleRate.ToString(CultureInfo.InvariantCulture), "-ac", "2", "pipe:1" };
        using var process = Start("ffmpeg", arguments, redirectOutput: true);
        var errorTask = process.StandardError.ReadToEndAsync();
        using var memory = new MemoryStream();
        await process.StandardOutput.BaseStream.CopyToAsync(memory);
        await process.WaitForExitAsync();
        var error = await errorTask;
        if (process.ExitCode != 0) throw new InvalidOperationException($"ffmpeg audio decode failed for {path}: {error}");

        var bytes = memory.ToArray();
        var totalFrames = bytes.Length / (sizeof(float) * channels);
        var autoDetector = new TransientDetector { Sensitivity = 50 };
        var leftDetector = new TransientDetector { Sensitivity = 50 };
        var rightDetector = new TransientDetector { Sensitivity = 50 };
        var auto = new List<AudioTransient>();
        var left = new List<AudioTransient>();
        var right = new List<AudioTransient>();
        const int chunkFrames = 4800;
        for (var firstFrame = 0; firstFrame < totalFrames; firstFrame += chunkFrames)
        {
            var frameCount = Math.Min(chunkFrames, totalFrames - firstFrame);
            var stereo = new float[frameCount * 2];
            Buffer.BlockCopy(bytes, firstFrame * channels * sizeof(float), stereo, 0, stereo.Length * sizeof(float));
            var leftSamples = new float[frameCount];
            var rightSamples = new float[frameCount];
            for (var frame = 0; frame < frameCount; frame++)
            {
                leftSamples[frame] = stereo[frame * 2];
                rightSamples[frame] = stereo[frame * 2 + 1];
            }
            var ticks = (long)Math.Round(firstFrame / (double)sampleRate * TimeSpan.TicksPerSecond);
            var timestamp = new MediaTimestamp(ticks, TimingQuality.StreamTimestamp, "fixture", ticks);
            auto.AddRange(autoDetector.Process(new(timestamp, stereo, sampleRate, 2)));
            left.AddRange(leftDetector.Process(new(timestamp, leftSamples, sampleRate, 1)));
            right.AddRange(rightDetector.Process(new(timestamp, rightSamples, sampleRate, 1)));
        }
        return new(auto, left, right);
    }

    public static async Task<IReadOnlyList<VisualTriggerResult>> AnalyzeVideoAsync(
        string path,
        double frameRate,
        IReadOnlyList<AudioTransient> triggers,
        int width,
        int height,
        IReadOnlyList<int> sensitivities)
    {
        if (triggers.Count == 0) return [];
        var arguments = new[]
        {
            "-v", "error", "-i", path, "-map", "0:v:0", "-vf", $"scale={width}:{height}:flags=area,format=gray",
            "-fps_mode", "passthrough", "-f", "rawvideo", "-pix_fmt", "gray", "pipe:1"
        };
        using var process = Start("ffmpeg", arguments, redirectOutput: true);
        var errorTask = process.StandardError.ReadToEndAsync();
        var stream = process.StandardOutput.BaseStream;
        var frameSize = checked(width * height);
        var buffer = new byte[frameSize];
        var orderedTriggers = triggers.Select((transient, index) => new PendingWindow(index, transient.Timestamp, transient.Timestamp.Time.TotalSeconds - .3, transient.Timestamp.Time.TotalSeconds + .3)).OrderBy(item => item.Start).ToArray();
        var active = new List<PendingWindow>();
        var results = new List<VisualTriggerResult>();
        var next = 0;
        var frameIndex = 0;
        while (await ReadFrameAsync(stream, buffer))
        {
            var seconds = frameIndex / frameRate;
            while (next < orderedTriggers.Length && orderedTriggers[next].Start <= seconds)
                active.Add(orderedTriggers[next++]);
            for (var index = active.Count - 1; index >= 0; index--)
            {
                var window = active[index];
                if (seconds > window.End)
                {
                    results.AddRange(await EvaluateAsync(window, sensitivities));
                    active.RemoveAt(index);
                    continue;
                }
                if (seconds + 1e-9 >= window.Start)
                {
                    var luma = buffer.ToArray();
                    var ticks = (long)Math.Round(seconds * TimeSpan.TicksPerSecond);
                    window.Frames.Add(new(new(ticks, TimingQuality.StreamTimestamp, "fixture", ticks), width, height, luma, frameIndex, NativeSampleIndex: frameIndex));
                }
            }
            frameIndex++;
        }
        foreach (var window in active) results.AddRange(await EvaluateAsync(window, sensitivities));
        await process.WaitForExitAsync();
        var error = await errorTask;
        if (process.ExitCode != 0) throw new InvalidOperationException($"ffmpeg video decode failed for {path}: {error}");
        return results;
    }

    private static async Task<IReadOnlyList<VisualTriggerResult>> EvaluateAsync(PendingWindow window, IReadOnlyList<int> sensitivities)
    {
        var output = new List<VisualTriggerResult>(sensitivities.Count);
        foreach (var sensitivity in sensitivities)
        {
            var candidate = await VisualDetectorFactory.Create(VisualDetectorKind.HumanClap).DetectAsync(window.Frames, window.Trigger, CancellationToken.None, new(sensitivity));
            output.Add(new(window.TriggerIndex, sensitivity, window.Trigger.Time.TotalSeconds,
                candidate?.Timestamp.Time.TotalSeconds, candidate?.Confidence, candidate?.MotionScore, candidate?.TemporalIndex,
                candidate?.Trace?.ApproachPeakIndex, candidate?.Trace?.FinalContactIndex, candidate?.Trace?.ApproachQuality,
                candidate?.Trace?.ContinuityQuality, candidate?.Trace?.DropQuality, candidate?.Trace?.SettleQuality,
                candidate?.Trace?.CandidateMargin));
        }
        return output;
    }

    private static async Task<bool> ReadFrameAsync(Stream stream, byte[] buffer)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset));
            if (read == 0) return offset == 0 ? false : throw new EndOfStreamException("Partial raw video frame from ffmpeg.");
            offset += read;
        }
        return true;
    }

    private static Process Start(string fileName, IEnumerable<string> arguments, bool redirectOutput)
    {
        var start = new ProcessStartInfo(fileName) { UseShellExecute = false, RedirectStandardOutput = redirectOutput, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        return Process.Start(start) ?? throw new InvalidOperationException($"Could not start {fileName}.");
    }

    private static string Text(JsonElement element, string property) => element.TryGetProperty(property, out var value) ? value.ToString() : "";
    private static int Number(JsonElement element, string property) => element.TryGetProperty(property, out var value) ? value.GetInt32() : 0;

    private sealed class PendingWindow(int triggerIndex, MediaTimestamp trigger, double start, double end)
    {
        public int TriggerIndex { get; } = triggerIndex;
        public MediaTimestamp Trigger { get; } = trigger;
        public double Start { get; } = start;
        public double End { get; } = end;
        public List<VideoFrame> Frames { get; } = [];
    }
}
