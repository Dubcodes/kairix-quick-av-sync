using Kairix.QuickAVSync.Capture;
using Kairix.QuickAVSync.Models;
using Kairix.QuickAVSync.Services;
using System.Diagnostics;
using Xunit.Abstractions;

namespace Kairix.QuickAVSync.Core.Tests;

public sealed class RollingBufferTests
{
    [Fact] public void WrapsAndReturnsChronologically() { var b = new RollingBuffer<int>(3); foreach (var i in Enumerable.Range(1, 5)) b.Add(i); Assert.Equal([3, 4, 5], b.Snapshot()); }
    [Fact] public void ResizeKeepsNewest() { var b = new RollingBuffer<int>(5); foreach (var i in Enumerable.Range(1, 5)) b.Add(i); b.Resize(2); Assert.Equal([4, 5], b.Snapshot()); }
    [Fact] public void RangeUsesTimestamps() { var b = new RollingBuffer<(long Time, string Value)>(5, x => x.Time); b.Add((10, "a")); b.Add((20, "b")); b.Add((30, "c")); Assert.Equal(["b", "c"], b.Range(15, 30).Select(x => x.Value)); }
    [Fact] public void ClearDropsAllRetainedReferencesWithoutChangingCapacity() { var b = new RollingBuffer<object>(3); b.Add(new()); b.Add(new()); b.Clear(); Assert.Empty(b.Snapshot()); Assert.Equal(3, b.Capacity); }
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
    [Fact] public void FullFrameInterlacedHasHalfFrameCadence() { var f = new CaptureFormat(1920, 1080, Rational.From(25), ScanMode.Interlaced, FieldOrder.TopFirst, InterlaceLayout: InterlaceLayout.FullFrame); Assert.Equal(20, f.TemporalImageDuration.TotalMilliseconds); Assert.Contains("50.000i", f.Display); }
    [Fact] public void SingleFieldInterlacedDoesNotDoubleNativeTemporalRate() { var f = new CaptureFormat(1920, 1080, Rational.From(50), ScanMode.Interlaced, FieldOrder.TopFirst, InterlaceLayout: InterlaceLayout.SingleField); Assert.Equal(20, f.TemporalImageDuration.TotalMilliseconds); Assert.Contains("50.000i", f.Display); }
    [Fact] public void UnknownScanIsNotDisplayedAsProgressiveOrInterlaced() { var display = new CaptureFormat(1920, 1080, Rational.From(50), ScanMode.Unknown, FieldOrder.Unknown).Display; Assert.Contains("50.000 fps", display); Assert.Contains("scan unknown", display); Assert.DoesNotContain("50.000p", display); Assert.DoesNotContain("50.000i", display); }
    [Fact] public void FiftyProgressiveFramesHaveTwentyMillisecondTemporalResolution() => Assert.Equal(20, new CaptureFormat(1920, 1080, Rational.From(50), ScanMode.Progressive, FieldOrder.Unknown).TemporalImageDuration.TotalMilliseconds);
    [Fact] public void VideoFormatDoesNotClaimAudioBeforeItIsMeasured() => Assert.DoesNotContain("audio", new CaptureFormat(1920, 1080, Rational.From(50), ScanMode.Progressive, FieldOrder.Unknown).Display, StringComparison.OrdinalIgnoreCase);
}

public sealed class TransientDetectorTests
{
    [Fact] public void SilenceAndNoiseDoNotTrigger() => Assert.Empty(new TransientDetector().Process(Chunk(0, Noise(4800, .003f), 48000)));
    [Fact] public void SingleImpulseTriggers() { var samples = Noise(4800, .002f); Pulse(samples, 1000, .8f); Assert.Single(new TransientDetector().Process(Chunk(0, samples, 48000))); }
    [Fact] public void EchoIsDebounced() { var samples = Noise(24000, .002f); Pulse(samples, 1000, .9f); Pulse(samples, 6000, .55f); Assert.Single(new TransientDetector().Process(Chunk(0, samples, 48000))); }
    [Theory] [InlineData(44100)] [InlineData(48000)] [InlineData(96000)] public void WorksAtDifferentRates(int rate) { var samples = Noise(rate / 4, .001f); Pulse(samples, rate / 10, .7f); Assert.Single(new TransientDetector().Process(Chunk(0, samples, rate))); }
    [Fact] public void TwoSeparatedImpulsesTriggerTwice() { var samples = Noise(48000, .001f); Pulse(samples, 1000, .8f); Pulse(samples, 20000, .8f); Assert.Equal(2, new TransientDetector().Process(Chunk(0, samples, 48000)).Count); }
    [Fact] public void SensitivityChangesWeakTransientAcceptanceWithoutChangingDefault() { var weak = Noise(4800, .001f); Pulse(weak, 1000, .03f); var low = new TransientDetector { Sensitivity = 0 }; var high = new TransientDetector { Sensitivity = 100 }; Assert.Empty(low.Process(Chunk(0, weak, 48000))); Assert.Single(high.Process(Chunk(0, weak, 48000))); }
    [Fact]
    public void FiveHundredIndependentEventCyclesRemainDeterministic()
    {
        for (var cycle = 0; cycle < 500; cycle++)
        {
            var samples = Noise(4_800, .002f); Pulse(samples, 1_000, .8f);
            Assert.Single(new TransientDetector().Process(Chunk(cycle * 100_000L, samples, 48_000)));
        }
    }
    private static AudioChunk Chunk(long ticks, float[] samples, int rate) => new(new(ticks, TimingQuality.StreamTimestamp), samples, rate, 1);
    private static float[] Noise(int count, float level) => Enumerable.Range(0, count).Select(i => (i % 7 - 3) * level / 3).ToArray();
    private static void Pulse(float[] samples, int at, float level) { for (var i = 0; i < 40; i++) samples[at + i] = level * (float)Math.Exp(-i / 10d); }
}

public sealed class SyntheticCaptureLifetimeTests
{
    [Fact]
    public async Task OneHundredStartStopCyclesLeaveNoActiveSyntheticSession()
    {
        for (var cycle = 0; cycle < 100; cycle++)
        {
            var backend = new SyntheticCaptureBackend();
            var device = Assert.Single(await backend.EnumerateDevicesAsync(CancellationToken.None));
            await using var session = await backend.OpenAsync(device, new(160, 90, PreferredPresentationWidth: 80, PreferredPresentationHeight: 45), CancellationToken.None);
            await session.StartAsync(CancellationToken.None);
            await session.StopAsync(CancellationToken.None);
        }
    }
}

public sealed class WorkWindowWaveformAndHistoryTests
{
    [Fact] public void SelectsSymmetricWindow() { var frames = Enumerable.Range(-5, 11).Select(i => new VideoFrame(new(i * 100_000, TimingQuality.StreamTimestamp), 1, 1, [0], i)).ToArray(); var selected = WorkWindowSelector.Around(frames, f => f.Timestamp, new(0, TimingQuality.StreamTimestamp), TimeSpan.FromMilliseconds(20)); Assert.Equal([-2, -1, 0, 1, 2], selected.Select(f => f.TemporalIndex)); }
    [Fact] public void WaveformPlacesImpulseNearCenter() { var samples = new float[480]; samples[240] = 1; var center = new MediaTimestamp(50_000, TimingQuality.StreamTimestamp); var chunk = new AudioChunk(new(0, TimingQuality.StreamTimestamp), samples, 48000, 1); var wave = new WaveformBuilder().Build([chunk], center, TimeSpan.FromMilliseconds(5), 100); Assert.InRange(Array.IndexOf(wave.ToArray(), wave.Max()), 49, 51); }
    [Fact] public void HistoryKeepsOnlyLatest() { var h = new SessionHistoryService(3); for (var i = 0; i < 5; i++) h.Add(new(DateTime.MinValue.AddSeconds(i), new(i, i.ToString()), null)); Assert.Equal([4d, 3d, 2d], h.Items.Select(x => x.Result.SignedMilliseconds)); }
    [Fact] public void TimelineMapsClickToNearestRealFrameWithoutChangingMarks() { var frames = Enumerable.Range(-2, 5).Select(i => new VideoFrame(new(i * 200_000, TimingQuality.StreamTimestamp), 1, 1, [0], i)).ToArray(); Assert.Equal(3, ReviewTimeline.NearestFrameIndex(frames, new(0, TimingQuality.StreamTimestamp), 21)); }
    [Fact] public void HoldGateRequiresAutoDetectAndNoHold() { Assert.True(ReviewTimeline.AcceptsAutomaticEvents(true, false)); Assert.False(ReviewTimeline.AcceptsAutomaticEvents(true, true)); Assert.False(ReviewTimeline.AcceptsAutomaticEvents(false, false)); }
    [Fact] public void AutomaticAnalysisReturnsLiveWhileManualAnalysisKeepsReviewOpen() { Assert.True(ReviewTimeline.ReturnsLiveAfterAnalysis(true)); Assert.False(ReviewTimeline.ReturnsLiveAfterAnalysis(false)); }
    [Fact]
    public void FiveAutomaticResultsRemainAvailableAndCanRunSequentiallyWithoutResumeLive()
    {
        var history = new SessionHistoryService(10);
        for (var eventNumber = 1; eventNumber <= 5; eventNumber++)
        {
            Assert.True(ReviewTimeline.AcceptsAutomaticEvents(autoDetect: true, hold: false));
            var state = new EventReviewState(T(eventNumber * 1000), T(eventNumber * 1000));
            var frame = new VideoFrame(T(eventNumber * 1000 + eventNumber * 10), 1, 1, [0], eventNumber);
            state.SetAutoCandidate(new(frame.Timestamp, eventNumber, .9, 1, frame));

            Assert.True(state.TryFinalize(out var result));
            Assert.NotNull(result);
            history.Add(new(DateTime.MinValue.AddSeconds(eventNumber), result!, state.AutoCandidate!.Confidence));

            Assert.True(ReviewTimeline.ReturnsLiveAfterAnalysis(automaticEvent: true));
            Assert.Equal(result, state.CurrentResult);
            Assert.NotNull(state.AutoCandidate);
        }

        Assert.Equal(5, history.Items.Count);
        Assert.Equal([50d, 40d, 30d, 20d, 10d], history.Items.Select(item => item.Result.SignedMilliseconds));
    }
    [Fact] public void AudioCorrectionKeepsFixedEventReferenceAndAutoCandidate() { var state = StateWithAuto(); var reference = state.EventReference; var candidate = state.AutoCandidate; state.MoveAudioMark(35, 250); Assert.Equal(reference, state.EventReference); Assert.Same(candidate, state.AutoCandidate); Assert.Equal(35, state.AudioOffsetMs); Assert.Equal(80, state.AutoOffsetMs); }
    [Fact] public void AudioCorrectionClampsAtRetainedWindowEdge() { var state = StateWithAuto(); state.MoveAudioMark(900, 250); Assert.Equal(250, state.AudioOffsetMs); state.MoveAudioMark(-900, 250); Assert.Equal(-250, state.AudioOffsetMs); }
    [Fact] public void PlayheadMovementEntersManualPreviewAndUsesPlayheadMinusAudio() { var state = StateWithAuto(); state.MoveAudioMark(20, 250); state.MovePlayhead(T(70)); Assert.Equal(ReviewResultMode.ManualPreview, state.ResultMode); Assert.Equal(50, state.CurrentResult!.SignedMilliseconds); Assert.NotNull(state.AutoCandidate); }
    [Fact] public void ManualCommitUsesManualVisualWithoutDestroyingAuto() { var state = StateWithAuto(); state.MovePlayhead(T(-30)); state.CommitManualVisual(); Assert.Equal(ReviewResultMode.ManualResult, state.ResultMode); Assert.Equal(-30, state.CurrentResult!.SignedMilliseconds); Assert.NotNull(state.AutoCandidate); }
    [Fact] public void MovingAudioUpdatesCommittedManualResult() { var state = StateWithAuto(); state.MovePlayhead(T(60)); state.CommitManualVisual(); state.MoveAudioMark(25, 250); Assert.Equal(35, state.CurrentResult!.SignedMilliseconds); Assert.Equal("AUDIO LEADS VIDEO BY 35 ms", state.CurrentResult.Wording); }
    [Fact] public void ShowingAutoForComparisonDoesNotLoseManualResult() { var state = StateWithAuto(); state.MovePlayhead(T(45)); state.CommitManualVisual(); state.ShowAutoCandidate(); Assert.Equal(ReviewResultMode.ManualResult, state.ResultMode); Assert.Equal(45, state.CurrentResult!.SignedMilliseconds); Assert.Equal(80, state.PlayheadOffsetMs); }
    [Fact] public void EventCanBeFinalizedOnlyOnce() { var state = StateWithAuto(); Assert.True(state.TryFinalize(out var result)); Assert.NotNull(result); Assert.False(state.TryFinalize(out _)); }
    private static EventReviewState StateWithAuto() { var state = new EventReviewState(T(0), T(0)); var frame = new VideoFrame(T(80), 1, 1, [0], 4); state.SetAutoCandidate(new(T(80), 4, .5, 5, frame)); return state; }
    private static MediaTimestamp T(double milliseconds) => new((long)(milliseconds * 10_000), TimingQuality.StreamTimestamp);
}

public sealed class WaveformDisplayTransformTests
{
    [Fact]
    public void AutoGainTargetsNinetyPercentAndPreservesRatios()
    {
        var display = WaveformDisplayTransform.Transform([0f, .25f, .10f], WaveformDisplayTransform.AutoGain);
        Assert.Equal(.9, display.Max(), 3); Assert.Equal(.36, display[2], 3);
    }

    [Fact]
    public void AutoGainHandlesFullScaleSilenceAndNearSilenceWithoutArtificialGain()
    {
        Assert.Equal(.9, WaveformDisplayTransform.Transform([1f], WaveformDisplayTransform.AutoGain)[0], 3);
        Assert.Equal(.0001, WaveformDisplayTransform.Transform([.0001f], WaveformDisplayTransform.AutoGain)[0], 6);
        Assert.Equal(0, WaveformDisplayTransform.Transform([0f, 0f], WaveformDisplayTransform.AutoGain).Max());
    }

    [Fact]
    public void DecibelScaleUsesTwentyLogTenAndClampsAtTheSelectedFloor()
    {
        Assert.Equal(0, WaveformDisplayTransform.Decibels(1, -60), 6);
        Assert.Equal(-20, WaveformDisplayTransform.Decibels(.1, -60), 6);
        Assert.Equal(-40, WaveformDisplayTransform.Decibels(.01, -60), 6);
        Assert.Equal(-60, WaveformDisplayTransform.Decibels(0, -60), 6);
        Assert.Equal(-96, WaveformDisplayTransform.Decibels(.0000001, -96), 6);
    }

    [Fact]
    public void DisplayOptionsDoNotAlterTheWaveformSnapshotOrReviewMeasurement()
    {
        var source = new float[] { 0, .1f, .25f, 0 };
        var snapshot = source.ToArray();
        foreach (var amplitude in WaveformDisplayTransform.Amplitudes) _ = WaveformDisplayTransform.Transform(source, amplitude);
        Assert.Equal(snapshot, source);
        var state = new EventReviewState(T(0), T(0)); var frame = new VideoFrame(T(60), 1, 1, [0], 3); state.SetAutoCandidate(new(T(60), 3, .5, 2, frame));
        Assert.Equal(60, state.CurrentResult!.SignedMilliseconds); Assert.Equal(60, state.AutoCandidate!.Timestamp.Time.TotalMilliseconds);
    }

    [Theory]
    [InlineData("Mirrored", "Centered Bars")]
    [InlineData("Filled", "Centered Fill")]
    [InlineData("Line", "Centered Line")]
    [InlineData("Peaks", "Peak Bars")]
    [InlineData("Filled Peaks", "Peak Fill")]
    public void LegacyStylesMigrateToCanonicalNames(string legacy, string canonical) => Assert.Equal(canonical, WaveformDisplayTransform.NormalizeStyle(legacy));

    private static MediaTimestamp T(double milliseconds) => new((long)(milliseconds * 10_000), TimingQuality.StreamTimestamp);
}

public sealed class VideoTimingCompensationTests
{
    private static readonly FieldReconstructionOptions Reconstruction50 = new(Rational.From(25), Rational.From(50), FieldOrder.TopFirst);
    private static readonly FieldReconstructionOptions Reconstruction5994 = new(Rational.From(30_000, 1_001), Rational.From(60_000, 1_001), FieldOrder.BottomFirst);

    [Fact]
    public void AutomaticDefaultsUseOneExactReconstructedFieldIntervalOnly()
    {
        Assert.Equal(200_000, VideoTimingCompensation.Automatic(Reconstruction50).Ticks100ns);
        Assert.Equal(20, VideoTimingCompensation.Automatic(Reconstruction50).Milliseconds, 6);
        Assert.Equal(20, VideoTimingCompensation.Automatic(Reconstruction50 with { FieldOrder = FieldOrder.BottomFirst }).Milliseconds, 6);
        var fractional = VideoTimingCompensation.Automatic(Reconstruction5994);
        Assert.Equal(ReconstructedFieldTimestampModel.FieldIntervalTicks100ns(Rational.From(60_000, 1_001)), fractional.Ticks100ns);
        Assert.Equal(16.6833, fractional.Milliseconds, 4);
        Assert.Equal(0, VideoTimingCompensation.Automatic(null).Ticks100ns);
    }

    [Fact]
    public void PairWideCompensationPreservesContinuousFieldCadence()
    {
        var raw = new[] { 20d, 40d, 60d, 80d }.Select(T).ToArray();
        var effective = raw.Select(timestamp => VideoTimingCompensation.CorrectedOffsetMilliseconds(T(0), timestamp, VideoTimingCompensation.Automatic(Reconstruction50))).ToArray();
        Assert.Equal([0d, 20d, 40d, 60d], effective);
        Assert.All(effective.Zip(effective.Skip(1)), pair => Assert.Equal(20, pair.Second - pair.First));
    }

    [Fact]
    public void PhysicalExampleDisplaysCorrectedResultWithoutMutatingRawTimestamp()
    {
        var audio = T(0); var rawVisual = T(22.7); var originalTicks = rawVisual.Ticks100ns;
        var result = VideoTimingCompensation.Calculate(audio, rawVisual, VideoTimingCompensation.Automatic(Reconstruction50));
        Assert.Equal(2.7, result.SignedMilliseconds, 6);
        Assert.Equal("AUDIO LEADS VIDEO BY 2.7 ms", result.Wording);
        Assert.Equal(originalTicks, rawVisual.Ticks100ns);
    }

    [Fact]
    public void ManualOverrideWinsAndResetResolvesBackToAutomatic()
    {
        var manual = VideoTimingCompensation.Resolve(Reconstruction50, 18.5);
        Assert.True(manual.IsManual); Assert.Equal(18.5, manual.Milliseconds, 6);
        var reset = VideoTimingCompensation.Resolve(Reconstruction50, null);
        Assert.False(reset.IsManual); Assert.Equal(20, reset.Milliseconds, 6);
        Assert.Equal(0, VideoTimingCompensation.Resolve(null, null).Milliseconds);
    }

    [Fact]
    public void ProfileKeysSeparateDeviceFormatReconstructionOrderAndRate()
    {
        var reconstructed = VideoTimingProfileKey.Create("device-a", "1080p25-yuy2", Reconstruction50);
        Assert.NotEqual(reconstructed, VideoTimingProfileKey.Create("device-b", "1080p25-yuy2", Reconstruction50));
        Assert.NotEqual(reconstructed, VideoTimingProfileKey.Create("device-a", "1080p50-yuy2", null));
        Assert.NotEqual(reconstructed, VideoTimingProfileKey.Create("device-a", "1080p25-yuy2", Reconstruction50 with { FieldOrder = FieldOrder.BottomFirst }));
        Assert.NotEqual(reconstructed, VideoTimingProfileKey.Create("device-a", "1080p25-yuy2", Reconstruction5994));
    }

    [Fact]
    public void ProfileSwitchRestoresOnlyThatProfilesManualOverride()
    {
        var reconstructedKey = VideoTimingProfileKey.Create("device", "1080p25-yuy2", Reconstruction50);
        var progressiveKey = VideoTimingProfileKey.Create("device", "1080p50-yuy2", null);
        var overrides = new Dictionary<string, double> { [reconstructedKey] = 18.5 };
        Assert.Equal(18.5, VideoTimingCompensation.Resolve(Reconstruction50, overrides.GetValueOrDefault(reconstructedKey)).Milliseconds, 6);
        Assert.Equal(0, VideoTimingCompensation.Resolve(null, overrides.TryGetValue(progressiveKey, out var progressive) ? progressive : null).Milliseconds);
        Assert.Equal(18.5, VideoTimingCompensation.Resolve(Reconstruction50, overrides.GetValueOrDefault(reconstructedKey)).Milliseconds, 6);
    }

    [Fact]
    public void EventReviewAppliesOffsetToAutoPreviewAndManualWithoutChangingMarks()
    {
        var offset = VideoTimingCompensation.Automatic(Reconstruction50);
        var state = new EventReviewState(T(0), T(0), offset);
        var frame = new VideoFrame(T(22.7), 1, 1, [0], 1);
        state.SetAutoCandidate(new(frame.Timestamp, 1, .8, 4, frame));
        Assert.Equal(22.7, state.CurrentRawResult!.SignedMilliseconds, 6);
        Assert.Equal(2.7, state.CurrentResult!.SignedMilliseconds, 6);
        state.MovePlayhead(T(30)); Assert.Equal(10, state.CurrentResult!.SignedMilliseconds, 6);
        state.CommitManualVisual(); Assert.Equal(10, state.CurrentResult!.SignedMilliseconds, 6);
        Assert.Equal(300_000, state.ManualVisualMark!.Value.Ticks100ns);
    }

    [Fact]
    public void ExpectedRawDetectorPositionAndReviewWindowMoveLaterByOffset()
    {
        var expected = VideoTimingCompensation.ExpectedRawVisual(T(1000), VideoTimingCompensation.Automatic(Reconstruction50));
        Assert.Equal(1020, expected.Ticks100ns / 10_000d, 6);
        var frames = Enumerable.Range(95, 12).Select(value => new VideoFrame(T(value * 10), 1, 1, [0], value)).ToArray();
        var selected = WorkWindowSelector.Around(frames, frame => frame.Timestamp, expected, TimeSpan.FromMilliseconds(20));
        Assert.Equal([1000d, 1010d, 1020d, 1030d, 1040d], selected.Select(frame => frame.Timestamp.Ticks100ns / 10_000d));
    }

    [Fact]
    public void CompensationDoesNotMakeUnrelatedClocksComparable()
    {
        var audio = new MediaTimestamp(0, TimingQuality.Unrelated, "audio");
        var visual = new MediaTimestamp(227_000, TimingQuality.DeviceHardware, "video");
        var result = VideoTimingCompensation.Calculate(audio, visual, VideoTimingCompensation.Automatic(Reconstruction50));
        Assert.False(result.TimingComparable); Assert.Equal("TIMING DOMAINS NOT CORRELATED", result.Wording);
    }

    [Fact]
    public void CompensationLeavesAllCapturedTimingMetadataAndAnalysisUntouched()
    {
        var observation = new VideoTimingObservation(VideoPrimaryTimestampSource.DeviceTimestamp, 1234, 10_000_000, 400_000, 400_000, 400_000);
        var frame = new VideoFrame(T(40), 1, 1, [7], 3, TimingObservation: observation, NativeSampleIndex: 9);
        var before = new FrameTimingAnalyzer().Analyze([frame], 50, 25);
        _ = VideoTimingCompensation.Calculate(T(0), frame.Timestamp, VideoTimingCompensation.Automatic(Reconstruction50));
        var after = new FrameTimingAnalyzer().Analyze([frame], 50, 25);
        Assert.Equal(T(40), frame.Timestamp); Assert.Same(observation, frame.TimingObservation); Assert.Equal(9, frame.NativeSampleIndex);
        Assert.Equal(before, after);
    }

    private static MediaTimestamp T(double milliseconds) => new((long)Math.Round(milliseconds * 10_000d), TimingQuality.StreamTimestamp);
}

public sealed class VisualDetectorTests(ITestOutputHelper output)
{
    [Fact] public async Task NoMotionHasNoCandidate() { var f = Frames(false); Assert.Null(await new MotionVisualClapDetector().DetectAsync(f, f[5].Timestamp, default)); }
    [Fact] public async Task ContactSequenceSelectsNearExpected() { var f = Frames(true); var c = await new MotionVisualClapDetector().DetectAsync(f, f[5].Timestamp, default); Assert.NotNull(c); Assert.InRange(c!.TemporalIndex, 3, 7); }
    [Fact] public async Task ContinuousBackgroundMotionDoesNotBecomeAClap() { var frames = Enumerable.Range(0, 12).Select(i => new VideoFrame(new(i * 100_000, TimingQuality.StreamTimestamp), 32, 18, Enumerable.Repeat((byte)(20 + i * 4), 32 * 18).ToArray(), i)).ToArray(); Assert.Null(await new MotionVisualClapDetector().DetectAsync(frames, frames[6].Timestamp, default)); }
    [Fact] public async Task BriefGlobalBrightnessFlashIsRejected() { var frames = Enumerable.Range(0, 11).Select(i => new VideoFrame(new(i * 100_000, TimingQuality.StreamTimestamp), 64, 36, Enumerable.Repeat((byte)(i == 5 ? 220 : 30), 64 * 36).ToArray(), i)).ToArray(); Assert.Null(await new MotionVisualClapDetector().DetectAsync(frames, frames[5].Timestamp, default)); }
    [Fact] public async Task SmallLocalizedContactStillFindsCandidate() { var frames = Enumerable.Range(0, 11).Select(i => { var pixels = Enumerable.Repeat((byte)35, 80 * 48).ToArray(); if (i is >= 5 and <= 7) Array.Fill(pixels, (byte)235, 1_720, 180); return new VideoFrame(new(i * 100_000, TimingQuality.StreamTimestamp), 80, 48, pixels, i); }).ToArray(); var candidate = await new MotionVisualClapDetector().DetectAsync(frames, frames[5].Timestamp, default); Assert.NotNull(candidate); Assert.InRange(candidate!.TemporalIndex, 5, 6); }
    [Theory] [InlineData(25, 1)] [InlineData(50, 1)] [InlineData(30000, 1001)] [InlineData(60, 1)] public async Task RisePeakDropSelectsContactFrameAtMultipleCadences(int numerator, int denominator) { var frames = LocalClapFrames(Rational.From(numerator, denominator), 35, 210, 150, 82, 12, 10); var candidate = await new MotionVisualClapDetector().DetectAsync(frames, frames[4].Timestamp, default); Assert.NotNull(candidate); Assert.Equal(4, candidate!.TemporalIndex); }
    [Theory] [InlineData(35, 62)] [InlineData(170, 30)] public async Task DistantLowAndHighContrastClapsRemainDetectable(byte background, byte hand) { var frames = LocalClapFrames(Rational.From(50), background, hand, 152, 84, 8, 6); var candidate = await new MotionVisualClapDetector().DetectAsync(frames, frames[4].Timestamp, default); Assert.NotNull(candidate); Assert.Equal(4, candidate!.TemporalIndex); Assert.InRange(candidate.Confidence, .08, .94); }
    [Theory] [InlineData(18, 16)] [InlineData(150, 84)] [InlineData(286, 148)] public async Task SmallClapWorksAtDifferentFramePositions(int x, int y) { var frames = LocalClapFrames(Rational.From(50), 40, 190, x, y, 8, 6); var candidate = await new MotionVisualClapDetector().DetectAsync(frames, frames[4].Timestamp, default); Assert.NotNull(candidate); Assert.Equal(4, candidate!.TemporalIndex); }
    [Fact] public async Task CloseLargeClapIsDetected() { var frames = LocalClapFrames(Rational.From(50), 30, 220, 120, 65, 70, 45); Assert.NotNull(await new MotionVisualClapDetector().DetectAsync(frames, frames[4].Timestamp, default)); }
    [Fact] public async Task SteadyLocalizedMovementIsNotAClap() { var frames = Enumerable.Range(0, 12).Select(i => Frame(i, pixels => FillRect(pixels, 320, 30 + i * 3, 80, 12, 10, 190))).ToArray(); Assert.Null(await new MotionVisualClapDetector().DetectAsync(frames, frames[6].Timestamp, default)); }
    [Fact] public async Task WholeFrameCameraMovementIsRejected() { var frames = Enumerable.Range(0, 12).Select(i => new VideoFrame(Ticks(i), 320, 180, Enumerable.Range(0, 320 * 180).Select(p => (byte)(25 + ((p % 320 + i * 5) % 80))).ToArray(), i)).ToArray(); Assert.Null(await new MotionVisualClapDetector().DetectAsync(frames, frames[6].Timestamp, default)); }
    [Fact] public async Task RandomSparseNoiseIsRejected() { var frames = Enumerable.Range(0, 12).Select(i => Frame(i, pixels => { var random = new Random(100 + i); for (var n = 0; n < 18; n++) pixels[random.Next(pixels.Length)] = 220; })).ToArray(); Assert.Null(await new MotionVisualClapDetector().DetectAsync(frames, frames[6].Timestamp, default)); }
    [Fact] public async Task SensitivityChangesWeakDistantAcceptanceWithoutChangingConfidence() { var frames = HighResolutionDistantFrames(); var detector = new MotionVisualClapDetector(); var low = await detector.DetectAsync(frames, frames[4].Timestamp, default, new(0)); var medium = await detector.DetectAsync(frames, frames[4].Timestamp, default, new(50)); var high = await detector.DetectAsync(frames, frames[4].Timestamp, default, new(100)); Assert.Null(low); Assert.NotNull(medium); Assert.NotNull(high); Assert.Equal(medium!.Confidence, high!.Confidence, 10); Assert.InRange(high.Confidence, .08, .7); }
    [Fact] public async Task HighSensitivityStillRejectsGlobalFlash() { var frames = Enumerable.Range(0, 11).Select(i => new VideoFrame(new(i * 100_000, TimingQuality.StreamTimestamp), 640, 360, Enumerable.Repeat((byte)(i == 5 ? 220 : 30), 640 * 360).ToArray(), i)).ToArray(); Assert.Null(await new MotionVisualClapDetector().DetectAsync(frames, frames[5].Timestamp, default, new(100))); }
    [Fact] public async Task HighSensitivityStillRejectsWholeFrameMotion() { var frames = Enumerable.Range(0, 12).Select(i => new VideoFrame(Ticks(i), 640, 360, Enumerable.Range(0, 640 * 360).Select(p => (byte)(25 + ((p % 640 + i * 5) % 80))).ToArray(), i)).ToArray(); Assert.Null(await new MotionVisualClapDetector().DetectAsync(frames, frames[6].Timestamp, default, new(100))); }
    [Fact] public async Task HighSensitivityStillRejectsSparseNoise() { var frames = Enumerable.Range(0, 12).Select(i => { var pixels = Enumerable.Repeat((byte)35, 640 * 360).ToArray(); var random = new Random(200 + i); for (var n = 0; n < 40; n++) pixels[random.Next(pixels.Length)] = 220; return new VideoFrame(Ticks(i), 640, 360, pixels, i); }).ToArray(); Assert.Null(await new MotionVisualClapDetector().DetectAsync(frames, frames[6].Timestamp, default, new(100))); }
    [Fact] public async Task RepresentativeHighResolutionWindowRemainsInteractive() { var frames = HighResolutionDistantFrames(); var stopwatch = Stopwatch.StartNew(); var candidate = await new MotionVisualClapDetector().DetectAsync(frames, frames[4].Timestamp, default, new(50)); stopwatch.Stop(); output.WriteLine($"640x360 detector runtime: {stopwatch.Elapsed.TotalMilliseconds:0.0} ms for {frames.Length} frames"); Assert.NotNull(candidate); Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2)); }
    [Theory] [InlineData(320, 180)] [InlineData(640, 360)] [InlineData(960, 540)] public async Task ProcessingResolutionPreservesContactRegionWhileIncreasingDetail(int width, int height) { var frames = ScaledClapFrames(width, height); var candidate = await new MotionVisualClapDetector().DetectAsync(frames, frames[4].Timestamp, default); Assert.NotNull(candidate); Assert.InRange(candidate!.TemporalIndex, 3, 4); var detail = MotionVisualClapDetector.DetailDescription(width, height); Assert.Contains($"{width / 20}x{height / 20} cells", detail); }
    [Fact] public async Task ObservedApproachSequenceSelectsContactNotMotionPeak() { var frames = Downsample(ContactFixture(), 640, 360); var candidate = await new MotionVisualClapDetector().DetectAsync(frames, frames[3].Timestamp, default); Assert.NotNull(candidate); Assert.Equal(4, candidate!.TemporalIndex); Assert.InRange(candidate.Trace!.ApproachPeakIndex, 1, 3); Assert.Equal(4, candidate.Trace.FinalContactIndex); Assert.InRange(candidate.Trace.PositionsAdvanced, 1, 3); }
    [Fact] public async Task FastClapSelectsFirstPostPeakContact() { var frames = Downsample(ContactFixture(contactIndex: 3), 640, 360); var candidate = await new MotionVisualClapDetector().DetectAsync(frames, frames[3].Timestamp, default); Assert.NotNull(candidate); Assert.Equal(3, candidate!.TemporalIndex); }
    [Fact] public async Task SlowApproachWithoutContactIsRejected() { var frames = Enumerable.Range(0, 10).Select(i => Frame(i, pixels => FillRect(pixels, 320, 30 + i * 5, 80, 12, 10, 190))).ToArray(); Assert.Null(await new MotionVisualClapDetector().DetectAsync(frames, frames[5].Timestamp, default)); }
    [Fact] public async Task ReconstructedBottomFieldContactIsSelected() { var frames = Downsample(ContactFixture(contactIndex: 3), 640, 360).Select((frame, index) => frame with { TemporalImageKind = index % 2 == 0 ? TemporalImageKind.TopField : TemporalImageKind.BottomField, NativeSampleIndex = index / 2 }).ToArray(); var candidate = await new MotionVisualClapDetector().DetectAsync(frames, frames[3].Timestamp, default); Assert.NotNull(candidate); Assert.Equal(3, candidate!.TemporalIndex); Assert.Equal(TemporalImageKind.BottomField, candidate.Frame.TemporalImageKind); }
    [Fact] public async Task ProgressiveEquivalentSelectsSamePhysicalContactPhase() { var frames = Downsample(ContactFixture(contactIndex: 3), 640, 360); var candidate = await new MotionVisualClapDetector().DetectAsync(frames, frames[3].Timestamp, default); Assert.NotNull(candidate); Assert.Equal(3, candidate!.TemporalIndex); Assert.Equal(TemporalImageKind.ProgressiveFrame, candidate.Frame.TemporalImageKind); }
    [Theory] [InlineData(320, 180)] [InlineData(640, 360)] [InlineData(960, 540)] [InlineData(1280, 720)] public async Task SameHighRasterContactIsResolutionInvariant(int width, int height) { var frames = Downsample(ContactFixture(), width, height); var candidate = await new MotionVisualClapDetector().DetectAsync(frames, frames[4].Timestamp, default); Assert.NotNull(candidate); Assert.Equal(4, candidate!.TemporalIndex); }
    [Theory] [InlineData(320, 180)] [InlineData(640, 360)] [InlineData(960, 540)] [InlineData(1280, 720)] public void DetectionResolutionDoesNotEnterTimestampMath(int width, int height) { var timestamp = new MediaTimestamp(1_234_567, TimingQuality.DeviceHardware, "qpc", 99); var observation = new VideoTimingObservation(VideoPrimaryTimestampSource.DeviceTimestamp, 8, 10_000_000, 1_234_567, 1_234_500, 1_234_400); var frame = new VideoFrame(timestamp, width, height, new byte[width * height], 42, TemporalImageKind.BottomField, TimestampOrigin.CaptureTimestampAssumedSecondField, width, TimingObservation: observation, NativeSampleIndex: 21); Assert.Equal(timestamp, frame.Timestamp); Assert.Same(observation, frame.TimingObservation); Assert.Equal(21, frame.NativeSampleIndex); Assert.Equal(42, frame.TemporalIndex); Assert.Equal(2.7, VideoTimingCompensation.Calculate(new(1_007_567, TimingQuality.DeviceHardware, "qpc"), frame.Timestamp, new(200_000, VideoTimingOffsetSource.Automatic)).SignedMilliseconds, 8); }
    [Fact] public void SyntheticFramesCarryHigherResolutionLumaAndBoundedColourPresentation() { var frame = SyntheticCaptureSession.CreateFrame(TimeSpan.Zero, 1, 0); Assert.True(frame.HasPresentation); Assert.Equal(160 * 90 * 4, frame.PresentationBgra!.Length); Assert.Equal(640 * 360, frame.Luma.Length); }
    [Theory] [InlineData(320, 180)] [InlineData(640, 360)] [InlineData(960, 540)] public void SyntheticFramesPropagateConfiguredProcessingDimensions(int width, int height) { var reviewWidth = width / 2; var reviewHeight = height / 2; var options = new CaptureOpenOptions(width, height, PreferredPresentationWidth: reviewWidth, PreferredPresentationHeight: reviewHeight); Assert.Equal((width, height), (options.PreferredAnalysisWidth, options.PreferredAnalysisHeight)); var frame = SyntheticCaptureSession.CreateFrame(TimeSpan.Zero, 1, 7, width, height, reviewWidth, reviewHeight); Assert.Equal((width, height, width * height), (frame.Width, frame.Height, frame.Luma.Length)); Assert.Equal((reviewWidth, reviewHeight, reviewWidth * reviewHeight * 4), (frame.PresentationWidth, frame.PresentationHeight, frame.PresentationBgra!.Length)); Assert.Equal(7, frame.NativeSampleIndex); }
    [Fact] public void ReconnectAndResolutionChangesDoNotChangeSyntheticCapturePhase() { var before = SyntheticCaptureSession.CreateFrame(TimeSpan.FromMilliseconds(120), 1, 6, 640, 360, 160, 90); var sameResolutionReconnect = SyntheticCaptureSession.CreateFrame(TimeSpan.FromMilliseconds(120), 1, 6, 640, 360, 160, 90); var changedResolutionReconnect = SyntheticCaptureSession.CreateFrame(TimeSpan.FromMilliseconds(120), 1, 6, 960, 540, 160, 90); Assert.Equal(before.Timestamp, sameResolutionReconnect.Timestamp); Assert.Equal(before.Timestamp, changedResolutionReconnect.Timestamp); Assert.Equal(before.NativeSampleIndex, changedResolutionReconnect.NativeSampleIndex); Assert.Equal(before.TemporalIndex, changedResolutionReconnect.TemporalIndex); }
    private static VideoFrame[] Frames(bool contact) => Enumerable.Range(0, 11).Select(i => { var pixels = new byte[64 * 36]; if (contact && i == 4) Array.Fill(pixels, (byte)100, 800, 500); if (contact && i is >= 5 and <= 7) Array.Fill(pixels, (byte)240, 800, 500); return new VideoFrame(new(i * 100_000, TimingQuality.StreamTimestamp), 64, 36, pixels, i); }).ToArray();
    private static VideoFrame[] LocalClapFrames(Rational cadence, byte background, byte hand, int x, int y, int width, int height) => Enumerable.Range(0, 10).Select(i =>
    {
        var pixels = Enumerable.Repeat(background, 320 * 180).ToArray();
        if (i == 2) FillRect(pixels, 320, Math.Max(0, x - width), y, Math.Max(2, width / 2), height, hand);
        if (i is >= 3 and <= 6) FillRect(pixels, 320, x, y, width, height, hand);
        if (i == 7) FillRect(pixels, 320, Math.Min(319 - width, x + width), y, width, height, hand);
        return new VideoFrame(MediaTimestamp.FromTimeSpan(TimeSpan.FromTicks(cadence.FrameDuration.Ticks * i), TimingQuality.StreamTimestamp), 320, 180, pixels, i);
    }).ToArray();
    private static VideoFrame Frame(int index, Action<byte[]> mutate) { var pixels = Enumerable.Repeat((byte)35, 320 * 180).ToArray(); mutate(pixels); return new(Ticks(index), 320, 180, pixels, index); }
    private static VideoFrame[] HighResolutionDistantFrames() => Enumerable.Range(0, 10).Select(i =>
    {
        var pixels = Enumerable.Repeat((byte)45, 640 * 360).ToArray();
        if (i == 2) FillRectSized(pixels, 640, 360, 302, 170, 5, 7, 68);
        if (i is >= 3 and <= 6) FillRectSized(pixels, 640, 360, 315, 170, 10, 7, 68);
        if (i == 7) FillRectSized(pixels, 640, 360, 334, 170, 10, 7, 68);
        return new VideoFrame(new(i * 200_000L, TimingQuality.StreamTimestamp), 640, 360, pixels, i);
    }).ToArray();
    private static VideoFrame[] ScaledClapFrames(int width, int height) => Enumerable.Range(0, 10).Select(i =>
    {
        var pixels = Enumerable.Repeat((byte)35, width * height).ToArray(); var boxWidth = Math.Max(8, width / 20); var boxHeight = Math.Max(6, height / 24); var x = width / 2; var y = height / 2;
        if (i == 2) FillRectSized(pixels, width, height, x - boxWidth, y, Math.Max(3, boxWidth / 2), boxHeight, 200);
        if (i is >= 3 and <= 6) FillRectSized(pixels, width, height, x, y, boxWidth, boxHeight, 200);
        if (i == 7) FillRectSized(pixels, width, height, x + boxWidth, y, boxWidth, boxHeight, 200);
        return new VideoFrame(new(i * 200_000L, TimingQuality.StreamTimestamp), width, height, pixels, i);
    }).ToArray();
    private static VideoFrame[] ContactFixture(int contactIndex = 4) => Enumerable.Range(0, 10).Select(i =>
    {
        const int width = 1280, height = 720; var pixels = Enumerable.Repeat((byte)35, width * height).ToArray(); const int boxWidth = 64, boxHeight = 30, x = 640, y = 360;
        if (i == contactIndex - 2) FillRectSized(pixels, width, height, x - boxWidth, y, boxWidth / 2, boxHeight, 210);
        if (i >= contactIndex - 1 && i <= contactIndex + 2) FillRectSized(pixels, width, height, x, y, boxWidth, boxHeight, 210);
        if (i == contactIndex + 3) FillRectSized(pixels, width, height, x + boxWidth, y, boxWidth, boxHeight, 210);
        return new VideoFrame(new(i * 200_000L, TimingQuality.DeviceHardware, "qpc", i), width, height, pixels, i, NativeSampleIndex: i);
    }).ToArray();
    private static VideoFrame[] Downsample(IReadOnlyList<VideoFrame> source, int width, int height) => source.Select(frame =>
    {
        var pixels = new byte[width * height];
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++) pixels[y * width + x] = frame.Luma[y * frame.Height / height * frame.Width + x * frame.Width / width];
        return frame with { Width = width, Height = height, Luma = pixels, Stride = width };
    }).ToArray();
    private static MediaTimestamp Ticks(int index) => new(index * 200_000L, TimingQuality.StreamTimestamp);
    private static void FillRect(byte[] pixels, int stride, int x, int y, int width, int height, byte value) { for (var row = y; row < Math.Min(180, y + height); row++) Array.Fill(pixels, value, row * stride + Math.Max(0, x), Math.Min(width, stride - Math.Max(0, x))); }
    private static void FillRectSized(byte[] pixels, int stride, int imageHeight, int x, int y, int width, int height, byte value) { for (var row = y; row < Math.Min(imageHeight, y + height); row++) Array.Fill(pixels, value, row * stride + Math.Max(0, x), Math.Min(width, stride - Math.Max(0, x))); }
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
    [Fact]
    public async Task LiveSyntheticPatternSelectsDeclaredPositiveSixtyMillisecondContact()
    {
        var audio = MediaTimestamp.FromTimeSpan(TimeSpan.FromSeconds(1), TimingQuality.StreamTimestamp, "synthetic-common");
        var frames = Enumerable.Range(25, 51).Select(index => SyntheticCaptureSession.CreateFrame(TimeSpan.FromMilliseconds(index * 20), 1.06, index, 320, 180, 160, 90)).ToArray();
        var visual = await new MotionVisualClapDetector().DetectAsync(frames, audio, default);
        Assert.NotNull(visual); var result = SyncResult.Calculate(audio, visual!.Timestamp);
        Assert.Equal(60, result.SignedMilliseconds, 6); Assert.Equal("AUDIO LEADS VIDEO BY 60 ms", result.Wording);
        Assert.Contains("expected +60 ms (audio leads video)", SyntheticCaptureBackend.DisplayName(TimeSpan.FromMilliseconds(60)));
    }

    [Theory]
    [InlineData(5)] [InlineData(10)] [InlineData(20)] [InlineData(60)] [InlineData(120)]
    [InlineData(-5)] [InlineData(-10)] [InlineData(-20)] [InlineData(-60)] [InlineData(-120)]
    public async Task KnownOffsetProducesCorrectLeadLag(double offsetMs)
    {
        var fixture = SyntheticFixture.Create(TimeSpan.FromMilliseconds(offsetMs)); var detector = new TransientDetector(); var transient = fixture.Audio.SelectMany(detector.Process).Single();
        var frames = WorkWindowSelector.Around(fixture.Video, f => f.Timestamp, transient.Timestamp, TimeSpan.FromMilliseconds(250)); var visual = await new MotionVisualClapDetector().DetectAsync(frames, transient.Timestamp, default);
        Assert.NotNull(visual); var result = SyncResult.Calculate(transient.Timestamp, visual!.Timestamp); Assert.True(result.TimingComparable); Assert.Equal(Math.Sign(offsetMs), Math.Sign(result.SignedMilliseconds)); Assert.InRange(Math.Abs(result.SignedMilliseconds - offsetMs), 0, 5);
    }

    [Theory] [InlineData(25, 1)] [InlineData(50, 1)] [InlineData(30000, 1001)] public async Task MeasurementStaysWithinOneNativeFrameAtDifferentCadences(int numerator, int denominator)
    {
        var rate = Rational.From(numerator, denominator); var fixture = SyntheticFixture.Create(TimeSpan.FromMilliseconds(80), rate); var transient = fixture.Audio.SelectMany(new TransientDetector().Process).Single();
        var frames = WorkWindowSelector.Around(fixture.Video, frame => frame.Timestamp, transient.Timestamp, TimeSpan.FromMilliseconds(250)); var visual = await new MotionVisualClapDetector().DetectAsync(frames, transient.Timestamp, default);
        Assert.NotNull(visual); var result = SyncResult.Calculate(transient.Timestamp, visual!.Timestamp);
        Assert.InRange(Math.Abs(result.SignedMilliseconds - 80), 0, rate.FrameDuration.TotalMilliseconds + 1);
    }

    [Fact] public async Task MismatchedSyntheticClockDomainsRefuseMeasurement()
    {
        var fixture = SyntheticFixture.Create(TimeSpan.FromMilliseconds(60)); var transient = fixture.Audio.SelectMany(new TransientDetector().Process).Single();
        var unrelatedFrames = fixture.Video.Select(frame => frame with { Timestamp = frame.Timestamp with { ClockDomain = "other-clock" } }).ToArray(); var visual = await new MotionVisualClapDetector().DetectAsync(unrelatedFrames, transient.Timestamp, default);
        Assert.NotNull(visual); Assert.False(SyncResult.Calculate(transient.Timestamp, visual!.Timestamp).TimingComparable);
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
