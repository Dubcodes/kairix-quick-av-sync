using Kairix.QuickAVSync.Models;

namespace Kairix.QuickAVSync.Services;

public sealed class FrameTimingAnalyzer
{
    public FrameTimingAnalysis Analyze(IReadOnlyList<VideoFrame> frames, double declaredTemporalRate, double declaredTransportRate = 0)
    {
        var expectedMs = declaredTemporalRate > 0 ? 1000d / declaredTemporalRate : 0;
        var primary = Statistics(frames.Select(frame => (long?)frame.Timestamp.Ticks100ns), expectedMs);
        var nativeFrames = CollapseNativeSamples(frames);
        if (declaredTransportRate <= 0 && frames.Count > 0) declaredTransportRate = declaredTemporalRate * nativeFrames.Count / frames.Count;
        var expectedTransportMs = declaredTransportRate > 0 ? 1000d / declaredTransportRate : 0;
        var transport = Statistics(nativeFrames.Select(frame => (long?)frame.Timestamp.Ticks100ns), expectedTransportMs);
        var arrival = Statistics(nativeFrames.Select(frame => frame.TimingObservation is { StopwatchFrequency: > 0 } timing
            ? (long?)Math.Round(timing.ArrivalStopwatchTicks * (double)TimeSpan.TicksPerSecond / timing.StopwatchFrequency)
            : null), expectedTransportMs);
        var device = Statistics(nativeFrames.Select(frame => frame.TimingObservation?.DeviceTimestampTicks100ns), expectedTransportMs);
        var sample = Statistics(nativeFrames.Select(frame => frame.TimingObservation?.SampleTimeTicks100ns), expectedTransportMs);
        var reader = Statistics(nativeFrames.Select(frame => frame.TimingObservation?.ReaderTimestampTicks100ns), expectedTransportMs);
        var error = declaredTemporalRate > 0 && primary.ObservedRate > 0
            ? Math.Abs(primary.ObservedRate - declaredTemporalRate) / declaredTemporalRate * 100
            : 0;
        var content = FrameContentAnalyzer.Analyze(frames, primary.ObservedRate);
        var valid = primary.BackwardCount == 0 && primary.DuplicateCount == 0 && error < 10;
        var status = primary.BackwardCount > 0 ? "INVALID VIDEO TIMESTAMPS · BACKWARDS"
            : primary.DuplicateCount > 0 ? "INVALID VIDEO TIMESTAMPS · DUPLICATES"
            : error >= 10 ? $"TIMELINE TIMING INVALID · DECLARED {declaredTemporalRate:0.###} · OBSERVED {primary.ObservedRate:0.###} FPS"
            : error >= 3 ? $"TIMELINE WARNING · DECLARED {declaredTemporalRate:0.###} · OBSERVED {primary.ObservedRate:0.###} FPS"
            : content.PairedRepeatDetected ? $"REPEATED FRAME PAIRS DETECTED · {primary.ObservedRate:0.##} TIMESTAMPED FPS · ≈{content.EstimatedUniqueImageRate:0.##} UNIQUE FPS"
            : $"TIMELINE · {primary.ObservedRate:0.##} fps · {primary.MedianIntervalMilliseconds:0.###} ms";
        return new(declaredTemporalRate, expectedMs, declaredTransportRate, transport, primary, arrival, device, sample, reader, frames.Count, nativeFrames.Count, content, error, valid, status);
    }

    private static IReadOnlyList<VideoFrame> CollapseNativeSamples(IReadOnlyList<VideoFrame> frames) => frames
        .Select((frame, index) => (Frame: frame, Key: (HasNative: frame.NativeSampleIndex >= 0, Value: frame.NativeSampleIndex >= 0 ? frame.NativeSampleIndex : index)))
        .GroupBy(item => item.Key)
        .Select(group => group.Last().Frame)
        .ToArray();

    private static CadenceStatistics Statistics(IEnumerable<long?> source, double expectedMs)
    {
        var values = source.Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        if (values.Length < 2) return new(values.Length, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        var deltas = new long[values.Length - 1];
        for (var i = 1; i < values.Length; i++) deltas[i - 1] = values[i] - values[i - 1];
        var positiveMs = deltas.Where(delta => delta > 0).Select(delta => delta / 10_000d).Order().ToArray();
        var median = Median(positiveMs);
        var deviations = positiveMs.Select(value => Math.Abs(value - median)).Order().ToArray();
        var jitter = median > 0 ? Median(deviations) / median * 100 : 0;
        var largeGapThreshold = expectedMs > 0 ? expectedMs * 1.75 : median * 1.75;
        return new(values.Length, deltas.Length, median > 0 ? 1000d / median : 0, median,
            positiveMs.Length == 0 ? 0 : positiveMs[0], positiveMs.Length == 0 ? 0 : positiveMs[^1], jitter,
            deltas.Count(delta => delta == 0), deltas.Count(delta => delta < 0),
            largeGapThreshold > 0 ? positiveMs.Count(delta => delta > largeGapThreshold) : 0);
    }

    private static double Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0) return 0;
        var middle = values.Count / 2;
        return values.Count % 2 == 0 ? (values[middle - 1] + values[middle]) / 2 : values[middle];
    }
}

public sealed record ReviewTimelineSnapshot(IReadOnlyList<VideoFrame> Frames, FrameTimingAnalysis Analysis, int RawFrameCount);

public static class ReviewTimelineIntegrity
{
    public static ReviewTimelineSnapshot Build(IReadOnlyList<VideoFrame> captureOrderFrames, MediaTimestamp center, TimeSpan halfWindow, double declaredTemporalRate, double declaredTransportRate = 0)
    {
        var raw = captureOrderFrames.Where(frame => Math.Abs(frame.Timestamp.Ticks100ns - center.Ticks100ns) <= halfWindow.Ticks).ToArray();
        var analysis = new FrameTimingAnalyzer().Analyze(raw, declaredTemporalRate, declaredTransportRate);
        var seen = new HashSet<long>();
        var review = raw.Where(frame => seen.Add(frame.Timestamp.Ticks100ns)).OrderBy(frame => frame.Timestamp.Ticks100ns).ToArray();
        return new(review, analysis, raw.Length);
    }
}
