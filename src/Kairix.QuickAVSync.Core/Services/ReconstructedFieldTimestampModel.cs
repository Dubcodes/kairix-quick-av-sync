using Kairix.QuickAVSync.Models;

namespace Kairix.QuickAVSync.Services;

public sealed record ReconstructedFieldPosition(
    TemporalImageKind Kind,
    MediaTimestamp Timestamp,
    TimestampOrigin TimestampOrigin,
    VideoTimingObservation? TimingObservation);

public static class ReconstructedFieldTimestampModel
{
    public static long FieldIntervalTicks100ns(Rational fieldRate)
    {
        var reduced = fieldRate.Reduce();
        if (reduced.Numerator <= 0 || reduced.Denominator <= 0) throw new ArgumentOutOfRangeException(nameof(fieldRate));
        return (long)Math.Round(10_000_000d * reduced.Denominator / reduced.Numerator, MidpointRounding.AwayFromZero);
    }

    public static IReadOnlyList<ReconstructedFieldPosition> Reconstruct(
        MediaTimestamp captureTimestamp,
        VideoTimingObservation? observation,
        FieldReconstructionOptions options)
    {
        if (options.TimestampPhase != TimestampPhaseAssumption.CaptureTimestampRepresentsSecondField)
            throw new NotSupportedException($"Unsupported timestamp phase assumption: {options.TimestampPhase}");

        var interval = options.FieldIntervalTicks100ns;
        var firstKind = options.FieldOrder == FieldOrder.BottomFirst ? TemporalImageKind.BottomField : TemporalImageKind.TopField;
        var secondKind = firstKind == TemporalImageKind.TopField ? TemporalImageKind.BottomField : TemporalImageKind.TopField;
        var firstTimestamp = new MediaTimestamp(captureTimestamp.Ticks100ns - interval, captureTimestamp.Quality, captureTimestamp.ClockDomain);
        return
        [
            new(firstKind, firstTimestamp, TimestampOrigin.ReconstructedFirstField, Shift(observation, -interval)),
            new(secondKind, captureTimestamp, TimestampOrigin.CaptureTimestampAssumedSecondField, observation)
        ];
    }

    private static VideoTimingObservation? Shift(VideoTimingObservation? value, long ticks100ns)
    {
        if (value is null) return null;
        var stopwatchShift = (long)Math.Round(ticks100ns / 10_000_000d * value.StopwatchFrequency, MidpointRounding.AwayFromZero);
        return value with
        {
            ArrivalStopwatchTicks = value.ArrivalStopwatchTicks + stopwatchShift,
            DeviceTimestampTicks100ns = Add(value.DeviceTimestampTicks100ns, ticks100ns),
            SampleTimeTicks100ns = Add(value.SampleTimeTicks100ns, ticks100ns),
            ReaderTimestampTicks100ns = Add(value.ReaderTimestampTicks100ns, ticks100ns)
        };
    }

    private static long? Add(long? value, long delta) => value is { } ticks ? ticks + delta : null;
}
