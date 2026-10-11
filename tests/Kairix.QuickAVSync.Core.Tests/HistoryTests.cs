using Kairix.QuickAVSync.Models;
using Kairix.QuickAVSync.Services;

namespace Kairix.QuickAVSync.Core.Tests;

public sealed class BoundedReviewHistoryTests
{
    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(10)]
    public void HistoryRetainsOnlyTheConfiguredNewestResults(int capacity)
    {
        var history = new SessionHistoryService(capacity);
        for (var i = 0; i < 12; i++) history.Add(Result(i));

        Assert.Equal(capacity, history.Capacity);
        Assert.Equal(capacity, history.Items.Count);
        Assert.Equal(11, history.Items[0].Result.SignedMilliseconds);
        Assert.Equal(12 - capacity, history.Items[^1].Result.SignedMilliseconds);
    }

    [Fact]
    public void UnsupportedDepthFallsBackToThreeAndShrinkingEvictsImmediately()
    {
        var history = new SessionHistoryService(10);
        for (var i = 0; i < 10; i++) history.Add(Result(i));

        history.SetCapacity(5);
        Assert.Equal(5, history.Items.Count);
        history.SetCapacity(4);

        Assert.Equal(3, history.Capacity);
        Assert.Equal([9d, 8d, 7d], history.Items.Select(item => item.Result.SignedMilliseconds));
    }

    [Fact]
    public void SnapshotOwnsOnlyProcessedReviewMediaAndDoesNotReferenceRollingFrameArrays()
    {
        var presentation = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
        var luma = Enumerable.Repeat((byte)19, 64).ToArray();
        var frame = Frame(20, 2, luma, presentation);
        var waveform = new[] { .1f, .8f, .2f };
        var ticks = new[] { -20d, 0d, 20d };
        var review = Review(frame);

        var snapshot = ReviewHistorySnapshotFactory.Create(review, [frame], waveform, ticks, timeline: null);

        Assert.Empty(snapshot.Frames[0].Luma);
        Assert.Equal(presentation, snapshot.Frames[0].PresentationBgra);
        Assert.NotSame(presentation, snapshot.Frames[0].PresentationBgra);
        Assert.NotSame(waveform, snapshot.Waveform);
        Assert.NotSame(ticks, snapshot.FrameTicks);
        presentation[0] = 255; luma[0] = 255; waveform[0] = 1; ticks[0] = 999;
        Assert.Equal(0, snapshot.Frames[0].PresentationBgra![0]);
        Assert.Equal(.1f, snapshot.Waveform[0]);
        Assert.Equal(-20, snapshot.FrameTicks[0]);
        Assert.Equal(32 + waveform.Length * sizeof(float) + ticks.Length * sizeof(double), snapshot.EstimatedBytes);
    }

    [Fact]
    public void RestoredHistoryPreservesMarksConfidenceDetectorMetadataTimelineAndResult()
    {
        var frame = Frame(40, 4, new byte[64], Enumerable.Repeat((byte)7, 32).ToArray());
        var review = Review(frame);
        review.MoveAudioMark(5, 300);
        var timeline = ReviewTimelineIntegrity.Build([frame], frame.Timestamp, TimeSpan.FromMilliseconds(300), 25).Analysis;
        var snapshot = ReviewHistorySnapshotFactory.Create(review, [frame], [.2f, .7f], [40d], timeline);

        var workspace = ReviewHistorySnapshotFactory.Restore(snapshot);

        Assert.True(workspace.RequiresHold);
        Assert.Equal(ReviewResultMode.HistoryReview, workspace.Review.ResultMode);
        Assert.Equal(snapshot.EventReference, workspace.Review.EventReference);
        Assert.Equal(snapshot.AudioMark, workspace.Review.AudioMark);
        Assert.Equal(snapshot.AutoCandidate.Timestamp, workspace.Review.AutoCandidate!.Timestamp);
        Assert.Equal(.87, workspace.Review.AutoCandidate.Confidence, 6);
        Assert.Equal(VisualDetectorKind.FlashTestSignal, workspace.Review.AutoCandidate.Trace!.DetectorKind);
        Assert.Same(timeline, workspace.Timeline);
        Assert.Equal(snapshot.Waveform, workspace.Waveform);
        Assert.Equal(snapshot.FrameTicks, workspace.FrameTicks);
        Assert.Equal(snapshot.AutoCandidate.Timestamp, workspace.Review.Playhead);
        Assert.Equal(snapshot.AutoCandidate.Timestamp.Ticks100ns - snapshot.AudioMark.Ticks100ns,
            (long)Math.Round(workspace.Review.CurrentRawResult!.SignedMilliseconds * 10_000));
    }

    [Fact]
    public void HistoricalReviewCannotBeReplacedUntilExplicitlyReturnedToArmedLiveOperation()
    {
        var frame = Frame(20, 2, new byte[64], new byte[32]);
        var workspace = ReviewHistorySnapshotFactory.Restore(
            ReviewHistorySnapshotFactory.Create(Review(frame), [frame], [0f], [20d], timeline: null));

        Assert.True(workspace.RequiresHold);
        Assert.False(ReviewTimeline.AcceptsAutomaticEvents(autoDetect: true, autoSpike: true, hold: workspace.RequiresHold, analysisRunning: false));

        workspace.Review.MovePlayhead(T(60));
        Assert.Equal(ReviewResultMode.ManualPreview, workspace.Review.ResultMode);
        Assert.Equal(ReviewResultMode.HistoryReview,
            ReviewHistorySnapshotFactory.Restore(ReviewHistorySnapshotFactory.Create(Review(frame), [frame], [0f], [20d], null)).Review.ResultMode);
        Assert.True(ReviewTimeline.AcceptsAutomaticEvents(autoDetect: true, autoSpike: true, hold: false, analysisRunning: false));
    }

    private static SessionResult Result(int value) => new(DateTime.UnixEpoch.AddSeconds(value), new(value, value.ToString()), .5);

    private static EventReviewState Review(VideoFrame frame)
    {
        var review = new EventReviewState(T(0), T(0), new(10_000, VideoTimingOffsetSource.Manual));
        review.SetAutoCandidate(new(frame.Timestamp, frame.TemporalIndex, .87, 4.2, frame,
            new(1, 4, 3, .8, .7, .1, VisualDetectorKind.FlashTestSignal, OnsetQuality: .9)));
        return review;
    }

    private static VideoFrame Frame(double milliseconds, int index, byte[] luma, byte[] presentation) =>
        new(T(milliseconds), 8, 8, luma, index, PresentationBgra: presentation,
            PresentationWidth: 4, PresentationHeight: 2, PresentationStride: 16);

    private static MediaTimestamp T(double milliseconds) =>
        new((long)Math.Round(milliseconds * 10_000), TimingQuality.StreamTimestamp);
}
