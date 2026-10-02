using Kairix.QuickAVSync.Capture;
using Kairix.QuickAVSync.Models;
using Kairix.QuickAVSync.Services;

namespace Kairix.QuickAVSync.Core.Tests;

public sealed class InputSignalTests
{
    [Fact]
    public async Task Coordinator_UsesExplicitPrecedence_AndIsolatesProviderFailure()
    {
        var device = new CaptureDeviceDescriptor("one", "Device", CaptureDeviceKind.ExternalCapture, "test");
        var observed = Signal(50, InputSignalProvenance.ObservedAnalysis, SignalAuthority.EstimatedHigh);
        var vendor = Signal(60, InputSignalProvenance.VendorApi, SignalAuthority.Authoritative);
        var standard = Signal(25, InputSignalProvenance.DeviceStandardProperty, SignalAuthority.Authoritative);
        var coordinator = new InputSignalCoordinator([new FakeProvider("broken", null, true), new FakeProvider("observed", observed), new FakeProvider("vendor", vendor), new FakeProvider("standard", standard)]);

        var result = await coordinator.QueryAsync(device, default);

        Assert.Same(standard, result);
    }

    [Fact]
    public async Task Coordinator_ReturnsNull_WhenEveryProviderIsUnavailable()
    {
        var coordinator = new InputSignalCoordinator([new FakeProvider("none", null), new FakeProvider("broken", null, true)]);
        Assert.Null(await coordinator.QueryAsync(new("one", "Device", CaptureDeviceKind.Unknown, "test"), default));
    }

    [Fact]
    public void Formatter_LabelsDeclarationsAndEstimatesHonestly()
    {
        var declared = new InputSignalInfo(1920, 1080, Rational.From(25), 50, 50, ScanMode.Interlaced,
            Provenance: InputSignalProvenance.UserDeclared, Authority: SignalAuthority.Authoritative);
        var estimated = Signal(50, InputSignalProvenance.ObservedAnalysis, SignalAuthority.EstimatedMedium);
        Assert.Contains("50i", InputSignalFormatter.Format(declared));
        Assert.Contains("User-declared", InputSignalFormatter.Format(declared));
        Assert.Contains("Scan unknown", InputSignalFormatter.Format(estimated));
        Assert.Contains("Estimated", InputSignalFormatter.Format(estimated));
    }

    [Theory]
    [InlineData(25, 1)]
    [InlineData(50, 1)]
    [InlineData(30000, 1001)]
    [InlineData(60000, 1001)]
    public void Matcher_RecommendsExactAuthoritativeTemporalRate(int numerator, int denominator)
    {
        var desired = Rational.From(numerator, denominator);
        var source = new InputSignalInfo(1920, 1080, desired, TemporalCadenceHz: desired.Value, ScanMode: ScanMode.Progressive,
            Provenance: InputSignalProvenance.UserDeclared, Authority: SignalAuthority.Authoritative);
        var wrong = new CaptureFormat(1920, 1080, Rational.From(30), ScanMode.Progressive, FieldOrder.Unknown);
        var exact = new CaptureFormat(1920, 1080, desired, ScanMode.Progressive, FieldOrder.Unknown);
        Assert.Same(exact, SourceAwareFormatMatcher.Recommend([wrong, exact], source));
    }

    [Fact]
    public void Matcher_DoesNotGuessOutputRateForInterlacedSourceWithUnknownHandling()
    {
        var source = new InputSignalInfo(1920, 1080, Rational.From(25), 50, 50, ScanMode.Interlaced,
            Provenance: InputSignalProvenance.UserDeclared, Authority: SignalAuthority.Authoritative);
        var p50 = new CaptureFormat(1920, 1080, Rational.From(50), ScanMode.Progressive, FieldOrder.Unknown);
        Assert.Null(SourceAwareFormatMatcher.Recommend([p50], source, InterlacedInputHandling.Unknown));
        Assert.False(SourceAwareFormatMatcher.CanAutomaticallyApply(source, InterlacedInputHandling.Unknown));
    }

    [Theory]
    [InlineData(InterlacedInputHandling.PreserveFields, 25)]
    [InlineData(InterlacedInputHandling.DeinterlaceToFrameRate, 25)]
    [InlineData(InterlacedInputHandling.DeinterlaceToFieldRate, 50)]
    public void Matcher_UsesExplicitInterlacedHandling(InterlacedInputHandling handling, int expectedRate)
    {
        var source = InputSignalOptions.Common.Single(option => option.Id == "1920x1080-50i").Signal!;
        var p25 = new CaptureFormat(1920, 1080, Rational.From(25), ScanMode.Progressive, FieldOrder.Unknown);
        var p50 = new CaptureFormat(1920, 1080, Rational.From(50), ScanMode.Progressive, FieldOrder.Unknown);
        Assert.Equal(expectedRate, SourceAwareFormatMatcher.Recommend([p50, p25], source, handling)!.FrameRate.Value);
    }

    [Theory]
    [InlineData(InterlacedInputHandling.PreserveFields, 30000, 1001)]
    [InlineData(InterlacedInputHandling.DeinterlaceToFieldRate, 60000, 1001)]
    public void Matcher_PreservesFractionalInterlacedCadence(InterlacedInputHandling handling, int numerator, int denominator)
    {
        var source = InputSignalOptions.Common.Single(option => option.Id == "1920x1080-59.94i").Signal!;
        var p2997 = new CaptureFormat(1920, 1080, Rational.From(30000, 1001), ScanMode.Progressive, FieldOrder.Unknown);
        var p5994 = new CaptureFormat(1920, 1080, Rational.From(60000, 1001), ScanMode.Progressive, FieldOrder.Unknown);
        Assert.Equal(Rational.From(numerator, denominator), SourceAwareFormatMatcher.Recommend([p5994, p2997], source, handling)!.FrameRate);
    }

    [Fact]
    public void Matcher_RecommendsObservedEstimateButMarksItUnsafeForAutomaticUse()
    {
        var format = new CaptureFormat(1920, 1080, Rational.From(50), ScanMode.Progressive, FieldOrder.Unknown);
        var observed = Signal(50, InputSignalProvenance.ObservedAnalysis, SignalAuthority.EstimatedHigh);
        Assert.Same(format, SourceAwareFormatMatcher.Recommend([format], observed));
        Assert.False(SourceAwareFormatMatcher.CanAutomaticallyApply(observed));
        var declared = new InputSignalInfo(1280, 720, Rational.From(25), TemporalCadenceHz: 25,
            Provenance: InputSignalProvenance.UserDeclared, Authority: SignalAuthority.Authoritative);
        Assert.Null(SourceAwareFormatMatcher.Recommend([format], declared));
    }

    [Theory]
    [InlineData(25, 25, 25)]
    [InlineData(50, 50, 50)]
    [InlineData(50, 25, 25)]
    [InlineData(60, 30, 30)]
    [InlineData(60, 50, 50)]
    [InlineData(59.94005994, 59.94005994, 59.94005994)]
    public void Analyzer_InfersTemporalCadence(double outputRate, double sourceRate, double expected)
    {
        var result = new ObservedSignalAnalyzer().Analyze(MovingFrames(outputRate, sourceRate, 6));
        Assert.NotNull(result.Signal.EffectiveTemporalRate);
        Assert.InRange(result.Signal.EffectiveTemporalRate!.Value, expected - .08, expected + .08);
    }

    [Theory]
    [InlineData(50, 25, "paired repeats")]
    [InlineData(60, 30, "paired repeats")]
    [InlineData(60, 50, "period 6")]
    [InlineData(60, 24, "period 5")]
    public void Analyzer_ReportsDeterministicFrameConversionPattern(double outputRate, double sourceRate, string expectedPattern)
    {
        var result = new ObservedSignalAnalyzer().Analyze(MovingFrames(outputRate, sourceRate, 6));
        Assert.StartsWith(expectedPattern, result.RepeatPattern);
    }

    [Fact]
    public void Analyzer_RejectsStaticSensorNoise()
    {
        var result = new ObservedSignalAnalyzer().Analyze(StaticNoiseFrames(50, 6));
        Assert.False(result.Signal.HasUsefulData);
        Assert.Equal(SignalAuthority.EstimatedLow, result.Signal.Authority);
        Assert.False(result.PairedRepeatDetected);
        Assert.False(result.SceneActivitySufficient);
    }

    [Fact]
    public void PassiveAndTimingAnalyzersShareNearIdenticalClassification()
    {
        var frames = Enumerable.Range(0, 8).Select(index => Frame(index, 25, 2, 2, [(byte)(100 + index), (byte)(100 + index), (byte)(100 + index), (byte)(100 + index)])).ToArray();
        var passive = new ObservedSignalAnalyzer().Analyze(frames);
        var timing = new FrameTimingAnalyzer().Analyze(frames, 25);
        Assert.Equal(timing.Content.NearIdenticalFraction, passive.NearIdenticalFraction, 6);
        Assert.Equal(0, passive.NearIdenticalFraction);
        Assert.False(passive.PairedRepeatDetected);
    }

    [Fact]
    public void Compatibility_WarnsButDoesNotRejectManualFieldRateCaptureForWeave()
    {
        var source = InputSignalOptions.Common.Single(option => option.Id == "1920x1080-50i").Signal!;
        var p50 = new CaptureFormat(1920, 1080, Rational.From(50), ScanMode.Progressive, FieldOrder.Unknown);
        var result = InterlaceCompatibility.Evaluate(source, InterlacedInputHandling.PreserveFields, p50);
        Assert.Contains("INTERLACE WARNING", result.Warning);
        Assert.Contains("repeated frame pairs", result.Warning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Compatibility_ReportsFortyMillisecondReviewWithoutInventingFields()
    {
        var source = InputSignalOptions.Common.Single(option => option.Id == "1920x1080-50i").Signal!;
        var p25 = new CaptureFormat(1920, 1080, Rational.From(25), ScanMode.Progressive, FieldOrder.Unknown);
        var result = InterlaceCompatibility.Evaluate(source, InterlacedInputHandling.PreserveFields, p25);
        Assert.Empty(result.Warning);
        Assert.Contains("40 ms", result.TemporalResolution);
        Assert.Contains("Field-level review not enabled", result.TemporalResolution);
    }

    [Fact]
    public void Compatibility_IgnoresInterlacedHandlingForProgressiveInput()
    {
        var source = InputSignalOptions.Common.Single(option => option.Id == "1920x1080-50p").Signal!;
        var p50 = new CaptureFormat(1920, 1080, Rational.From(50), ScanMode.Progressive, FieldOrder.Unknown);
        var result = InterlaceCompatibility.Evaluate(source, InterlacedInputHandling.PreserveFields, p50);
        Assert.False(result.Active);
        Assert.Empty(result.Warning);
        Assert.Empty(result.TemporalResolution);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(13)]
    public void Analyzer_HandlesSlowFastAndCameraLikeMovement(int pixelsPerSourceFrame)
    {
        var result = new ObservedSignalAnalyzer().Analyze(MovingFrames(50, 50, 5, pixelsPerSourceFrame));
        Assert.InRange(result.Signal.EffectiveTemporalRate!.Value, 49.9, 50.1);
    }

    [Fact]
    public void Analyzer_FindsStrongAlternatingRowEvidence()
    {
        var result = new ObservedSignalAnalyzer().Analyze(InterlacedEvidenceFrames(50, 6));
        Assert.Equal(ScanMode.Interlaced, result.Signal.ScanMode);
        Assert.True(result.InterlaceEvidence >= .58);
    }

    [Fact]
    public void Analyzer_DoesNotCallProgressiveDetailInterlaced()
    {
        var result = new ObservedSignalAnalyzer().Analyze(MovingFrames(50, 50, 6, 7, highDetail: true));
        Assert.Equal(ScanMode.Unknown, result.Signal.ScanMode);
        Assert.True(result.InterlaceEvidence < .58);
    }

    [Fact]
    public void Analyzer_LeavesDeinterlacedFiftyScanUnknown()
    {
        var result = new ObservedSignalAnalyzer().Analyze(MovingFrames(50, 50, 6));
        Assert.InRange(result.Signal.EffectiveTemporalRate!.Value, 49.9, 50.1);
        Assert.Equal(ScanMode.Unknown, result.Signal.ScanMode);
    }

    [Fact]
    public void Analyzer_DescribesAllFreshFramesWithoutInventingAPeriod()
    {
        var result = new ObservedSignalAnalyzer().Analyze(MovingFrames(25, 25, 6));
        Assert.Equal("100% temporally unique", result.RepeatPattern);
        Assert.False(result.PairedRepeatDetected);
    }

    private static InputSignalInfo Signal(double cadence, InputSignalProvenance provenance, SignalAuthority authority) =>
        new(TemporalCadenceHz: cadence, Provenance: provenance, Authority: authority, ProviderName: provenance.ToString());

    private static IReadOnlyList<VideoFrame> MovingFrames(double outputRate, double sourceRate, int seconds, int speed = 3, bool highDetail = false)
    {
        const int width = 64, height = 48;
        var count = (int)Math.Round(outputRate * seconds);
        var frames = new List<VideoFrame>(count);
        for (var i = 0; i < count; i++)
        {
            var sourceIndex = (int)Math.Floor(i * sourceRate / outputRate + 1e-6);
            var luma = new byte[width * height];
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var detail = highDetail ? ((x + y) % 2) * 90 : 0;
                var moving = (x + sourceIndex * speed) % width < 20 ? 110 : 12;
                luma[y * width + x] = (byte)Math.Min(255, 20 + detail + moving);
            }
            frames.Add(Frame(i, outputRate, width, height, luma));
        }
        return frames;
    }

    private static IReadOnlyList<VideoFrame> StaticNoiseFrames(double rate, int seconds)
    {
        const int width = 64, height = 48;
        var frames = new List<VideoFrame>();
        for (var i = 0; i < rate * seconds; i++)
        {
            var luma = Enumerable.Range(0, width * height).Select(pixel => (byte)(100 + ((pixel * 17 + i * 13) % 3 - 1))).ToArray();
            frames.Add(Frame(i, rate, width, height, luma));
        }
        return frames;
    }

    private static IReadOnlyList<VideoFrame> InterlacedEvidenceFrames(double rate, int seconds)
    {
        const int width = 64, height = 48;
        var frames = new List<VideoFrame>();
        var current = new byte[width * height];
        for (var i = 0; i < rate * seconds; i++)
        {
            for (var y = i % 2; y < height; y += 2)
            for (var x = 0; x < width; x++) current[y * width + x] = (byte)((x + i * 11) % 220 + 20);
            frames.Add(Frame(i, rate, width, height, current.ToArray()));
        }
        return frames;
    }

    private static VideoFrame Frame(int index, double rate, int width, int height, byte[] luma) =>
        new(new((long)Math.Round(index / rate * 10_000_000), TimingQuality.StreamTimestamp), width, height, luma, index);

    private sealed class FakeProvider(string name, InputSignalInfo? value, bool throws = false) : IInputSignalProvider
    {
        public string Name => name;
        public bool CanHandle(CaptureDeviceDescriptor device) => true;
        public Task<InputSignalInfo?> GetSignalAsync(CaptureDeviceDescriptor device, CancellationToken cancellationToken) =>
            throws ? Task.FromException<InputSignalInfo?>(new InvalidOperationException("unavailable")) : Task.FromResult(value);
    }
}
