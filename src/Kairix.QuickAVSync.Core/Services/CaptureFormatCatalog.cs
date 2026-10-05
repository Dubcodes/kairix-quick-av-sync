using Kairix.QuickAVSync.Models;

namespace Kairix.QuickAVSync.Services;

public enum CaptureFormatVisibility
{
    Recommended,
    StandardDefinition,
    Advanced
}

public static class CaptureFormatCatalog
{
    private static readonly (int Width, int Height)[] RecommendedRasters =
    [
        (1280, 720),
        (1920, 1080),
        (2048, 1080),
        (3840, 2160),
        (4096, 2160)
    ];

    private static readonly double[] RecommendedRates =
    [
        24000d / 1001,
        24,
        25,
        30000d / 1001,
        30,
        50,
        60000d / 1001,
        60
    ];

    public static CaptureFormatVisibility Classify(CaptureFormat format)
    {
        if (!IsRecommendedRate(format.FrameRate.Value)) return CaptureFormatVisibility.Advanced;
        if ((format.Width, format.Height) is (720, 576) or (720, 480)) return CaptureFormatVisibility.StandardDefinition;
        return RecommendedRasters.Contains((format.Width, format.Height))
            ? CaptureFormatVisibility.Recommended
            : CaptureFormatVisibility.Advanced;
    }

    public static bool IsNormallyVisible(CaptureFormat format) => Classify(format) != CaptureFormatVisibility.Advanced;

    public static IReadOnlyList<CaptureFormatOption> VisibleOptions(
        IEnumerable<CaptureFormatOption> allOptions,
        bool showAll,
        string? selectedId = null)
    {
        var all = allOptions.ToArray();
        if (showAll) return all;
        return all.Where(option => string.IsNullOrWhiteSpace(option.Id) ||
            string.Equals(option.Id, selectedId, StringComparison.Ordinal) ||
            option.Format is { } format && IsNormallyVisible(format)).ToArray();
    }

    public static InterpretationFormatOption? SelectInterpretation(
        IEnumerable<InterpretationFormatOption> options,
        CaptureFormat? preferred,
        InterpretationFormatOption? prior,
        bool reconstructFields,
        FieldOrder savedOrder)
    {
        var available = options.ToArray();
        bool MatchesRequestedMode(InterpretationFormatOption value) =>
            value.ReconstructFields == reconstructFields && (!value.ReconstructFields || value.FieldOrder == savedOrder);
        return available.FirstOrDefault(value => preferred is not null && value.TransportRate == preferred.FrameRate && value.TransportScanMode == preferred.ScanMode && value.TransportInterlaceLayout == preferred.InterlaceLayout && MatchesRequestedMode(value))
            ?? available.FirstOrDefault(value => prior is not null && value.TransportRate == prior.TransportRate && value.TransportScanMode == prior.TransportScanMode && value.TransportInterlaceLayout == prior.TransportInterlaceLayout && MatchesRequestedMode(value))
            ?? available.FirstOrDefault(MatchesRequestedMode)
            ?? available.FirstOrDefault();
    }

    private static bool IsRecommendedRate(double rate) => RecommendedRates.Any(candidate =>
        Math.Abs(candidate - rate) <= Math.Max(.025, candidate * .001));
}
