using Kairix.QuickAVSync.Models;
using Kairix.QuickAVSync.Services;
using Kairix.QuickAVSync.Windows.Infrastructure;
using Kairix.QuickAVSync.Windows.Capture;
using System.Reflection;

namespace Kairix.QuickAVSync.Windows.Tests;

public sealed class SettingsTests
{
    [Fact] public void RoundTripsOnlyAllowedConfiguration()
    {
        var path = Path.Combine(Path.GetTempPath(), $"kairix-{Guid.NewGuid():N}.json");
        try { using var service = new SettingsService(path); service.Save(new() { LastDeviceId = "device", AutoDetect = false, VisualSensitivity = 77, RollingBufferSeconds = 12, DetectionWidth = 960, DetectionHeight = 540, ReviewWidth = 480, ReviewHeight = 270, Theme = "Solar Flare", AudioDisplayStyle = "Line", SettingsPanelExpanded = false, NativeFormatByDevice = new() { ["device"] = "1920x1080|30000/1001|p|Nv12" }, ReconstructFieldsByDevice = new() { ["device"] = true }, ReconstructionFieldOrderByDevice = new() { ["device"] = "bottom" }, VideoTimingOffsetOverridesMilliseconds = new() { ["profile"] = 18.5 } }); var loaded = service.Load(); Assert.Equal("device", loaded.LastDeviceId); Assert.False(loaded.AutoDetect); Assert.Equal(77, loaded.VisualSensitivity); Assert.Equal(12, loaded.RollingBufferSeconds); Assert.Equal((960, 540), (loaded.DetectionWidth, loaded.DetectionHeight)); Assert.Equal((480, 270), (loaded.ReviewWidth, loaded.ReviewHeight)); Assert.Equal("Solar Flare", loaded.Theme); Assert.Equal("Line", loaded.AudioDisplayStyle); Assert.False(loaded.SettingsPanelExpanded); Assert.Equal("1920x1080|30000/1001|p|Nv12", loaded.NativeFormatByDevice["device"]); Assert.True(loaded.ReconstructFieldsByDevice["device"]); Assert.Equal("bottom", loaded.ReconstructionFieldOrderByDevice["device"]); Assert.Equal(18.5, loaded.VideoTimingOffsetOverridesMilliseconds["profile"]); var json = File.ReadAllText(path); Assert.DoesNotContain("waveform", json, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("history", json, StringComparison.OrdinalIgnoreCase); }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact] public void DebouncedSaveWritesOnlyTheLatestPendingSnapshotWhenFlushed()
    {
        var path = Path.Combine(Path.GetTempPath(), $"kairix-{Guid.NewGuid():N}.json");
        try { using var service = new SettingsService(path); service.ScheduleSave(new() { VisualSensitivity = 20 }, TimeSpan.FromSeconds(30)); service.ScheduleSave(new() { VisualSensitivity = 81 }, TimeSpan.FromSeconds(30)); Assert.False(File.Exists(path)); service.FlushPending(); Assert.Equal(81, service.Load().VisualSensitivity); }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact] public void CorruptOrStaleProcessingSettingsFallBackSafely()
    {
        var path = Path.Combine(Path.GetTempPath(), $"kairix-{Guid.NewGuid():N}.json");
        try { File.WriteAllText(path, "{ definitely-not-json"); using (var corrupt = new SettingsService(path)) Assert.Equal(640, corrupt.Load().DetectionWidth); File.WriteAllText(path, "{\"DetectionWidth\":-1,\"DetectionHeight\":0,\"ReviewWidth\":99999,\"ReviewHeight\":0,\"Theme\":\"Missing\"}"); using var stale = new SettingsService(path); var loaded = stale.Load(); Assert.Equal((640, 360), (loaded.DetectionWidth, loaded.DetectionHeight)); Assert.Equal((160, 90), (loaded.ReviewWidth, loaded.ReviewHeight)); Assert.Equal("Graphite", loaded.Theme); }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
    [Fact]
    public void InvalidTimingOverridesAreDiscarded()
    {
        var path = Path.Combine(Path.GetTempPath(), $"kairix-{Guid.NewGuid():N}.json");
        try { File.WriteAllText(path, "{\"VideoTimingOffsetOverridesMilliseconds\":{\"valid\":18.5,\"huge\":6000}}"); using var service = new SettingsService(path); var loaded = service.Load(); Assert.Equal(18.5, loaded.VideoTimingOffsetOverridesMilliseconds["valid"]); Assert.False(loaded.VideoTimingOffsetOverridesMilliseconds.ContainsKey("huge")); }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}

public sealed class WindowsEnumerationTests
{
    [Fact] public void AudioEndpointEnumerationIsDeterministicAndUnique()
    {
        var endpoints = new WindowsAudioEndpointService().Enumerate();
        Assert.Equal(endpoints.Count, endpoints.Select(x => x.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
    [Fact] public async Task MediaFoundationEnumerationReturnsUniqueDescriptors()
    {
        var devices = await new WindowsCaptureBackend().EnumerateDevicesAsync(default);
        Assert.Equal(devices.Count, devices.Select(x => x.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
    [Fact] public async Task GenericProviderDoesNotMislabelCaptureOutputAsPhysicalInput()
    {
        var provider = new GenericWindowsInputSignalProvider();
        var device = new CaptureDeviceDescriptor("one", "Capture", CaptureDeviceKind.ExternalCapture, "windows-mf");
        Assert.True(provider.CanHandle(device));
        Assert.Null(await provider.GetSignalAsync(device, default));
    }
}

public sealed class NativeFieldFrameConverterTests
{
    [Theory]
    [InlineData(VideoPixelFormat.Nv12)] [InlineData(VideoPixelFormat.Yuy2)] [InlineData(VideoPixelFormat.Uyvy)] [InlineData(VideoPixelFormat.Bgr24)] [InlineData(VideoPixelFormat.Bgra32)]
    public void FastLumaIsByteExactForProgressiveTopAndBottom(VideoPixelFormat format)
    {
        var (bytes, layout) = KnownRows(format, 10, 20, 30, 40);
        foreach (var kind in new[] { TemporalImageKind.ProgressiveFrame, TemporalImageKind.TopField, TemporalImageKind.BottomField })
        {
            var converted = NativeVideoFrameConverter.Convert(bytes, layout, 4, 4, 2, 2, kind);
            var expectedRows = kind switch { TemporalImageKind.TopField => new byte[] { 10, 10, 30, 30 }, TemporalImageKind.BottomField => [20, 20, 40, 40], _ => [10, 20, 30, 40] };
            Assert.Equal(expectedRows.SelectMany(value => Enumerable.Repeat(value, 4)), converted.Luma);
        }
    }

    [Theory] [InlineData(320, 180)] [InlineData(640, 360)] [InlineData(960, 540)]
    public void ReusablePlanPropagatesConfiguredAnalysisDimensions(int width, int height)
    {
        var source = new byte[960 * 540 * 2]; for (var index = 0; index < source.Length; index += 2) source[index] = 90;
        var plan = NativeVideoFrameConverter.CreatePlan(new(960, 540, 1920, VideoPixelFormat.Yuy2), width, height, 160, 90);
        var first = NativeVideoFrameConverter.Convert(source, plan); var second = NativeVideoFrameConverter.Convert(source, plan);
        Assert.Equal(width * height, first.Luma.Length); Assert.Equal(first.Luma, second.Luma); Assert.Equal(160 * 90 * 4, first.Bgra.Length);
    }
    [Fact]
    public void Yuy2WovenRowsBecomeDistinctBobbedTopAndBottomImages()
    {
        var bytes = Yuy2Rows(10, 20, 30, 40);
        var layout = new NativeFrameLayout(4, 4, 8, VideoPixelFormat.Yuy2);
        var top = NativeVideoFrameConverter.Convert(bytes, layout, 2, 4, 2, 4, TemporalImageKind.TopField);
        var bottom = NativeVideoFrameConverter.Convert(bytes, layout, 2, 4, 2, 4, TemporalImageKind.BottomField);
        Assert.Equal([10, 10, 10, 10, 30, 30, 30, 30], top.Luma.Select(value => (int)value));
        Assert.Equal([20, 20, 20, 20, 40, 40, 40, 40], bottom.Luma.Select(value => (int)value));
        Assert.NotEqual(top.Bgra, bottom.Bgra);
    }

    [Fact]
    public void BgraWovenRowsUseTheSameParityAwareBobAbstraction()
    {
        var bytes = Enumerable.Range(0, 4).SelectMany(row => Enumerable.Range(0, 2).SelectMany(_ => new byte[] { (byte)(10 + row * 30), (byte)(10 + row * 30), (byte)(10 + row * 30), 255 })).ToArray();
        var layout = new NativeFrameLayout(2, 4, 8, VideoPixelFormat.Bgra32);
        var top = NativeVideoFrameConverter.Convert(bytes, layout, 2, 4, 2, 4, TemporalImageKind.TopField);
        var bottom = NativeVideoFrameConverter.Convert(bytes, layout, 2, 4, 2, 4, TemporalImageKind.BottomField);
        Assert.Equal([10, 10, 10, 10, 70, 70, 70, 70], top.Luma.Select(value => (int)value));
        Assert.Equal([40, 40, 40, 40, 100, 100, 100, 100], bottom.Luma.Select(value => (int)value));
    }

    [Fact]
    public void ProgressiveConversionRemainsOneWholeFrameImage()
    {
        var bytes = Yuy2Rows(10, 20, 30, 40);
        var converted = NativeVideoFrameConverter.Convert(bytes, new(4, 4, 8, VideoPixelFormat.Yuy2), 2, 4, 2, 4);
        Assert.Equal([10, 10, 20, 20, 30, 30, 40, 40], converted.Luma.Select(value => (int)value));
    }

    private static byte[] Yuy2Rows(params byte[] rows) => rows.SelectMany(y => new byte[] { y, 128, y, 128, y, 128, y, 128 }).ToArray();
    private static (byte[] Bytes, NativeFrameLayout Layout) KnownRows(VideoPixelFormat format, params byte[] rows)
    {
        return format switch
        {
            VideoPixelFormat.Nv12 => ([.. rows.SelectMany(value => Enumerable.Repeat(value, 4)), .. Enumerable.Repeat((byte)128, 8)], new(4, 4, 4, format)),
            VideoPixelFormat.Yuy2 => (rows.SelectMany(value => new byte[] { value, 128, value, 128, value, 128, value, 128 }).ToArray(), new(4, 4, 8, format)),
            VideoPixelFormat.Uyvy => (rows.SelectMany(value => new byte[] { 128, value, 128, value, 128, value, 128, value }).ToArray(), new(4, 4, 8, format)),
            VideoPixelFormat.Bgr24 => (rows.SelectMany(value => Enumerable.Range(0, 4).SelectMany(_ => new[] { value, value, value })).ToArray(), new(4, 4, 12, format)),
            _ => (rows.SelectMany(value => Enumerable.Range(0, 4).SelectMany(_ => new[] { value, value, value, (byte)255 })).ToArray(), new(4, 4, 16, format))
        };
    }
}

public sealed class NativeFormatRankingTests
{
    [Fact]
    public void RanksBroadcast1080pAndNativeLumaFormatsDeterministically()
    {
        var candidates = new[]
        {
            Candidate(0, 1280, 720, 60, VideoPixelFormat.Nv12),
            Candidate(1, 1920, 1080, 25, VideoPixelFormat.Bgra32),
            Candidate(2, 1920, 1080, 50, VideoPixelFormat.Yuy2),
            Candidate(3, 1920, 1080, 50, VideoPixelFormat.Nv12)
        };

        Assert.Equal([2, 3, 1, 0], WindowsNativeFormatRanker.Rank(candidates).Select(candidate => candidate.NativeIndex));
    }

    [Fact]
    public void FallsBackUntilADeviceAcceptsACandidate()
    {
        var attempts = new List<int>();
        var selected = WindowsNativeFormatRanker.TryInRankedOrder(
            new[] { Candidate(0, 1920, 1080, 50, VideoPixelFormat.Yuy2), Candidate(1, 1920, 1080, 25, VideoPixelFormat.Yuy2), Candidate(2, 1280, 720, 50, VideoPixelFormat.Nv12) },
            candidate => { attempts.Add(candidate.NativeIndex); return candidate.NativeIndex == 1; });

        Assert.Equal([0, 1], attempts);
        Assert.Equal(1, selected?.NativeIndex);
    }

    [Fact]
    public void UnknownPixelSubtypeIsRankedAfterSupportedEquivalent()
    {
        var ranked = WindowsNativeFormatRanker.Rank(new[] { Candidate(0, 1920, 1080, 50, VideoPixelFormat.Unknown), Candidate(1, 1920, 1080, 50, VideoPixelFormat.Uyvy) });
        Assert.Equal([1, 0], ranked.Select(candidate => candidate.NativeIndex));
    }

    [Fact]
    public void AutoPrefersSensibleNativeQualityOverLowResolutionCurrentDefault()
    {
        var ranked = WindowsNativeFormatRanker.Rank(new[]
        {
            Candidate(0, 640, 480, 30, VideoPixelFormat.Yuy2) with { IsCurrent = true },
            Candidate(1, 1920, 1080, 30, VideoPixelFormat.Nv12)
        });

        Assert.Equal(1, ranked[0].NativeIndex);
        Assert.Equal(Rational.From(30), ranked[0].Format.FrameRate);
    }

    [Fact]
    public void ManualModeLeadsThenFallsBackToAutoRanking()
    {
        var modes = new[] { Candidate(0, 1920, 1080, 30, VideoPixelFormat.Nv12), Candidate(1, 1280, 720, 60, VideoPixelFormat.Yuy2) };
        var selectedId = WindowsNativeFormatRanker.ModeId(modes[1]);
        var ranked = WindowsNativeFormatRanker.Rank(modes, selectedId);
        Assert.Equal([1, 0], ranked.Select(candidate => candidate.NativeIndex));
    }

    [Fact]
    public void StrictManualModeReturnsOnlyExactCandidate()
    {
        var modes = new[] { Candidate(0, 1920, 1080, 30, VideoPixelFormat.Nv12), Candidate(1, 1920, 1080, 50, VideoPixelFormat.Yuy2) };
        var exact = WindowsNativeFormatRanker.Rank(modes, WindowsNativeFormatRanker.ModeId(modes[1]), null, true);
        Assert.Single(exact); Assert.Equal(1, exact[0].NativeIndex);
    }

    [Fact]
    public void StrictManualModeDoesNotFallBackWhenUnavailable()
    {
        var modes = new[] { Candidate(0, 1920, 1080, 30, VideoPixelFormat.Nv12) };
        Assert.Throws<InvalidOperationException>(() => WindowsNativeFormatRanker.Rank(modes, "1920x1080|50/1|p|Yuy2", null, true));
    }

    [Fact]
    public void StrictPostNegotiationVerificationChecksRateAndPixelFormat()
    {
        var requested = Candidate(0, 1920, 1080, 50, VideoPixelFormat.Yuy2);
        Assert.True(WindowsNativeFormatVerifier.Matches(requested, requested.Format, VideoPixelFormat.Yuy2));
        Assert.False(WindowsNativeFormatVerifier.Matches(requested, requested.Format with { FrameRate = Rational.From(60) }, VideoPixelFormat.Yuy2));
        Assert.False(WindowsNativeFormatVerifier.Matches(requested, requested.Format, VideoPixelFormat.Nv12));
    }

    [Fact]
    public void StableModeIdKeepsTheNativeRationalRate()
    {
        var candidate = new WindowsNativeFormatCandidate(12, new(1920, 1080, Rational.From(30_000, 1_001), ScanMode.Progressive, FieldOrder.Unknown, PixelFormat: VideoPixelFormat.Nv12), VideoPixelFormat.Nv12);
        Assert.Contains("30000/1001", WindowsNativeFormatRanker.ModeId(candidate));
    }

    [Fact]
    public void ModeIdPreservesScanLayoutAndFieldOrder()
    {
        var progressive = new CaptureFormat(1920, 1080, Rational.From(25), ScanMode.Progressive, FieldOrder.Unknown, PixelFormat: VideoPixelFormat.Yuy2);
        var unknown = progressive with { ScanMode = ScanMode.Unknown };
        var topFirst = progressive with { ScanMode = ScanMode.Interlaced, FieldOrder = FieldOrder.TopFirst, InterlaceLayout = InterlaceLayout.FullFrame };
        var bottomFirst = topFirst with { FieldOrder = FieldOrder.BottomFirst };

        Assert.Equal(4, new[] { progressive, unknown, topFirst, bottomFirst }.Select(format => WindowsNativeFormatRanker.ModeId(format, VideoPixelFormat.Yuy2)).Distinct().Count());
    }

    [Fact]
    public void PreservesFractionalNativeFrameRate()
    {
        var rate = Rational.From(30_000, 1_001);
        var candidate = new WindowsNativeFormatCandidate(0, new(1920, 1080, rate, ScanMode.Progressive, FieldOrder.Unknown, PixelFormat: VideoPixelFormat.Yuy2), VideoPixelFormat.Yuy2, true);
        Assert.Equal(rate, WindowsNativeFormatRanker.Rank([candidate])[0].Format.FrameRate);
    }

    [Fact]
    public void AutoWithUserDeclared1080i50AndUnknownHandlingUsesGenericRanking()
    {
        var p60 = Candidate(0, 1920, 1080, 60, VideoPixelFormat.Yuy2);
        var p50 = Candidate(1, 1920, 1080, 50, VideoPixelFormat.Yuy2);
        var source = InputSignalOptions.Common.Single(option => option.Id == "1920x1080-50i").Signal;
        Assert.Equal(WindowsNativeFormatRanker.Rank([p60, p50]).Select(candidate => candidate.NativeIndex), WindowsNativeFormatRanker.Rank([p60, p50], null, source).Select(candidate => candidate.NativeIndex));
    }

    [Fact]
    public void AutoWithUserDeclared1080p25Prefers1080p25()
    {
        var p60 = Candidate(0, 1920, 1080, 60, VideoPixelFormat.Yuy2);
        var p25 = Candidate(1, 1920, 1080, 25, VideoPixelFormat.Yuy2);
        var source = InputSignalOptions.Common.Single(option => option.Id == "1920x1080-25p").Signal;
        Assert.Equal(1, WindowsNativeFormatRanker.Rank([p60, p25], null, source)[0].NativeIndex);
    }

    [Fact]
    public void ExplicitCaptureModeOverridesSourcePreference()
    {
        var p60 = Candidate(0, 1920, 1080, 60, VideoPixelFormat.Yuy2);
        var p50 = Candidate(1, 1920, 1080, 50, VideoPixelFormat.Yuy2);
        var source = InputSignalOptions.Common.Single(option => option.Id == "1920x1080-50i").Signal;
        Assert.Equal(0, WindowsNativeFormatRanker.Rank([p60, p50], WindowsNativeFormatRanker.ModeId(p60), source)[0].NativeIndex);
    }

    [Fact]
    public void EstimatedAndUnknownSourcesLeaveGenericRankingUnchanged()
    {
        var modes = new[] { Candidate(0, 1920, 1080, 60, VideoPixelFormat.Yuy2), Candidate(1, 1920, 1080, 50, VideoPixelFormat.Yuy2) };
        var estimated = new InputSignalInfo(1920, 1080, TemporalCadenceHz: 50, Provenance: InputSignalProvenance.ObservedAnalysis, Authority: SignalAuthority.EstimatedHigh);
        Assert.Equal(WindowsNativeFormatRanker.Rank(modes).Select(candidate => candidate.NativeIndex), WindowsNativeFormatRanker.Rank(modes, null, estimated).Select(candidate => candidate.NativeIndex));
        Assert.Equal(WindowsNativeFormatRanker.Rank(modes).Select(candidate => candidate.NativeIndex), WindowsNativeFormatRanker.Rank(modes, null, InputSignalInfo.Unknown).Select(candidate => candidate.NativeIndex));
    }

    [Fact]
    public void MissingSourceMatchFallsBackToGenericRanking()
    {
        var modes = new[] { Candidate(0, 1920, 1080, 60, VideoPixelFormat.Yuy2), Candidate(1, 1280, 720, 50, VideoPixelFormat.Yuy2) };
        var source = InputSignalOptions.Common.Single(option => option.Id == "1920x1080-50i").Signal;
        Assert.Equal(WindowsNativeFormatRanker.Rank(modes).Select(candidate => candidate.NativeIndex), WindowsNativeFormatRanker.Rank(modes, null, source).Select(candidate => candidate.NativeIndex));
    }

    private static WindowsNativeFormatCandidate Candidate(int index, int width, int height, int rate, VideoPixelFormat pixel) =>
        new(index, new(width, height, Rational.From(rate), ScanMode.Progressive, FieldOrder.Unknown, PixelFormat: pixel), pixel);
}

public sealed class HardwareProbeArgumentTests
{
    [Fact]
    public void PreviouslyFailingPositionalShapeRetainsDeviceWithoutSource()
    {
        var parsed = HardwareProbeArguments.Parse(["USB Capture SDI", "--mode", "1920x1080|50/1|p|Yuy2", "--analyze-signal"]);
        Assert.Equal("USB Capture SDI", parsed.DeviceName); Assert.Equal("1920x1080|50/1|p|Yuy2", parsed.ModeId); Assert.Null(parsed.SourceId); Assert.True(parsed.AnalyzeSignal);
    }

    [Fact]
    public void CanonicalShapeRetainsDeviceWithoutSource()
    {
        var parsed = HardwareProbeArguments.Parse(["--device", "USB Capture SDI", "--mode", "1920x1080|50/1|p|Yuy2", "--analyze-signal"]);
        Assert.Equal("USB Capture SDI", parsed.DeviceName); Assert.Equal("1920x1080|50/1|p|Yuy2", parsed.ModeId); Assert.Null(parsed.SourceId);
    }

    [Fact] public void SourceAndTimingFlagsParse() { var parsed = HardwareProbeArguments.Parse(["--device", "USB Capture SDI", "--source", "1920x1080-50i", "--timing-detail"]); Assert.Equal("1920x1080-50i", parsed.SourceId); Assert.Null(parsed.ModeId); Assert.True(parsed.TimingDetail); }
    [Fact] public void FieldReconstructionParses() { var parsed = HardwareProbeArguments.Parse(["--device", "USB Capture SDI", "--reconstruct-fields", "--field-order", "bottom"]); Assert.True(parsed.ReconstructFields); Assert.Equal("bottom", parsed.FieldOrder); }
    [Fact] public void DeviceWithoutModeParses() { var parsed = HardwareProbeArguments.Parse(["--device", "A quoted friendly name"]); Assert.Equal("A quoted friendly name", parsed.DeviceName); Assert.Null(parsed.ModeId); }
    [Fact] public void UnknownOptionFails() => Assert.Throws<ArgumentException>(() => HardwareProbeArguments.Parse(["--wat"]));
    [Fact] public void MissingValueFails() => Assert.Throws<ArgumentException>(() => HardwareProbeArguments.Parse(["--device"]));
    [Fact] public void MissingModeValueFails() => Assert.Throws<ArgumentException>(() => HardwareProbeArguments.Parse(["--device", "USB Capture SDI", "--mode", "--analyze-signal"]));
    [Fact] public void ProcessingResolutionsParse() { var parsed = HardwareProbeArguments.Parse(["--detection-resolution", "960x540", "--review-resolution", "320X180"]); Assert.Equal((960, 540, 320, 180), (parsed.DetectionWidth, parsed.DetectionHeight, parsed.ReviewWidth, parsed.ReviewHeight)); }
    [Fact] public void ProcessingResolutionDefaultsAreStable() { var parsed = HardwareProbeArguments.Parse([]); Assert.Equal((640, 360, 160, 90), (parsed.DetectionWidth, parsed.DetectionHeight, parsed.ReviewWidth, parsed.ReviewHeight)); }
    [Theory] [InlineData("640")] [InlineData("0x360")] [InlineData("640x0")] [InlineData("99999x360")] public void InvalidProcessingResolutionFails(string value) => Assert.Throws<ArgumentException>(() => HardwareProbeArguments.Parse(["--detection-resolution", value]));

    [Fact]
    public void DuplicateFriendlyNameRequiresDeviceId()
    {
        var devices = new[] { Device("id-one", "USB Capture SDI"), Device("id-two", "USB Capture SDI") };
        var parsed = HardwareProbeArguments.Parse(["--device", "USB Capture SDI"]);
        Assert.Contains("ambiguous", Assert.Throws<InvalidOperationException>(() => parsed.ResolveDevice(devices)).Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("id-two", HardwareProbeArguments.Parse(["--device-id", "id-two"]).ResolveDevice(devices).Id);
    }

    [Fact]
    public void NoRequestedDeviceNeverChoosesArbitraryFirstDevice()
    {
        var devices = new[] { Device("one", "One"), Device("two", "Two") };
        Assert.Throws<InvalidOperationException>(() => HardwareProbeArguments.Parse([]).ResolveDevice(devices));
    }

    private static CaptureDeviceDescriptor Device(string id, string name) => new(id, name, CaptureDeviceKind.ExternalCapture, WindowsCaptureBackend.BackendName);
}

public sealed class InterlaceMetadataTests
{
    [Fact]
    public void MissingAttributeRemainsUnknown()
    {
        var info = WindowsInterlaceMetadata.Parse(null);
        Assert.False(info.AttributePresent);
        Assert.Equal(ScanMode.Unknown, info.ScanMode);
    }

    [Fact]
    public void ProgressiveValueIsMappedTruthfully() => Assert.Equal(ScanMode.Progressive, WindowsInterlaceMetadata.Parse(2).ScanMode);

    [Theory]
    [InlineData(3, FieldOrder.TopFirst, InterlaceLayout.FullFrame)]
    [InlineData(4, FieldOrder.BottomFirst, InterlaceLayout.FullFrame)]
    [InlineData(5, FieldOrder.TopFirst, InterlaceLayout.SingleField)]
    [InlineData(6, FieldOrder.BottomFirst, InterlaceLayout.SingleField)]
    public void InterlacedValuesPreserveLayoutAndOrder(int raw, FieldOrder order, InterlaceLayout layout)
    {
        var info = WindowsInterlaceMetadata.Parse(raw);
        Assert.True(info.AttributePresent);
        Assert.Equal(ScanMode.Interlaced, info.ScanMode);
        Assert.Equal(order, info.FieldOrder);
        Assert.Equal(layout, info.Layout);
    }

    [Fact]
    public void MixedAndUnexpectedValuesNeverPretendToBeProgressive()
    {
        Assert.Equal(ScanMode.Unknown, WindowsInterlaceMetadata.Parse(7).ScanMode);
        Assert.Equal(ScanMode.Unknown, WindowsInterlaceMetadata.Parse(99).ScanMode);
    }
}

public sealed class MemoryStatusTests
{
    [Fact]
    public void SegmentsPartitionPhysicalMemoryWithoutDoubleCountingProcess()
    {
        var status = new MemoryStatus(1_000, 300, 100);
        Assert.Equal(700UL, status.SystemUsedBytes);
        Assert.Equal(100UL, status.ProcessUsedBytes);
        Assert.Equal(600UL, status.OtherSystemUsedBytes);
        Assert.Equal(status.TotalBytes, status.AvailableBytes + status.OtherSystemUsedBytes + status.ProcessUsedBytes);
    }

    [Fact]
    public void ProcessSegmentCannotExceedTotalUsedMemory()
    {
        var status = new MemoryStatus(1_000, 950, 100);
        Assert.Equal(50UL, status.ProcessUsedBytes);
        Assert.Equal(0UL, status.OtherSystemUsedBytes);
        Assert.Equal(status.TotalBytes, status.AvailableBytes + status.OtherSystemUsedBytes + status.ProcessUsedBytes);
    }
}

public sealed class CaptureReadinessTests
{
    [Fact] public void ComparableVideoAndAudioAreReady() => Assert.Equal("Ready to clap", WindowsCaptureReadiness.Describe(true, true, true, false, TimingQuality.DeviceHardware));
    [Fact] public void VideoWithoutAudioIsLiveButNotReady() => Assert.Equal("Video live — embedded audio not found", WindowsCaptureReadiness.Describe(true, false, false, false, TimingQuality.DeviceHardware));
    [Fact] public void UncorrelatedVideoIsNotPresentedAsReady() => Assert.Equal("Video live — timing not comparable", WindowsCaptureReadiness.Describe(true, true, true, false, TimingQuality.StreamTimestamp));
    [Fact] public void AudioFailureDoesNotMarkVideoFailed() => Assert.Equal("Video live — embedded audio unavailable", WindowsCaptureReadiness.Describe(true, true, false, true, TimingQuality.DeviceHardware));
}

public sealed class ColourConversionTests
{
    [Fact] public void NeutralYuvProducesNeutralBgr() { WindowsColorConversion.YuvToBgr(128, 128, 128, out var blue, out var green, out var red); Assert.InRange(blue, 128, 132); Assert.InRange(green, 128, 132); Assert.InRange(red, 128, 132); }
    [Fact] public void RedYuvDoesNotSwapRedAndBlue() { WindowsColorConversion.YuvToBgr(81, 90, 240, out var blue, out var green, out var red); Assert.InRange(red, 240, 255); Assert.InRange(green, 0, 20); Assert.InRange(blue, 0, 20); }
}

public sealed class MediaFoundationInteropContractTests
{
    [Fact]
    public void CriticalInterfaceIdsMatchWindowsSdk()
    {
        Assert.Equal(new("2CD2D921-C447-44A7-A13C-4ADABFC247E3"), Interface("IMFAttributes").GUID);
        Assert.Equal(new("7FEE9E9A-4A89-47A6-899C-B6A53A70FB67"), Interface("IMFActivate").GUID);
        Assert.Equal(new("44AE0FA8-EA31-4109-8D2E-4CAE4997C555"), Interface("IMFMediaType").GUID);
        Assert.Equal(new("70AE66F2-C809-4E4F-8915-BDCB406B7993"), Interface("IMFSourceReader").GUID);
        Assert.Equal(new("C40A00F2-B93A-4D80-AE8C-5A1C634F58E4"), Interface("IMFSample").GUID);
        Assert.Equal(new("045FA593-8799-42B8-BC8D-8968C6453507"), Interface("IMFMediaBuffer").GUID);
    }

    [Fact]
    public void SampleVtableSuffixMatchesWindowsSdkExactly()
    {
        string[] expected = ["GetSampleFlags", "SetSampleFlags", "GetSampleTime", "SetSampleTime", "GetSampleDuration", "SetSampleDuration", "GetBufferCount", "GetBufferByIndex", "ConvertToContiguousBuffer", "AddBuffer", "RemoveBufferByIndex", "RemoveAllBuffers", "GetTotalLength", "CopyToBuffer"];
        var methods = DeclaredMethods("IMFSample");
        Assert.Equal(expected, methods.TakeLast(expected.Length).Select(method => method.Name));
        Assert.All(methods.TakeLast(expected.Length), method => Assert.True((method.MethodImplementationFlags & MethodImplAttributes.PreserveSig) != 0, method.Name));
    }

    [Fact]
    public void ReaderAndBufferVtablesMatchWindowsSdk()
    {
        Assert.Equal(["GetStreamSelection", "SetStreamSelection", "GetNativeMediaType", "GetCurrentMediaType", "SetCurrentMediaType", "SetCurrentPosition", "ReadSample", "Flush", "GetServiceForStream", "GetPresentationAttribute"], DeclaredMethods("IMFSourceReader").Select(method => method.Name));
        Assert.Equal(["Lock", "Unlock", "GetCurrentLength", "SetCurrentLength", "GetMaxLength"], DeclaredMethods("IMFMediaBuffer").Select(method => method.Name));
    }

    [Fact]
    public void AttributeCallsPreserveNativeHresults()
    {
        Assert.All(DeclaredMethods("IMFAttributes"), method => Assert.True((method.MethodImplementationFlags & MethodImplAttributes.PreserveSig) != 0, method.Name));
    }

    private static Type Interface(string name) => typeof(WindowsCaptureBackend).Assembly.GetType($"Kairix.QuickAVSync.Windows.Capture.{name}", true)!;
    private static MethodInfo[] DeclaredMethods(string name) => Interface(name).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).OrderBy(method => method.MetadataToken).ToArray();
}

public sealed class OperatorUiContractTests
{
    private static string Ui => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Ui", "MainWindow.xaml"));

    [Fact]
    public void SettingsAreTabbedAndCollapsedRailCanReopen()
    {
        foreach (var id in new[] { "AppearanceTab", "DetectionTab", "ReviewTab", "CaptureTab", "ShortcutsTab", "AboutTab", "OpenSettingsRailButton" }) Assert.Contains($"AutomationProperties.AutomationId=\"{id}\"", Ui);
        Assert.Contains("TabStripPlacement=\"Right\"", Ui);
        Assert.Contains("AudioDisplayStyleSelector", Ui);
        Assert.Contains("AutomaticDetectionToggle", Ui);
    }

    [Fact]
    public void ResultPanelAndVideoUseOnlyOperatorEssentials()
    {
        Assert.DoesNotContain("TIMING CORRECTION", Ui);
        Assert.DoesNotContain("RAW MEASUREMENT", Ui);
        Assert.DoesNotContain("TIMING QUALITY", Ui);
        Assert.DoesNotContain("StringFormat=Detected:", Ui);
        Assert.Contains("VISUAL CONFIDENCE", Ui);
        Assert.Contains("SESSION HISTORY", Ui);
    }

    [Fact]
    public void ReviewControlsHaveRequiredOrderAndAvailability()
    {
        var labels = new[] { "Pre A", "Pre V", "Mark Visual", "Nxt V", "Nxt A", "Resume Live" };
        var prior = -1;
        foreach (var label in labels) { var next = Ui.IndexOf($"Content=\"{label}\"", StringComparison.Ordinal); Assert.True(next > prior, label); prior = next; }
        Assert.Contains("Content=\"Mark Visual\" Command=\"{Binding MarkVisualCommand}\" IsEnabled=\"{Binding IsInReview}\"", Ui);
    }

    [Theory]
    [InlineData("Mirrored")]
    [InlineData("Filled")]
    [InlineData("Line")]
    public void AudioDisplayStylesArePersistable(string style)
    {
        var path = Path.Combine(Path.GetTempPath(), $"kairix-style-{Guid.NewGuid():N}.json");
        try { using var service = new SettingsService(path); service.Save(new() { AudioDisplayStyle = style }); Assert.Equal(style, service.Load().AudioDisplayStyle); }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}

public sealed class ThemeContrastTests
{
    [Fact]
    public void EveryThemeMaintainsOrdinaryTextContrast()
    {
        var files = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Themes"), "*.xaml");
        Assert.Equal(7, files.Length);
        foreach (var file in files)
        {
            var colors = ReadColors(file);
            AssertContrast(file, colors, "PrimaryTextBrush", "PanelBackgroundBrush");
            AssertContrast(file, colors, "SecondaryTextBrush", "PanelBackgroundBrush");
            AssertContrast(file, colors, "MutedTextBrush", "PanelBackgroundBrush");
            AssertContrast(file, colors, "ControlTextBrush", "ControlBackgroundBrush");
            AssertContrast(file, colors, "AccentTextBrush", "AccentBrush");
            AssertContrast(file, colors, "PrimaryTextBrush", "PanelSecondaryBrush");
        }
    }

    private static Dictionary<string, string> ReadColors(string path)
    {
        var document = System.Xml.Linq.XDocument.Load(path); System.Xml.Linq.XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        return document.Descendants().Where(element => element.Name.LocalName == "SolidColorBrush").ToDictionary(element => element.Attribute(x + "Key")!.Value, element => element.Attribute("Color")!.Value);
    }

    private static void AssertContrast(string file, IReadOnlyDictionary<string, string> colors, string foreground, string background) =>
        Assert.True(Contrast(colors[foreground], colors[background]) >= 4.5, $"{Path.GetFileNameWithoutExtension(file)} {foreground}/{background} contrast was {Contrast(colors[foreground], colors[background]):0.00}");

    private static double Contrast(string first, string second)
    {
        var a = Luminance(first); var b = Luminance(second); return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
    }

    private static double Luminance(string color)
    {
        var hex = color.TrimStart('#'); if (hex.Length == 8) hex = hex[2..];
        static double Channel(int value) { var normalized = value / 255d; return normalized <= .04045 ? normalized / 12.92 : Math.Pow((normalized + .055) / 1.055, 2.4); }
        return .2126 * Channel(Convert.ToInt32(hex[..2], 16)) + .7152 * Channel(Convert.ToInt32(hex.Substring(2, 2), 16)) + .0722 * Channel(Convert.ToInt32(hex.Substring(4, 2), 16));
    }
}
