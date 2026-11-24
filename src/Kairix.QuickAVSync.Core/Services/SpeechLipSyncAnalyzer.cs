using System.Diagnostics;
using Kairix.QuickAVSync.Models;

namespace Kairix.QuickAVSync.Services;

/// <summary>Bounded on-demand speech/lip correlation, independent of IVisualClapDetector.</summary>
public sealed class SpeechLipSyncAnalyzer
{
    public const int MaximumMarkers = 10;
    public const double MaximumLagMilliseconds = 300;
    private const double LagStepMilliseconds = 10;
    private const double IndependentLagPeakDistanceMilliseconds = 60;
    private const double MinimumCorrelationMargin = .06;

    public Task<LipSyncAnalysisResult> AnalyzeAsync(IReadOnlyList<VideoFrame> frames, IReadOnlyList<AudioChunk> audio,
        NormalizedMouthRegion region, VideoTimingOffset timingOffset, CancellationToken cancellationToken) => Task.Run(() =>
    {
        var visual = ExtractVisualFeatures(frames, region, cancellationToken); var audioFeatures = ExtractAudioFeatures(audio, cancellationToken);
        var result = AnalyzeFeatures(visual, audioFeatures, timingOffset, cancellationToken);
        var window = frames.Count < 2 ? 0 : (frames[^1].Timestamp.Ticks100ns - frames[0].Timestamp.Ticks100ns) / 10_000d;
        return result with { VideoFrameCount = frames.Count, AudioFeatureBinCount = audioFeatures.Count, AnalysisWindowMilliseconds = Math.Max(0, window) };
    }, cancellationToken);

    public LipSyncAnalysisResult AnalyzeFeatures(IReadOnlyList<LipSyncFeatureSample> visual, IReadOnlyList<LipSyncFeatureSample> audio,
        VideoTimingOffset timingOffset = default, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        if (visual.Count < 8 || audio.Count < 8) return Empty("Not enough recent speech and mouth movement", stopwatch, timingOffset, visual.Count, audio.Count);
        if (!visual[0].Timestamp.IsComparableTo(audio[0].Timestamp)) return Empty("Audio and video timing domains are not comparable", stopwatch, timingOffset, visual.Count, audio.Count);
        var visualEvents = LocalPeaks(visual); var audioEvents = LocalPeaks(audio);
        if (visualEvents.Count < 2 || audioEvents.Count < 2) return Empty("Not enough independent speech events", stopwatch, timingOffset, visual.Count, audio.Count, visualEvents.Count, audioEvents.Count);

        cancellationToken.ThrowIfCancellationRequested();
        var surface = FindLagSurface(visual, audio, timingOffset, cancellationToken);
        if (surface.Margin < MinimumCorrelationMargin) return Empty("Ambiguous correlation peaks", stopwatch, timingOffset, visual.Count, audio.Count, visualEvents.Count, audioEvents.Count, surface: surface);
        var matched = MatchMonotonicOneToOne(visualEvents, audioEvents, surface.BestCorrectedLag, timingOffset, cancellationToken);
        if (matched.Count < 2) return Empty("Not enough independent matched speech events", stopwatch, timingOffset, visual.Count, audio.Count, visualEvents.Count, audioEvents.Count, Math.Max(visualEvents.Count, audioEvents.Count) - matched.Count, surface);

        var centre = WeightedMedian(matched.Select(item => (item.SignedMilliseconds, Math.Max(.01, item.Confidence))).ToArray());
        var consistent = matched.Where(item => Math.Abs(item.SignedMilliseconds - centre) <= 90).OrderByDescending(item => item.Confidence)
            .Take(MaximumMarkers).OrderBy(item => item.VisualTimestamp.Ticks100ns).ToArray();
        if (consistent.Length < 2) return Empty("Speech events are not consistent enough", stopwatch, timingOffset, visual.Count, audio.Count, visualEvents.Count, audioEvents.Count, matched.Count - consistent.Length, surface);
        var correctedAggregate = WeightedMedian(consistent.Select(item => (item.SignedMilliseconds, Math.Max(.01, item.Confidence))).ToArray());
        var rawAggregate = WeightedMedian(consistent.Select(item => (item.RawSignedMilliseconds, Math.Max(.01, item.Confidence))).ToArray());
        var deviations = consistent.Select(item => Math.Abs(item.SignedMilliseconds - correctedAggregate)).OrderBy(value => value).ToArray(); var spread = Median(deviations);
        if (spread > 70) return Empty("Speech events are not consistent enough", stopwatch, timingOffset, visual.Count, audio.Count, visualEvents.Count, audioEvents.Count, matched.Count - consistent.Length, surface);

        var primary = consistent.OrderByDescending(item => item.Confidence).ThenBy(item => item.EventIndex).First();
        var markers = consistent.Select((item, index) => item with { IsPrimary = item == primary, EventIndex = index }).ToArray();
        var ambiguityQuality = Math.Clamp(surface.Margin / .25, 0, 1);
        var confidence = Math.Clamp(markers.Average(item => item.Confidence) * (1 - spread / 140d) * (.55 + .45 * ambiguityQuality), 0, 1);
        stopwatch.Stop();
        return new(ResultFor(correctedAggregate), markers, confidence, spread, $"Estimated Lip Sync · {markers.Length} speech events", stopwatch.Elapsed,
            ResultFor(rawAggregate), timingOffset.Ticks100ns, timingOffset.Source, surface.BestRawLag, surface.BestCorrectedLag,
            surface.BestScore, surface.CompetingScore, surface.Margin, 0, audio.Count, visualEvents.Count, audioEvents.Count,
            Math.Max(visualEvents.Count, audioEvents.Count) - markers.Length, WindowMilliseconds(visual, audio));
    }

    private static IReadOnlyList<LipSyncMarker> MatchMonotonicOneToOne(IReadOnlyList<LipSyncFeatureSample> visual,
        IReadOnlyList<LipSyncFeatureSample> audio, double preferredCorrectedLag, VideoTimingOffset timingOffset, CancellationToken cancellationToken)
    {
        var scores = new double[visual.Count + 1, audio.Count + 1]; var decisions = new byte[visual.Count + 1, audio.Count + 1];
        for (var i = 1; i <= visual.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var j = 1; j <= audio.Count; j++)
            {
                var skipVisual = scores[i - 1, j]; var skipAudio = scores[i, j - 1];
                var pair = PairScore(visual[i - 1], audio[j - 1], preferredCorrectedLag, timingOffset, out _, out _);
                var matched = pair >= .20 ? scores[i - 1, j - 1] + pair : double.NegativeInfinity;
                if (matched >= skipVisual && matched >= skipAudio) { scores[i, j] = matched; decisions[i, j] = 3; }
                else if (skipVisual >= skipAudio) { scores[i, j] = skipVisual; decisions[i, j] = 1; }
                else { scores[i, j] = skipAudio; decisions[i, j] = 2; }
            }
        }
        var output = new List<LipSyncMarker>(); var vi = visual.Count; var ai = audio.Count;
        while (vi > 0 && ai > 0)
        {
            if (decisions[vi, ai] == 3)
            {
                var v = visual[vi - 1]; var a = audio[ai - 1]; var confidence = PairScore(v, a, preferredCorrectedLag, timingOffset, out var raw, out var corrected);
                output.Add(new(a.Timestamp, v.Timestamp, corrected, confidence, VisualEvidence: v.Strength, AudioEvidence: a.Strength,
                    RawSignedMilliseconds: raw, VideoTimingOffsetTicks100ns: timingOffset.Ticks100ns, VideoTimingOffsetSource: timingOffset.Source)); vi--; ai--;
            }
            else if (decisions[vi, ai] == 1) vi--; else if (decisions[vi, ai] == 2) ai--; else { vi--; ai--; }
        }
        output.Reverse(); return output.Select((item, index) => item with { EventIndex = index }).ToArray();
    }

    internal static IReadOnlyList<LipSyncMarker> MatchEventsForTesting(IReadOnlyList<LipSyncFeatureSample> visual,
        IReadOnlyList<LipSyncFeatureSample> audio, double preferredCorrectedLag, VideoTimingOffset timingOffset = default) =>
        MatchMonotonicOneToOne(visual, audio, preferredCorrectedLag, timingOffset, CancellationToken.None);

    private static double PairScore(LipSyncFeatureSample visual, LipSyncFeatureSample audio, double preferredCorrectedLag,
        VideoTimingOffset timingOffset, out double raw, out double corrected)
    {
        raw = (visual.Timestamp.Ticks100ns - audio.Timestamp.Ticks100ns) / 10_000d; corrected = raw - timingOffset.Milliseconds;
        if (Math.Abs(corrected) > MaximumLagMilliseconds) return 0;
        var agreement = Math.Exp(-Math.Pow((corrected - preferredCorrectedLag) / 55d, 2) / 2);
        return Math.Sqrt(Math.Clamp(visual.Strength, 0, 1) * Math.Clamp(audio.Strength, 0, 1)) * agreement;
    }

    private static LagSurface FindLagSurface(IReadOnlyList<LipSyncFeatureSample> visual, IReadOnlyList<LipSyncFeatureSample> audio,
        VideoTimingOffset timingOffset, CancellationToken cancellationToken)
    {
        var orderedAudio = audio.OrderBy(item => item.Timestamp.Ticks100ns).ToArray(); var bins = new List<(double Lag, double Score)>();
        for (var correctedLag = -MaximumLagMilliseconds; correctedLag <= MaximumLagMilliseconds; correctedLag += LagStepMilliseconds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rawLag = correctedLag + timingOffset.Milliseconds; double score = 0; var count = 0;
            foreach (var sample in visual)
            {
                var target = sample.Timestamp.Ticks100ns - (long)Math.Round(rawLag * 10_000); var nearest = Nearest(orderedAudio, target);
                if (nearest is null || Math.Abs(nearest.Timestamp.Ticks100ns - target) > TimeSpan.FromMilliseconds(15).Ticks) continue;
                score += sample.Strength * nearest.Strength; count++;
            }
            bins.Add((correctedLag, count == 0 ? 0 : score / Math.Sqrt(count)));
        }
        var best = bins.OrderByDescending(item => item.Score).ThenBy(item => Math.Abs(item.Lag)).First();
        var competitor = bins.Where(item => Math.Abs(item.Lag - best.Lag) >= IndependentLagPeakDistanceMilliseconds).OrderByDescending(item => item.Score).ThenBy(item => Math.Abs(item.Lag)).FirstOrDefault();
        var margin = best.Score <= 1e-9 ? 0 : Math.Clamp((best.Score - competitor.Score) / best.Score, 0, 1);
        return new(best.Lag + timingOffset.Milliseconds, best.Lag, best.Score, competitor.Score, margin);
    }

    public static IReadOnlyList<LipSyncFeatureSample> ExtractVisualFeatures(IReadOnlyList<VideoFrame> frames, NormalizedMouthRegion region,
        CancellationToken cancellationToken = default)
    {
        var normalized = region.Normalize(); var output = new List<LipSyncFeatureSample>(Math.Max(0, frames.Count - 1)); VideoFrame? previous = null;
        foreach (var frame in frames.OrderBy(item => item.Timestamp.Ticks100ns))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (previous is not null && previous.Width == frame.Width && previous.Height == frame.Height)
            {
                var x0 = Math.Clamp((int)Math.Floor(normalized.X * frame.Width), 0, frame.Width - 1); var y0 = Math.Clamp((int)Math.Floor(normalized.Y * frame.Height), 0, frame.Height - 1);
                var x1 = Math.Clamp((int)Math.Ceiling((normalized.X + normalized.Width) * frame.Width), x0 + 1, frame.Width); var y1 = Math.Clamp((int)Math.Ceiling((normalized.Y + normalized.Height) * frame.Height), y0 + 1, frame.Height);
                double local = 0; var count = 0;
                for (var y = y0; y < y1; y += 2) for (var x = x0; x < x1; x += 2) { var index = y * frame.EffectiveStride + x; local += Math.Abs(frame.Luma[index] - previous.Luma[index]) / 255d; count++; }
                var global = Math.Abs(frame.Luma.Average(value => (double)value) - previous.Luma.Average(value => (double)value)) / 255d;
                output.Add(new(frame.Timestamp, Math.Clamp(local / Math.Max(1, count) - global, 0, 1)));
            }
            previous = frame;
        }
        return Normalize(output);
    }

    public static IReadOnlyList<LipSyncFeatureSample> ExtractAudioFeatures(IReadOnlyList<AudioChunk> chunks, CancellationToken cancellationToken = default)
    {
        var output = new List<LipSyncFeatureSample>();
        foreach (var chunk in chunks.OrderBy(item => item.Timestamp.Ticks100ns))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (chunk.SampleRate <= 0 || chunk.Channels <= 0) continue; var binFrames = Math.Max(1, chunk.SampleRate / 50);
            for (var first = 0; first < chunk.FrameCount; first += binFrames)
            {
                var count = Math.Min(binFrames, chunk.FrameCount - first); double energy = 0;
                for (var frame = 0; frame < count; frame++) for (var channel = 0; channel < chunk.Channels; channel++) { var sample = chunk.Samples[(first + frame) * chunk.Channels + channel]; energy += sample * sample; }
                var ticks = chunk.Timestamp.Ticks100ns + (long)Math.Round(first / (double)chunk.SampleRate * TimeSpan.TicksPerSecond);
                output.Add(new(chunk.Timestamp with { Ticks100ns = ticks, RawValue = ticks }, Math.Sqrt(energy / Math.Max(1, count * chunk.Channels))));
            }
        }
        return Normalize(output);
    }

    private static List<LipSyncFeatureSample> LocalPeaks(IReadOnlyList<LipSyncFeatureSample> samples)
    {
        var strengths = samples.Select(item => item.Strength).OrderBy(value => value).ToArray(); var threshold = Math.Max(.18, Median(strengths) + .12); var peaks = new List<LipSyncFeatureSample>();
        foreach (var sample in samples.Where((item, index) => index > 0 && index < samples.Count - 1 && item.Strength >= threshold && item.Strength >= samples[index - 1].Strength && item.Strength > samples[index + 1].Strength).OrderByDescending(item => item.Strength))
            if (peaks.All(existing => Math.Abs(existing.Timestamp.Ticks100ns - sample.Timestamp.Ticks100ns) >= TimeSpan.FromMilliseconds(120).Ticks)) peaks.Add(sample);
        return peaks.OrderBy(item => item.Timestamp.Ticks100ns).Take(32).ToList();
    }

    private static LipSyncFeatureSample? Nearest(IReadOnlyList<LipSyncFeatureSample> samples, long ticks)
    {
        var low = 0; var high = samples.Count - 1;
        while (low <= high) { var middle = low + (high - low) / 2; if (samples[middle].Timestamp.Ticks100ns < ticks) low = middle + 1; else high = middle - 1; }
        if (low == 0) return samples[0]; if (low >= samples.Count) return samples[^1];
        return Math.Abs(samples[low].Timestamp.Ticks100ns - ticks) < Math.Abs(samples[low - 1].Timestamp.Ticks100ns - ticks) ? samples[low] : samples[low - 1];
    }

    private static IReadOnlyList<LipSyncFeatureSample> Normalize(IReadOnlyList<LipSyncFeatureSample> samples)
    {
        if (samples.Count == 0) return []; var ordered = samples.Select(item => item.Strength).OrderBy(value => value).ToArray();
        var floor = ordered[(int)Math.Floor((ordered.Length - 1) * .25)]; var ceiling = ordered[(int)Math.Floor((ordered.Length - 1) * .95)]; var range = Math.Max(1e-9, ceiling - floor);
        return samples.Select(item => item with { Strength = Math.Clamp((item.Strength - floor) / range, 0, 1) }).ToArray();
    }

    private static SyncResult ResultFor(double value) { var rounded = Math.Round(Math.Abs(value), 1); var wording = Math.Abs(value) < .05 ? "AUDIO AND VIDEO IN SYNC" : value > 0 ? $"AUDIO LEADS VIDEO BY {rounded:0.#} ms" : $"AUDIO LAGS VIDEO BY {rounded:0.#} ms"; return new(value, wording, true); }
    private static double WeightedMedian((double Value, double Weight)[] values) { var ordered = values.OrderBy(item => item.Value).ToArray(); var half = ordered.Sum(item => item.Weight) / 2; double sum = 0; foreach (var item in ordered) { sum += item.Weight; if (sum >= half) return item.Value; } return ordered[^1].Value; }
    private static double Median(IReadOnlyList<double> values) => values.Count == 0 ? 0 : values.Count % 2 == 0 ? (values[values.Count / 2 - 1] + values[values.Count / 2]) / 2 : values[values.Count / 2];
    private static double WindowMilliseconds(IReadOnlyList<LipSyncFeatureSample> visual, IReadOnlyList<LipSyncFeatureSample> audio) { var first = Math.Min(visual[0].Timestamp.Ticks100ns, audio[0].Timestamp.Ticks100ns); var last = Math.Max(visual[^1].Timestamp.Ticks100ns, audio[^1].Timestamp.Ticks100ns); return Math.Max(0, (last - first) / 10_000d); }
    private static LipSyncAnalysisResult Empty(string status, Stopwatch stopwatch, VideoTimingOffset offset, int visualBins, int audioBins, int visualEvents = 0, int audioEvents = 0, int rejected = 0, LagSurface? surface = null)
    {
        stopwatch.Stop(); return new(null, [], null, null, $"NOT ENOUGH LIP-SYNC EVIDENCE · {status}", stopwatch.Elapsed, null,
            offset.Ticks100ns, offset.Source, surface?.BestRawLag, surface?.BestCorrectedLag, surface?.BestScore, surface?.CompetingScore,
            surface?.Margin, 0, audioBins, visualEvents, audioEvents, rejected, 0);
    }
    private sealed record LagSurface(double BestRawLag, double BestCorrectedLag, double BestScore, double CompetingScore, double Margin);
}
