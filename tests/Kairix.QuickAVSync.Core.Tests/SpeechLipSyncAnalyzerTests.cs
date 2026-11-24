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
        var events = Enumerable.Range(0, 20).Select(index => 300d + index * 180).ToArray();
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
        Assert.Single(snapshot.Markers);
        Assert.True(snapshot.EstimatedBytes > 0);
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
}
