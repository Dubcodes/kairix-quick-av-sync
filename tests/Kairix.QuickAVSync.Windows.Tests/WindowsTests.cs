using Kairix.QuickAVSync.Models;
using Kairix.QuickAVSync.Windows.Infrastructure;
using Kairix.QuickAVSync.Windows.Capture;
using System.Reflection;

namespace Kairix.QuickAVSync.Windows.Tests;

public sealed class SettingsTests
{
    [Fact] public void RoundTripsOnlyAllowedConfiguration()
    {
        var path = Path.Combine(Path.GetTempPath(), $"kairix-{Guid.NewGuid():N}.json");
        try { var service = new SettingsService(path); service.Save(new() { LastDeviceId = "device", AutoDetect = false, VisualSensitivity = 77, RollingBufferSeconds = 12, NativeFormatByDevice = new() { ["device"] = "1920x1080|30000/1001|p|Nv12" }, InputSignalByDevice = new() { ["device"] = "1920x1080-50i" } }); var loaded = service.Load(); Assert.Equal("device", loaded.LastDeviceId); Assert.False(loaded.AutoDetect); Assert.Equal(77, loaded.VisualSensitivity); Assert.Equal(12, loaded.RollingBufferSeconds); Assert.Equal("1920x1080|30000/1001|p|Nv12", loaded.NativeFormatByDevice["device"]); Assert.Equal("1920x1080-50i", loaded.InputSignalByDevice["device"]); var json = File.ReadAllText(path); Assert.DoesNotContain("waveform", json, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("history", json, StringComparison.OrdinalIgnoreCase); }
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

    private static WindowsNativeFormatCandidate Candidate(int index, int width, int height, int rate, VideoPixelFormat pixel) =>
        new(index, new(width, height, Rational.From(rate), ScanMode.Progressive, FieldOrder.Unknown, PixelFormat: pixel), pixel);
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
