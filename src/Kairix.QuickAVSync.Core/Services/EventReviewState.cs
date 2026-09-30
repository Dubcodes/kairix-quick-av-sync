using Kairix.QuickAVSync.Models;

namespace Kairix.QuickAVSync.Services;

public enum ReviewResultMode
{
    None,
    Auto,
    ManualPreview,
    ManualResult
}

public sealed class EventReviewState
{
    private bool _finalized;

    public EventReviewState(MediaTimestamp eventReference, MediaTimestamp audioMark)
    {
        EventReference = eventReference;
        AudioMark = audioMark;
        Playhead = eventReference;
    }

    public MediaTimestamp EventReference { get; }
    public MediaTimestamp AudioMark { get; private set; }
    public VisualCandidate? AutoCandidate { get; private set; }
    public MediaTimestamp? ManualVisualMark { get; private set; }
    public MediaTimestamp Playhead { get; private set; }
    public ReviewResultMode ResultMode { get; private set; }

    public double AudioOffsetMs => OffsetFromReference(AudioMark);
    public double? AutoOffsetMs => AutoCandidate is null ? null : OffsetFromReference(AutoCandidate.Timestamp);
    public double? ManualVisualOffsetMs => ManualVisualMark is null ? null : OffsetFromReference(ManualVisualMark.Value);
    public double PlayheadOffsetMs => OffsetFromReference(Playhead);

    public SyncResult? CurrentResult
    {
        get
        {
            var visual = ResultMode switch
            {
                ReviewResultMode.Auto => AutoCandidate?.Timestamp,
                ReviewResultMode.ManualPreview => Playhead,
                ReviewResultMode.ManualResult => ManualVisualMark,
                _ => null
            };
            return visual is null ? null : SyncResult.Calculate(AudioMark, visual.Value);
        }
    }

    public void SetInitialPlayhead(MediaTimestamp timestamp) => Playhead = timestamp;

    public void SetAutoCandidate(VisualCandidate candidate)
    {
        AutoCandidate = candidate;
        if (ResultMode == ReviewResultMode.None)
        {
            Playhead = candidate.Timestamp;
            ResultMode = ReviewResultMode.Auto;
        }
    }

    public void MovePlayhead(MediaTimestamp timestamp)
    {
        Playhead = timestamp;
        ResultMode = ReviewResultMode.ManualPreview;
    }

    public void ShowAutoCandidate()
    {
        if (AutoCandidate is null) return;
        Playhead = AutoCandidate.Timestamp;
        if (ResultMode == ReviewResultMode.None) ResultMode = ReviewResultMode.Auto;
    }

    public void CommitManualVisual()
    {
        ManualVisualMark = Playhead;
        ResultMode = ReviewResultMode.ManualResult;
    }

    public void MoveAudioMark(double offsetMilliseconds, double halfWindowMilliseconds)
    {
        var clamped = Math.Clamp(offsetMilliseconds, -halfWindowMilliseconds, halfWindowMilliseconds);
        AudioMark = new(
            EventReference.Ticks100ns + (long)Math.Round(clamped * 10_000d),
            AudioMark.Quality,
            AudioMark.ClockDomain,
            AudioMark.RawValue);
    }

    public bool TryFinalize(out SyncResult? result)
    {
        result = CurrentResult;
        if (_finalized || result is not { TimingComparable: true }) return false;
        _finalized = true;
        return true;
    }

    private double OffsetFromReference(MediaTimestamp timestamp) =>
        (timestamp.Ticks100ns - EventReference.Ticks100ns) / 10_000d;
}
