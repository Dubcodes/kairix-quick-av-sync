using Kairix.QuickAVSync.Models;
using Kairix.QuickAVSync.Services;

namespace Kairix.QuickAVSync.Core.Tests;

public sealed class FieldReconstructionTests
{
    [Fact]
    public void TopFirst25pProducesAlternatingTwentyMillisecondPositions()
    {
        var options = new FieldReconstructionOptions(Rational.From(25), Rational.From(50), FieldOrder.TopFirst);
        var frames = new[] { 40d, 80d, 120d }.SelectMany((milliseconds, pair) => Positions(milliseconds, pair, options)).ToArray();

        Assert.Equal([20d, 40d, 60d, 80d, 100d, 120d], frames.Select(frame => frame.Timestamp.Time.TotalMilliseconds));
        Assert.Equal([TemporalImageKind.TopField, TemporalImageKind.BottomField, TemporalImageKind.TopField, TemporalImageKind.BottomField, TemporalImageKind.TopField, TemporalImageKind.BottomField], frames.Select(frame => frame.TemporalImageKind));
        Assert.Equal(TimestampOrigin.ReconstructedFirstField, frames[0].TimestampOrigin);
        Assert.Equal(TimestampOrigin.CaptureTimestampAssumedSecondField, frames[1].TimestampOrigin);
        var timing = new FrameTimingAnalyzer().Analyze(frames, 50);
        Assert.True(timing.TimingValid);
        Assert.InRange(timing.Primary.MedianIntervalMilliseconds, 19.999, 20.001);
    }

    [Fact]
    public void BottomFirstAssignsEarlierTimeToBottomAndDirectTimeToTop()
    {
        var options = new FieldReconstructionOptions(Rational.From(25), Rational.From(50), FieldOrder.BottomFirst);
        var frames = Positions(40, 0, options);
        Assert.Equal(TemporalImageKind.BottomField, frames[0].TemporalImageKind);
        Assert.Equal(20, frames[0].Timestamp.Time.TotalMilliseconds);
        Assert.Equal(TemporalImageKind.TopField, frames[1].TemporalImageKind);
        Assert.Equal(40, frames[1].Timestamp.Time.TotalMilliseconds);
    }

    [Fact]
    public void Fractional5994UsesExactRationalFieldIntervalWithoutAccumulation()
    {
        var options = new FieldReconstructionOptions(Rational.From(30_000, 1_001), Rational.From(60_000, 1_001), FieldOrder.TopFirst);
        Assert.Equal(166_833, options.FieldIntervalTicks100ns);
        var capture = new MediaTimestamp(1_000_000, TimingQuality.DeviceHardware, "windows-qpc-100ns", 1_000_000);
        var positions = ReconstructedFieldTimestampModel.Reconstruct(capture, null, options);
        Assert.Equal(833_167, positions[0].Timestamp.Ticks100ns);
        Assert.Equal(1_000_000, positions[1].Timestamp.Ticks100ns);
        Assert.InRange((positions[1].Timestamp.Ticks100ns - positions[0].Timestamp.Ticks100ns) / 10_000d, 16.6832, 16.6834);
    }

    [Fact]
    public void DerivedFieldPreservesClockDomainAndOneFactualTransportObservation()
    {
        var observation = new VideoTimingObservation(VideoPrimaryTimestampSource.DeviceTimestamp, 5_000_000, 10_000_000, 400_000, 400_000, 400_000);
        var capture = new MediaTimestamp(400_000, TimingQuality.DeviceHardware, "windows-qpc-100ns", 400_000);
        var positions = ReconstructedFieldTimestampModel.Reconstruct(capture, observation, new(Rational.From(25), Rational.From(50), FieldOrder.TopFirst));
        Assert.Equal(capture.ClockDomain, positions[0].Timestamp.ClockDomain);
        Assert.Equal(capture.Quality, positions[0].Timestamp.Quality);
        Assert.Null(positions[0].Timestamp.RawValue);
        var derivedObservation = Assert.IsType<VideoTimingObservation>(positions[0].TimingObservation);
        Assert.Equal(400_000, derivedObservation.DeviceTimestampTicks100ns!.Value);
        Assert.Equal(5_000_000, derivedObservation.ArrivalStopwatchTicks);
        Assert.Same(observation, positions[1].TimingObservation);
    }

    [Fact]
    public void HalfSecondReconstructedReviewContainsFieldRatePositions()
    {
        var options = new FieldReconstructionOptions(Rational.From(25), Rational.From(50), FieldOrder.TopFirst);
        var frames = Enumerable.Range(1, 13).SelectMany(pair => Positions(pair * 40d, pair, options)).ToArray();
        var timeline = ReviewTimelineIntegrity.Build(frames, new MediaTimestamp(TimeSpan.FromMilliseconds(270).Ticks, TimingQuality.DeviceHardware), TimeSpan.FromMilliseconds(250), 50);
        Assert.InRange(timeline.Frames.Count, 25, 26);
        Assert.True(timeline.Analysis.TimingValid);
        Assert.InRange(timeline.Analysis.Primary.MedianIntervalMilliseconds, 19.999, 20.001);
    }

    [Theory]
    [InlineData(25_000, 1_000)]
    [InlineData(50_000, 1_000)]
    [InlineData(60_000, 1_001)]
    public void ProgressiveInterpretationKeepsOneTemporalImagePerCapturedFrame(int numerator, int denominator)
    {
        var rate = Rational.From(numerator, denominator);
        var interpretation = new InterpretationFormatOption(rate, ScanMode.Progressive);
        Assert.False(interpretation.ReconstructFields);
        Assert.Equal(rate.Value, interpretation.ReviewTemporalRate, 9);
    }

    [Fact]
    public async Task ReconstructedWovenClapCanSelectBottomFieldContact()
    {
        var frames = Enumerable.Range(0, 8).Select(index =>
        {
            var pixels = Enumerable.Repeat((byte)35, 80 * 48).ToArray();
            if (index == 1) Array.Fill(pixels, (byte)85, 1_720, 180);
            if (index is 2 or 3) Array.Fill(pixels, (byte)235, 1_720, 180);
            return new VideoFrame(new(index * 200_000L, TimingQuality.DeviceHardware, "windows-qpc-100ns"), 80, 48, pixels, index,
                TemporalImageKind: index % 2 == 0 ? TemporalImageKind.TopField : TemporalImageKind.BottomField,
                TimestampOrigin: index % 2 == 0 ? TimestampOrigin.ReconstructedFirstField : TimestampOrigin.CaptureTimestampAssumedSecondField);
        }).ToArray();
        var candidate = await new MotionVisualClapDetector().DetectAsync(frames, frames[3].Timestamp, default, new(75));
        Assert.NotNull(candidate);
        Assert.Equal(TemporalImageKind.BottomField, candidate!.Frame.TemporalImageKind);
        Assert.Equal(frames[3].Timestamp, candidate.Timestamp);
    }

    private static VideoFrame[] Positions(double captureMilliseconds, int pair, FieldReconstructionOptions options)
    {
        var capture = new MediaTimestamp(TimeSpan.FromMilliseconds(captureMilliseconds).Ticks, TimingQuality.DeviceHardware, "windows-qpc-100ns", TimeSpan.FromMilliseconds(captureMilliseconds).Ticks);
        var positions = ReconstructedFieldTimestampModel.Reconstruct(capture, null, options);
        return positions.Select((position, index) => new VideoFrame(position.Timestamp, 2, 2, [(byte)(pair * 2 + index), 0, 0, 0], pair * 2 + index,
            TemporalImageKind: position.Kind, TimestampOrigin: position.TimestampOrigin, Stride: 2, TimingObservation: position.TimingObservation, NativeSampleIndex: pair)).ToArray();
    }
}
