using System.Globalization;
using Kairix.QuickAVSync.Models;

namespace Kairix.QuickAVSync.Services;

public sealed record CaptureStatusReadout(string DriverCapture, string ObservedDelivery, string SourceCadence);

public static class CaptureStatusPresenter
{
    public static CaptureStatusReadout Describe(
        CaptureFormat? negotiated,
        double? observedDeliveryRate,
        ObservedSignalAnalysis? analysis,
        InputSignalInfo? signal = null)
    {
        var driver = negotiated is null ? "Not negotiated" : CaptureFormatFormatter.Format(negotiated);
        var observed = observedDeliveryRate is > 0 ? $"{observedDeliveryRate:0.###} fps" : "Measuring…";

        var authoritative = signal is { Authority: SignalAuthority.Authoritative, EffectiveTemporalRate: > 0 };
        if (authoritative)
        {
            var provider = string.IsNullOrWhiteSpace(signal!.ProviderName) ? "hardware metadata" : signal.ProviderName;
            return new(driver, observed, $"{signal.EffectiveTemporalRate:0.###} fps · authoritative · {provider}");
        }

        var inferred = analysis is { SceneActivitySufficient: true }
            && analysis.Signal.Authority is SignalAuthority.EstimatedMedium or SignalAuthority.EstimatedHigh
            && analysis.Signal.EffectiveTemporalRate is > 0;
        if (!inferred) return new(driver, observed, "Unknown");

        var rate = analysis!.EstimatedUniqueImageRate is > 0 ? analysis.EstimatedUniqueImageRate : analysis.Signal.EffectiveTemporalRate;
        var repeat = analysis.PairedRepeatDetected ? " · repeated frames" : "";
        return new(driver, observed, $"≈{rate:0.###} fps{repeat} · estimated");
    }
}

public sealed record AudioLevelReading(
    bool HasSamples,
    double LeftDbfs,
    double RightDbfs,
    int SampleRate,
    int Channels,
    int AggregatedChunks);

public readonly record struct HistoryTrendPoint(int Position, int TotalPositions, int NewestFirstIndex, double Milliseconds, bool IsNewest);

public static class HistoryTrendSeries
{
    public const int MaximumPoints = 20;

    public static IReadOnlyList<HistoryTrendPoint> Build(IEnumerable<SessionResult> newestFirst)
    {
        var retained = newestFirst.Take(MaximumPoints).Reverse().ToArray();
        var points = new List<HistoryTrendPoint>(retained.Length);
        for (var position = 0; position < retained.Length; position++)
        {
            var result = retained[position].Result;
            if (!result.TimingComparable || !double.IsFinite(result.SignedMilliseconds)) continue;
            points.Add(new(position, retained.Length, retained.Length - 1 - position, result.SignedMilliseconds, position == retained.Length - 1));
        }
        return points;
    }

    public static string FormatTooltip(double milliseconds) =>
        milliseconds.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + " ms";

    public static double NiceRange(IEnumerable<double> milliseconds)
    {
        var maximum = milliseconds.Where(double.IsFinite).Select(Math.Abs).DefaultIfEmpty().Max();
        if (maximum <= 10) return 10;

        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(maximum)));
        var normalized = maximum / magnitude;
        var step = normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 5 ? 5 : 10;
        return step * magnitude;
    }
}

public sealed class AudioLevelAccumulator
{
    public const double FloorDbfs = -60;
    private readonly object _gate = new();
    private double _leftPeak;
    private double _rightPeak;
    private int _sampleRate;
    private int _channels;
    private int _chunks;

    public void Add(AudioChunk chunk)
    {
        if (chunk.Channels <= 0 || chunk.SampleRate <= 0 || chunk.Samples.Length < chunk.Channels) return;
        double left = 0, right = 0;
        for (var index = 0; index + chunk.Channels <= chunk.Samples.Length; index += chunk.Channels)
        {
            var leftSample = chunk.Samples[index];
            var rightSample = chunk.Samples[index + Math.Min(1, chunk.Channels - 1)];
            if (float.IsFinite(leftSample)) left = Math.Max(left, Math.Abs(leftSample));
            if (float.IsFinite(rightSample)) right = Math.Max(right, Math.Abs(rightSample));
        }

        lock (_gate)
        {
            _leftPeak = Math.Max(_leftPeak, left);
            _rightPeak = Math.Max(_rightPeak, right);
            _sampleRate = chunk.SampleRate;
            _channels = chunk.Channels;
            _chunks++;
        }
    }

    public AudioLevelReading ReadAndReset()
    {
        lock (_gate)
        {
            var reading = new AudioLevelReading(_chunks > 0, ToDbfs(_leftPeak), ToDbfs(_rightPeak), _sampleRate, _channels, _chunks);
            _leftPeak = 0;
            _rightPeak = 0;
            _chunks = 0;
            return reading;
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _leftPeak = 0;
            _rightPeak = 0;
            _sampleRate = 0;
            _channels = 0;
            _chunks = 0;
        }
    }

    public static double ToDbfs(double linear) => linear <= 0 ? FloorDbfs : Math.Max(FloorDbfs, 20 * Math.Log10(Math.Min(1, linear)));
    public static double Normalize(double dbfs) => Math.Clamp((dbfs - FloorDbfs) / -FloorDbfs, 0, 1);
}
