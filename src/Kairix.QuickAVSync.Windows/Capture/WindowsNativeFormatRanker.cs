using Kairix.QuickAVSync.Models;

namespace Kairix.QuickAVSync.Windows.Capture;

public sealed record WindowsNativeFormatCandidate(int NativeIndex, CaptureFormat Format, VideoPixelFormat PixelFormat);

public static class WindowsNativeFormatRanker
{
    public static IReadOnlyList<WindowsNativeFormatCandidate> Rank(IEnumerable<WindowsNativeFormatCandidate> candidates) => candidates
        .OrderByDescending(candidate => candidate.Format.Width == 1920 && candidate.Format.Height == 1080)
        .ThenByDescending(candidate => RatePriority(candidate.Format.FrameRate.Value))
        .ThenByDescending(candidate => candidate.Format.Width * candidate.Format.Height)
        .ThenBy(candidate => ConversionCost(candidate.PixelFormat))
        .ThenByDescending(candidate => candidate.Format.ScanMode == ScanMode.Progressive)
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

    private static int ConversionCost(VideoPixelFormat format) => format switch
    {
        VideoPixelFormat.Nv12 => 0,
        VideoPixelFormat.Yuy2 or VideoPixelFormat.Uyvy => 1,
        VideoPixelFormat.Bgra32 or VideoPixelFormat.Bgr24 => 2,
        _ => 100
    };

    private static int RatePriority(double rate) =>
        Math.Abs(rate - 50) < .02 ? 5 :
        Math.Abs(rate - 25) < .02 ? 4 :
        Math.Abs(rate - 59.94) < .02 ? 3 :
        Math.Abs(rate - 29.97) < .02 ? 2 : 1;
}
