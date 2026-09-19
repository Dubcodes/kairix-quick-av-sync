using Kairix.QuickAVSync.Models;
using Kairix.QuickAVSync.Services;

namespace Kairix.QuickAVSync.Tests;

public sealed class RollingBufferTests
{
    [Fact] public void WrapsAndReturnsChronologically() { var b = new RollingBuffer<int>(3); foreach (var i in Enumerable.Range(1, 5)) b.Add(i); Assert.Equal([3, 4, 5], b.Snapshot()); }
    [Fact] public void ResizeKeepsNewest() { var b = new RollingBuffer<int>(5); foreach (var i in Enumerable.Range(1, 5)) b.Add(i); b.Resize(2); Assert.Equal([4, 5], b.Snapshot()); Assert.Equal(2, b.Capacity); }
    [Fact] public void RangeUsesTimestamps() { var b = new RollingBuffer<(long Time, string Value)>(5, x => x.Time); b.Add((10, "a")); b.Add((20, "b")); b.Add((30, "c")); Assert.Equal(["b", "c"], b.Range(15, 30).Select(x => x.Value)); }
}

public sealed class SyncResultTests
{
    [Fact] public void EqualIsInSync() => Assert.Equal("AUDIO AND VIDEO IN SYNC", SyncResult.Calculate(T(0), T(0)).Wording);
    [Fact] public void PositiveMeansAudioLeads() { var r = SyncResult.Calculate(T(0), T(64)); Assert.Equal(64, r.SignedMilliseconds); Assert.Equal("AUDIO LEADS VIDEO BY 64 ms", r.Wording); }
    [Fact] public void NegativeMeansAudioLags() { var r = SyncResult.Calculate(T(42), T(0)); Assert.Equal(-42, r.SignedMilliseconds); Assert.Equal("AUDIO LAGS VIDEO BY 42 ms", r.Wording); }
    private static MediaTimestamp T(double ms) => new((long)(ms * 10_000), TimingQuality.StreamTimestamp);
}

public sealed class TimingModelTests
{
    [Theory] [InlineData(25, 1, 40)] [InlineData(50, 1, 20)] [InlineData(30000, 1001, 33.3667)] [InlineData(60000, 1001, 16.6833)]
    public void RationalFrameRates(int n, int d, double expectedMs) => Assert.Equal(expectedMs, Rational.From(n, d).FrameDuration.TotalMilliseconds, .001);
    [Fact] public void InterlacedHasHalfFrameCadence() { var f = new CaptureFormat(1920, 1080, Rational.From(25), ScanMode.Interlaced, FieldOrder.TopFirst); Assert.Equal(20, f.TemporalImageDuration.TotalMilliseconds); Assert.Equal(FieldOrder.TopFirst, f.FieldOrder); }
}

public sealed class TransientDetectorTests
{
    [Fact] public void SilenceAndNoiseDoNotTrigger() { var d = new TransientDetector(); Assert.Empty(d.Process(Chunk(0, Noise(4800, .003f), 48000))); }
    [Fact] public void SingleImpulseTriggers() { var samples = Noise(4800, .002f); for (var i = 1000; i < 1032; i++) samples[i] = .8f; Assert.Single(new TransientDetector().Process(Chunk(0, samples, 48000))); }
    [Fact] public void EchoIsDebounced() { var d = new TransientDetector(); var samples = Noise(24000, .002f); Pulse(samples, 1000, .9f); Pulse(samples, 6000, .55f); Assert.Single(d.Process(Chunk(0, samples, 48000))); }
    [Theory] [InlineData(44100)] [InlineData(48000)] [InlineData(96000)]
    public void WorksAtDifferentSampleRates(int rate) { var samples = Noise(rate / 4, .001f); Pulse(samples, rate / 10, .7f); Assert.Single(new TransientDetector().Process(Chunk(0, samples, rate))); }
    [Fact] public void TwoSeparatedImpulsesTriggerTwice() { var rate = 48000; var samples = Noise(rate, .001f); Pulse(samples, 1000, .8f); Pulse(samples, 20000, .8f); Assert.Equal(2, new TransientDetector().Process(Chunk(0, samples, rate)).Count); }
    private static AudioChunk Chunk(long ticks, float[] samples, int rate) => new(new(ticks, TimingQuality.StreamTimestamp), samples, rate, 1);
    private static float[] Noise(int count, float level) => Enumerable.Range(0, count).Select(i => (i % 7 - 3) * level / 3).ToArray();
    private static void Pulse(float[] samples, int at, float level) { for (var i = 0; i < 40; i++) samples[at + i] = level * (float)Math.Exp(-i / 10d); }
}

public sealed class WorkWindowAndHistoryTests
{
    [Fact] public void SelectsSymmetricWindow() { var frames = Enumerable.Range(-5, 11).Select(i => new VideoFrame(new(i * 100_000, TimingQuality.StreamTimestamp), 1, 1, [0], i)).ToArray(); var selected = WorkWindowSelector.Around(frames, f => f.Timestamp, new(0, TimingQuality.StreamTimestamp), TimeSpan.FromMilliseconds(20)); Assert.Equal([-2, -1, 0, 1, 2], selected.Select(f => f.TemporalIndex)); }
    [Fact] public void HistoryKeepsOnlyLatest() { var h = new SessionHistoryService(3); for (var i = 0; i < 5; i++) h.Add(new(DateTime.MinValue.AddSeconds(i), new(i, i.ToString()), null)); Assert.Equal([4d, 3d, 2d], h.Items.Select(x => x.Result.SignedMilliseconds)); }
}

public sealed class SettingsTests
{
    [Fact] public void RoundTripsOnlyConfiguration()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"kairix-{Guid.NewGuid():N}.json");
        try { var service = new SettingsService(path); service.Save(new() { LastDeviceId = "device", AutoDetect = false, RollingBufferSeconds = 12 }); var loaded = service.Load(); Assert.Equal("device", loaded.LastDeviceId); Assert.False(loaded.AutoDetect); Assert.Equal(12, loaded.RollingBufferSeconds); var json = File.ReadAllText(path); Assert.DoesNotContain("waveform", json, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("history", json, StringComparison.OrdinalIgnoreCase); }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}

public sealed class VisualDetectorTests
{
    [Fact] public async Task NoMotionHasNoCandidate() { var frames = Frames(false); Assert.Null(await new VisualClapDetector().DetectAsync(frames, frames[5].Timestamp, default)); }
    [Fact] public async Task ContactSequenceSelectsNearExpected()
    {
        var frames = Frames(true); var candidate = await new VisualClapDetector().DetectAsync(frames, frames[5].Timestamp, default); Assert.NotNull(candidate); Assert.InRange(candidate!.TemporalIndex, 3, 7); Assert.InRange(candidate.Confidence, .05, .94);
    }
    [Fact] public async Task ConstantMotionIsLowerConfidenceThanContact()
    {
        var contact = await new VisualClapDetector().DetectAsync(Frames(true), new(500_000, TimingQuality.StreamTimestamp), default);
        var constant = await new VisualClapDetector().DetectAsync(ConstantMotion(), new(500_000, TimingQuality.StreamTimestamp), default);
        Assert.True(constant is null || contact!.Confidence >= constant.Confidence);
    }
    private static VideoFrame[] Frames(bool contact) => Enumerable.Range(0, 11).Select(i => { var pixels = new byte[64 * 36]; if (contact && i is >= 4 and <= 6) Array.Fill(pixels, (byte)(i == 5 ? 240 : 100), 800, 500); return new VideoFrame(new(i * 100_000, TimingQuality.StreamTimestamp), 64, 36, pixels, i); }).ToArray();
    private static VideoFrame[] ConstantMotion() => Enumerable.Range(0, 11).Select(i => { var p = new byte[64 * 36]; Array.Fill(p, (byte)120, i * 20, 120); return new VideoFrame(new(i * 100_000, TimingQuality.StreamTimestamp), 64, 36, p, i); }).ToArray();
}

public sealed class CapturePolicyTests
{
    [Fact] public void PairingPrefersSharedContainerId()
    {
        var video = new CaptureDevice("v", "HDMI Capture Video", "same", true, 100);
        var endpoints = new[] { new AudioEndpoint("mic", "Laptop Microphone", "other", true), new AudioEndpoint("a", "HDMI Capture Audio", "same") };
        var result = new DevicePairingService().Pair(video, endpoints);
        Assert.True(result.IsCertain); Assert.Equal("a", result.Endpoint?.Id);
    }
    [Fact] public void PairingNeverFallsBackToDefaultMicrophone()
    {
        var result = new DevicePairingService().Pair(new("v", "USB HDMI Capture", null, true, 100), [new("mic", "USB HDMI Capture Microphone", null, true)]);
        Assert.Null(result.Endpoint);
    }
    [Fact] public void FormatSelectorPrefersBroadcast1080p50()
    {
        var formats = new[] { new CaptureFormat(3840, 2160, Rational.From(30), ScanMode.Progressive, FieldOrder.Unknown), new CaptureFormat(1920, 1080, Rational.From(25), ScanMode.Progressive, FieldOrder.Unknown), new CaptureFormat(1920, 1080, Rational.From(50), ScanMode.Progressive, FieldOrder.Unknown) };
        Assert.Equal(50, CaptureFormatSelector.Select(formats)!.FrameRate.Value);
    }
    [Fact] public void TimingServiceLabelsDeviceAndStreamDomains()
    {
        var timing = new MediaTimingService(); Assert.Equal(TimingQuality.StreamTimestamp, timing.Normalize(10).Quality); Assert.Equal(20, timing.Normalize(30).Ticks100ns);
        Assert.Equal(TimingQuality.DeviceQpc, timing.Normalize(100, 1_000).Quality); Assert.Equal(50, timing.Normalize(200, 1_050).Ticks100ns);
    }
}
