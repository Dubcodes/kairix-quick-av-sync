using Kairix.QuickAVSync.Capture;
using Kairix.QuickAVSync.Models;

namespace Kairix.QuickAVSync.Services;

public sealed class InputSignalCoordinator(IEnumerable<IInputSignalProvider> providers, IDiagnosticSink? diagnostics = null)
{
    private readonly IInputSignalProvider[] _providers = providers.ToArray();
    private readonly IDiagnosticSink _diagnostics = diagnostics ?? NullDiagnosticSink.Instance;

    public async Task<InputSignalInfo?> QueryAsync(CaptureDeviceDescriptor device, CancellationToken cancellationToken)
    {
        var results = new List<InputSignalInfo>();
        foreach (var provider in _providers.Where(candidate => candidate.CanHandle(device)))
        {
            try
            {
                var result = await provider.GetSignalAsync(device, cancellationToken).ConfigureAwait(false);
                if (result?.HasUsefulData == true) results.Add(result);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _diagnostics.Write("input-signal", $"Provider '{provider.Name}' unavailable: {ex.GetType().Name}: {ex.Message}");
            }
        }

        return SelectStrongest(results);
    }

    public static InputSignalInfo? SelectStrongest(IEnumerable<InputSignalInfo?> candidates) => candidates
        .Where(candidate => candidate?.HasUsefulData == true)
        .Select(candidate => candidate!)
        .OrderByDescending(Precedence)
        .ThenByDescending(candidate => candidate.LockStatus == SignalLockStatus.Locked)
        .FirstOrDefault();

    private static int Precedence(InputSignalInfo signal) => signal switch
    {
        { Authority: SignalAuthority.Authoritative, Provenance: InputSignalProvenance.OperatingSystem or InputSignalProvenance.DeviceStandardProperty } => 600,
        { Authority: SignalAuthority.Authoritative, Provenance: InputSignalProvenance.VendorApi } => 550,
        { Provenance: InputSignalProvenance.UserDeclared } => 500,
        { Authority: SignalAuthority.Authoritative } => 450,
        { Authority: SignalAuthority.EstimatedHigh } => 300,
        { Authority: SignalAuthority.EstimatedMedium } => 200,
        { Authority: SignalAuthority.EstimatedLow } => 100,
        _ => 0
    };
}

public static class InputSignalOptions
{
    public static IReadOnlyList<InputSignalOption> Common { get; } = Build();

    public static InputSignalOption Find(string? id) => Common.FirstOrDefault(option => string.Equals(option.Id, id, StringComparison.Ordinal)) ?? InputSignalOption.Auto;

    private static IReadOnlyList<InputSignalOption> Build()
    {
        var options = new List<InputSignalOption> { InputSignalOption.Auto };
        Add(options, 1920, 1080, 24000, 1001, ScanMode.Progressive);
        Add(options, 1920, 1080, 24, 1, ScanMode.Progressive);
        Add(options, 1920, 1080, 25, 1, ScanMode.Progressive);
        Add(options, 1920, 1080, 25, 1, ScanMode.Interlaced, 50);
        Add(options, 1920, 1080, 50, 1, ScanMode.Progressive);
        Add(options, 1920, 1080, 30000, 1001, ScanMode.Progressive);
        Add(options, 1920, 1080, 30, 1, ScanMode.Progressive);
        Add(options, 1920, 1080, 30000, 1001, ScanMode.Interlaced, 60000d / 1001);
        Add(options, 1920, 1080, 60000, 1001, ScanMode.Progressive);
        Add(options, 1920, 1080, 60, 1, ScanMode.Progressive);
        Add(options, 1280, 720, 24000, 1001, ScanMode.Progressive);
        Add(options, 1280, 720, 24, 1, ScanMode.Progressive);
        Add(options, 1280, 720, 25, 1, ScanMode.Progressive);
        Add(options, 1280, 720, 30000, 1001, ScanMode.Progressive);
        Add(options, 1280, 720, 30, 1, ScanMode.Progressive);
        Add(options, 1280, 720, 50, 1, ScanMode.Progressive);
        Add(options, 1280, 720, 60000, 1001, ScanMode.Progressive);
        Add(options, 1280, 720, 60, 1, ScanMode.Progressive);
        Add(options, 720, 576, 25, 1, ScanMode.Interlaced, 50);
        Add(options, 720, 480, 30000, 1001, ScanMode.Interlaced, 60000d / 1001);
        return options;
    }

    private static void Add(List<InputSignalOption> options, int width, int height, int numerator, int denominator, ScanMode scanMode, double? fieldRate = null)
    {
        var rate = Rational.From(numerator, denominator);
        var cadence = scanMode == ScanMode.Interlaced ? fieldRate ?? rate.Value * 2 : rate.Value;
        var signal = new InputSignalInfo(width, height, rate, fieldRate, cadence, scanMode, FieldOrder.Unknown,
            SignalLockStatus.Unknown, InputSignalProvenance.UserDeclared, SignalAuthority.Authoritative, "User", null);
        var scan = scanMode == ScanMode.Interlaced ? $"{RateLabel(cadence)}i" : $"{RateLabel(rate.Value)}p";
        options.Add(new($"{width}x{height}-{scan}", $"{width}×{height} · {scan}", signal));
    }

    private static string RateLabel(double rate)
    {
        if (Math.Abs(rate - 24000d / 1001) < .01) return "23.976";
        if (Math.Abs(rate - 30000d / 1001) < .01) return "29.97";
        if (Math.Abs(rate - 60000d / 1001) < .01) return "59.94";
        return rate.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    }
}

public static class InputSignalFormatter
{
    public static string Format(InputSignalInfo? signal)
    {
        if (signal?.HasUsefulData != true) return "Input: Unknown";
        var format = FormatCore(signal);
        var source = signal.Provenance switch
        {
            InputSignalProvenance.UserDeclared => "User-declared",
            InputSignalProvenance.ObservedAnalysis => "Estimated",
            InputSignalProvenance.VendorApi => "Device-reported",
            InputSignalProvenance.DeviceStandardProperty or InputSignalProvenance.OperatingSystem => "System-reported",
            _ => "Source unknown"
        };
        return $"Input: {format} · {source}";
    }

    public static string Detail(InputSignalInfo? signal)
    {
        if (signal?.HasUsefulData != true) return "No physical input signal information is available.";
        var provider = string.IsNullOrWhiteSpace(signal.ProviderName) ? "Unknown provider" : signal.ProviderName;
        return $"{provider}; provenance {signal.Provenance}; authority {signal.Authority}; lock {signal.LockStatus}. Capture output is reported separately.";
    }

    private static string FormatCore(InputSignalInfo signal)
    {
        var size = signal.Width is > 0 && signal.Height is > 0 ? $"{signal.Width}×{signal.Height} · " : "";
        if (signal.ScanMode == ScanMode.Interlaced && signal.EffectiveTemporalRate is { } fieldRate)
            return $"{size}{fieldRate:0.##}i";
        if (signal.ScanMode == ScanMode.Progressive && signal.EffectiveTemporalRate is { } progressiveRate)
            return $"{size}{progressiveRate:0.##}p";
        if (signal.EffectiveTemporalRate is { } cadence)
            return $"{size}≈{cadence:0.##} Hz source cadence · Scan unknown";
        return $"{size}Scan {signal.ScanMode.ToString().ToLowerInvariant()}".TrimEnd(' ', '·');
    }
}

public static class SourceAwareFormatMatcher
{
    public static bool CanAutomaticallyApply(InputSignalInfo? source) => source?.HasUsefulData == true &&
        (source.Provenance == InputSignalProvenance.UserDeclared || source.Authority == SignalAuthority.Authoritative);

    public static CaptureFormat? Recommend(IEnumerable<CaptureFormat> candidates, InputSignalInfo? source)
    {
        if (source?.HasUsefulData != true || source.EffectiveTemporalRate is not { } temporalRate) return null;

        var matches = candidates.Where(candidate => source.Width is not { } width || candidate.Width == width)
            .Where(candidate => source.Height is not { } height || candidate.Height == height)
            .Where(candidate => NearlyEqual(OutputTemporalRate(candidate), temporalRate))
            .OrderByDescending(candidate => candidate.ScanMode == ScanMode.Progressive)
            .ThenByDescending(candidate => candidate.Width <= 1920 && candidate.Height <= 1080)
            .ThenByDescending(candidate => candidate.Width * candidate.Height)
            .ThenBy(candidate => Math.Abs(OutputTemporalRate(candidate) - temporalRate))
            .ToArray();
        return matches.FirstOrDefault();
    }

    private static double OutputTemporalRate(CaptureFormat format) =>
        format.ScanMode == ScanMode.Interlaced && format.InterlaceLayout == InterlaceLayout.FullFrame
            ? format.FrameRate.Value * 2 : format.FrameRate.Value;

    private static bool NearlyEqual(double left, double right) => Math.Abs(left - right) <= Math.Max(0.025, right * 0.0015);
}

public sealed class ObservedSignalAnalyzer
{
    private static readonly double[] StandardRates = [24000d / 1001, 24, 25, 30000d / 1001, 30, 50, 60000d / 1001, 60];

    public ObservedSignalAnalysis Analyze(IReadOnlyList<VideoFrame> input)
    {
        var frames = input.OrderBy(frame => frame.Timestamp.Ticks100ns).ToArray();
        if (frames.Length < 8) return Unknown(frames.Length);
        var intervals = frames.Zip(frames.Skip(1), (left, right) => (right.Timestamp.Ticks100ns - left.Timestamp.Ticks100ns) / 10_000d)
            .Where(value => value > 0 && value < 1000).ToArray();
        if (intervals.Length < 6) return Unknown(frames.Length);

        var medianInterval = Median(intervals);
        var observedRate = 1000d / medianInterval;
        var jitter = Median(intervals.Select(value => Math.Abs(value - medianInterval)).ToArray()) / medianInterval * 100;
        var differences = new double[frames.Length - 1];
        var parity = new double[frames.Length - 1];
        for (var i = 1; i < frames.Length; i++)
            (differences[i - 1], parity[i - 1]) = Difference(frames[i - 1], frames[i]);

        var sorted = differences.Order().ToArray();
        var q20 = sorted[(int)Math.Floor((sorted.Length - 1) * .2)];
        // A low quantile near zero is the duplicate cluster; with no such cluster,
        // keep the threshold below ordinary slow motion instead of following it up.
        var duplicateThreshold = Math.Clamp(q20 * .5, .006, .012);
        var fresh = differences.Select(value => value > duplicateThreshold).ToArray();
        var moving = differences.Count(value => value > duplicateThreshold);
        var duplicateFraction = 1d - moving / (double)differences.Length;
        var activity = Median(differences.Where(value => value > duplicateThreshold).ToArray());
        var duration = (frames[^1].Timestamp.Ticks100ns - frames[0].Timestamp.Ticks100ns) / 10_000_000d;

        if (moving < Math.Max(4, differences.Length / 20) || activity < .0025)
            return Unknown(frames.Length, observedRate, medianInterval, jitter, duplicateFraction, duration);

        var rawCadence = observedRate * moving / differences.Length;
        var cadence = Snap(rawCadence);
        var repeat = DescribePattern(fresh);
        var interlaceEvidence = InterlaceEvidence(parity, fresh);
        var scan = interlaceEvidence >= .58 ? ScanMode.Interlaced : ScanMode.Unknown;
        var authority = duration >= 4 && jitter < 8 && cadence.HasValue ? SignalAuthority.EstimatedHigh
            : duration >= 2 && cadence.HasValue ? SignalAuthority.EstimatedMedium : SignalAuthority.EstimatedLow;
        var signal = new InputSignalInfo(TemporalCadenceHz: cadence ?? rawCadence, ScanMode: scan,
            Provenance: InputSignalProvenance.ObservedAnalysis, Authority: authority, ProviderName: "Passive frame analysis");
        return new(signal, observedRate, medianInterval, jitter, duplicateFraction, repeat, interlaceEvidence, frames.Length, duration);
    }

    private static ObservedSignalAnalysis Unknown(int count, double rate = 0, double interval = 0, double jitter = 0, double duplicate = 0, double duration = 0) =>
        new(InputSignalInfo.Unknown with { Provenance = InputSignalProvenance.ObservedAnalysis, Authority = SignalAuthority.EstimatedLow, ProviderName = "Passive frame analysis" },
            rate, interval, jitter, duplicate, "insufficient scene activity", 0, count, duration);

    private static (double Difference, double Parity) Difference(VideoFrame left, VideoFrame right)
    {
        var width = Math.Min(left.Width, right.Width);
        var height = Math.Min(left.Height, right.Height);
        if (width <= 0 || height <= 0) return (0, 0);
        var stepX = Math.Max(1, width / 160);
        var stepY = Math.Max(1, height / 90);
        double even = 0, odd = 0;
        var evenCount = 0;
        var oddCount = 0;
        for (var y = 0; y < height; y += stepY)
        {
            for (var x = 0; x < width; x += stepX)
            {
                var a = y * left.EffectiveStride + x;
                var b = y * right.EffectiveStride + x;
                if ((uint)a >= left.Luma.Length || (uint)b >= right.Luma.Length) continue;
                var delta = Math.Abs(left.Luma[a] - right.Luma[b]) / 255d;
                if ((y & 1) == 0) { even += delta; evenCount++; }
                else { odd += delta; oddCount++; }
            }
        }
        even /= Math.Max(1, evenCount);
        odd /= Math.Max(1, oddCount);
        return ((even + odd) / 2, (even - odd) / (even + odd + .0001));
    }

    private static double InterlaceEvidence(IReadOnlyList<double> parity, IReadOnlyList<bool> fresh)
    {
        var relevant = parity.Where((_, index) => fresh[index]).ToArray();
        if (relevant.Length < 6) return 0;
        var magnitude = Median(relevant.Select(Math.Abs).ToArray());
        var alternating = relevant.Zip(relevant.Skip(1), (a, b) => Math.Sign(a) != Math.Sign(b)).Count(value => value) / (double)(relevant.Length - 1);
        return Math.Clamp(magnitude * alternating, 0, 1);
    }

    private static string DescribePattern(IReadOnlyList<bool> fresh)
    {
        for (var period = 2; period <= Math.Min(12, fresh.Count / 3); period++)
        {
            var mismatches = 0;
            for (var i = period; i < fresh.Count; i++) if (fresh[i] != fresh[i % period]) mismatches++;
            if (mismatches <= Math.Max(1, (fresh.Count - period) / 20))
                return $"period {period}: {fresh.Take(period).Count(value => value)} fresh / {period}";
        }
        return $"{fresh.Count(value => value) * 100d / fresh.Count:0.#}% temporally unique";
    }

    private static double? Snap(double value)
    {
        var nearest = StandardRates.MinBy(rate => Math.Abs(rate - value));
        return Math.Abs(nearest - value) / nearest <= .04 ? nearest : null;
    }

    private static double Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0) return 0;
        var sorted = values.Order().ToArray();
        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 0 ? (sorted[middle - 1] + sorted[middle]) / 2 : sorted[middle];
    }
}
