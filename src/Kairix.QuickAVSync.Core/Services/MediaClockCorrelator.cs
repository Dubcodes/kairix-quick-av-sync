using Kairix.QuickAVSync.Models;

namespace Kairix.QuickAVSync.Services;

public sealed record ClockObservation(string StreamId, string SourceDomain, long SourceTicks100ns, long ReferenceTicks100ns, TimingQuality Quality);
public sealed record ClockNormalization(MediaTimestamp Timestamp, bool Discontinuity, string Diagnostic);

public sealed class MediaClockCorrelator(long discontinuityThresholdTicks = 2_500_000)
{
    private sealed record Mapping(long SourceOrigin, long ReferenceOrigin, long LastSource, long LastNormalized, TimingQuality Quality);
    private readonly object _gate = new();
    private readonly Dictionary<string, Mapping> _mappings = new(StringComparer.Ordinal);

    public ClockNormalization Normalize(ClockObservation observation)
    {
        lock (_gate)
        {
            var key = $"{observation.StreamId}\0{observation.SourceDomain}";
            if (!_mappings.TryGetValue(key, out var map))
            {
                map = new(observation.SourceTicks100ns, observation.ReferenceTicks100ns, observation.SourceTicks100ns, observation.ReferenceTicks100ns, observation.Quality);
                _mappings[key] = map;
                return new(new(observation.ReferenceTicks100ns, observation.Quality, "common-monotonic", observation.SourceTicks100ns), false, "Clock mapping established");
            }
            var normalized = map.ReferenceOrigin + observation.SourceTicks100ns - map.SourceOrigin;
            var backwards = observation.SourceTicks100ns < map.LastSource;
            var jump = Math.Abs(normalized - map.LastNormalized) > discontinuityThresholdTicks && Math.Abs(observation.ReferenceTicks100ns - normalized) > discontinuityThresholdTicks;
            if (backwards || jump)
            {
                _mappings[key] = new(observation.SourceTicks100ns, observation.ReferenceTicks100ns, observation.SourceTicks100ns, observation.ReferenceTicks100ns, observation.Quality);
                return new(new(observation.ReferenceTicks100ns, TimingQuality.ArrivalFallback, "common-monotonic", observation.SourceTicks100ns), true, backwards ? "Source clock moved backwards; mapping reset" : "Clock discontinuity; mapping reset");
            }
            _mappings[key] = map with { LastSource = observation.SourceTicks100ns, LastNormalized = normalized, Quality = observation.Quality };
            return new(new(normalized, observation.Quality, "common-monotonic", observation.SourceTicks100ns), false, "Clock mapping stable");
        }
    }

    public static TimingQuality CombinedQuality(MediaTimestamp audio, MediaTimestamp video) =>
        !audio.IsComparableTo(video) ? TimingQuality.Unrelated : (TimingQuality)Math.Min((int)audio.Quality, (int)video.Quality);
}
