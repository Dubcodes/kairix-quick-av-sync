using Kairix.QuickAVSync.Capture;
using Kairix.QuickAVSync.Models;
using Kairix.QuickAVSync.Services;

namespace Kairix.QuickAVSync.Core.Tests;

public sealed class VisualDetectorEngineTests
{
    [Fact]
    public async Task FactoryRunsOnlyTheSelectedSpecializedEngine()
    {
        var frames = FlashFrames();
        Assert.Null(await VisualDetectorFactory.Create(VisualDetectorKind.HumanClap).DetectAsync(frames, frames[5].Timestamp, default));
        Assert.Equal(5, (await VisualDetectorFactory.Create(VisualDetectorKind.FlashTestSignal).DetectAsync(frames, frames[5].Timestamp, default))!.TemporalIndex);
    }

    [Fact]
    public async Task HumanClapSelectsContactAndNotPostHoldReset()
    {
        var frames = HumanClapFrames();
        var candidate = await new MotionVisualClapDetector().DetectAsync(frames, frames[5].Timestamp, default);
        Assert.NotNull(candidate); Assert.Equal(5, candidate!.TemporalIndex); Assert.Equal(VisualDetectorKind.HumanClap, candidate.Trace!.DetectorKind);
        Assert.True(candidate.Trace.ApproachQuality > 0); Assert.True(candidate.Trace.ContinuityQuality > 0);
    }

    [Fact]
    public async Task HumanClapRejectsMotionOnsetWithoutApproach()
    {
        var frames = LocalizedOnsetFrames();
        Assert.Null(await new MotionVisualClapDetector().DetectAsync(frames, frames[5].Timestamp, default, new(100)));
    }

    [Fact]
    public async Task FlashSelectsFirstOnsetAndNotDisappearance()
    {
        var frames = FlashFrames();
        var candidate = await new FlashTestSignalVisualDetector().DetectAsync(frames, frames[7].Timestamp, default);
        Assert.NotNull(candidate); Assert.Equal(5, candidate!.TemporalIndex); Assert.Equal(VisualDetectorKind.FlashTestSignal, candidate.Trace!.DetectorKind);
        Assert.True(candidate.Trace.PreStability > .8); Assert.True(candidate.Trace.LumaDelta > 0);
    }

    [Fact]
    public async Task RegionalFlashWithinLargerImageIsDetected()
    {
        var frames = FlashFrames(width: 640, height: 360, regionWidth: 90, regionHeight: 70);
        var candidate = await new FlashTestSignalVisualDetector().DetectAsync(frames, frames[5].Timestamp, default);
        Assert.NotNull(candidate); Assert.Equal(5, candidate!.TemporalIndex);
    }

    [Fact]
    public async Task FlashRejectsGradualBrightnessChange()
    {
        var frames = Enumerable.Range(0, 12).Select(i => SolidFrame(i, 320, 180, (byte)(30 + i * 3))).ToArray();
        Assert.Null(await new FlashTestSignalVisualDetector().DetectAsync(frames, frames[6].Timestamp, default, new(100)));
    }

    [Fact]
    public async Task FlashRejectsContinuousMotionAndSparseNoise()
    {
        var motion = Enumerable.Range(0, 12).Select(i => Frame(i, 320, 180, pixels => FillRect(pixels, 320, 25 + i * 4, 80, 18, 14, 210))).ToArray();
        var noise = Enumerable.Range(0, 12).Select(i => Frame(i, 320, 180, pixels => { var random = new Random(300 + i); for (var n = 0; n < 30; n++) pixels[random.Next(pixels.Length)] = 230; })).ToArray();
        var detector = new FlashTestSignalVisualDetector();
        Assert.Null(await detector.DetectAsync(motion, motion[6].Timestamp, default, new(100)));
        Assert.Null(await detector.DetectAsync(noise, noise[6].Timestamp, default, new(100)));
    }

    [Fact]
    public async Task FlashChoosesBestValidOnsetAcrossMultipleTransitions()
    {
        var frames = Enumerable.Range(0, 15).Select(i => Frame(i, 320, 180, pixels =>
        {
            if (i is 3 or 4) FillRect(pixels, 320, 145, 75, 30, 30, 85);
            if (i is >= 10 and <= 12) FillRect(pixels, 320, 100, 55, 120, 70, 230);
        })).ToArray();
        var candidate = await new FlashTestSignalVisualDetector().DetectAsync(frames, frames[4].Timestamp, default, new(100));
        Assert.NotNull(candidate); Assert.Equal(10, candidate!.TemporalIndex);
    }

    [Fact]
    public async Task FlashSensitivityChangesAcceptanceWithoutManufacturingConfidence()
    {
        var frames = FlashFrames(regionWidth: 50, regionHeight: 40, flash: 90);
        var detector = new FlashTestSignalVisualDetector();
        var low = await detector.DetectAsync(frames, frames[5].Timestamp, default, new(0));
        var high = await detector.DetectAsync(frames, frames[5].Timestamp, default, new(100));
        Assert.Null(low); Assert.NotNull(high); Assert.InRange(high!.Confidence, .1, .95);
        var secondHigh = await detector.DetectAsync(frames, frames[5].Timestamp, default, new(100));
        Assert.Equal(high.Confidence, secondHigh!.Confidence, 10);
    }

    [Theory]
    [InlineData(VisualDetectorKind.HumanClap)]
    [InlineData(VisualDetectorKind.FlashTestSignal)]
    public async Task FiveSequentialSyntheticEventsResolveToTheSameKnownCue(VisualDetectorKind kind)
    {
        var detector = VisualDetectorFactory.Create(kind);
        for (var eventNumber = 0; eventNumber < 5; eventNumber++)
        {
            var audio = TimeSpan.FromSeconds(1 + eventNumber * 2);
            var visual = audio + TimeSpan.FromMilliseconds(60);
            var first = audio - TimeSpan.FromMilliseconds(300);
            var frames = Enumerable.Range(0, 31).Select(index =>
            {
                var now = first + TimeSpan.FromMilliseconds(index * 20);
                return SyntheticCaptureSession.CreateFrame(now, visual.TotalSeconds, eventNumber * 100 + index, 320, 180, 160, 90);
            }).ToArray();
            var candidate = await detector.DetectAsync(frames, MediaTimestamp.FromTimeSpan(audio, TimingQuality.StreamTimestamp, "synthetic-common"), default);
            Assert.NotNull(candidate); Assert.Equal(visual.Ticks, candidate!.Timestamp.Ticks100ns);
            Assert.Equal(60, SyncResult.Calculate(MediaTimestamp.FromTimeSpan(audio, TimingQuality.StreamTimestamp, "synthetic-common"), candidate.Timestamp).SignedMilliseconds, 6);
        }
    }

    private static VideoFrame[] HumanClapFrames() => Enumerable.Range(0, 13).Select(i => Frame(i, 320, 180, pixels =>
    {
        var gap = i switch { < 2 => 60, 2 => 48, 3 => 34, 4 => 18, < 9 => 0, _ => 60 };
        FillRect(pixels, 320, 160 - gap / 2 - 26, 70, 26, 40, 225);
        FillRect(pixels, 320, 160 + gap / 2, 70, 26, 40, 225);
    })).ToArray();

    private static VideoFrame[] FlashFrames(int width = 320, int height = 180, int regionWidth = 120, int regionHeight = 70, byte flash = 230) =>
        Enumerable.Range(0, 12).Select(i => Frame(i, width, height, pixels =>
        {
            if (i is >= 5 and <= 7) FillRect(pixels, width, (width - regionWidth) / 2, (height - regionHeight) / 2, regionWidth, regionHeight, flash);
        })).ToArray();

    private static VideoFrame[] LocalizedOnsetFrames() => Enumerable.Range(0, 11).Select(i => Frame(i, 320, 180, pixels =>
    {
        if (i is >= 5 and <= 7) FillRect(pixels, 320, 135, 70, 50, 40, 220);
    })).ToArray();

    private static VideoFrame SolidFrame(int index, int width, int height, byte value) =>
        new(Ticks(index), width, height, Enumerable.Repeat(value, width * height).ToArray(), index);

    private static VideoFrame Frame(int index, int width, int height, Action<byte[]> mutate)
    {
        var pixels = Enumerable.Repeat((byte)30, width * height).ToArray(); mutate(pixels);
        return new(Ticks(index), width, height, pixels, index);
    }

    private static MediaTimestamp Ticks(int index) => new(index * 200_000L, TimingQuality.StreamTimestamp, "fixture");

    private static void FillRect(byte[] pixels, int stride, int x, int y, int width, int height, byte value)
    {
        var imageHeight = pixels.Length / stride;
        for (var row = Math.Max(0, y); row < Math.Min(imageHeight, y + height); row++)
            for (var column = Math.Max(0, x); column < Math.Min(stride, x + width); column++)
                pixels[row * stride + column] = value;
    }
}

public sealed class AudioCandidateOutcomeTests
{
    private static readonly MediaTimestamp Audio = new(1_000_000, TimingQuality.StreamTimestamp, "fixture");
    private static readonly AudioTransient Strong = new(Audio, .8f, .002f, .25f, 3.2f, 400, 20);
    private static readonly AudioTransient Weak = new(Audio, .06f, .01f, .035f, 1.7f, 6, 1.5f);

    [Fact]
    public void VisualMatchAlwaysConfirmsAndStrongAudioWithoutVisualBecomesCandidate()
    {
        var frame = new VideoFrame(Audio with { Ticks100ns = 1_200_000 }, 1, 1, [0], 1);
        var visual = new VisualCandidate(frame.Timestamp, 1, .8, 8, frame);
        Assert.Equal(AutomaticEventOutcome.Confirmed, ReviewTimeline.ClassifyAutomaticOutcome(visual, Weak));
        Assert.Equal(AutomaticEventOutcome.AudioCandidate, ReviewTimeline.ClassifyAutomaticOutcome(null, Strong));
        Assert.Equal(AutomaticEventOutcome.Rejected, ReviewTimeline.ClassifyAutomaticOutcome(null, Weak));
    }

    [Fact]
    public void AudioCandidateHasNoFakeResultOrHistoryFinalizationButSupportsManualRecovery()
    {
        var review = new EventReviewState(Audio, Audio); review.SetAudioCandidate(Audio with { Ticks100ns = 1_200_000 });
        Assert.Equal(ReviewResultMode.AudioCandidate, review.ResultMode); Assert.Null(review.CurrentResult); Assert.False(review.TryFinalize(out _));
        review.MovePlayhead(Audio with { Ticks100ns = 1_300_000 });
        Assert.Equal(ReviewResultMode.ManualPreview, review.ResultMode); Assert.Equal(30, review.CurrentResult!.SignedMilliseconds);
    }

    [Fact]
    public void HoldBlocksReplacementWhileArmedAllowsLaterConfirmedEvent()
    {
        Assert.False(ReviewTimeline.AcceptsAutomaticEvents(true, true, hold: true, analysisRunning: false));
        Assert.True(ReviewTimeline.AcceptsAutomaticEvents(true, true, hold: false, analysisRunning: false));
        var frame = new VideoFrame(Audio, 1, 1, [0], 1);
        Assert.Equal(AutomaticEventOutcome.Confirmed, ReviewTimeline.ClassifyAutomaticOutcome(new(Audio, 1, .8, 8, frame), Strong));
    }

    [Fact]
    public void AudioFallbackUsesNamedImpulsiveEvidenceThresholds()
    {
        var accepted = AudioCandidateClassifier.Evaluate(Strong); var rejected = AudioCandidateClassifier.Evaluate(Weak);
        Assert.True(accepted.Passed); Assert.False(rejected.Passed); Assert.True(accepted.Quality > rejected.Quality);
    }
}
