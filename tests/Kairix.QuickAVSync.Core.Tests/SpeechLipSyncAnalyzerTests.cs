using Kairix.QuickAVSync.Models;
using Kairix.QuickAVSync.Services;

namespace Kairix.QuickAVSync.Core.Tests;

public sealed class SpeechLipSyncAnalyzerTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(80)]
    [InlineData(-120)]
    public void DeterministicFeatureSignalsRecoverKnownSignedOffset(double offsetMilliseconds)
    {
        var analyzer = new SpeechLipSyncAnalyzer();
        var audio = Signal([400, 900, 1450, 2100, 2800], 0);
        var visual = Signal([400, 900, 1450, 2100, 2800], offsetMilliseconds);

        var result = analyzer.AnalyzeFeatures(visual, audio);

        Assert.True(result.HasEstimate, result.Status);
        Assert.InRange(result.Result!.SignedMilliseconds, offsetMilliseconds - .01, offsetMilliseconds + .01);
        Assert.InRange(result.Markers.Count, 2, SpeechLipSyncAnalyzer.MaximumMarkers);
        Assert.Single(result.Markers, marker => marker.IsPrimary);
        Assert.Equal(result.Markers.Max(marker => marker.Confidence), result.Markers.Single(marker => marker.IsPrimary).Confidence);
        Assert.All(result.Markers, marker => Assert.Equal(offsetMilliseconds, marker.SignedMilliseconds, 6));
        Assert.Equal(offsetMilliseconds > 0 ? "AUDIO LEADS VIDEO" : offsetMilliseconds < 0 ? "AUDIO LAGS VIDEO" : "AUDIO AND VIDEO IN SYNC",
            result.Result.Wording.Split(" BY ")[0]);
    }

    [Fact]
    public void MarkerCollectionIsBoundedAndChronological()
    {
        double[] events = [250, 430, 690, 850, 1120, 1350, 1510, 1780, 1990, 2290, 2470, 2760, 3010, 3190, 3520, 3790];
        var result = new SpeechLipSyncAnalyzer().AnalyzeFeatures(Signal(events, 35), Signal(events, 0));
        Assert.True(result.HasEstimate, result.Status);
        Assert.Equal(SpeechLipSyncAnalyzer.MaximumMarkers, result.Markers.Count);
        Assert.Equal(result.Markers.OrderBy(marker => marker.VisualTimestamp.Ticks100ns), result.Markers);
    }

    [Fact]
    public void TooLittleOrInconsistentEvidenceDoesNotProduceNumericResult()
    {
        var analyzer = new SpeechLipSyncAnalyzer();
        var little = analyzer.AnalyzeFeatures(Signal([500], 0), Signal([500], 0));
        Assert.False(little.HasEstimate);
        Assert.Null(little.Result);

        var visual = Signal([400, 900, 1450, 2100], 0);
        var audio = Signal([100, 830, 1600, 1900], 0);
        var inconsistent = analyzer.AnalyzeFeatures(visual, audio);
        Assert.False(inconsistent.HasEstimate);
        Assert.Null(inconsistent.Result);
    }

    [Fact]
    public void OneBadEventDoesNotMoveTheRobustAggregate()
    {
        var audioEvents = new double[] { 400, 900, 1450, 2100, 2800 };
        var visualEvents = new double[] { 440, 940, 1490, 2140, 2600 };
        var result = new SpeechLipSyncAnalyzer().AnalyzeFeatures(Signal(visualEvents, 0), Signal(audioEvents, 0));
        Assert.True(result.HasEstimate, result.Status);
        Assert.InRange(result.Result!.SignedMilliseconds, 39.99, 40.01);
        Assert.DoesNotContain(result.Markers, marker => marker.SignedMilliseconds == -200);
    }

    [Fact]
    public void AudioOnlyVisualOnlyAndGlobalExposureDoNotProduceMarkers()
    {
        var analyzer = new SpeechLipSyncAnalyzer(); var signal = Signal([400, 900, 1450], 0);
        Assert.False(analyzer.AnalyzeFeatures([], signal).HasEstimate);
        Assert.False(analyzer.AnalyzeFeatures(signal, []).HasEstimate);

        var frames = Enumerable.Range(0, 30).Select(index =>
        {
            var value = (byte)(index % 2 == 0 ? 20 : 220); var ticks = TimeSpan.FromMilliseconds(index * 20).Ticks;
            return new VideoFrame(new(ticks, TimingQuality.StreamTimestamp, "test", ticks), 8, 8, Enumerable.Repeat(value, 64).ToArray(), index);
        }).ToArray();
        var global = SpeechLipSyncAnalyzer.ExtractVisualFeatures(frames, NormalizedMouthRegion.Default);
        Assert.All(global, sample => Assert.Equal(0, sample.Strength));
        Assert.False(analyzer.AnalyzeFeatures(global, signal).HasEstimate);
    }

    [Fact]
    public void NormalizedMouthRegionClampsInsideFrame()
    {
        var region = new NormalizedMouthRegion(-.2, .95, 2, .01).Normalize();
        Assert.InRange(region.X, 0, 1 - region.Width);
        Assert.InRange(region.Y, 0, 1 - region.Height);
        Assert.InRange(region.Width, .05, 1);
        Assert.InRange(region.Height, .05, 1);
    }

    [Fact]
    public void HistorySnapshotOwnsCompactMediaAndBoundedMarkers()
    {
        var timestamp = new MediaTimestamp(10_000, TimingQuality.StreamTimestamp, "test");
        var sourceLuma = new byte[] { 1, 2, 3, 4 };
        var sourceWaveform = new float[] { .1f, .8f };
        var frames = new[] { new VideoFrame(timestamp, 2, 2, sourceLuma, 1) };
        var markers = new[] { new LipSyncMarker(timestamp, timestamp, 0, .8, true) };
        var analysis = new LipSyncAnalysisResult(new(0, "Estimated Lip Sync"), markers, .8, 0, "ok", TimeSpan.Zero);

        var snapshot = LipSyncHistorySnapshotFactory.Create(timestamp, frames, sourceWaveform, [0d], analysis);
        sourceLuma[0] = 99; sourceWaveform[0] = 99;

        Assert.Equal(1, snapshot.Frames[0].Luma[0]);
        Assert.Equal(.1f, snapshot.Waveform[0]);
        Assert.Single(snapshot.Analysis.Markers);
        Assert.True(snapshot.EstimatedBytes > 0);
    }

    [Theory]
    [InlineData(20, 20, 0)]
    [InlineData(60, 20, 40)]
    [InlineData(-20, 20, -40)]
    [InlineData(40, 0, 40)]
    public void VideoTimingCompensationPreservesRawEvidenceAndCorrectsReportedResult(
        double rawMilliseconds, double timingOffsetMilliseconds, double expectedCorrectedMilliseconds)
    {
        var events = new double[] { 400, 900, 1450, 2100, 2800 };
        var offset = new VideoTimingOffset((long)Math.Round(timingOffsetMilliseconds * 10_000),
            timingOffsetMilliseconds == 0 ? VideoTimingOffsetSource.Automatic : VideoTimingOffsetSource.Manual);

        var result = new SpeechLipSyncAnalyzer().AnalyzeFeatures(Signal(events, rawMilliseconds), Signal(events, 0), offset);

        Assert.True(result.HasEstimate, result.Status);
        Assert.Equal(rawMilliseconds, result.RawResult!.SignedMilliseconds, 6);
        Assert.Equal(expectedCorrectedMilliseconds, result.Result!.SignedMilliseconds, 6);
        Assert.Equal(offset.Ticks100ns, result.VideoTimingOffsetTicks100ns);
        Assert.Equal(offset.Source, result.VideoTimingOffsetSource);
        Assert.All(result.Markers, marker =>
        {
            Assert.Equal(rawMilliseconds, marker.RawSignedMilliseconds, 6);
            Assert.Equal(expectedCorrectedMilliseconds, marker.SignedMilliseconds, 6);
            Assert.Equal(offset.Ticks100ns, marker.VideoTimingOffsetTicks100ns);
            Assert.Equal(marker.AudioTimestamp.Ticks100ns + (long)Math.Round(rawMilliseconds * 10_000), marker.VisualTimestamp.Ticks100ns);
        });
    }

    [Fact]
    public void MatchesAreMonotonicOneToOneAndPrimaryIdentityIsStable()
    {
        var events = new double[] { 400, 900, 1450, 2100, 2800 };
        var analyzer = new SpeechLipSyncAnalyzer();
        var first = analyzer.AnalyzeFeatures(Signal(events, 40), Signal(events, 0));
        var second = analyzer.AnalyzeFeatures(Signal(events, 40), Signal(events, 0));

        Assert.True(first.HasEstimate, first.Status);
        Assert.Equal(first.Markers, second.Markers);
        Assert.Equal(first.Markers.Count, first.Markers.Select(marker => marker.AudioTimestamp.Ticks100ns).Distinct().Count());
        Assert.Equal(first.Markers.Count, first.Markers.Select(marker => marker.VisualTimestamp.Ticks100ns).Distinct().Count());
        Assert.Equal(Enumerable.Range(0, first.Markers.Count), first.Markers.Select(marker => marker.EventIndex));
        Assert.True(first.Markers.Zip(first.Markers.Skip(1)).All(pair =>
            pair.First.AudioTimestamp.Ticks100ns < pair.Second.AudioTimestamp.Ticks100ns &&
            pair.First.VisualTimestamp.Ticks100ns < pair.Second.VisualTimestamp.Ticks100ns));
        Assert.Equal(first.Markers.Max(marker => marker.Confidence), first.Markers.Single(marker => marker.IsPrimary).Confidence);
    }

    [Fact]
    public void OneAudioEventCannotCreateTwoMarkersAndBestLocalPairWins()
    {
        var audio = new[] { Event(400, 1), Event(900, 1) };
        var visual = new[] { Event(440, 1), Event(470, .7), Event(940, 1) };

        var markers = SpeechLipSyncAnalyzer.MatchEventsForTesting(visual, audio, 40);

        Assert.Equal(2, markers.Count);
        Assert.Equal(2, markers.Select(marker => marker.AudioTimestamp.Ticks100ns).Distinct().Count());
        Assert.Contains(markers, marker => marker.VisualTimestamp.Ticks100ns == TimeSpan.FromMilliseconds(440).Ticks);
        Assert.DoesNotContain(markers, marker => marker.VisualTimestamp.Ticks100ns == TimeSpan.FromMilliseconds(470).Ticks);
    }

    [Fact]
    public void OneVisualEventCannotConsumeTwoAudioEventsAndPairsCannotCross()
    {
        var audio = new[] { Event(380, .7), Event(400, 1), Event(900, 1) };
        var visual = new[] { Event(440, 1), Event(940, 1) };

        var markers = SpeechLipSyncAnalyzer.MatchEventsForTesting(visual, audio, 40);

        Assert.Equal(2, markers.Count);
        Assert.Equal(2, markers.Select(marker => marker.VisualTimestamp.Ticks100ns).Distinct().Count());
        Assert.Contains(markers, marker => marker.AudioTimestamp.Ticks100ns == TimeSpan.FromMilliseconds(400).Ticks);
        Assert.DoesNotContain(markers, marker => marker.AudioTimestamp.Ticks100ns == TimeSpan.FromMilliseconds(380).Ticks);
        Assert.True(markers.Zip(markers.Skip(1)).All(pair =>
            pair.First.AudioTimestamp.Ticks100ns < pair.Second.AudioTimestamp.Ticks100ns &&
            pair.First.VisualTimestamp.Ticks100ns < pair.Second.VisualTimestamp.Ticks100ns));
    }

    [Fact]
    public void RepeatingPeriodicEvidenceIsReportedAsAmbiguousRatherThanHighPrecision()
    {
        var periodic = Enumerable.Range(0, 17).Select(index => 300d + index * 200).ToArray();
        var result = new SpeechLipSyncAnalyzer().AnalyzeFeatures(Signal(periodic, 0), Signal(periodic, 0));

        Assert.False(result.HasEstimate);
        Assert.Contains("Ambiguous correlation peaks", result.Status);
        Assert.NotNull(result.BestCorrelationScore);
        Assert.NotNull(result.CompetingCorrelationScore);
        Assert.True(result.CorrelationMargin < .06);
    }

    [Fact]
    public void CanceledAnalysisStopsBeforeProducingAResult()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var events = new double[] { 400, 900, 1450, 2100, 2800 };
        Assert.Throws<OperationCanceledException>(() =>
            new SpeechLipSyncAnalyzer().AnalyzeFeatures(Signal(events, 40), Signal(events, 0), cancellationToken: cancellation.Token));
    }

    [Fact]
    public void HistorySnapshotRoundTripsRawCorrectedMarkersAndTimingMetadata()
    {
        var timestamp = new MediaTimestamp(10_000, TimingQuality.StreamTimestamp, "test");
        var offset = new VideoTimingOffset(200_000, VideoTimingOffsetSource.Manual);
        var marker = new LipSyncMarker(timestamp, timestamp with { Ticks100ns = 610_000 }, 40, .8, true, 0, .7, .9, 60, offset.Ticks100ns, offset.Source);
        var analysis = new LipSyncAnalysisResult(new(40, "corrected"), [marker], .8, 0, "ok", TimeSpan.Zero,
            new(60, "raw"), offset.Ticks100ns, offset.Source, 60, 40, .9, .2, .7, 12, 13, 3, 3, 0, 5000);
        var snapshot = LipSyncHistorySnapshotFactory.Create(timestamp, [new(timestamp, 1, 1, [1], 0)], [.1f], [0d], analysis);

        Assert.Equal(analysis.Result, snapshot.Analysis.Result);
        Assert.Equal(analysis.RawResult, snapshot.Analysis.RawResult);
        Assert.Equal(offset.Ticks100ns, snapshot.Analysis.VideoTimingOffsetTicks100ns);
        Assert.Equal(offset.Source, snapshot.Analysis.VideoTimingOffsetSource);
        Assert.Equal(marker, Assert.Single(snapshot.Analysis.Markers));
    }

    private static IReadOnlyList<LipSyncFeatureSample> Signal(IReadOnlyList<double> events, double offsetMilliseconds)
    {
        var output = new List<LipSyncFeatureSample>();
        for (var ms = 0d; ms <= 4000; ms += 20)
        {
            var strength = events.Any(value => Math.Abs(ms - (value + offsetMilliseconds)) <= 10.0001) ? 1d : .01;
            var ticks = TimeSpan.FromMilliseconds(ms).Ticks;
            output.Add(new(new(ticks, TimingQuality.StreamTimestamp, "test", ticks), strength));
        }
        return output;
    }

    private static LipSyncFeatureSample Event(double milliseconds, double strength)
    {
        var ticks = TimeSpan.FromMilliseconds(milliseconds).Ticks;
        return new(new(ticks, TimingQuality.StreamTimestamp, "test", ticks), strength);
    }
}
