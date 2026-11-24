using System.Diagnostics;
using Kairix.QuickAVSync.Models;

namespace Kairix.QuickAVSync.Services;

/// <summary>
/// Bounded, on-demand speech/lip correlation. This is deliberately separate from
/// IVisualClapDetector: it never participates in continuous automatic detection.
/// </summary>
public sealed class SpeechLipSyncAnalyzer
{
    public const int MaximumMarkers = 10;
    public const double MaximumLagMilliseconds = 300;

    public Task<LipSyncAnalysisResult> AnalyzeAsync(
        IReadOnlyList<VideoFrame> frames,
        IReadOnlyList<AudioChunk> audio,
        NormalizedMouthRegion region,
        CancellationToken cancellationToken) => Task.Run(() =>
        AnalyzeFeatures(ExtractVisualFeatures(frames, region), ExtractAudioFeatures(audio), cancellationToken), cancellationToken);

    public LipSyncAnalysisResult AnalyzeFeatures(
        IReadOnlyList<LipSyncFeatureSample> visual,
        IReadOnlyList<LipSyncFeatureSample> audio,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        if (visual.Count < 8 || audio.Count < 8)
            return Empty("Not enough recent speech and mouth movement", stopwatch);
        if (!visual[0].Timestamp.IsComparableTo(audio[0].Timestamp))
            return Empty("Audio and video timing domains are not comparable", stopwatch);

        var visualEvents = LocalPeaks(visual);
        var audioEvents = LocalPeaks(audio);
        if (visualEvents.Count < 2 || audioEvents.Count < 2)
            return Empty("Not enough independent speech events", stopwatch);

        var bestLag = FindBestLag(visual, audio);
        var possibilities = new List<LipSyncMarker>();
        foreach (var v in visualEvents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var nearest = audioEvents
                .Select(a => (Audio: a, Delta: (v.Timestamp.Ticks100ns - a.Timestamp.Ticks100ns) / 10_000d))
                .Where(item => Math.Abs(item.Delta) <= MaximumLagMilliseconds)
                .OrderBy(item => Math.Abs(item.Delta - bestLag))
                .FirstOrDefault();
            if (nearest.Audio is null) continue;
            var strength = Math.Sqrt(Math.Clamp(v.Strength, 0, 1) * Math.Clamp(nearest.Audio.Strength, 0, 1));
            possibilities.Add(new(nearest.Audio.Timestamp, v.Timestamp, nearest.Delta, strength));
        }

        if (possibilities.Count < 2) return Empty("Not enough matched speech events", stopwatch);
        var centre = WeightedMedian(possibilities.Select(item => (item.SignedMilliseconds, Math.Max(.01, item.Confidence))).ToArray());
        var consistent = possibilities.Where(item => Math.Abs(item.SignedMilliseconds - centre) <= 90)
            .OrderByDescending(item => item.Confidence).Take(MaximumMarkers).OrderBy(item => item.VisualTimestamp.Ticks100ns).ToArray();
        if (consistent.Length < 2) return Empty("Speech events are not consistent enough", stopwatch);

        var aggregate = WeightedMedian(consistent.Select(item => (item.SignedMilliseconds, Math.Max(.01, item.Confidence))).ToArray());
        var deviations = consistent.Select(item => Math.Abs(item.SignedMilliseconds - aggregate)).OrderBy(value => value).ToArray();
        var spread = Median(deviations);
        if (spread > 70) return Empty("Speech events are not consistent enough", stopwatch);

        var primaryIndex = Array.IndexOf(consistent, consistent.OrderByDescending(item => item.Confidence).First());
        var markers = consistent.Select((item, index) => item with { IsPrimary = index == primaryIndex }).ToArray();
        var result = new SyncResult(aggregate, "Estimated Lip Sync", true);
        var confidence = Math.Clamp(markers.Average(item => item.Confidence) * (1 - spread / 140d), 0, 1);
        stopwatch.Stop();
        return new(result, markers, confidence, spread, $"Estimated Lip Sync · {markers.Length} speech events", stopwatch.Elapsed);
    }

    public static IReadOnlyList<LipSyncFeatureSample> ExtractVisualFeatures(IReadOnlyList<VideoFrame> frames, NormalizedMouthRegion region)
    {
        var normalized = region.Normalize();
        var output = new List<LipSyncFeatureSample>(Math.Max(0, frames.Count - 1));
        VideoFrame? previous = null;
        foreach (var frame in frames.OrderBy(item => item.Timestamp.Ticks100ns))
        {
            if (previous is not null && previous.Width == frame.Width && previous.Height == frame.Height)
            {
                var x0 = Math.Clamp((int)Math.Floor(normalized.X * frame.Width), 0, frame.Width - 1);
                var y0 = Math.Clamp((int)Math.Floor(normalized.Y * frame.Height), 0, frame.Height - 1);
                var x1 = Math.Clamp((int)Math.Ceiling((normalized.X + normalized.Width) * frame.Width), x0 + 1, frame.Width);
                var y1 = Math.Clamp((int)Math.Ceiling((normalized.Y + normalized.Height) * frame.Height), y0 + 1, frame.Height);
                double local = 0; var count = 0;
                for (var y = y0; y < y1; y += 2)
                for (var x = x0; x < x1; x += 2)
                {
                    var index = y * frame.EffectiveStride + x;
                    local += Math.Abs(frame.Luma[index] - previous.Luma[index]) / 255d; count++;
                }
                var global = Math.Abs(frame.Luma.Average(value => (double)value) - previous.Luma.Average(value => (double)value)) / 255d;
                output.Add(new(frame.Timestamp, Math.Clamp(local / Math.Max(1, count) - global * .55, 0, 1)));
            }
            previous = frame;
        }
        return Normalize(output);
    }

    public static IReadOnlyList<LipSyncFeatureSample> ExtractAudioFeatures(IReadOnlyList<AudioChunk> chunks)
    {
        var output = new List<LipSyncFeatureSample>();
        foreach (var chunk in chunks.OrderBy(item => item.Timestamp.Ticks100ns))
        {
            if (chunk.SampleRate <= 0 || chunk.Channels <= 0) continue;
            var binFrames = Math.Max(1, chunk.SampleRate / 50);
            for (var first = 0; first < chunk.FrameCount; first += binFrames)
            {
                var count = Math.Min(binFrames, chunk.FrameCount - first); double energy = 0;
                for (var frame = 0; frame < count; frame++)
                for (var channel = 0; channel < chunk.Channels; channel++)
                {
                    var sample = chunk.Samples[(first + frame) * chunk.Channels + channel]; energy += sample * sample;
                }
                var ticks = chunk.Timestamp.Ticks100ns + (long)Math.Round(first / (double)chunk.SampleRate * TimeSpan.TicksPerSecond);
                output.Add(new(chunk.Timestamp with { Ticks100ns = ticks, RawValue = ticks }, Math.Sqrt(energy / Math.Max(1, count * chunk.Channels))));
            }
        }
        return Normalize(output);
    }

    private static List<LipSyncFeatureSample> LocalPeaks(IReadOnlyList<LipSyncFeatureSample> samples)
    {
        var strengths = samples.Select(item => item.Strength).OrderBy(value => value).ToArray();
        var threshold = Math.Max(.18, Median(strengths) + .12);
        var peaks = new List<LipSyncFeatureSample>();
        foreach (var sample in samples.Where((item, index) => index > 0 && index < samples.Count - 1 && item.Strength >= threshold
                     && item.Strength >= samples[index - 1].Strength && item.Strength > samples[index + 1].Strength)
                 .OrderByDescending(item => item.Strength))
        {
            if (peaks.All(existing => Math.Abs(existing.Timestamp.Ticks100ns - sample.Timestamp.Ticks100ns) >= TimeSpan.FromMilliseconds(120).Ticks))
                peaks.Add(sample);
        }
        return peaks.OrderBy(item => item.Timestamp.Ticks100ns).Take(32).ToList();
    }

    private static double FindBestLag(IReadOnlyList<LipSyncFeatureSample> visual, IReadOnlyList<LipSyncFeatureSample> audio)
    {
        var orderedAudio = audio.OrderBy(item => item.Timestamp.Ticks100ns).ToArray();
        var bestLag = 0d; var bestScore = double.NegativeInfinity;
        for (var lag = -MaximumLagMilliseconds; lag <= MaximumLagMilliseconds; lag += 10)
        {
            double score = 0; var count = 0;
            foreach (var sample in visual)
            {
                var target = sample.Timestamp.Ticks100ns - (long)Math.Round(lag * 10_000);
                var nearest = Nearest(orderedAudio, target);
                if (nearest is null || Math.Abs(nearest.Timestamp.Ticks100ns - target) > TimeSpan.FromMilliseconds(15).Ticks) continue;
                score += sample.Strength * nearest.Strength; count++;
            }
            score = count == 0 ? double.NegativeInfinity : score / Math.Sqrt(count);
            if (score > bestScore) { bestScore = score; bestLag = lag; }
        }
        return bestLag;
    }

    private static LipSyncFeatureSample? Nearest(IReadOnlyList<LipSyncFeatureSample> samples, long ticks)
    {
        var low = 0; var high = samples.Count - 1;
        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            if (samples[middle].Timestamp.Ticks100ns < ticks) low = middle + 1; else high = middle - 1;
        }
        if (low == 0) return samples[0]; if (low >= samples.Count) return samples[^1];
        return Math.Abs(samples[low].Timestamp.Ticks100ns - ticks) < Math.Abs(samples[low - 1].Timestamp.Ticks100ns - ticks) ? samples[low] : samples[low - 1];
    }

    private static IReadOnlyList<LipSyncFeatureSample> Normalize(IReadOnlyList<LipSyncFeatureSample> samples)
    {
        if (samples.Count == 0) return [];
        var ordered = samples.Select(item => item.Strength).OrderBy(value => value).ToArray();
        var floor = ordered[(int)Math.Floor((ordered.Length - 1) * .25)];
        var ceiling = ordered[(int)Math.Floor((ordered.Length - 1) * .95)];
        var range = Math.Max(1e-9, ceiling - floor);
        return samples.Select(item => item with { Strength = Math.Clamp((item.Strength - floor) / range, 0, 1) }).ToArray();
    }

    private static double WeightedMedian((double Value, double Weight)[] values)
    {
        var ordered = values.OrderBy(item => item.Value).ToArray(); var half = ordered.Sum(item => item.Weight) / 2; double sum = 0;
        foreach (var item in ordered) { sum += item.Weight; if (sum >= half) return item.Value; }
        return ordered[^1].Value;
    }

    private static double Median(IReadOnlyList<double> values) => values.Count == 0 ? 0 : values.Count % 2 == 0
        ? (values[values.Count / 2 - 1] + values[values.Count / 2]) / 2 : values[values.Count / 2];

    private static LipSyncAnalysisResult Empty(string status, Stopwatch stopwatch)
    {
        stopwatch.Stop(); return new(null, [], null, null, status, stopwatch.Elapsed);
    }
}
