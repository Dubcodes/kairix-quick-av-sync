using System.Globalization;

namespace Kairix.QuickAVSync.DetectorFixtureRunner;

internal sealed record FixtureAnnotation(string Filename, string Annotation, string Type, double AnchorSeconds, string Split)
{
    public string Id => $"{Filename}|{Annotation}";

    public static FixtureAnnotation Parse(string line, double frameRate)
    {
        var fields = line.Split(',');
        if (fields.Length != 3) throw new InvalidDataException($"Invalid manifest row: {line}");
        var position = fields[1].Split(':');
        if (position.Length != 2 || !int.TryParse(position[0], out var seconds) || !int.TryParse(position[1], out var frame))
            throw new InvalidDataException($"Invalid SS:FF annotation: {fields[1]}");
        if (frame < 0 || frame >= Math.Ceiling(frameRate))
            throw new InvalidDataException($"Frame component is outside the {frameRate:0.###} fps cadence: {fields[1]}");
        return new(fields[0], fields[1], fields[2], seconds + frame / frameRate,
            fields[0].Contains("-black", StringComparison.OrdinalIgnoreCase) ? "tuning" : "validation");
    }
}

internal sealed record ContactReference(string Filename, string Annotation, double ContactSeconds)
{
    public string Id => $"{Filename}|{Annotation}";

    public static ContactReference Parse(string line)
    {
        var fields = line.Split(',').Select(field => field.Trim('"')).ToArray();
        if (fields.Length < 4) throw new InvalidDataException($"Invalid contact-reference row: {line}");
        return new(fields[0], fields[1], double.Parse(fields[3], CultureInfo.InvariantCulture));
    }
}

internal sealed record MediaInventory(
    string Filename,
    double DurationSeconds,
    string VideoCodec,
    int Width,
    int Height,
    string PixelFormat,
    string FieldOrder,
    string RealFrameRate,
    string AverageFrameRate,
    string VideoTimeBase,
    string AudioCodec,
    int AudioSampleRate,
    int AudioChannels,
    string AudioLayout,
    string AudioTimeBase)
{
    public double FrameRate => ParseRate(AverageFrameRate);

    private static double ParseRate(string text)
    {
        var parts = text.Split('/');
        return parts.Length == 2
            ? double.Parse(parts[0], CultureInfo.InvariantCulture) / double.Parse(parts[1], CultureInfo.InvariantCulture)
            : double.Parse(text, CultureInfo.InvariantCulture);
    }
}

internal sealed record ChannelAnalysis(string Name, IReadOnlyList<Kairix.QuickAVSync.Models.AudioTransient> Transients);

internal sealed record ClipAudioAnalysis(
    IReadOnlyList<Kairix.QuickAVSync.Models.AudioTransient> Auto,
    IReadOnlyList<Kairix.QuickAVSync.Models.AudioTransient> Left,
    IReadOnlyList<Kairix.QuickAVSync.Models.AudioTransient> Right);

internal sealed record VisualTriggerResult(
    int TriggerIndex,
    int Sensitivity,
    double TriggerSeconds,
    double? CandidateSeconds,
    double? Confidence,
    double? Score,
    int? TemporalIndex,
    int? ApproachIndex,
    int? ContactIndex,
    double? ApproachQuality,
    double? ContinuityQuality,
    double? DropQuality,
    double? SettleQuality,
    double? Dominance);

internal sealed record EventBenchmarkRow(
    string Filename,
    string Split,
    int Sensitivity,
    string? Annotation,
    string Type,
    double? AnchorSeconds,
    double? AudioTriggerSeconds,
    bool AudioMatched,
    double? CandidateSeconds,
    bool VisualDetected,
    bool Confirmed,
    bool FalsePositive,
    bool Duplicate,
    double? ErrorFromAnchorMilliseconds,
    double? ResolvedContactSeconds,
    double? ErrorFromContactPositions,
    double? Confidence,
    double? Score,
    int? TemporalIndex,
    int? ApproachIndex,
    int? ContactIndex,
    double? ApproachQuality,
    double? ContinuityQuality,
    double? DropQuality,
    double? SettleQuality,
    double? Dominance);

internal sealed record BenchmarkSummary(
    string Split,
    string Type,
    int Sensitivity,
    int Annotated,
    int AudioTriggered,
    int VisualDetected,
    int Confirmed,
    int Missed,
    int FalsePositives,
    int Duplicates,
    double? ConfidenceMedian,
    double? ConfidenceMinimum,
    double? ConfidenceMaximum);

internal sealed record AudioBenchmarkRow(
    string Filename,
    string Split,
    string Channel,
    string? Annotation,
    string Type,
    double? AnchorSeconds,
    double TriggerSeconds,
    double OffsetMilliseconds,
    bool Matched,
    bool FalseTrigger,
    float Peak,
    float NoiseFloor,
    float WindowRms,
    float CrestFactor,
    float PeakToNoiseRatio,
    float AttackRatio,
    bool AudioCandidate,
    double AudioCandidateQuality);

internal sealed record RunnerOptions(string ClipRoot, string Manifest, string Contacts, string Output, string Label, string Split, int Width, int Height, int[] Sensitivities)
{
    public static RunnerOptions Parse(string[] args)
    {
        var clipRoot = "testClips";
        var manifest = Path.Combine("tests", "fixtures", "real-clap-manifest.csv");
        var contacts = Path.Combine("tests", "fixtures", "real-clap-contact-reference.csv");
        var output = Path.Combine("artifacts", "fixture-analysis");
        var label = "human-baseline";
        var split = "all";
        var width = 640;
        var height = 360;
        var sensitivities = new[] { 35, 50, 65, 80 };
        for (var index = 0; index < args.Length; index++)
        {
            var option = args[index];
            var value = index + 1 < args.Length ? args[++index] : throw new ArgumentException($"Missing value for {option}");
            switch (option)
            {
                case "--clip-root": clipRoot = value; break;
                case "--manifest": manifest = value; break;
                case "--contacts": contacts = value; break;
                case "--output": output = value; break;
                case "--label": label = value; break;
                case "--split": split = value.ToLowerInvariant(); break;
                case "--resolution":
                    var dimensions = value.Split('x', 'X');
                    if (dimensions.Length != 2) throw new ArgumentException("Resolution must be WIDTHxHEIGHT.");
                    width = int.Parse(dimensions[0], CultureInfo.InvariantCulture);
                    height = int.Parse(dimensions[1], CultureInfo.InvariantCulture);
                    break;
                case "--sensitivities":
                    sensitivities = value.Split(',').Select(item => int.Parse(item, CultureInfo.InvariantCulture)).ToArray();
                    break;
                default: throw new ArgumentException($"Unknown option {option}");
            }
        }
        if (!Directory.Exists(clipRoot)) throw new DirectoryNotFoundException($"Clip root not found: {clipRoot}");
        if (!File.Exists(manifest)) throw new FileNotFoundException("Fixture manifest not found.", manifest);
        if (!File.Exists(contacts)) throw new FileNotFoundException("Contact-reference file not found.", contacts);
        if (width < 16 || height < 16 || sensitivities.Length == 0 || sensitivities.Any(value => value is < 0 or > 100))
            throw new ArgumentOutOfRangeException(nameof(args));
        if (label.Any(character => !(char.IsLetterOrDigit(character) || character is '-' or '_')))
            throw new ArgumentException("Label may contain only letters, digits, hyphens, and underscores.");
        if (split is not ("all" or "tuning" or "validation"))
            throw new ArgumentException("Split must be all, tuning, or validation.");
        return new(Path.GetFullPath(clipRoot), Path.GetFullPath(manifest), Path.GetFullPath(contacts), Path.GetFullPath(output), label, split, width, height, sensitivities);
    }

    public bool Includes(string filename) => Split == "all"
        || Split == "tuning" && filename.Contains("-black", StringComparison.OrdinalIgnoreCase)
        || Split == "validation" && filename.Contains("-grey", StringComparison.OrdinalIgnoreCase);
}
