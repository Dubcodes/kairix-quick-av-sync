using System.Diagnostics;
using Kairix.QuickAVSync.Models;
using Kairix.QuickAVSync.Services;

namespace Kairix.QuickAVSync.Core.Tests;

public sealed class FrameTimingAnalyzerTests
{
    [Fact]
    public void True50pReviewUsesExactly25RealSamplesAt20Milliseconds()
    {
        var frames = Enumerable.Range(-12, 25).Select(index => Frame(index * 20d, index)).ToArray();
        var snapshot = ReviewTimelineIntegrity.Build(frames, Timestamp(0), TimeSpan.FromMilliseconds(250), 50);
        var ticks = snapshot.Frames.Select(frame => frame.Timestamp.Time.TotalMilliseconds).ToArray();
        Assert.Equal(25, snapshot.Frames.Count);
        Assert.Equal(Enumerable.Range(-12, 25).Select(index => index * 20d), ticks);
        Assert.All(ticks.Zip(ticks.Skip(1)), pair => Assert.Equal(20, pair.Second - pair.First, 6));
        Assert.DoesNotContain(ticks.Zip(ticks.Skip(1)), pair => Math.Abs((pair.Second - pair.First) - 16.667) < .01);
    }

    [Theory]
    [InlineData(24000, 1001, 41.708333)]
    [InlineData(24, 1, 41.666667)]
    [InlineData(25, 1, 40)]
    [InlineData(30000, 1001, 33.366667)]
    [InlineData(30, 1, 33.333333)]
    [InlineData(50, 1, 20)]
    [InlineData(60000, 1001, 16.683333)]
    [InlineData(60, 1, 16.666667)]
    public void CommonCadencesAreDerivedFromTimestamps(int numerator, int denominator, double expectedMilliseconds)
    {
        var rate = Rational.From(numerator, denominator); var frames = CadenceFrames(rate, 90);
        var analysis = new FrameTimingAnalyzer().Analyze(frames, rate.Value);
        Assert.Equal(expectedMilliseconds, analysis.Primary.MedianIntervalMilliseconds, .001);
        Assert.Equal(rate.Value, analysis.Primary.ObservedRate, .01);
    }

    [Fact]
    public void DuplicateTimestampIsInvalidAndCreatesOnlyOneReviewPosition()
    {
        var frames = new[] { Frame(0, 0), Frame(20, 1), Frame(20, 2), Frame(40, 3) };
        var snapshot = ReviewTimelineIntegrity.Build(frames, Timestamp(20), TimeSpan.FromMilliseconds(100), 50);
        Assert.Equal(1, snapshot.Analysis.Primary.DuplicateCount);
        Assert.False(snapshot.Analysis.TimingValid);
        Assert.Equal(3, snapshot.Frames.Count);
    }

    [Fact]
    public void BackwardsTimestampIsDetectedBeforeReviewSorting()
    {
        var frames = new[] { Frame(0, 0), Frame(20, 1), Frame(10, 2), Frame(40, 3) };
        var snapshot = ReviewTimelineIntegrity.Build(frames, Timestamp(20), TimeSpan.FromMilliseconds(100), 50);
        Assert.Equal(1, snapshot.Analysis.Primary.BackwardCount);
        Assert.False(snapshot.Analysis.TimingValid);
        Assert.Equal([0d, 10d, 20d, 40d], snapshot.Frames.Select(frame => frame.Timestamp.Time.TotalMilliseconds));
    }

    [Fact]
    public void LargeGapAndDeclared50Observed60AreExplicit()
    {
        var frames = Enumerable.Range(0, 12).Select(index => Frame(index == 11 ? 300 : index * (1000d / 60), index)).ToArray();
        var analysis = new FrameTimingAnalyzer().Analyze(frames, 50);
        Assert.True(analysis.Primary.LargeGapCount > 0);
        Assert.True(analysis.DeclaredVsObservedErrorPercent > 15);
        Assert.Contains("TIMELINE TIMING INVALID", analysis.Status);
        Assert.False(analysis.TimingValid);
    }

    [Fact]
    public void ArrivalAndTimestampCadencesAreIndependent()
    {
        var frames = Enumerable.Range(0, 90).Select(index => Frame(index * (1000d / 60), index, arrivalMilliseconds: index * 20d)).ToArray();
        var analysis = new FrameTimingAnalyzer().Analyze(frames, 50);
        Assert.Equal(60, analysis.Primary.ObservedRate, .05);
        Assert.Equal(50, analysis.Arrival.ObservedRate, .05);
    }

    [Fact]
    public void SixtyArrivalAndTimestampCadenceAgree()
    {
        var frames = Enumerable.Range(0, 90).Select(index => Frame(index * (1000d / 60), index, arrivalMilliseconds: index * (1000d / 60))).ToArray();
        var analysis = new FrameTimingAnalyzer().Analyze(frames, 60);
        Assert.Equal(60, analysis.Primary.ObservedRate, .05);
        Assert.Equal(60, analysis.Arrival.ObservedRate, .05);
    }

    [Fact]
    public void FiftyArrivalAndTimestampCadenceAgree()
    {
        var frames = Enumerable.Range(0, 75).Select(index => Frame(index * 20d, index, arrivalMilliseconds: index * 20d, luma: (byte)index)).ToArray();
        var analysis = new FrameTimingAnalyzer().Analyze(frames, 50);
        Assert.Equal(50, analysis.Primary.ObservedRate, .01); Assert.Equal(50, analysis.Arrival.ObservedRate, .01); Assert.Equal(0, analysis.NearIdenticalConsecutiveImages);
    }

    [Fact]
    public void ReconstructedFieldsSeparateFiftyHertzReviewFromTwentyFiveHertzTransport()
    {
        var frames = Enumerable.Range(1, 6).SelectMany(native =>
        {
            var capturedMs = native * 40d; var ticks = (long)(capturedMs * TimeSpan.TicksPerMillisecond);
            var arrival = (long)(capturedMs * Stopwatch.Frequency / 1000d);
            var observation = new VideoTimingObservation(VideoPrimaryTimestampSource.DeviceTimestamp, arrival, Stopwatch.Frequency, ticks, ticks, ticks);
            return new[]
            {
                new VideoFrame(Timestamp(capturedMs - 20), 2, 2, [10, 10, 10, 10], native * 2, TemporalImageKind: TemporalImageKind.TopField, TimestampOrigin: TimestampOrigin.ReconstructedFirstField, Stride: 2, TimingObservation: observation, NativeSampleIndex: native),
                new VideoFrame(Timestamp(capturedMs), 2, 2, [20, 20, 20, 20], native * 2 + 1, TemporalImageKind: TemporalImageKind.BottomField, TimestampOrigin: TimestampOrigin.CaptureTimestampAssumedSecondField, Stride: 2, TimingObservation: observation, NativeSampleIndex: native)
            };
        }).ToArray();
        var analysis = new FrameTimingAnalyzer().Analyze(frames, 50, 25);
        Assert.Equal(50, analysis.Primary.ObservedRate, .001);
        Assert.Equal(20, analysis.Primary.MedianIntervalMilliseconds, .001);
        Assert.Equal(25, analysis.CaptureTransport.ObservedRate, .001);
        Assert.Equal(40, analysis.CaptureTransport.MedianIntervalMilliseconds, .001);
        Assert.Equal(25, analysis.Arrival.ObservedRate, .001);
        Assert.Equal(6, analysis.NativeSamplesAnalyzed);
        Assert.Equal(0, analysis.Arrival.DuplicateCount);
        Assert.Equal(0, analysis.DeviceTimestamp.DuplicateCount);
    }

    [Fact]
    public void RateConversionLikeRepeatsRemainAtTheirValidTimelinePositions()
    {
        byte[] pattern = [10, 20, 20, 30, 40, 40];
        var frames = pattern.Select((luma, index) => Frame(index * (1000d / 60), index, luma: luma)).ToArray();
        var snapshot = ReviewTimelineIntegrity.Build(frames, Timestamp(40), TimeSpan.FromMilliseconds(100), 60);
        Assert.Equal(2, snapshot.Analysis.NearIdenticalConsecutiveImages); Assert.Equal(pattern.Length, snapshot.Frames.Count); Assert.True(snapshot.Analysis.TimingValid);
    }

    [Fact]
    public void RepeatedContentAtDistinctTimestampsIsRetainedAsDiagnosticEvidence()
    {
        var frames = new[] { Frame(0, 0, luma: 10), Frame(20, 1, luma: 30), Frame(40, 2, luma: 30), Frame(60, 3, luma: 50) };
        var snapshot = ReviewTimelineIntegrity.Build(frames, Timestamp(30), TimeSpan.FromMilliseconds(100), 50);
        Assert.Equal(1, snapshot.Analysis.NearIdenticalConsecutiveImages);
        Assert.Equal(4, snapshot.Frames.Count);
        Assert.True(snapshot.Analysis.TimingValid);
    }

    [Fact]
    public void StaticSceneIsReportedButDoesNotInvalidateTiming()
    {
        var frames = Enumerable.Range(0, 4).Select(index => Frame(index * 20, index, luma: 12)).ToArray();
        var analysis = new FrameTimingAnalyzer().Analyze(frames, 50);
        Assert.Equal(3, analysis.NearIdenticalConsecutiveImages);
        Assert.True(analysis.TimingValid);
        Assert.False(analysis.Content.PairedRepeatDetected);
        Assert.False(analysis.Content.SceneActivitySufficient);
    }

    [Fact]
    public void Progressive50pHasNoPairedRepeat()
    {
        byte[] images = [10, 30, 50, 70, 90];
        var analysis = new FrameTimingAnalyzer().Analyze(images.Select((value, index) => Frame(index * 20, index, luma: value)).ToArray(), 50);
        Assert.Equal(50, analysis.Primary.ObservedRate, .01);
        Assert.False(analysis.Content.PairedRepeatDetected);
        Assert.True(analysis.Content.SceneActivitySufficient);
    }

    [Fact]
    public void Timestamped50pWithAabbccContentReportsPairedRepeatsWithoutDroppingSamples()
    {
        byte[] images = [10, 10, 40, 40, 80, 80];
        var frames = images.Select((value, index) => Frame(index * 20, index, luma: value)).ToArray();
        var snapshot = ReviewTimelineIntegrity.Build(frames, Timestamp(50), TimeSpan.FromMilliseconds(100), 50);
        Assert.True(snapshot.Analysis.TimingValid);
        Assert.True(snapshot.Analysis.Content.PairedRepeatDetected);
        Assert.Equal(25, snapshot.Analysis.Content.EstimatedUniqueImageRate!.Value, .01);
        Assert.Equal(6, snapshot.Frames.Count);
        Assert.Contains("REPEATED FRAME PAIRS", snapshot.Analysis.Status);
    }

    [Fact]
    public void Woven25pHasNewImagesAndFortyMillisecondSpacing()
    {
        byte[] images = [10, 30, 50, 70, 90];
        var analysis = new FrameTimingAnalyzer().Analyze(images.Select((value, index) => Frame(index * 40, index, luma: value)).ToArray(), 25);
        Assert.Equal(25, analysis.Primary.ObservedRate, .01);
        Assert.Equal(40, analysis.Primary.MedianIntervalMilliseconds, 3);
        Assert.Equal(0, analysis.NearIdenticalConsecutiveImages);
        Assert.False(analysis.Content.PairedRepeatDetected);
    }

    [Fact]
    public void NoisyStaticSceneIsNotMisclassifiedAsPairedConversion()
    {
        var frames = Enumerable.Range(0, 6).Select(index =>
        {
            var luma = new byte[] { (byte)(100 + index % 2), (byte)(101 - index % 2), 100, 101 };
            var ticks = index * 20 * TimeSpan.TicksPerMillisecond;
            return new VideoFrame(new(ticks, TimingQuality.StreamTimestamp), 2, 2, luma, index, Stride: 2);
        }).ToArray();
        var analysis = new FrameTimingAnalyzer().Analyze(frames, 50);
        Assert.False(analysis.Content.PairedRepeatDetected);
        Assert.False(analysis.Content.SceneActivitySufficient);
        Assert.Equal("insufficient scene activity", analysis.Content.Status);
    }

    private static VideoFrame[] CadenceFrames(Rational rate, int count) => Enumerable.Range(0, count)
        .Select(index => Frame(index * 1000d / rate.Value, index)).ToArray();

    private static VideoFrame Frame(double milliseconds, int index, double? arrivalMilliseconds = null, byte luma = 0)
    {
        var ticks = (long)Math.Round(milliseconds * TimeSpan.TicksPerMillisecond);
        var arrivalTicks = (long)Math.Round((arrivalMilliseconds ?? milliseconds) * Stopwatch.Frequency / 1000d);
        var observation = new VideoTimingObservation(VideoPrimaryTimestampSource.DeviceTimestamp, arrivalTicks, Stopwatch.Frequency, ticks, ticks, ticks);
        return new(new(ticks, TimingQuality.DeviceHardware, "qpc", ticks), 2, 2, [luma, luma, luma, luma], index, Stride: 2, TimingObservation: observation);
    }

    private static MediaTimestamp Timestamp(double milliseconds) => new((long)Math.Round(milliseconds * TimeSpan.TicksPerMillisecond), TimingQuality.DeviceHardware, "qpc");
}
