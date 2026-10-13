using Kairix.QuickAVSync.Models;
using Kairix.QuickAVSync.Services;

namespace Kairix.QuickAVSync.Core.Tests;

public sealed class LiveOperatorServicesTests
{
    [Fact]
    public void CaptureReadoutDoesNotConflateNegotiatedAndObservedRates()
    {
        var negotiated = Format(30);

        var readout = CaptureStatusPresenter.Describe(negotiated, 29.97, null);

        Assert.Contains("30.000p", readout.DriverCapture);
        Assert.Equal("29.97 fps", readout.ObservedDelivery);
        Assert.Equal("Unknown", readout.SourceCadence);
    }

    [Fact]
    public void EstimatedSourceCadenceRequiresUsefulSceneEvidenceAndIsLabelledEstimated()
    {
        var signal = new InputSignalInfo(TemporalCadenceHz: 25, Authority: SignalAuthority.EstimatedHigh, ProviderName: "passive analysis");
        var insufficient = Analysis(signal, sceneActivity: false, pairedRepeat: true, uniqueRate: 25);
        var useful = Analysis(signal, sceneActivity: true, pairedRepeat: true, uniqueRate: 25);

        Assert.Equal("Unknown", CaptureStatusPresenter.Describe(Format(50), 50, insufficient, signal).SourceCadence);
        var source = CaptureStatusPresenter.Describe(Format(50), 50, useful, signal).SourceCadence;
        Assert.Contains("≈25 fps", source);
        Assert.Contains("repeated frames", source);
        Assert.Contains("estimated", source);
        Assert.DoesNotContain("authoritative", source);
    }

    [Fact]
    public void AuthoritativeHardwareCadenceIsExplicitlyAttributed()
    {
        var signal = new InputSignalInfo(TemporalCadenceHz: 50, Authority: SignalAuthority.Authoritative, ProviderName: "capture hardware");

        var source = CaptureStatusPresenter.Describe(Format(50), 50, null, signal).SourceCadence;

        Assert.Equal("50 fps · authoritative · capture hardware", source);
    }

    [Fact]
    public void AudioMeterAggregatesStereoPeaksAcrossChunksThenResets()
    {
        var meter = new AudioLevelAccumulator();
        meter.Add(Chunk([.25f, .125f, -.5f, -.25f], channels: 2));
        meter.Add(Chunk([.1f, -.75f], channels: 2));

        var reading = meter.ReadAndReset();

        Assert.True(reading.HasSamples);
        Assert.Equal(2, reading.AggregatedChunks);
        Assert.Equal(48_000, reading.SampleRate);
        Assert.Equal(2, reading.Channels);
        Assert.InRange(reading.LeftDbfs, -6.03, -6.01);
        Assert.InRange(reading.RightDbfs, -2.51, -2.49);
        Assert.False(meter.ReadAndReset().HasSamples);
    }

    [Fact]
    public void AudioMeterUsesOnePeakForMonoAndFloorsSilence()
    {
        var meter = new AudioLevelAccumulator();
        meter.Add(Chunk([0, 0, 0], channels: 1));

        var reading = meter.ReadAndReset();

        Assert.Equal(AudioLevelAccumulator.FloorDbfs, reading.LeftDbfs);
        Assert.Equal(reading.LeftDbfs, reading.RightDbfs);
        Assert.Equal(0, AudioLevelAccumulator.Normalize(AudioLevelAccumulator.FloorDbfs));
        Assert.Equal(1, AudioLevelAccumulator.Normalize(0));
    }

    [Fact]
    public void HistoryTrendIsBoundedChronologicalAndPreservesSignedValues()
    {
        var newestFirst = Enumerable.Range(0, 25)
            .Select(index => Result(index % 2 == 0 ? index : -index))
            .ToArray();

        var points = HistoryTrendSeries.Build(newestFirst);

        Assert.Equal(HistoryTrendSeries.MaximumPoints, points.Count);
        Assert.Equal(-19, points[0].Milliseconds);
        Assert.Equal(0, points[^1].Milliseconds);
        Assert.True(points[^1].IsNewest);
        Assert.Contains(points, point => point.Milliseconds > 0);
        Assert.Contains(points, point => point.Milliseconds < 0);
    }

    [Theory]
    [InlineData(-43.24, "-43.2 ms")]
    [InlineData(12.54, "+12.5 ms")]
    [InlineData(0, "0.0 ms")]
    public void HistoryTrendTooltipIsOnlyOneSignedValue(double value, string expected) =>
        Assert.Equal(expected, HistoryTrendSeries.FormatTooltip(value));

    [Theory]
    [InlineData(7.5, 10)]
    [InlineData(31, 50)]
    [InlineData(73.4, 100)]
    [InlineData(101, 200)]
    public void HistoryTrendRangeUsesCleanContainingNumbers(double value, double expected) =>
        Assert.Equal(expected, HistoryTrendSeries.NiceRange([value]));

    [Fact]
    public void HistoryTrendRangeIsSymmetricAndHasASensibleEmptyMinimum()
    {
        Assert.Equal(50, HistoryTrendSeries.NiceRange([31]));
        Assert.Equal(50, HistoryTrendSeries.NiceRange([-31]));
        Assert.Equal(50, HistoryTrendSeries.NiceRange([-31, 18]));
        Assert.Equal(10, HistoryTrendSeries.NiceRange([0]));
        Assert.Equal(10, HistoryTrendSeries.NiceRange([]));
    }

    [Fact]
    public void HistoryTrendPointMapsBackToItsExactNewestFirstIdentity()
    {
        var newest = Result(3); var middle = Result(-2); var oldest = Result(1);
        var points = HistoryTrendSeries.Build([newest, middle, oldest]);

        Assert.Equal([1d, -2d, 3d], points.Select(point => point.Milliseconds));
        Assert.Equal([2, 1, 0], points.Select(point => point.NewestFirstIndex));
    }

    [Fact]
    public void HistoryTrendOmitsInvalidValuesWithoutInventingZeroAndShrinksWithHistory()
    {
        SessionResult[] history =
        [
            Result(double.NaN),
            Result(12, comparable: false),
            Result(-8)
        ];

        var points = HistoryTrendSeries.Build(history);

        var point = Assert.Single(points);
        Assert.Equal(-8, point.Milliseconds);
        Assert.Equal(0, point.Position);
        Assert.Equal(3, point.TotalPositions);
        Assert.Empty(HistoryTrendSeries.Build([]));
        Assert.Single(HistoryTrendSeries.Build(history.TakeLast(1)));
    }

    [Fact]
    public void HistoryTrendPointsCannotRetainReviewSnapshotsOrMedia()
    {
        Assert.DoesNotContain(typeof(HistoryTrendPoint).GetProperties(), property =>
            property.PropertyType == typeof(ReviewHistorySnapshot) || property.Name.Contains("Snapshot", StringComparison.Ordinal));
    }

    private static CaptureFormat Format(int rate) => new(1920, 1080, Rational.From(rate), ScanMode.Progressive, FieldOrder.Unknown, 48_000, VideoPixelFormat.Yuy2);
    private static AudioChunk Chunk(float[] samples, int channels) => new(new MediaTimestamp(0, TimingQuality.StreamTimestamp), samples, 48_000, channels);
    private static ObservedSignalAnalysis Analysis(InputSignalInfo signal, bool sceneActivity, bool pairedRepeat, double? uniqueRate) =>
        new(signal, 50, 20, 0, .5, "AABB", 0, 100, 2, sceneActivity, pairedRepeat, uniqueRate);
    private static SessionResult Result(double milliseconds, bool comparable = true) =>
        new(DateTime.UtcNow, new(milliseconds, "test", comparable), null);
}
