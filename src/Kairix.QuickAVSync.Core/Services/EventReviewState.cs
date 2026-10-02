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

    public EventReviewState(MediaTimestamp eventReference, MediaTimestamp audioMark, VideoTimingOffset videoTimingOffset = default)
    {
        EventReference = eventReference;
        AudioMark = audioMark;
        VideoTimingOffset = videoTimingOffset;
        Playhead = VideoTimingCompensation.ExpectedRawVisual(eventReference, videoTimingOffset);
    }

    public MediaTimestamp EventReference { get; }
    public MediaTimestamp AudioMark { get; private set; }
    public VisualCandidate? AutoCandidate { get; private set; }
    public MediaTimestamp? ManualVisualMark { get; private set; }
    public MediaTimestamp Playhead { get; private set; }
    public ReviewResultMode ResultMode { get; private set; }
    public VideoTimingOffset VideoTimingOffset { get; private set; }

    public double AudioOffsetMs => OffsetFromReference(AudioMark);
    public double? AutoOffsetMs => AutoCandidate is null ? null : CorrectedOffsetFromReference(AutoCandidate.Timestamp);
    public double? ManualVisualOffsetMs => ManualVisualMark is null ? null : CorrectedOffsetFromReference(ManualVisualMark.Value);
    public double PlayheadOffsetMs => CorrectedOffsetFromReference(Playhead);
    public double RawPlayheadOffsetMs => OffsetFromReference(Playhead);
    public MediaTimestamp ExpectedRawVisualTimestamp => VideoTimingCompensation.ExpectedRawVisual(AudioMark, VideoTimingOffset);

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
            return visual is null ? null : VideoTimingCompensation.Calculate(AudioMark, visual.Value, VideoTimingOffset);
        }
    }

    public SyncResult? CurrentRawResult
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

    public void SetVideoTimingOffset(VideoTimingOffset value) => VideoTimingOffset = value;

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
    private double CorrectedOffsetFromReference(MediaTimestamp timestamp) =>
        VideoTimingCompensation.CorrectedOffsetMilliseconds(EventReference, timestamp, VideoTimingOffset);
}
