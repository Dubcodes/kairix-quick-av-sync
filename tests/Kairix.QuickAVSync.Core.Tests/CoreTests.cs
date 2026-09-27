using Kairix.QuickAVSync.Capture;
using Kairix.QuickAVSync.Models;
using Kairix.QuickAVSync.Services;

namespace Kairix.QuickAVSync.Core.Tests;

public sealed class RollingBufferTests
{
    [Fact] public void WrapsAndReturnsChronologically() { var b = new RollingBuffer<int>(3); foreach (var i in Enumerable.Range(1, 5)) b.Add(i); Assert.Equal([3, 4, 5], b.Snapshot()); }
    [Fact] public void ResizeKeepsNewest() { var b = new RollingBuffer<int>(5); foreach (var i in Enumerable.Range(1, 5)) b.Add(i); b.Resize(2); Assert.Equal([4, 5], b.Snapshot()); }
    [Fact] public void RangeUsesTimestamps() { var b = new RollingBuffer<(long Time, string Value)>(5, x => x.Time); b.Add((10, "a")); b.Add((20, "b")); b.Add((30, "c")); Assert.Equal(["b", "c"], b.Range(15, 30).Select(x => x.Value)); }
}

public sealed class SyncResultTests
{
    [Fact] public void EqualIsInSync() => Assert.Equal("AUDIO AND VIDEO IN SYNC", SyncResult.Calculate(T(0), T(0)).Wording);
    [Fact] public void PositiveMeansAudioLeads() { var r = SyncResult.Calculate(T(0), T(64)); Assert.Equal(64, r.SignedMilliseconds); Assert.Equal("AUDIO LEADS VIDEO BY 64 ms", r.Wording); }
    [Fact] public void NegativeMeansAudioLags() { var r = SyncResult.Calculate(T(42), T(0)); Assert.Equal(-42, r.SignedMilliseconds); Assert.Equal("AUDIO LAGS VIDEO BY 42 ms", r.Wording); }
    [Fact] public void UnrelatedClocksRefuseFalsePrecision() { var r = SyncResult.Calculate(new(0, TimingQuality.StreamTimestamp, "a"), new(10, TimingQuality.StreamTimestamp, "b")); Assert.False(r.TimingComparable); Assert.True(double.IsNaN(r.SignedMilliseconds)); }
    private static MediaTimestamp T(double ms) => new((long)(ms * 10_000), TimingQuality.StreamTimestamp);
}

public sealed class TimingModelTests
{
    [Theory] [InlineData(25, 1, 40)] [InlineData(50, 1, 20)] [InlineData(30000, 1001, 33.3667)] [InlineData(60000, 1001, 16.6833)] public void RationalFrameRates(int n, int d, double expectedMs) => Assert.Equal(expectedMs, Rational.From(n, d).FrameDuration.TotalMilliseconds, .001);
    [Fact] public void InterlacedHasHalfFrameCadence() { var f = new CaptureFormat(1920, 1080, Rational.From(25), ScanMode.Interlaced, FieldOrder.TopFirst); Assert.Equal(20, f.TemporalImageDuration.TotalMilliseconds); }
}

public sealed class TransientDetectorTests
{
    [Fact] public void SilenceAndNoiseDoNotTrigger() => Assert.Empty(new TransientDetector().Process(Chunk(0, Noise(4800, .003f), 48000)));
    [Fact] public void SingleImpulseTriggers() { var samples = Noise(4800, .002f); Pulse(samples, 1000, .8f); Assert.Single(new TransientDetector().Process(Chunk(0, samples, 48000))); }
    [Fact] public void EchoIsDebounced() { var samples = Noise(24000, .002f); Pulse(samples, 1000, .9f); Pulse(samples, 6000, .55f); Assert.Single(new TransientDetector().Process(Chunk(0, samples, 48000))); }
    [Theory] [InlineData(44100)] [InlineData(48000)] [InlineData(96000)] public void WorksAtDifferentRates(int rate) { var samples = Noise(rate / 4, .001f); Pulse(samples, rate / 10, .7f); Assert.Single(new TransientDetector().Process(Chunk(0, samples, rate))); }
    [Fact] public void TwoSeparatedImpulsesTriggerTwice() { var samples = Noise(48000, .001f); Pulse(samples, 1000, .8f); Pulse(samples, 20000, .8f); Assert.Equal(2, new TransientDetector().Process(Chunk(0, samples, 48000)).Count); }
    private static AudioChunk Chunk(long ticks, float[] samples, int rate) => new(new(ticks, TimingQuality.StreamTimestamp), samples, rate, 1);
    private static float[] Noise(int count, float level) => Enumerable.Range(0, count).Select(i => (i % 7 - 3) * level / 3).ToArray();
    private static void Pulse(float[] samples, int at, float level) { for (var i = 0; i < 40; i++) samples[at + i] = level * (float)Math.Exp(-i / 10d); }
}

public sealed class WorkWindowWaveformAndHistoryTests
{
    [Fact] public void SelectsSymmetricWindow() { var frames = Enumerable.Range(-5, 11).Select(i => new VideoFrame(new(i * 100_000, TimingQuality.StreamTimestamp), 1, 1, [0], i)).ToArray(); var selected = WorkWindowSelector.Around(frames, f => f.Timestamp, new(0, TimingQuality.StreamTimestamp), TimeSpan.FromMilliseconds(20)); Assert.Equal([-2, -1, 0, 1, 2], selected.Select(f => f.TemporalIndex)); }
    [Fact] public void WaveformPlacesImpulseNearCenter() { var samples = new float[480]; samples[240] = 1; var center = new MediaTimestamp(50_000, TimingQuality.StreamTimestamp); var chunk = new AudioChunk(new(0, TimingQuality.StreamTimestamp), samples, 48000, 1); var wave = new WaveformBuilder().Build([chunk], center, TimeSpan.FromMilliseconds(5), 100); Assert.InRange(Array.IndexOf(wave.ToArray(), wave.Max()), 49, 51); }
    [Fact] public void HistoryKeepsOnlyLatest() { var h = new SessionHistoryService(3); for (var i = 0; i < 5; i++) h.Add(new(DateTime.MinValue.AddSeconds(i), new(i, i.ToString()), null)); Assert.Equal([4d, 3d, 2d], h.Items.Select(x => x.Result.SignedMilliseconds)); }
}

public sealed class VisualDetectorTests
{
    [Fact] public async Task NoMotionHasNoCandidate() { var f = Frames(false); Assert.Null(await new MotionVisualClapDetector().DetectAsync(f, f[5].Timestamp, default)); }
    [Fact] public async Task ContactSequenceSelectsNearExpected() { var f = Frames(true); var c = await new MotionVisualClapDetector().DetectAsync(f, f[5].Timestamp, default); Assert.NotNull(c); Assert.InRange(c!.TemporalIndex, 3, 7); }
    private static VideoFrame[] Frames(bool contact) => Enumerable.Range(0, 11).Select(i => { var pixels = new byte[64 * 36]; if (contact && i is >= 4 and <= 6) Array.Fill(pixels, (byte)(i == 5 ? 240 : 100), 800, 500); return new VideoFrame(new(i * 100_000, TimingQuality.StreamTimestamp), 64, 36, pixels, i); }).ToArray();
}

public sealed class DeviceRankingTests
{
    private static CaptureDeviceDescriptor D(string id, CaptureDeviceKind kind, string name = "device") => new(id, name, kind, "test");
    [Fact] public void ExternalBeatsWebcam() { var r = new CaptureDeviceRanker().SelectBest([D("web", CaptureDeviceKind.IntegratedCamera), D("cap", CaptureDeviceKind.ExternalCapture)], null, null); Assert.Equal("cap", r!.Id); }
    [Fact] public void RememberedDeviceWins() { var r = new CaptureDeviceRanker().SelectBest([D("one", CaptureDeviceKind.ExternalCapture), D("two", CaptureDeviceKind.ExternalCapture)], "two", null); Assert.Equal("two", r!.Id); }
    [Fact] public void MissingRememberedUsesExternal() { var r = new CaptureDeviceRanker().SelectBest([D("web", CaptureDeviceKind.IntegratedCamera), D("cap", CaptureDeviceKind.ExternalCapture)], "missing", null); Assert.Equal("cap", r!.Id); }
    [Fact] public void TwoExternalCardsAreDeterministic() { var r = new CaptureDeviceRanker().Rank([D("b", CaptureDeviceKind.ExternalCapture, "Beta"), D("a", CaptureDeviceKind.ExternalCapture, "Alpha")], null, null); Assert.Equal(["a", "b"], r.Select(x => x.Id)); }
    [Fact] public void OnlyWebcamCanBeSelectedWhenSoleChoice() { var r = new CaptureDeviceRanker().SelectBest([D("web", CaptureDeviceKind.IntegratedCamera)], null, null); Assert.Equal("web", r!.Id); }
}

public sealed class PairingTests
{
    private static CaptureDeviceDescriptor Video(string? container = null, string? parent = null, string name = "Acme HDMI Capture") => new("v", name, CaptureDeviceKind.ExternalCapture, "test", container, parent);
    [Fact] public void ExactContainerWins() { var r = new DevicePairingService().Pair(Video("same"), [new("mic", "Laptop Mic", "other", IsDefaultMicrophone: true), new("a", "Acme Audio", "same")]); Assert.Equal(PairingConfidence.ExactContainer, r.Confidence); Assert.Equal("a", r.Endpoint!.Id); }
    [Fact] public void NonmatchingContainerDoesNotPair() => Assert.Null(new DevicePairingService().Pair(Video("x"), [new("a", "Unrelated", "y")]).Endpoint);
    [Fact] public void MultipleExactEndpointsChooseDeterministically() { var r = new DevicePairingService().Pair(Video("x"), [new("b", "Other", "x"), new("a", "Acme HDMI Audio", "x")]); Assert.Equal("a", r.Endpoint!.Id); }
    [Fact] public void NoEndpointReturnsNone() => Assert.Equal(PairingConfidence.None, new DevicePairingService().Pair(Video(), []).Confidence);
    [Fact] public void AmbiguousNameFallbackRefusesPairing() { var endpoints = new[] { new AudioEndpointDescriptor("a", "Acme HDMI One", null), new AudioEndpointDescriptor("b", "Acme HDMI Two", null) }; Assert.Null(new DevicePairingService().Pair(Video(), endpoints).Endpoint); }
    [Fact] public void BuiltInDefaultMicrophoneIsNeverFallback() => Assert.Null(new DevicePairingService().Pair(Video(), [new("mic", "Acme HDMI Capture Microphone", null, IsDefaultMicrophone: true)]).Endpoint);
    [Fact] public void SharedParentIsStrongMatch() => Assert.Equal(PairingConfidence.HardwareParent, new DevicePairingService().Pair(Video(parent: "p"), [new("a", "Audio", null, "p")]).Confidence);
}

public sealed class ClockCorrelationTests
{
    [Fact] public void SameDomainNormalizesKnownOffset() { var c = new MediaClockCorrelator(); c.Normalize(new("v", "qpc", 100, 1000, TimingQuality.DeviceHardware)); var n = c.Normalize(new("v", "qpc", 150, 1050, TimingQuality.DeviceHardware)); Assert.Equal(1050, n.Timestamp.Ticks100ns); Assert.False(n.Discontinuity); }
    [Fact] public void BackwardsClockDegradesAndResets() { var c = new MediaClockCorrelator(); c.Normalize(new("v", "qpc", 100, 1000, TimingQuality.DeviceHardware)); var n = c.Normalize(new("v", "qpc", 90, 1100, TimingQuality.DeviceHardware)); Assert.True(n.Discontinuity); Assert.Equal(TimingQuality.ArrivalFallback, n.Timestamp.Quality); }
    [Fact] public void UnrelatedDomainsAreMarkedUnrelated() { var a = new MediaTimestamp(0, TimingQuality.StreamTimestamp, "audio"); var v = new MediaTimestamp(0, TimingQuality.StreamTimestamp, "video"); Assert.Equal(TimingQuality.Unrelated, MediaClockCorrelator.CombinedQuality(a, v)); }
    [Fact] public void QualityFallsBackToWeakerClock() { var a = new MediaTimestamp(0, TimingQuality.DeviceHardware, "qpc"); var v = new MediaTimestamp(0, TimingQuality.StreamTimestamp, "qpc"); Assert.Equal(TimingQuality.StreamTimestamp, MediaClockCorrelator.CombinedQuality(a, v)); }
}

public sealed class SyntheticPipelineTests
{
    [Theory] [InlineData(60)] [InlineData(-60)] public async Task KnownOffsetProducesCorrectLeadLag(double offsetMs)
    {
        var fixture = SyntheticFixture.Create(TimeSpan.FromMilliseconds(offsetMs)); var detector = new TransientDetector(); var transient = fixture.Audio.SelectMany(detector.Process).Single();
        var frames = WorkWindowSelector.Around(fixture.Video, f => f.Timestamp, transient.Timestamp, TimeSpan.FromMilliseconds(250)); var visual = await new MotionVisualClapDetector().DetectAsync(frames, transient.Timestamp, default);
        Assert.NotNull(visual); var result = SyncResult.Calculate(transient.Timestamp, visual!.Timestamp); Assert.True(result.TimingComparable); Assert.Equal(Math.Sign(offsetMs), Math.Sign(result.SignedMilliseconds)); Assert.InRange(Math.Abs(result.SignedMilliseconds - offsetMs), 0, 25);
    }
}

public sealed class ArchitectureAndSupersessionTests
{
    [Fact] public void CoreAssemblyHasNoWindowsDesktopReferences()
    {
        var forbidden = new[] { "PresentationFramework", "PresentationCore", "WindowsBase", "System.Windows.Forms" };
        var references = typeof(Rational).Assembly.GetReferencedAssemblies().Select(x => x.Name).ToArray();
        Assert.DoesNotContain(references, x => forbidden.Contains(x));
        Assert.DoesNotContain(typeof(Rational).Assembly.GetTypes(), x => x.FullName?.StartsWith("System.Windows", StringComparison.Ordinal) == true);
    }
    [Fact] public void SupersededAnalysisCannotBecomeCurrent() { var gate = new AnalysisGeneration(); var first = gate.Next(); var second = gate.Next(); Assert.False(gate.IsCurrent(first)); Assert.True(gate.IsCurrent(second)); }
}
