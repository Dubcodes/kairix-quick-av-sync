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
        var scaledDenominator = checked(10_000_000L * reduced.Denominator);
        return checked((scaledDenominator + reduced.Numerator / 2L) / reduced.Numerator);
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
            // Both positions retain the one factual transport observation from the
            // native sample. Only the review timestamp is reconstructed.
            new(firstKind, firstTimestamp, TimestampOrigin.ReconstructedFirstField, observation),
            new(secondKind, captureTimestamp, TimestampOrigin.CaptureTimestampAssumedSecondField, observation)
        ];
    }
}
