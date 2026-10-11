using System.Buffers;
using Kairix.QuickAVSync.Capture;
using Kairix.QuickAVSync.Models;

namespace Kairix.QuickAVSync.Services;

public sealed class TransientDetector
{
    private readonly double _refractorySeconds;
    private double _noise = .002;
    private long _lastDetected = long.MinValue / 2;
    private int _sensitivity = 50;
    public TransientDetector(double refractorySeconds = .28) => _refractorySeconds = refractorySeconds;
    public int Sensitivity { get => Volatile.Read(ref _sensitivity); set => Interlocked.Exchange(ref _sensitivity, Math.Clamp(value, 0, 100)); }
    public IReadOnlyList<AudioTransient> Process(AudioChunk chunk)
    {
        if (chunk.SampleRate <= 0 || chunk.Channels <= 0) return [];
        var output = new List<AudioTransient>(); var frames = chunk.FrameCount; const int window = 32;
        for (var frame = 0; frame < frames; frame += window)
        {
            var end = Math.Min(frames, frame + window); double energy = 0; float peak = 0; var peakFrame = frame;
            for (var i = frame; i < end; i++)
            {
                float aggregate = 0;
                for (var c = 0; c < chunk.Channels; c++) aggregate = Math.Max(aggregate, Math.Abs(chunk.Samples[i * chunk.Channels + c]));
                if (aggregate > peak) { peak = aggregate; peakFrame = i; }
                energy += aggregate * aggregate;
            }
            var rms = Math.Sqrt(energy / Math.Max(1, end - frame));
            var timestamp = chunk.Timestamp.Ticks100ns + TimeSpan.FromSeconds((double)frame / chunk.SampleRate).Ticks;
            var elapsed = TimeSpan.FromTicks(timestamp - _lastDetected).TotalSeconds;
            var normalizedSensitivity = Sensitivity / 100d;
            var thresholdScale = normalizedSensitivity <= .5 ? 1 + (.5 - normalizedSensitivity) * 1.6 : 1 - (normalizedSensitivity - .5) * .8;
            var threshold = Math.Max(.025 * thresholdScale, _noise * 7.5 * thresholdScale);
            if (peak > threshold && rms > Math.Max(.008 * thresholdScale, _noise * 2.8 * thresholdScale) && elapsed >= _refractorySeconds)
            {
                double prePeakEnergy = 0; var prePeakFrames = 0;
                for (var i = frame; i < peakFrame; i++)
                {
                    float aggregate = 0;
                    for (var c = 0; c < chunk.Channels; c++) aggregate = Math.Max(aggregate, Math.Abs(chunk.Samples[i * chunk.Channels + c]));
                    prePeakEnergy += aggregate * aggregate; prePeakFrames++;
                }
                var crest = peak / Math.Max((float)rms, .0001f);
                var peakToNoise = peak / Math.Max((float)_noise, .0001f);
                var prePeakRms = Math.Sqrt(prePeakEnergy / Math.Max(1, prePeakFrames));
                var attack = peak / Math.Max((float)prePeakRms, Math.Max((float)_noise * 2, .0001f));
                output.Add(new(new(timestamp, chunk.Timestamp.Quality, chunk.Timestamp.ClockDomain, chunk.Timestamp.RawValue), peak, (float)_noise, (float)rms, crest, peakToNoise, attack));
                _lastDetected = timestamp;
            }
            else if (peak < threshold) _noise = Math.Clamp(_noise * .985 + rms * .015, .0001, .2);
        }
        return output;
    }
}

public sealed record AudioCandidateQuality(bool Passed, double Quality);

public static class AudioCandidateClassifier
{
    public const float MinimumPeak = .16f;
    public const float MinimumCrestFactor = 1.7f;
    public const float MinimumPeakToNoiseRatio = 16f;
    public const float MinimumAttackRatio = 3f;

    public static AudioCandidateQuality Evaluate(AudioTransient transient)
    {
        var peak = Math.Clamp((transient.Peak - .08) / .42, 0, 1);
        var crest = Math.Clamp((transient.CrestFactor - 1) / 2.5, 0, 1);
        var signalToNoise = Math.Clamp((transient.PeakToNoiseRatio - 6) / 34, 0, 1);
        var attack = Math.Clamp((transient.AttackRatio - 1) / 12, 0, 1);
        var quality = peak * .3 + crest * .25 + signalToNoise * .25 + attack * .2;
        var passed = transient.Peak >= MinimumPeak
            && transient.CrestFactor >= MinimumCrestFactor
            && transient.PeakToNoiseRatio >= MinimumPeakToNoiseRatio
            && transient.AttackRatio >= MinimumAttackRatio;
        return new(passed, quality);
    }
}

public static class WorkWindowSelector
{
    public static IReadOnlyList<T> Around<T>(IEnumerable<T> items, Func<T, MediaTimestamp> timestamp, MediaTimestamp center, TimeSpan halfWindow) =>
        items.Where(x => Math.Abs(timestamp(x).Ticks100ns - center.Ticks100ns) <= halfWindow.Ticks).OrderBy(x => timestamp(x).Ticks100ns).ToArray();
}

public sealed class WaveformBuilder
{
    public IReadOnlyList<float> Build(IReadOnlyList<AudioChunk> chunks, MediaTimestamp center, TimeSpan halfWindow, int points)
    {
        if (points < 1) throw new ArgumentOutOfRangeException(nameof(points));
        var output = new float[points]; var start = center.Ticks100ns - halfWindow.Ticks; var span = halfWindow.Ticks * 2d;
        foreach (var chunk in chunks)
        {
            if (chunk.SampleRate <= 0 || chunk.Channels <= 0) continue;
            for (var i = 0; i < chunk.FrameCount; i++)
            {
                var tick = chunk.Timestamp.Ticks100ns + (long)(i / (double)chunk.SampleRate * TimeSpan.TicksPerSecond);
                var bin = (int)((tick - start) / span * points); if (bin < 0 || bin >= points) continue;
                float peak = 0; for (var c = 0; c < chunk.Channels; c++) peak = Math.Max(peak, Math.Abs(chunk.Samples[i * chunk.Channels + c]));
                output[bin] = Math.Max(output[bin], peak);
            }
        }
        return output;
    }
}

public sealed class MotionVisualClapDetector : IVisualClapDetector
{
    public static string DetailDescription(int width, int height)
    {
        if (width < 2 || height < 2) return "unavailable";
        var (columns, rows, stepX, stepY) = DetailGrid(width, height);
        return $"{columns}x{rows} cells · approximately {(width + stepX - 1) / stepX}x{(height + stepY - 1) / stepY} samples";
    }

    public Task<VisualCandidate?> DetectAsync(IReadOnlyList<VideoFrame> frames, MediaTimestamp expected, CancellationToken cancellationToken, VisualDetectionOptions? options = null) => Task.Run(() =>
    {
        if (frames.Count < 4) return null;
        var sensitivity = (options ?? new()).NormalizedSensitivity;
        var qualityThreshold = .81 - sensitivity * .26;
        var acceptedEvidenceThreshold = .10 - sensitivity * .095;
        const double minimumEvidence = .0025;
        var features = Enumerable.Repeat(VisualTransitionFeatures.Empty, frames.Count).ToArray();
        var evidence = new double[frames.Count];
        for (var i = 1; i < frames.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested(); features[i] = ExtractFeatures(frames[i - 1], frames[i]); evidence[i] = features[i].LocalizedMotion;
        }
        var measured = evidence.Skip(1).ToArray(); var baseline = Median(measured);
        var deviations = measured.Select(value => Math.Abs(value - baseline)).ToArray(); var spread = Math.Max(.0015, Median(deviations) * 1.4826);
        var candidates = new List<ContactEvaluation>();
        for (var peakIndex = 2; peakIndex < frames.Count - 2; peakIndex++)
        {
            var peak = evidence[peakIndex]; var previous = evidence[peakIndex - 1];
            var meaningfulApproach = Math.Max(minimumEvidence, baseline + spread * .15);
            if (peak < minimumEvidence || peak < baseline + spread * .5 || peak < previous * .82 || previous < meaningfulApproach) continue;

            // Stage 1: identify every plausible localized approach. Expected time
            // remains a weak tie-breaker and never substitutes for contact evidence.
            var rise = Math.Clamp((peak - Math.Max(baseline, previous * .55)) / Math.Max(.01, spread * 8), 0, 1);
            var prominence = Math.Clamp((peak - baseline) / Math.Max(peak, .0001), 0, 1);
            var leadIn = Math.Clamp((previous - baseline) / Math.Max(peak - baseline, .0001), 0, 1);
            var continuity = SpatialContinuity(features[peakIndex - 1], features[peakIndex]);
            var sustainedApproach = evidence[peakIndex - 2] >= meaningfulApproach;
            if (sustainedApproach) continuity = (continuity + SpatialContinuity(features[peakIndex - 2], features[peakIndex - 1])) / 2;
            var approachQuality = rise * .25 + prominence * .2 + leadIn * .3 + continuity * .25;

            // With a sustained approach, the transition into the touching image
            // can itself be the contact onset. Score it against the following hold.
            if (sustainedApproach)
            {
                var next = evidence[peakIndex + 1];
                var following = evidence[peakIndex + 2];
                AddCandidate(peakIndex, peakIndex, next, following, approachQuality, continuity, peak);
            }

            // Stage 2: score every plausible contact/settle image one to three
            // temporal positions after this approach. Selection happens only after
            // all approach/contact pairs have been evaluated.
            for (var contactIndex = peakIndex + 1; contactIndex <= Math.Min(frames.Count - 3, peakIndex + 3); contactIndex++)
            {
                var contact = evidence[contactIndex];
                var post = contactIndex + 1 < evidence.Length ? evidence[contactIndex + 1] : contact;
                var post2 = contactIndex + 2 < evidence.Length ? evidence[contactIndex + 2] : post;
                var postMean = (post + post2) / 2;
                AddCandidate(peakIndex, contactIndex, contact, postMean, approachQuality, continuity, contact);
            }

            void AddCandidate(int approachIndex, int contactIndex, double scoringContact, double postMean, double candidateApproachQuality, double candidateContinuity, double observedContact)
            {
                var dropQuality = Math.Clamp((peak - scoringContact) / Math.Max(peak, .0001), 0, 1);
                var settleQuality = Math.Clamp((peak - postMean) / Math.Max(peak, .0001), 0, 1);
                if (dropQuality < .18 || settleQuality < .12) return;
                var localizedQuality = Math.Clamp((peak - baseline) / Math.Max(peak + .025, .0001), 0, 1);
                var absoluteStrength = Math.Clamp(peak / .12, 0, 1);
                var patternQuality = candidateApproachQuality * .27 + candidateContinuity * .16 + dropQuality * .23 + settleQuality * .19 + localizedQuality * .1 + absoluteStrength * .05;
                var contactProximity = Math.Exp(-Math.Abs(frames[contactIndex].Timestamp.Ticks100ns - expected.Ticks100ns) / (double)TimeSpan.FromMilliseconds(160).Ticks);
                // Once an approach has peaked, the first credible settled image is
                // the contact. Later quiet images must not outrank it merely because
                // they have accumulated more post-contact stillness.
                var delayedContactPenalty = Math.Max(0, contactIndex - approachIndex - 1) * .09;
                var selectionScore = patternQuality + contactProximity * .06 - delayedContactPenalty;
                candidates.Add(new(approachIndex, contactIndex, selectionScore, patternQuality, peak, observedContact, postMean, candidateApproachQuality, candidateContinuity, dropQuality, settleQuality));
            }
        }
        if (candidates.Count == 0) return null;
        var strongestQuality = candidates.Max(candidate => candidate.PatternQuality);
        var ranked = candidates.Where(candidate => candidate.PatternQuality >= strongestQuality - .18)
            .OrderByDescending(candidate => candidate.SelectionScore).ThenBy(candidate => candidate.ContactIndex).ToArray();
        var best = ranked[0];
        if (best.PatternQuality < qualityThreshold || best.ApproachEvidence < acceptedEvidenceThreshold) return null;
        var dominance = ranked.Length == 1 ? 1 : Math.Clamp((best.PatternQuality - ranked[1].PatternQuality) / .25, 0, 1);
        var confidence = Math.Clamp(.1 + best.PatternQuality * .75 + dominance * .1, .1, .95);
        var chosen = frames[best.ContactIndex];
        return new VisualCandidate(chosen.Timestamp, chosen.TemporalIndex, confidence, best.SelectionScore * 10, chosen,
            new(best.ApproachIndex, best.ContactIndex, best.ContactIndex - best.ApproachIndex, best.ApproachEvidence, best.ContactEvidence, best.PostEvidence,
                VisualDetectorKind.HumanClap, best.ApproachQuality, best.ContinuityQuality, best.DropQuality, best.SettleQuality, dominance));
    }, cancellationToken);

    private sealed record ContactEvaluation(
        int ApproachIndex,
        int ContactIndex,
        double SelectionScore,
        double PatternQuality,
        double ApproachEvidence,
        double ContactEvidence,
        double PostEvidence,
        double ApproachQuality,
        double ContinuityQuality,
        double DropQuality,
        double SettleQuality);

    private static double SpatialContinuity(VisualTransitionFeatures previous, VisualTransitionFeatures current)
    {
        if (previous.LocalizedMotion <= 0 || current.LocalizedMotion <= 0) return 0;
        var distance = Math.Sqrt(Math.Pow(previous.CentroidX - current.CentroidX, 2) + Math.Pow(previous.CentroidY - current.CentroidY, 2));
        return Math.Clamp(1 - distance / .35, 0, 1);
    }

    private static double Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0) return 0; var ordered = values.OrderBy(value => value).ToArray(); var middle = ordered.Length / 2;
        return ordered.Length % 2 == 0 ? (ordered[middle - 1] + ordered[middle]) / 2 : ordered[middle];
    }

    internal static VisualTransitionFeatures ExtractFeatures(VideoFrame previous, VideoFrame current)
    {
        var width = Math.Min(previous.Width, current.Width); var height = Math.Min(previous.Height, current.Height);
        if (width < 2 || height < 2) return VisualTransitionFeatures.Empty;
        // 640x360 intentionally remains the compatibility baseline (32x18
        // cells and a 320x180 sample grid). Lower settings do less work while
        // larger stored rasters add proportionally finer cells and samples.
        var (columns, rows, stepX, stepY) = DetailGrid(width, height);
        var sampledColumns = (width + stepX - 1) / stepX; var sampledRows = (height + stepY - 1) / stepY;
        var maximumPerCell = ((sampledColumns + columns - 1) / columns + 1) * ((sampledRows + rows - 1) / rows + 1);
        var cellCount = columns * rows;
        var cellSamples = ArrayPool<float>.Shared.Rent(cellCount * maximumPerCell);
        var counts = ArrayPool<int>.Shared.Rent(cellCount);
        var scores = ArrayPool<double>.Shared.Rent(cellCount);
        var coherentFractions = ArrayPool<double>.Shared.Rent(cellCount);
        Array.Clear(counts, 0, cellCount);
        try
        {
            double total = 0, signedTotal = 0, centralTotal = 0, centralSignedTotal = 0; var samples = 0; var centralSamples = 0; var globallyChanged = 0;
            for (var y = 0; y < height; y += stepY) for (var x = 0; x < width; x += stepX)
            {
                var previousOffset = y * previous.EffectiveStride + x; var currentOffset = y * current.EffectiveStride + x;
                if (previousOffset >= previous.Luma.Length || currentOffset >= current.Luma.Length) continue;
                var signedDifference = (current.Luma[currentOffset] - previous.Luma[previousOffset]) / 255d;
                var difference = Math.Abs(signedDifference);
                var block = Math.Min(rows - 1, y * rows / height) * columns + Math.Min(columns - 1, x * columns / width);
                var count = counts[block]; if (count < maximumPerCell) cellSamples[block * maximumPerCell + count] = (float)difference;
                counts[block] = count + 1; total += difference; signedTotal += signedDifference; samples++; if (difference > .055) globallyChanged++;
                if (x >= width / 4 && x < width * 3 / 4 && y >= height / 4 && y < height * 3 / 4)
                {
                    centralTotal += difference; centralSignedTotal += signedDifference; centralSamples++;
                }
            }
            if (samples == 0) return VisualTransitionFeatures.Empty;
            var globalMean = total / samples; var globalChangedFraction = globallyChanged / (double)samples;
            double scoreTotal = 0, centroidX = 0, centroidY = 0; var active = 0; var broad = 0; var strongestIndex = 0;
            for (var index = 0; index < cellCount; index++)
            {
                var count = Math.Min(counts[index], maximumPerCell); scores[index] = 0; coherentFractions[index] = 0;
                if (count == 0) continue;
                var offset = index * maximumPerCell; Array.Sort(cellSamples, offset, count);
                var topCount = Math.Max(1, count / 5); double topTotal = 0; var changed = 0; var strong = 0;
                for (var sampleIndex = 0; sampleIndex < count; sampleIndex++)
                {
                    var value = cellSamples[offset + sampleIndex];
                    if (sampleIndex >= count - topCount) topTotal += value;
                    if (value > .045) changed++; if (value > .11) strong++;
                }
                var changedFraction = changed / (double)count; var score = topTotal / topCount * .62 + changedFraction * .18 + strong / (double)count * .2;
                coherentFractions[index] = changedFraction; scores[index] = score; scoreTotal += score;
                centroidX += score * ((index % columns + .5) / columns); centroidY += score * ((index / columns + .5) / rows);
                if (score > .035) active++; if (score > .01) broad++; if (score > scores[strongestIndex]) strongestIndex = index;
            }
            var activeFraction = active / (double)cellCount; var broadFraction = broad / (double)cellCount; var scoreMean = scoreTotal / cellCount;
            double variationTotal = 0; for (var index = 0; index < cellCount; index++) { var delta = scores[index] - scoreMean; variationTotal += delta * delta; }
            var scoreVariation = Math.Sqrt(variationTotal / cellCount);
            var strongest = scores[strongestIndex];
            var row = strongestIndex / columns; var column = strongestIndex % columns; double bestNeighbor = 0;
            for (var dy = -1; dy <= 1; dy++) for (var dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue; var neighborRow = row + dy; var neighborColumn = column + dx;
                if (neighborRow >= 0 && neighborRow < rows && neighborColumn >= 0 && neighborColumn < columns) bestNeighbor = Math.Max(bestNeighbor, scores[neighborRow * columns + neighborColumn]);
            }
            var localCoherence = Math.Max(bestNeighbor, coherentFractions[strongestIndex] * .35);
            var localizedMotion = Math.Max(0, strongest * .72 + localCoherence * .24 - globalMean * .08);
            if (globalChangedFraction > .72 && scoreVariation < .08) localizedMotion = 0;
            if (globalMean > .01 && broadFraction > .65) localizedMotion = 0;
            if (activeFraction > .58 && strongest < scoreMean * 2.2) localizedMotion = 0;
            if (coherentFractions[strongestIndex] < .05 && bestNeighbor < .04) localizedMotion = 0;
            if (scoreTotal <= 0) { centroidX = (column + .5) / columns; centroidY = (row + .5) / rows; }
            else { centroidX /= scoreTotal; centroidY /= scoreTotal; }
            return new(localizedMotion, globalMean, signedTotal / samples, globalChangedFraction,
                centralSamples == 0 ? 0 : centralTotal / centralSamples,
                centralSamples == 0 ? 0 : centralSignedTotal / centralSamples,
                centroidX, centroidY);
        }
        finally
        {
            ArrayPool<float>.Shared.Return(cellSamples); ArrayPool<int>.Shared.Return(counts);
            ArrayPool<double>.Shared.Return(scores); ArrayPool<double>.Shared.Return(coherentFractions);
        }
    }

    private static (int Columns, int Rows, int StepX, int StepY) DetailGrid(int width, int height)
    {
        var columns = Math.Clamp(width / 20, 16, 96); var rows = Math.Clamp(height / 20, 9, 54);
        return (columns, rows, Math.Max(1, width / (columns * 10)), Math.Max(1, height / (rows * 10)));
    }
}

internal sealed record VisualTransitionFeatures(
    double LocalizedMotion,
    double GlobalAbsoluteChange,
    double SignedLuminanceChange,
    double ChangedFraction,
    double CentralAbsoluteChange,
    double CentralSignedLuminanceChange,
    double CentroidX,
    double CentroidY)
{
    public static VisualTransitionFeatures Empty { get; } = new(0, 0, 0, 0, 0, 0, .5, .5);
}

public sealed class FlashTestSignalVisualDetector : IVisualClapDetector
{
    public Task<VisualCandidate?> DetectAsync(IReadOnlyList<VideoFrame> frames, MediaTimestamp expected, CancellationToken cancellationToken, VisualDetectionOptions? options = null) => Task.Run(() =>
    {
        if (frames.Count < 5) return null;
        var sensitivity = (options ?? new()).NormalizedSensitivity;
        var features = Enumerable.Repeat(VisualTransitionFeatures.Empty, frames.Count).ToArray();
        for (var i = 1; i < frames.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            features[i] = MotionVisualClapDetector.ExtractFeatures(frames[i - 1], frames[i]);
        }

        var candidates = new List<FlashEvaluation>();
        for (var onsetIndex = 3; onsetIndex < frames.Count - 1; onsetIndex++)
        {
            var onset = features[onsetIndex];
            var positiveLuma = Math.Max(onset.SignedLuminanceChange, onset.CentralSignedLuminanceChange * .55);
            var magnitude = Math.Max(onset.GlobalAbsoluteChange, onset.CentralAbsoluteChange * .55);
            if (positiveLuma <= 0 || magnitude < .004) continue;

            var preMotion = features.Skip(onsetIndex - 3).Take(3)
                .Average(feature => feature.GlobalAbsoluteChange);
            var preStability = 1 - Math.Clamp(preMotion / Math.Max(.012, magnitude * .45), 0, 1);
            var postMotion = features[onsetIndex + 1].GlobalAbsoluteChange;
            var holdQuality = 1 - Math.Clamp(postMotion / Math.Max(.012, magnitude * .55), 0, 1);
            var sharpness = Math.Clamp((magnitude - preMotion) / Math.Max(.025, magnitude), 0, 1);
            var lumaQuality = Math.Clamp(positiveLuma / .16, 0, 1);
            var spatialCoherence = Math.Clamp(Math.Max(onset.ChangedFraction / .18, onset.CentralAbsoluteChange / .22), 0, 1);
            var onsetQuality = preStability * .27 + sharpness * .25 + lumaQuality * .22 + spatialCoherence * .16 + holdQuality * .10;
            var proximity = Math.Exp(-Math.Abs(frames[onsetIndex].Timestamp.Ticks100ns - expected.Ticks100ns) / (double)TimeSpan.FromMilliseconds(160).Ticks);
            candidates.Add(new(onsetIndex, onsetQuality + proximity * .035, onsetQuality, preStability, positiveLuma, onset.ChangedFraction, sharpness, holdQuality));
        }

        if (candidates.Count == 0) return null;
        var qualityThreshold = .60 - sensitivity * .16;
        var magnitudeThreshold = .055 - sensitivity * .04;
        var ranked = candidates.Where(candidate => candidate.LumaDelta >= magnitudeThreshold)
            .OrderByDescending(candidate => candidate.SelectionScore).ThenBy(candidate => candidate.OnsetIndex).ToArray();
        if (ranked.Length == 0) return null;
        var best = ranked[0];
        if (best.OnsetQuality < qualityThreshold || best.LumaDelta < magnitudeThreshold) return null;
        var margin = ranked.Length == 1 ? 1 : Math.Clamp((best.OnsetQuality - ranked[1].OnsetQuality) / .25, 0, 1);
        var confidence = Math.Clamp(.1 + best.OnsetQuality * .75 + margin * .1, .1, .95);
        var chosen = frames[best.OnsetIndex];
        return new VisualCandidate(chosen.Timestamp, chosen.TemporalIndex, confidence, best.SelectionScore * 10, chosen,
            new(best.OnsetIndex, best.OnsetIndex, 0, best.LumaDelta, best.LumaDelta, best.HoldQuality,
                VisualDetectorKind.FlashTestSignal, CandidateMargin: margin, PreStability: best.PreStability,
                LumaDelta: best.LumaDelta, ChangedFraction: best.ChangedFraction, OnsetQuality: best.OnsetQuality));
    }, cancellationToken);

    private sealed record FlashEvaluation(
        int OnsetIndex,
        double SelectionScore,
        double OnsetQuality,
        double PreStability,
        double LumaDelta,
        double ChangedFraction,
        double Sharpness,
        double HoldQuality);
}

public static class VisualDetectorFactory
{
    private static readonly IVisualClapDetector HumanClap = new MotionVisualClapDetector();
    private static readonly IVisualClapDetector Flash = new FlashTestSignalVisualDetector();

    public static IVisualClapDetector Create(VisualDetectorKind kind) => kind switch
    {
        VisualDetectorKind.FlashTestSignal => Flash,
        _ => HumanClap
    };
}

// Compatibility name retained for existing callers.
public sealed class VisualClapDetector : IVisualClapDetector
{
    private readonly MotionVisualClapDetector _inner = new();
    public Task<VisualCandidate?> DetectAsync(IReadOnlyList<VideoFrame> frames, MediaTimestamp expected, CancellationToken cancellationToken, VisualDetectionOptions? options = null) => _inner.DetectAsync(frames, expected, cancellationToken, options);
}

public sealed class SessionHistoryService(int capacity = 3)
{
    private int _capacity = NormalizeCapacity(capacity);
    private readonly LinkedList<SessionResult> _items = [];
    public IReadOnlyList<SessionResult> Items => _items.ToArray();
    public int Capacity => _capacity;
    public void Add(SessionResult item) { _items.AddFirst(item); while (_items.Count > _capacity) _items.RemoveLast(); }
    public void SetCapacity(int capacity) { _capacity = NormalizeCapacity(capacity); while (_items.Count > _capacity) _items.RemoveLast(); }
    private static int NormalizeCapacity(int capacity) => capacity switch { 5 => 5, 10 => 10, _ => 3 };
}

public sealed record HistoryReviewWorkspace(
    EventReviewState Review,
    IReadOnlyList<VideoFrame> Frames,
    IReadOnlyList<float> Waveform,
    IReadOnlyList<double> FrameTicks,
    FrameTimingAnalysis? Timeline)
{
    public bool RequiresHold => true;
}

public static class ReviewHistorySnapshotFactory
{
    public static ReviewHistorySnapshot Create(EventReviewState review, IReadOnlyList<VideoFrame> frames, IReadOnlyList<float> waveform,
        IReadOnlyList<double> frameTicks, FrameTimingAnalysis? timeline)
    {
        if (review.AutoCandidate is not { } candidate) throw new ArgumentException("A confirmed automatic candidate is required.", nameof(review));
        var compact = frames.Select(Compact).ToArray();
        if (compact.Length == 0) throw new ArgumentException("A retained review window is required.", nameof(frames));
        var candidateFrame = compact.FirstOrDefault(frame => frame.Timestamp == candidate.Timestamp && frame.TemporalIndex == candidate.TemporalIndex)
            ?? compact.OrderBy(frame => Math.Abs(frame.Timestamp.Ticks100ns - candidate.Timestamp.Ticks100ns)).First();
        var compactCandidate = candidate with { Frame = candidateFrame };
        var waveformCopy = waveform.ToArray(); var ticksCopy = frameTicks.ToArray();
        var bytes = compact.Sum(frame => (long)(frame.HasPresentation ? frame.PresentationBgra!.Length : frame.Luma.Length))
            + waveformCopy.LongLength * sizeof(float) + ticksCopy.LongLength * sizeof(double);
        return new(review.EventReference, review.AudioMark, compactCandidate, Array.AsReadOnly(compact), Array.AsReadOnly(waveformCopy),
            Array.AsReadOnly(ticksCopy), timeline, review.VideoTimingOffset.Ticks100ns, review.VideoTimingOffset.Source, bytes);
    }

    public static HistoryReviewWorkspace Restore(ReviewHistorySnapshot snapshot)
    {
        var review = new EventReviewState(snapshot.EventReference, snapshot.AudioMark,
            new(snapshot.VideoTimingOffsetTicks100ns, snapshot.VideoTimingOffsetSource));
        review.SetHistoryCandidate(snapshot.AutoCandidate);
        return new(review, snapshot.Frames, snapshot.Waveform, snapshot.FrameTicks, snapshot.Timeline);
    }

    private static VideoFrame Compact(VideoFrame frame)
    {
        if (frame.HasPresentation)
        {
            return frame with
            {
                Width = frame.PresentationWidth,
                Height = frame.PresentationHeight,
                Luma = [],
                Stride = frame.PresentationWidth,
                PresentationBgra = frame.PresentationBgra!.ToArray()
            };
        }
        return frame with { Luma = frame.Luma.ToArray(), PresentationBgra = frame.PresentationBgra?.ToArray() };
    }
}
