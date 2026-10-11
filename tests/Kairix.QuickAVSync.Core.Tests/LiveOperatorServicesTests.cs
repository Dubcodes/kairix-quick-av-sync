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

    private static CaptureFormat Format(int rate) => new(1920, 1080, Rational.From(rate), ScanMode.Progressive, FieldOrder.Unknown, 48_000, VideoPixelFormat.Yuy2);
    private static AudioChunk Chunk(float[] samples, int channels) => new(new MediaTimestamp(0, TimingQuality.StreamTimestamp), samples, 48_000, channels);
    private static ObservedSignalAnalysis Analysis(InputSignalInfo signal, bool sceneActivity, bool pairedRepeat, double? uniqueRate) =>
        new(signal, 50, 20, 0, .5, "AABB", 0, 100, 2, sceneActivity, pairedRepeat, uniqueRate);
}
