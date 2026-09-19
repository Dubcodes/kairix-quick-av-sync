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
        var output = new List<AudioTransient>();
        var frames = chunk.Samples.Length / chunk.Channels;
        const int window = 32;
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
            var offset = TimeSpan.FromSeconds((double)frame / chunk.SampleRate).Ticks;
            var timestamp = chunk.Timestamp.Ticks100ns + offset;
            var elapsed = TimeSpan.FromTicks(timestamp - _lastDetected).TotalSeconds;
            var threshold = Math.Max(.025, _noise * 7.5);
            if (peak > threshold && rms > Math.Max(.008, _noise * 2.8) && elapsed >= _refractorySeconds)
            {
                output.Add(new(new(timestamp, chunk.Timestamp.Quality), peak, (float)_noise)); _lastDetected = timestamp;
            }
            else if (peak < threshold) _noise = Math.Clamp(_noise * .985 + rms * .015, .0001, .2);
        }
        return output;
    }
}

public static class WorkWindowSelector
{
    public static IReadOnlyList<T> Around<T>(IEnumerable<T> items, Func<T, MediaTimestamp> timestamp, MediaTimestamp center, TimeSpan halfWindow) =>
        items.Where(x => Math.Abs((timestamp(x).Ticks100ns - center.Ticks100ns)) <= halfWindow.Ticks).OrderBy(x => timestamp(x).Ticks100ns).ToArray();
}

public sealed class VisualClapDetector
{
    public Task<VisualCandidate?> DetectAsync(IReadOnlyList<VideoFrame> frames, MediaTimestamp expected, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            if (frames.Count < 3) return null;
            var differences = new double[frames.Count];
            for (var i = 1; i < frames.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var a = frames[i - 1].Luma; var b = frames[i].Luma; var length = Math.Min(a.Length, b.Length);
                if (length == 0) continue;
                double sum = 0;
                for (var p = 0; p < length; p += 4) sum += Math.Abs(a[p] - b[p]);
                differences[i] = sum / Math.Ceiling(length / 4d) / 255d;
            }
            var bestIndex = -1; var bestScore = 0d;
            for (var i = 1; i < frames.Count - 1; i++)
            {
                var proximity = Math.Exp(-Math.Abs(frames[i].Timestamp.Ticks100ns - expected.Ticks100ns) / TimeSpan.FromMilliseconds(160).Ticks);
                var change = differences[i] + Math.Abs(differences[i] - differences[i + 1]) * .7;
                var score = change * (.45 + .55 * proximity);
                if (score > bestScore) { bestScore = score; bestIndex = i; }
            }
            if (bestIndex < 0 || bestScore < .012) return null;
            var confidence = Math.Clamp((bestScore - .01) / .16, 0.05, .94);
            var chosen = frames[bestIndex];
            return new VisualCandidate(chosen.Timestamp, chosen.TemporalIndex, confidence, bestScore, chosen);
        }, cancellationToken);
    }
}

public sealed class SessionHistoryService(int capacity = 3)
{
    private readonly int _capacity = capacity;
    private readonly LinkedList<SessionResult> _items = [];
    public IReadOnlyList<SessionResult> Items => _items.ToArray();
    public void Add(SessionResult item) { _items.AddFirst(item); while (_items.Count > _capacity) _items.RemoveLast(); }
}
