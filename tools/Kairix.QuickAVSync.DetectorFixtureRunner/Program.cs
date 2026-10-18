using System.Globalization;
using System.Text;
using Kairix.QuickAVSync.DetectorFixtureRunner;
using Kairix.QuickAVSync.Models;
using Kairix.QuickAVSync.Services;

var options = RunnerOptions.Parse(args);
FixtureProcesses.RequireTools();
Directory.CreateDirectory(options.Output);

var clipPaths = Directory.GetFiles(options.ClipRoot, "*.mp4").Where(path => options.Includes(Path.GetFileName(path)))
    .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToArray();
if (clipPaths.Length == 0) throw new InvalidOperationException("No MP4 fixtures found.");
var inventories = new List<MediaInventory>();
foreach (var path in clipPaths)
{
    var inventory = await FixtureProcesses.ProbeAsync(path);
    inventories.Add(inventory);
    Console.WriteLine($"inventory {inventory.Filename}: {inventory.Width}x{inventory.Height} {inventory.AverageFrameRate} {inventory.FieldOrder}, audio {inventory.AudioSampleRate} Hz {inventory.AudioChannels}ch");
}
WriteInventory(Path.Combine(options.Output, "media-inventory.csv"), inventories);

var rates = inventories.Select(item => item.FrameRate).Distinct().ToArray();
if (rates.Length != 1 || Math.Abs(rates[0] - 50) > .001)
    throw new InvalidOperationException("This manifest uses SS:FF and requires one verified 50 fps fixture cadence.");
var manifestLines = File.ReadAllLines(options.Manifest).Skip(1).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
var annotations = manifestLines.Select(line => FixtureAnnotation.Parse(line, rates[0])).Where(item => options.Includes(item.Filename)).ToArray();
var contactReferences = File.ReadAllLines(options.Contacts).Skip(1).Where(line => !string.IsNullOrWhiteSpace(line))
    .Select(ContactReference.Parse).Where(item => options.Includes(item.Filename)).ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
var unknownFiles = annotations.Select(item => item.Filename).Except(inventories.Select(item => item.Filename), StringComparer.OrdinalIgnoreCase).ToArray();
if (unknownFiles.Length > 0) throw new InvalidOperationException($"Manifest clips missing: {string.Join(", ", unknownFiles)}");

var eventRows = new List<EventBenchmarkRow>();
var audioRows = new List<AudioBenchmarkRow>();
foreach (var inventory in inventories)
{
    var path = Path.Combine(options.ClipRoot, inventory.Filename);
    var clipAnnotations = annotations.Where(item => item.Filename.Equals(inventory.Filename, StringComparison.OrdinalIgnoreCase)).OrderBy(item => item.AnchorSeconds).ToArray();
    var audio = await FixtureProcesses.AnalyzeAudioAsync(path, inventory.AudioSampleRate, inventory.AudioChannels);
    AppendAudioRows(audioRows, inventory.Filename, clipAnnotations, "Auto", audio.Auto);
    AppendAudioRows(audioRows, inventory.Filename, clipAnnotations, "Left", audio.Left);
    AppendAudioRows(audioRows, inventory.Filename, clipAnnotations, "Right", audio.Right);
    Console.WriteLine($"audio {inventory.Filename}: Auto={audio.Auto.Count}, Left={audio.Left.Count}, Right={audio.Right.Count}");

    var visual = await FixtureProcesses.AnalyzeVideoAsync(path, inventory.FrameRate, audio.Auto, options.Width, options.Height, options.Sensitivities);
    foreach (var sensitivity in options.Sensitivities)
        AppendEventRows(eventRows, inventory.Filename, clipAnnotations, contactReferences, audio.Auto, visual.Where(item => item.Sensitivity == sensitivity).OrderBy(item => item.TriggerIndex).ToArray(), sensitivity, inventory.FrameRate);
    Console.WriteLine($"visual {inventory.Filename}: {visual.Count} trigger/sensitivity evaluations");
}

var summaries = BuildSummaries(eventRows, options.Sensitivities);
var eventPath = Path.Combine(options.Output, $"{options.Label}-events.csv");
var summaryPath = Path.Combine(options.Output, $"{options.Label}-summary.csv");
WriteEvents(eventPath, eventRows);
WriteSummaries(summaryPath, summaries);
WriteAudio(Path.Combine(options.Output, "audio-channel-analysis.csv"), audioRows);
WriteReport(Path.Combine(options.Output, "fixture-report.md"), inventories, annotations, summaries, eventRows, audioRows, options);
Console.WriteLine($"Fixture benchmark complete: {options.Output}");

static void AppendEventRows(List<EventBenchmarkRow> rows, string filename, IReadOnlyList<FixtureAnnotation> annotations,
    IReadOnlyDictionary<string, ContactReference> contactReferences, IReadOnlyList<AudioTransient> triggers,
    IReadOnlyList<VisualTriggerResult> visual, int sensitivity, double frameRate)
{
    const double window = .25;
    var audioMatches = GreedyMatch(annotations.Select(item => item.AnchorSeconds).ToArray(), triggers.Select(item => item.Timestamp.Time.TotalSeconds).ToArray(), window);
    var candidates = visual.Where(item => item.CandidateSeconds is not null).ToArray();
    var visualMatches = GreedyMatch(annotations.Select(item => item.AnchorSeconds).ToArray(), candidates.Select(item => item.CandidateSeconds!.Value).ToArray(), window);
    var matchedCandidateIndices = visualMatches.Values.ToHashSet();
    for (var annotationIndex = 0; annotationIndex < annotations.Count; annotationIndex++)
    {
        var annotation = annotations[annotationIndex];
        var hasAudio = audioMatches.TryGetValue(annotationIndex, out var triggerIndex);
        var hasVisual = visualMatches.TryGetValue(annotationIndex, out var candidateIndex);
        var candidate = hasVisual ? candidates[candidateIndex] : null;
        contactReferences.TryGetValue(annotation.Id, out var contactReference);
        rows.Add(new(filename, annotation.Split, sensitivity, annotation.Annotation, annotation.Type, annotation.AnchorSeconds,
            hasAudio ? triggers[triggerIndex].Timestamp.Time.TotalSeconds : null, hasAudio,
            candidate?.CandidateSeconds, hasVisual, hasVisual, false, false,
            candidate?.CandidateSeconds is double candidateSeconds ? (candidateSeconds - annotation.AnchorSeconds) * 1000 : null,
            contactReference?.ContactSeconds,
            candidate?.CandidateSeconds is double resolvedCandidate && contactReference is not null
                ? (resolvedCandidate - contactReference.ContactSeconds) * frameRate : null,
            candidate?.Confidence, candidate?.Score, candidate?.TemporalIndex, candidate?.ApproachIndex, candidate?.ContactIndex,
            candidate?.ApproachQuality, candidate?.ContinuityQuality, candidate?.DropQuality, candidate?.SettleQuality, candidate?.Dominance));
    }
    for (var candidateIndex = 0; candidateIndex < candidates.Length; candidateIndex++)
    {
        if (matchedCandidateIndices.Contains(candidateIndex)) continue;
        var candidate = candidates[candidateIndex];
        var nearAnnotation = annotations.Any(annotation => Math.Abs(annotation.AnchorSeconds - candidate.CandidateSeconds!.Value) <= window);
        rows.Add(new(filename, filename.Contains("-black", StringComparison.OrdinalIgnoreCase) ? "tuning" : "validation", sensitivity,
            null, "unmatched", null, candidate.TriggerSeconds, false, candidate.CandidateSeconds, true, true, !nearAnnotation, nearAnnotation,
            null, null, null, candidate.Confidence, candidate.Score, candidate.TemporalIndex, candidate.ApproachIndex, candidate.ContactIndex,
            candidate.ApproachQuality, candidate.ContinuityQuality, candidate.DropQuality, candidate.SettleQuality, candidate.Dominance));
    }
}

static void AppendAudioRows(List<AudioBenchmarkRow> rows, string filename, IReadOnlyList<FixtureAnnotation> annotations, string channel, IReadOnlyList<AudioTransient> transients)
{
    const double window = .25;
    var matches = GreedyMatch(annotations.Select(item => item.AnchorSeconds).ToArray(), transients.Select(item => item.Timestamp.Time.TotalSeconds).ToArray(), window);
    var matchedTransientIndices = matches.Values.ToHashSet();
    for (var annotationIndex = 0; annotationIndex < annotations.Count; annotationIndex++)
    {
        var annotation = annotations[annotationIndex];
        if (!matches.TryGetValue(annotationIndex, out var transientIndex)) continue;
        var transient = transients[transientIndex];
        var quality = AudioCandidateClassifier.Evaluate(transient);
        rows.Add(new(filename, annotation.Split, channel, annotation.Annotation, annotation.Type, annotation.AnchorSeconds,
            transient.Timestamp.Time.TotalSeconds, (transient.Timestamp.Time.TotalSeconds - annotation.AnchorSeconds) * 1000,
            true, false, transient.Peak, transient.NoiseFloor, transient.WindowRms, transient.CrestFactor, transient.PeakToNoiseRatio, transient.AttackRatio, quality.Passed, quality.Quality));
    }
    for (var transientIndex = 0; transientIndex < transients.Count; transientIndex++)
    {
        if (matchedTransientIndices.Contains(transientIndex)) continue;
        var transient = transients[transientIndex];
        var quality = AudioCandidateClassifier.Evaluate(transient);
        rows.Add(new(filename, filename.Contains("-black", StringComparison.OrdinalIgnoreCase) ? "tuning" : "validation", channel,
            null, "unmatched", null, transient.Timestamp.Time.TotalSeconds, 0, false, true,
            transient.Peak, transient.NoiseFloor, transient.WindowRms, transient.CrestFactor, transient.PeakToNoiseRatio, transient.AttackRatio, quality.Passed, quality.Quality));
    }
}

static Dictionary<int, int> GreedyMatch(IReadOnlyList<double> anchors, IReadOnlyList<double> events, double window)
{
    var possibilities = (from anchorIndex in Enumerable.Range(0, anchors.Count)
                         from eventIndex in Enumerable.Range(0, events.Count)
                         let distance = Math.Abs(anchors[anchorIndex] - events[eventIndex])
                         where distance <= window
                         orderby distance
                         select (anchorIndex, eventIndex)).ToArray();
    var result = new Dictionary<int, int>();
    var usedEvents = new HashSet<int>();
    foreach (var possibility in possibilities)
        if (!result.ContainsKey(possibility.anchorIndex) && usedEvents.Add(possibility.eventIndex)) result[possibility.anchorIndex] = possibility.eventIndex;
    return result;
}

static IReadOnlyList<BenchmarkSummary> BuildSummaries(IReadOnlyList<EventBenchmarkRow> rows, IReadOnlyList<int> sensitivities)
{
    var output = new List<BenchmarkSummary>();
    foreach (var sensitivity in sensitivities)
    foreach (var split in new[] { "tuning", "validation", "all" })
    foreach (var type in new[] { "all", "clap", "vertical-clap" })
    {
        var selected = rows.Where(row => row.Sensitivity == sensitivity && (split == "all" || row.Split == split)).ToArray();
        var annotations = selected.Where(row => row.Annotation is not null && (type == "all" || row.Type == type)).ToArray();
        var extras = selected.Where(row => row.Annotation is null && type == "all").ToArray();
        var confidences = annotations.Where(row => row.Confirmed && row.Confidence is not null).Select(row => row.Confidence!.Value).OrderBy(value => value).ToArray();
        output.Add(new(split, type, sensitivity, annotations.Length, annotations.Count(row => row.AudioMatched), annotations.Count(row => row.VisualDetected),
            annotations.Count(row => row.Confirmed), annotations.Count(row => !row.Confirmed), extras.Count(row => row.FalsePositive), extras.Count(row => row.Duplicate),
            Median(confidences), confidences.Length == 0 ? null : confidences[0], confidences.Length == 0 ? null : confidences[^1]));
    }
    return output;
}

static double? Median(double[] values) => values.Length == 0 ? null : values.Length % 2 == 0 ? (values[values.Length / 2 - 1] + values[values.Length / 2]) / 2 : values[values.Length / 2];
static string Csv(object? value) => value switch
{
    null => "",
    bool boolean => boolean ? "true" : "false",
    double number => number.ToString("0.######", CultureInfo.InvariantCulture),
    float number => number.ToString("0.######", CultureInfo.InvariantCulture),
    _ => $"\"{value.ToString()!.Replace("\"", "\"\"")}\""
};

static void WriteInventory(string path, IReadOnlyList<MediaInventory> items)
{
    using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
    writer.WriteLine("filename,durationSeconds,videoCodec,width,height,pixelFormat,fieldOrder,rFrameRate,avgFrameRate,videoTimeBase,audioCodec,audioSampleRate,audioChannels,audioLayout,audioTimeBase");
    foreach (var item in items) writer.WriteLine(string.Join(',', Csv(item.Filename), Csv(item.DurationSeconds), Csv(item.VideoCodec), item.Width, item.Height, Csv(item.PixelFormat), Csv(item.FieldOrder), Csv(item.RealFrameRate), Csv(item.AverageFrameRate), Csv(item.VideoTimeBase), Csv(item.AudioCodec), item.AudioSampleRate, item.AudioChannels, Csv(item.AudioLayout), Csv(item.AudioTimeBase)));
}

static void WriteEvents(string path, IReadOnlyList<EventBenchmarkRow> rows)
{
    using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
    writer.WriteLine("filename,split,sensitivity,annotation,type,anchorSeconds,audioTriggerSeconds,audioMatched,candidateSeconds,visualDetected,confirmed,falsePositive,duplicate,errorFromAnchorMilliseconds,resolvedContactSeconds,errorFromContactPositions,confidence,score,temporalIndex,approachIndex,contactIndex,approachQuality,continuityQuality,dropQuality,settleQuality,dominance");
    foreach (var row in rows) writer.WriteLine(string.Join(',', Csv(row.Filename), Csv(row.Split), row.Sensitivity, Csv(row.Annotation), Csv(row.Type), Csv(row.AnchorSeconds), Csv(row.AudioTriggerSeconds), Csv(row.AudioMatched), Csv(row.CandidateSeconds), Csv(row.VisualDetected), Csv(row.Confirmed), Csv(row.FalsePositive), Csv(row.Duplicate), Csv(row.ErrorFromAnchorMilliseconds), Csv(row.ResolvedContactSeconds), Csv(row.ErrorFromContactPositions), Csv(row.Confidence), Csv(row.Score), Csv(row.TemporalIndex), Csv(row.ApproachIndex), Csv(row.ContactIndex), Csv(row.ApproachQuality), Csv(row.ContinuityQuality), Csv(row.DropQuality), Csv(row.SettleQuality), Csv(row.Dominance)));
}

static void WriteSummaries(string path, IReadOnlyList<BenchmarkSummary> rows)
{
    using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
    writer.WriteLine("split,type,sensitivity,annotated,audioTriggered,visualDetected,confirmed,missed,falsePositives,duplicates,confidenceMedian,confidenceMinimum,confidenceMaximum");
    foreach (var row in rows) writer.WriteLine(string.Join(',', Csv(row.Split), Csv(row.Type), row.Sensitivity, row.Annotated, row.AudioTriggered, row.VisualDetected, row.Confirmed, row.Missed, row.FalsePositives, row.Duplicates, Csv(row.ConfidenceMedian), Csv(row.ConfidenceMinimum), Csv(row.ConfidenceMaximum)));
}

static void WriteAudio(string path, IReadOnlyList<AudioBenchmarkRow> rows)
{
    using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
    writer.WriteLine("filename,split,channel,annotation,type,anchorSeconds,triggerSeconds,offsetMilliseconds,matched,falseTrigger,peak,noiseFloor,windowRms,crestFactor,peakToNoiseRatio,attackRatio,audioCandidate,audioCandidateQuality");
    foreach (var row in rows) writer.WriteLine(string.Join(',', Csv(row.Filename), Csv(row.Split), Csv(row.Channel), Csv(row.Annotation), Csv(row.Type), Csv(row.AnchorSeconds), Csv(row.TriggerSeconds), Csv(row.OffsetMilliseconds), Csv(row.Matched), Csv(row.FalseTrigger), Csv(row.Peak), Csv(row.NoiseFloor), Csv(row.WindowRms), Csv(row.CrestFactor), Csv(row.PeakToNoiseRatio), Csv(row.AttackRatio), Csv(row.AudioCandidate), Csv(row.AudioCandidateQuality)));
}

static void WriteReport(string path, IReadOnlyList<MediaInventory> media, IReadOnlyList<FixtureAnnotation> annotations,
    IReadOnlyList<BenchmarkSummary> summaries, IReadOnlyList<EventBenchmarkRow> events,
    IReadOnlyList<AudioBenchmarkRow> audio, RunnerOptions options)
{
    using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
    writer.WriteLine("# Real fixture benchmark");
    writer.WriteLine();
    writer.WriteLine($"Run label: `{options.Label}`. Analysis raster: {options.Width}×{options.Height}. Event matching window: ±250 ms.");
    writer.WriteLine();
    writer.WriteLine($"Inventory: {media.Count} clips, {media.Sum(item => item.DurationSeconds):0.00} seconds, {annotations.Count} annotated events. All clips are {media[0].AverageFrameRate} progressive with {media[0].AudioSampleRate} Hz {media[0].AudioLayout} audio.");
    writer.WriteLine("Annotations are SS:FF event anchors at the verified 50 fps cadence, not exact contact truth.");
    writer.WriteLine();
    writer.WriteLine("## Human automatic benchmark");
    writer.WriteLine();
    writer.WriteLine("| Split | Type | Sensitivity | Annotated | Audio | Confirmed | Missed | False positives | Duplicates | Confidence median/range |");
    writer.WriteLine("|---|---|---:|---:|---:|---:|---:|---:|---:|---|");
    foreach (var summary in summaries.Where(item => item.Split != "all" || item.Type == "all"))
        writer.WriteLine($"| {summary.Split} | {summary.Type} | {summary.Sensitivity} | {summary.Annotated} | {summary.AudioTriggered} | {summary.Confirmed} | {summary.Missed} | {summary.FalsePositives} | {summary.Duplicates} | {Format(summary.ConfidenceMedian)} ({Format(summary.ConfidenceMinimum)}–{Format(summary.ConfidenceMaximum)}) |");
    writer.WriteLine();
    writer.WriteLine("## Manually resolved contact subset");
    writer.WriteLine();
    writer.WriteLine("| Split | Sensitivity | References | Detected | Exact | Within ±1 position |");
    writer.WriteLine("|---|---:|---:|---:|---:|---:|");
    foreach (var split in new[] { "tuning", "validation", "all" })
    foreach (var sensitivity in options.Sensitivities)
    {
        var selected = events.Where(row => row.Sensitivity == sensitivity && row.ResolvedContactSeconds is not null
            && (split == "all" || row.Split == split)).ToArray();
        var detected = selected.Where(row => row.ErrorFromContactPositions is not null).ToArray();
        writer.WriteLine($"| {split} | {sensitivity} | {selected.Length} | {detected.Length} | {detected.Count(row => Math.Abs(row.ErrorFromContactPositions!.Value) < .5)} | {detected.Count(row => Math.Abs(row.ErrorFromContactPositions!.Value) <= 1.000001)} |");
    }
    writer.WriteLine();
    writer.WriteLine("## Audio trigger evidence");
    writer.WriteLine();
    writer.WriteLine("| Channel | Matched annotations | Unmatched triggers | Audio Candidate matched | Audio Candidate unmatched |");
    writer.WriteLine("|---|---:|---:|---:|---:|");
    foreach (var channel in new[] { "Auto", "Left", "Right" })
    {
        var selected = audio.Where(item => item.Channel == channel).ToArray();
        writer.WriteLine($"| {channel} | {selected.Count(item => item.Matched)} | {selected.Count(item => item.FalseTrigger)} | {selected.Count(item => item.Matched && item.AudioCandidate)} | {selected.Count(item => item.FalseTrigger && item.AudioCandidate)} |");
    }
    writer.WriteLine();
    writer.WriteLine("Left/Right are channel labels only; microphone identity is not inferred.");
}

static string Format(double? value) => value?.ToString("0.000", CultureInfo.InvariantCulture) ?? "—";
