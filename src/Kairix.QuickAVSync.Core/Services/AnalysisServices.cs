using System.Buffers;
using Kairix.QuickAVSync.Capture;
using Kairix.QuickAVSync.Models;

namespace Kairix.QuickAVSync.Services;

public sealed class TransientDetector
{
    private readonly double _refractorySeconds;
    private double _noise = .002;
    private long _lastDetected = long.MinValue / 2;
    public TransientDetector(double refractorySeconds = .28) => _refractorySeconds = refractorySeconds;
    public IReadOnlyList<AudioTransient> Process(AudioChunk chunk)
    {
        if (chunk.SampleRate <= 0 || chunk.Channels <= 0) return [];
        var output = new List<AudioTransient>(); var frames = chunk.FrameCount; const int window = 32;
        for (var frame = 0; frame < frames; frame += window)
        {
            var end = Math.Min(frames, frame + window); double energy = 0; float peak = 0;
            for (var i = frame; i < end; i++)
            {
                float aggregate = 0;
                for (var c = 0; c < chunk.Channels; c++) aggregate = Math.Max(aggregate, Math.Abs(chunk.Samples[i * chunk.Channels + c]));
                peak = Math.Max(peak, aggregate); energy += aggregate * aggregate;
            }
            var rms = Math.Sqrt(energy / Math.Max(1, end - frame));
            var timestamp = chunk.Timestamp.Ticks100ns + TimeSpan.FromSeconds((double)frame / chunk.SampleRate).Ticks;
            var elapsed = TimeSpan.FromTicks(timestamp - _lastDetected).TotalSeconds;
            var threshold = Math.Max(.025, _noise * 7.5);
            if (peak > threshold && rms > Math.Max(.008, _noise * 2.8) && elapsed >= _refractorySeconds)
            {
                output.Add(new(new(timestamp, chunk.Timestamp.Quality, chunk.Timestamp.ClockDomain, chunk.Timestamp.RawValue), peak, (float)_noise));
                _lastDetected = timestamp;
            }
            else if (peak < threshold) _noise = Math.Clamp(_noise * .985 + rms * .015, .0001, .2);
        }
        return output;
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
        var scoreThreshold = sensitivity <= .5 ? 2.5 + (.5 - sensitivity) * 7 : 2.5 - (sensitivity - .5) * 3;
        var evidenceThreshold = sensitivity <= .5 ? .008 + (.5 - sensitivity) * .224 : .008 - (sensitivity - .5) * .008;
        var evidence = new double[frames.Count];
        for (var i = 1; i < frames.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested(); evidence[i] = SpatialEvidence(frames[i - 1], frames[i]);
        }
        var measured = evidence.Skip(1).ToArray(); var baseline = Median(measured);
        var deviations = measured.Select(value => Math.Abs(value - baseline)).ToArray(); var spread = Math.Max(.0015, Median(deviations) * 1.4826);
        var bestIndex = -1; var bestPeakIndex = -1; var bestScore = 0d;
        for (var i = 1; i < frames.Count - 1; i++)
        {
            var proximity = Math.Exp(-Math.Abs(frames[i].Timestamp.Ticks100ns - expected.Ticks100ns) / (double)TimeSpan.FromMilliseconds(160).Ticks);
            var previous = evidence[i - 1]; var peak = evidence[i]; var next = evidence[i + 1];
            // A contact can be the low-motion image immediately after a strong final
            // approach. Advance only when the measured rise/peak/drop supports it.
            var hasRise = previous > baseline + spread * .5 && peak > Math.Max(baseline + spread, previous * 1.12);
            var hasPostPeakDrop = next < peak * .52 && peak - next > Math.Max(.006, spread * .8);
            var localFloor = Math.Max(baseline, (previous + next) * .38);
            var transientness = Math.Max(0, peak - localFloor);
            var score = transientness / spread * (.72 + .28 * proximity) * (hasRise && hasPostPeakDrop ? 3 : 1);
            var candidateIndex = hasRise && hasPostPeakDrop ? i + 1 : i;
            var currentDistance = bestIndex < 0 ? long.MaxValue : Math.Abs(frames[bestIndex].Timestamp.Ticks100ns - expected.Ticks100ns);
            var candidateDistance = Math.Abs(frames[candidateIndex].Timestamp.Ticks100ns - expected.Ticks100ns);
            if (score > bestScore + .01 || (Math.Abs(score - bestScore) <= .01 && candidateDistance < currentDistance)) { bestScore = score; bestPeakIndex = i; bestIndex = candidateIndex; }
        }
        if (bestIndex < 0 || bestPeakIndex < 0 || bestScore < scoreThreshold || evidence[bestPeakIndex] < evidenceThreshold) return null;
        var normalizedStrength = Math.Clamp(evidence[bestPeakIndex] / .2, 0, 1);
        var temporalConfidence = 1 - Math.Exp(-Math.Max(0, bestScore - 1.2) / 5);
        var confidence = Math.Clamp(.08 + .45 * temporalConfidence * Math.Sqrt(normalizedStrength) + .35 * normalizedStrength, .08, .94); var chosen = frames[bestIndex];
        return new VisualCandidate(chosen.Timestamp, chosen.TemporalIndex, confidence, bestScore, chosen);
    }, cancellationToken);

    private static double Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0) return 0; var ordered = values.OrderBy(value => value).ToArray(); var middle = ordered.Length / 2;
        return ordered.Length % 2 == 0 ? (ordered[middle - 1] + ordered[middle]) / 2 : ordered[middle];
    }

    private static double SpatialEvidence(VideoFrame previous, VideoFrame current)
    {
        var width = Math.Min(previous.Width, current.Width); var height = Math.Min(previous.Height, current.Height);
        if (width < 2 || height < 2) return 0;
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
            double total = 0; var samples = 0; var globallyChanged = 0;
            for (var y = 0; y < height; y += stepY) for (var x = 0; x < width; x += stepX)
            {
                var previousOffset = y * previous.EffectiveStride + x; var currentOffset = y * current.EffectiveStride + x;
                if (previousOffset >= previous.Luma.Length || currentOffset >= current.Luma.Length) continue;
                var difference = Math.Abs(previous.Luma[previousOffset] - current.Luma[currentOffset]) / 255f;
                var block = Math.Min(rows - 1, y * rows / height) * columns + Math.Min(columns - 1, x * columns / width);
                var count = counts[block]; if (count < maximumPerCell) cellSamples[block * maximumPerCell + count] = difference;
                counts[block] = count + 1; total += difference; samples++; if (difference > .055) globallyChanged++;
            }
            if (samples == 0) return 0;
            var globalMean = total / samples; var globalChangedFraction = globallyChanged / (double)samples;
            double scoreTotal = 0; var active = 0; var broad = 0; var strongestIndex = 0;
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
                if (score > .035) active++; if (score > .01) broad++; if (score > scores[strongestIndex]) strongestIndex = index;
            }
            var activeFraction = active / (double)cellCount; var broadFraction = broad / (double)cellCount; var scoreMean = scoreTotal / cellCount;
            double variationTotal = 0; for (var index = 0; index < cellCount; index++) { var delta = scores[index] - scoreMean; variationTotal += delta * delta; }
            var scoreVariation = Math.Sqrt(variationTotal / cellCount);
            if (globalChangedFraction > .72 && scoreVariation < .08) return 0;
            var strongest = scores[strongestIndex];
            if (globalMean > .01 && broadFraction > .65) return 0;
            if (activeFraction > .58 && strongest < scoreMean * 2.2) return 0;
            var row = strongestIndex / columns; var column = strongestIndex % columns; double bestNeighbor = 0;
            for (var dy = -1; dy <= 1; dy++) for (var dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue; var neighborRow = row + dy; var neighborColumn = column + dx;
                if (neighborRow >= 0 && neighborRow < rows && neighborColumn >= 0 && neighborColumn < columns) bestNeighbor = Math.Max(bestNeighbor, scores[neighborRow * columns + neighborColumn]);
            }
            if (coherentFractions[strongestIndex] < .05 && bestNeighbor < .04) return 0;
            var localCoherence = Math.Max(bestNeighbor, coherentFractions[strongestIndex] * .35);
            return Math.Max(0, strongest * .72 + localCoherence * .24 + globalMean * .04 - globalMean * .12);
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

// Compatibility name retained for existing callers.
public sealed class VisualClapDetector : IVisualClapDetector
{
    private readonly MotionVisualClapDetector _inner = new();
    public Task<VisualCandidate?> DetectAsync(IReadOnlyList<VideoFrame> frames, MediaTimestamp expected, CancellationToken cancellationToken, VisualDetectionOptions? options = null) => _inner.DetectAsync(frames, expected, cancellationToken, options);
}

public sealed class SessionHistoryService(int capacity = 3)
{
    private readonly int _capacity = capacity;
    private readonly LinkedList<SessionResult> _items = [];
    public IReadOnlyList<SessionResult> Items => _items.ToArray();
    public void Add(SessionResult item) { _items.AddFirst(item); while (_items.Count > _capacity) _items.RemoveLast(); }
}
