using Kairix.QuickAVSync.Models;
using Kairix.QuickAVSync.Windows.Infrastructure;
using Kairix.QuickAVSync.Windows.Capture;

namespace Kairix.QuickAVSync.Windows.Tests;

public sealed class SettingsTests
{
    [Fact] public void RoundTripsOnlyAllowedConfiguration()
    {
        var path = Path.Combine(Path.GetTempPath(), $"kairix-{Guid.NewGuid():N}.json");
        try { var service = new SettingsService(path); service.Save(new() { LastDeviceId = "device", AutoDetect = false, RollingBufferSeconds = 12 }); var loaded = service.Load(); Assert.Equal("device", loaded.LastDeviceId); Assert.False(loaded.AutoDetect); Assert.Equal(12, loaded.RollingBufferSeconds); var json = File.ReadAllText(path); Assert.DoesNotContain("waveform", json, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("history", json, StringComparison.OrdinalIgnoreCase); }
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

        Assert.Equal([3, 2, 1, 0], WindowsNativeFormatRanker.Rank(candidates).Select(candidate => candidate.NativeIndex));
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

    private static WindowsNativeFormatCandidate Candidate(int index, int width, int height, int rate, VideoPixelFormat pixel) =>
        new(index, new(width, height, Rational.From(rate), ScanMode.Progressive, FieldOrder.Unknown, PixelFormat: pixel), pixel);
}
