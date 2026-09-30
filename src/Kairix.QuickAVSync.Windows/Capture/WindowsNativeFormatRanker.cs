using Kairix.QuickAVSync.Models;

namespace Kairix.QuickAVSync.Windows.Capture;

public sealed record WindowsNativeFormatCandidate(int NativeIndex, CaptureFormat Format, VideoPixelFormat PixelFormat, bool IsCurrent = false);

public static class WindowsNativeFormatRanker
{
    public static IReadOnlyList<WindowsNativeFormatCandidate> Rank(IEnumerable<WindowsNativeFormatCandidate> candidates) => candidates
        .OrderByDescending(candidate => candidate.IsCurrent)
        .ThenBy(candidate => ConversionCost(candidate.PixelFormat))
        .ThenByDescending(candidate => candidate.Format.ScanMode == ScanMode.Progressive)
        .ThenByDescending(candidate => IsSensibleSize(candidate.Format))
        .ThenByDescending(candidate => candidate.Format.Width * candidate.Format.Height)
        .ThenByDescending(candidate => candidate.Format.FrameRate.Value)
        .ThenBy(candidate => candidate.NativeIndex)
        .ToArray();

    public static WindowsNativeFormatCandidate? TryInRankedOrder(
        IEnumerable<WindowsNativeFormatCandidate> candidates,
        Func<WindowsNativeFormatCandidate, bool> tryCandidate)
    {
        foreach (var candidate in Rank(candidates))
        {
            if (tryCandidate(candidate)) return candidate;
        }

        return null;
    }

    private static int ConversionCost(VideoPixelFormat format) => format == VideoPixelFormat.Unknown ? 100 : 0;

    private static bool IsSensibleSize(CaptureFormat format) => format.Width <= 1920 && format.Height <= 1080;
}
