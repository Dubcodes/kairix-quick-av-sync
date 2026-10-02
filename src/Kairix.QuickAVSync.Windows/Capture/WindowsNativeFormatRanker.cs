using Kairix.QuickAVSync.Models;
using Kairix.QuickAVSync.Services;

namespace Kairix.QuickAVSync.Windows.Capture;

public sealed record WindowsNativeFormatCandidate(int NativeIndex, CaptureFormat Format, VideoPixelFormat PixelFormat, bool IsCurrent = false);

public static class WindowsNativeFormatRanker
{
    public static IReadOnlyList<WindowsNativeFormatCandidate> Rank(IEnumerable<WindowsNativeFormatCandidate> candidates) => candidates
        .OrderBy(candidate => ConversionCost(candidate.PixelFormat))
        .ThenByDescending(candidate => candidate.Format.ScanMode == ScanMode.Progressive)
        .ThenByDescending(candidate => IsSensibleSize(candidate.Format))
        .ThenByDescending(candidate => candidate.Format.Width * candidate.Format.Height)
        .ThenByDescending(candidate => candidate.Format.FrameRate.Value)
        .ThenByDescending(candidate => candidate.IsCurrent)
        .ThenBy(candidate => candidate.NativeIndex)
        .ToArray();

    public static IReadOnlyList<WindowsNativeFormatCandidate> Rank(IEnumerable<WindowsNativeFormatCandidate> candidates, string? preferredModeId)
    {
        var list = candidates.ToArray();
        if (string.IsNullOrWhiteSpace(preferredModeId)) return Rank(list);
        var autoRanked = Rank(list);
        var positions = autoRanked.Select((candidate, index) => (candidate.NativeIndex, index)).ToDictionary(x => x.NativeIndex, x => x.index);
        return list.OrderByDescending(candidate => string.Equals(ModeId(candidate), preferredModeId, StringComparison.Ordinal)).ThenBy(candidate => positions[candidate.NativeIndex]).ToArray();
    }

    public static IReadOnlyList<WindowsNativeFormatCandidate> Rank(
        IEnumerable<WindowsNativeFormatCandidate> candidates,
        string? preferredModeId,
        InputSignalInfo? preferredSourceSignal)
        => Rank(candidates, preferredModeId, preferredSourceSignal, InterlacedInputHandling.Unknown);

    public static IReadOnlyList<WindowsNativeFormatCandidate> Rank(
        IEnumerable<WindowsNativeFormatCandidate> candidates,
        string? preferredModeId,
        InputSignalInfo? preferredSourceSignal,
        InterlacedInputHandling interlacedInputHandling)
    {
        var list = candidates.ToArray();
        return string.IsNullOrWhiteSpace(preferredModeId)
            ? RankForSource(list, preferredSourceSignal, interlacedInputHandling)
            : Rank(list, preferredModeId);
    }

    public static IReadOnlyList<WindowsNativeFormatCandidate> Rank(
        IEnumerable<WindowsNativeFormatCandidate> candidates,
        string? preferredModeId,
        InputSignalInfo? preferredSourceSignal,
        bool requirePreferredMode)
    {
        var list = candidates.ToArray();
        if (!requirePreferredMode) return Rank(list, preferredModeId, preferredSourceSignal);
        if (string.IsNullOrWhiteSpace(preferredModeId)) throw new InvalidOperationException("Strict native-format selection requires a preferred mode ID.");
        var exact = list.Where(candidate => string.Equals(ModeId(candidate), preferredModeId, StringComparison.Ordinal)).ToArray();
        return exact.Length == 1 ? exact : throw new InvalidOperationException($"REQUESTED CAPTURE FORMAT NOT ACCEPTED: native mode '{preferredModeId}' is not exposed by this device.");
    }

    public static IReadOnlyList<WindowsNativeFormatCandidate> RankForSource(IEnumerable<WindowsNativeFormatCandidate> candidates, InputSignalInfo? source, InterlacedInputHandling handling = InterlacedInputHandling.Unknown)
    {
        var autoRanked = Rank(candidates);
        if (!SourceAwareFormatMatcher.CanAutomaticallyApply(source, handling)) return autoRanked;
        var formats = autoRanked.Select(candidate => candidate.Format).ToArray();
        var recommended = SourceAwareFormatMatcher.Recommend(formats, source, handling);
        if (recommended is null) return autoRanked;
        return autoRanked.OrderByDescending(candidate => candidate.Format == recommended).ToArray();
    }

    public static string ModeId(WindowsNativeFormatCandidate candidate) => ModeId(candidate.Format, candidate.PixelFormat);
    public static string ModeId(CaptureFormat format, VideoPixelFormat pixelFormat)
    {
        var scan = format.ScanMode switch
        {
            _ when format.InterlaceLayout == InterlaceLayout.Mixed => "i-mixed",
            ScanMode.Progressive => "p",
            ScanMode.Interlaced => $"i-{format.InterlaceLayout.ToString().ToLowerInvariant()}-{format.FieldOrder.ToString().ToLowerInvariant()}",
            _ => "u"
        };
        return $"{format.Width}x{format.Height}|{format.FrameRate.Numerator}/{format.FrameRate.Denominator}|{scan}|{pixelFormat}";
    }

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

public static class WindowsNativeFormatVerifier
{
    public static bool Matches(WindowsNativeFormatCandidate requested, CaptureFormat negotiated, VideoPixelFormat negotiatedPixelFormat) =>
        requested.Format.Width == negotiated.Width && requested.Format.Height == negotiated.Height &&
        requested.Format.FrameRate == negotiated.FrameRate && requested.Format.ScanMode == negotiated.ScanMode &&
        requested.Format.InterlaceLayout == negotiated.InterlaceLayout && requested.Format.FieldOrder == negotiated.FieldOrder &&
        requested.PixelFormat == negotiatedPixelFormat;
}
