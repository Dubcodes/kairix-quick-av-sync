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
    public Task<VisualCandidate?> DetectAsync(IReadOnlyList<VideoFrame> frames, MediaTimestamp expected, CancellationToken cancellationToken) => Task.Run(() =>
    {
        if (frames.Count < 4) return null;
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
        if (bestIndex < 0 || bestPeakIndex < 0 || bestScore < 2.5 || evidence[bestPeakIndex] < .008) return null;
        var normalizedStrength = Math.Clamp(evidence[bestPeakIndex] / .2, 0, 1);
        var confidence = Math.Clamp(.08 + .58 * (1 - Math.Exp(-Math.Max(0, bestScore - 1.2) / 5)) + .25 * normalizedStrength, .08, .94); var chosen = frames[bestIndex];
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
        const int columns = 16, rows = 9; var cells = Enumerable.Range(0, columns * rows).Select(_ => new List<double>()).ToArray(); double total = 0; var samples = 0; var globallyChanged = 0;
        var stepX = Math.Max(1, width / 160); var stepY = Math.Max(1, height / 90);
        for (var y = 0; y < height; y += stepY) for (var x = 0; x < width; x += stepX)
        {
            var previousOffset = y * previous.EffectiveStride + x; var currentOffset = y * current.EffectiveStride + x;
            if (previousOffset >= previous.Luma.Length || currentOffset >= current.Luma.Length) continue;
            var difference = Math.Abs(previous.Luma[previousOffset] - current.Luma[currentOffset]) / 255d; var block = Math.Min(rows - 1, y * rows / height) * columns + Math.Min(columns - 1, x * columns / width);
            cells[block].Add(difference); total += difference; samples++; if (difference > .055) globallyChanged++;
        }
        if (samples == 0) return 0;
        var globalMean = total / samples; var globalChangedFraction = globallyChanged / (double)samples;
        var scores = new double[cells.Length]; var coherentFractions = new double[cells.Length];
        for (var index = 0; index < cells.Length; index++)
        {
            var values = cells[index]; if (values.Count == 0) continue;
            values.Sort((left, right) => right.CompareTo(left)); var topCount = Math.Max(1, values.Count / 5);
            var topMean = values.Take(topCount).Average(); var changedFraction = values.Count(value => value > .045) / (double)values.Count; var strongFraction = values.Count(value => value > .11) / (double)values.Count;
            coherentFractions[index] = changedFraction;
            scores[index] = topMean * .62 + changedFraction * .18 + strongFraction * .2;
        }
        var active = scores.Count(value => value > .035); var activeFraction = active / (double)scores.Length; var broadFraction = scores.Count(value => value > .01) / (double)scores.Length; var scoreMean = scores.Average();
        var scoreVariation = Math.Sqrt(scores.Select(value => (value - scoreMean) * (value - scoreMean)).Average());
        // Uniform exposure changes and broad camera motion are deliberately not clap evidence.
        if (globalChangedFraction > .72 && scoreVariation < .08) return 0;
        var strongestIndex = Enumerable.Range(0, scores.Length).MaxBy(index => scores[index]); var strongest = scores[strongestIndex];
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
        var localEvidence = strongest * .72 + localCoherence * .24 + globalMean * .04;
        return Math.Max(0, localEvidence - globalMean * .12);
    }
}

// Compatibility name retained for existing callers.
public sealed class VisualClapDetector : IVisualClapDetector
{
    private readonly MotionVisualClapDetector _inner = new();
    public Task<VisualCandidate?> DetectAsync(IReadOnlyList<VideoFrame> frames, MediaTimestamp expected, CancellationToken cancellationToken) => _inner.DetectAsync(frames, expected, cancellationToken);
}

public sealed class SessionHistoryService(int capacity = 3)
{
    private readonly int _capacity = capacity;
    private readonly LinkedList<SessionResult> _items = [];
    public IReadOnlyList<SessionResult> Items => _items.ToArray();
    public void Add(SessionResult item) { _items.AddFirst(item); while (_items.Count > _capacity) _items.RemoveLast(); }
}
