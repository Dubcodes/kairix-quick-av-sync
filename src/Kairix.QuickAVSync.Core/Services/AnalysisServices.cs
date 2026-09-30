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
        if (frames.Count < 3) return null;
        var differences = new double[frames.Count];
        for (var i = 1; i < frames.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested(); var a = frames[i - 1].Luma; var b = frames[i].Luma; var length = Math.Min(a.Length, b.Length);
            if (length == 0) continue; double sum = 0;
            for (var p = 0; p < length; p += 4) sum += Math.Abs(a[p] - b[p]);
            differences[i] = sum / Math.Ceiling(length / 4d) / 255d;
        }
        var measured = differences.Skip(1).ToArray(); var baseline = Median(measured);
        var deviations = measured.Select(value => Math.Abs(value - baseline)).ToArray(); var spread = Math.Max(.0015, Median(deviations) * 1.4826);
        var bestIndex = -1; var bestScore = 0d;
        for (var i = 1; i < frames.Count - 1; i++)
        {
            var proximity = Math.Exp(-Math.Abs(frames[i].Timestamp.Ticks100ns - expected.Ticks100ns) / (double)TimeSpan.FromMilliseconds(160).Ticks);
            // A clap contact is a brief local change, not continuous background motion.
            var localFloor = (differences[i - 1] + differences[i + 1]) / 2d;
            var transientness = Math.Max(0, differences[i] - Math.Max(baseline, localFloor * .55));
            var score = transientness / spread * (.7 + .3 * proximity);
            if (score > bestScore) { bestScore = score; bestIndex = i; }
        }
        if (bestIndex < 0 || bestScore < 2.5) return null;
        var confidence = Math.Clamp((bestScore - 2) / 12, .08, .96); var chosen = frames[bestIndex];
        return new VisualCandidate(chosen.Timestamp, chosen.TemporalIndex, confidence, bestScore, chosen);
    }, cancellationToken);

    private static double Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0) return 0; var ordered = values.OrderBy(value => value).ToArray(); var middle = ordered.Length / 2;
        return ordered.Length % 2 == 0 ? (ordered[middle - 1] + ordered[middle]) / 2 : ordered[middle];
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
