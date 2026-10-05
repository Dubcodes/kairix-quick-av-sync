using Kairix.QuickAVSync.Models;

namespace Kairix.QuickAVSync.Services;

public static class ReviewTimeline
{
    public static int NearestFrameIndex(IReadOnlyList<VideoFrame> frames, MediaTimestamp audioZero, double relativeMilliseconds)
    {
        if (frames.Count == 0) return -1;
        var target = audioZero.Ticks100ns + (long)Math.Round(relativeMilliseconds * 10_000d);
        return Enumerable.Range(0, frames.Count).MinBy(index => Math.Abs(frames[index].Timestamp.Ticks100ns - target));
    }

    public static bool AcceptsAutomaticEvents(bool autoDetect, bool hold) => autoDetect && !hold;

    public static bool ReturnsLiveAfterAnalysis(bool automaticEvent) => automaticEvent;
}
