using System.Globalization;
using System.Text;
using Kairix.QuickAVSync.Models;

namespace Kairix.QuickAVSync.Services;

public readonly record struct VideoTimingOffset(long Ticks100ns, VideoTimingOffsetSource Source)
{
    public double Milliseconds => Ticks100ns / 10_000d;
    public bool IsManual => Source == VideoTimingOffsetSource.Manual;
}

public static class VideoTimingCompensation
{
    public static VideoTimingOffset Automatic(FieldReconstructionOptions? reconstruction) =>
        new(reconstruction?.FieldIntervalTicks100ns ?? 0, VideoTimingOffsetSource.Automatic);

    public static VideoTimingOffset Resolve(FieldReconstructionOptions? reconstruction, double? manualMilliseconds) =>
        manualMilliseconds is { } value && double.IsFinite(value)
            ? new((long)Math.Round(value * 10_000d), VideoTimingOffsetSource.Manual)
            : Automatic(reconstruction);

    public static MediaTimestamp ExpectedRawVisual(MediaTimestamp audio, VideoTimingOffset offset) =>
        new(audio.Ticks100ns + offset.Ticks100ns, audio.Quality, audio.ClockDomain);

    public static double CorrectedOffsetMilliseconds(MediaTimestamp reference, MediaTimestamp rawVisual, VideoTimingOffset offset) =>
        (rawVisual.Ticks100ns - offset.Ticks100ns - reference.Ticks100ns) / 10_000d;

    public static SyncResult Calculate(MediaTimestamp audio, MediaTimestamp rawVisual, VideoTimingOffset offset)
    {
        if (!audio.IsComparableTo(rawVisual)) return new(double.NaN, "TIMING DOMAINS NOT CORRELATED", false);
        var corrected = new MediaTimestamp(rawVisual.Ticks100ns - offset.Ticks100ns, rawVisual.Quality, rawVisual.ClockDomain);
        return SyncResult.Calculate(audio, corrected);
    }
}

public static class VideoTimingProfileKey
{
    public static string Create(string deviceId, string nativeFormatId, FieldReconstructionOptions? reconstruction)
    {
        var device = Encode(deviceId);
        var format = Encode(nativeFormatId);
        if (reconstruction is null) return $"v1|d={device}|f={format}|r=0|o=Unknown|t=0/1";
        var target = reconstruction.TargetFieldRate.Reduce();
        return $"v1|d={device}|f={format}|r=1|o={reconstruction.FieldOrder}|t={target.Numerator.ToString(CultureInfo.InvariantCulture)}/{target.Denominator.ToString(CultureInfo.InvariantCulture)}";
    }

    private static string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? ""));
}
