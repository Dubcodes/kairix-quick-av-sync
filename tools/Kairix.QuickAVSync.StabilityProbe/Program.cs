using System.Diagnostics;
using Kairix.QuickAVSync.Capture;
using Kairix.QuickAVSync.Models;
using Kairix.QuickAVSync.Services;

var options = ProbeOptions.Parse(args);
Directory.CreateDirectory(options.OutputDirectory);
var output = Path.Combine(options.OutputDirectory, $"synthetic-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.csv");
await using var writer = new StreamWriter(output);
await writer.WriteLineAsync("elapsedSeconds,workingSet,privateBytes,managedBytes,heapBytes,lohBytes,gen0,gen1,gen2,handles,threads,videoCount,videoCapacity,audioCount,audioCapacity,frames,audioChunks,events,reconnects,activeTasks");

var stopwatch = Stopwatch.StartNew();
var nextSample = TimeSpan.Zero; long frames = 0, chunks = 0, events = 0; var reconnects = 0;
var video = new RollingBuffer<VideoFrame>(250, frame => frame.Timestamp.Ticks100ns); var audio = new RollingBuffer<AudioChunk>(1000, chunk => chunk.Timestamp.Ticks100ns); var detector = new TransientDetector();
for (var cycle = 0; cycle < Math.Max(1, options.ReconnectCycles + 1); cycle++)
{
    var backend = new SyntheticCaptureBackend(); var device = (await backend.EnumerateDevicesAsync(CancellationToken.None)).Single();
    await using var session = await backend.OpenAsync(device, new(options.DetectionWidth, options.DetectionHeight, PreferredPresentationWidth: options.ReviewWidth, PreferredPresentationHeight: options.ReviewHeight), CancellationToken.None);
    session.VideoSampleReceived += (_, frame) => { video.Add(frame); Interlocked.Increment(ref frames); };
    session.AudioSampleReceived += (_, chunk) => { audio.Add(chunk); Interlocked.Increment(ref chunks); Interlocked.Add(ref events, detector.Process(chunk).Count); };
    await session.StartAsync(CancellationToken.None);
    var cycleDuration = options.ReconnectCycles > 0 ? options.ReconnectPause : options.Duration;
    var deadline = stopwatch.Elapsed + cycleDuration;
    while (stopwatch.Elapsed < deadline)
    {
        if (stopwatch.Elapsed >= nextSample)
        {
            WriteSample(writer, stopwatch.Elapsed, video, audio, frames, chunks, events, reconnects, activeTasks: 1);
            nextSample += options.SampleInterval;
        }
        await Task.Delay(100);
    }
    await session.StopAsync(CancellationToken.None);
    if (cycle < options.ReconnectCycles) reconnects++;
}
// WriteSample performs a synchronous flush.  Do not add a second async flush here:
// on a mapped workspace drive it can remain pending after the final row is durable,
// leaving a completed probe process alive without any active capture work.
WriteSample(writer, stopwatch.Elapsed, video, audio, frames, chunks, events, reconnects, activeTasks: 0);
Console.WriteLine($"Stability probe complete: {output}");

static void WriteSample(StreamWriter writer, TimeSpan elapsed, RollingBuffer<VideoFrame> video, RollingBuffer<AudioChunk> audio, long frames, long chunks, long events, int reconnects, int activeTasks)
{
    using var process = Process.GetCurrentProcess(); var memory = GC.GetGCMemoryInfo(); var loh = memory.GenerationInfo.Length > 3 ? memory.GenerationInfo[3].SizeAfterBytes : 0;
    writer.WriteLine(string.Join(',', elapsed.TotalSeconds.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture), process.WorkingSet64, process.PrivateMemorySize64, GC.GetTotalMemory(false), memory.HeapSizeBytes, loh, GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2), process.HandleCount, process.Threads.Count, video.Count, video.Capacity, audio.Count, audio.Capacity, Interlocked.Read(ref frames), Interlocked.Read(ref chunks), Interlocked.Read(ref events), reconnects, activeTasks)); writer.Flush();
}

sealed record ProbeOptions(TimeSpan Duration, TimeSpan SampleInterval, int DetectionWidth, int DetectionHeight, int ReviewWidth, int ReviewHeight, int ReconnectCycles, TimeSpan ReconnectPause, string OutputDirectory)
{
    public static ProbeOptions Parse(string[] args)
    {
        var duration = TimeSpan.FromMinutes(1); var sample = TimeSpan.FromSeconds(10); var width = 640; var height = 360; var reviewWidth = 160; var reviewHeight = 90; var reconnects = 0; var pause = TimeSpan.FromMilliseconds(250); var directory = Path.Combine("artifacts", "stability");
        for (var index = 0; index < args.Length; index++)
        {
            var value = index + 1 < args.Length ? args[++index] : throw new ArgumentException($"Missing value for {args[index]}");
            switch (args[index - 1]) { case "--duration": duration = TimeSpan.Parse(value); break; case "--sample-interval": sample = TimeSpan.Parse(value); break; case "--detection": var dimensions = value.Split('x', 'X'); width = int.Parse(dimensions[0]); height = int.Parse(dimensions[1]); break; case "--reconnect-cycles": reconnects = int.Parse(value); break; case "--reconnect-pause": pause = TimeSpan.Parse(value); break; case "--output": directory = value; break; default: throw new ArgumentException($"Unknown option {args[index - 1]}"); }
        }
        if (duration <= TimeSpan.Zero || sample <= TimeSpan.Zero || width < 2 || height < 2 || reconnects < 0 || pause <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(args));
        return new(duration, sample, width, height, reviewWidth, reviewHeight, reconnects, pause, directory);
    }
}
