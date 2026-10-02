using System.Collections.Concurrent;
using System.Diagnostics;
using Kairix.QuickAVSync.Capture;
using Kairix.QuickAVSync.Models;
using Kairix.QuickAVSync.Services;
using Kairix.QuickAVSync.Windows.Capture;

HardwareProbeArguments options;
try { options = HardwareProbeArguments.Parse(args); }
catch (ArgumentException ex) { Console.Error.WriteLine($"ARGUMENT ERROR: {ex.Message}"); Console.Error.WriteLine(Usage()); return 2; }
if (options.Help) { Console.WriteLine(Usage()); return 0; }

var sink = new ConsoleSink(); var backend = new WindowsCaptureBackend(sink);
var devices = await backend.EnumerateDevicesAsync(default);
CaptureDeviceDescriptor device;
try { device = options.ResolveDevice(devices); }
catch (InvalidOperationException ex) { Console.Error.WriteLine($"DEVICE RESOLUTION FAILED: {ex.Message}"); return 3; }

Console.WriteLine("REQUESTED DEVICE:"); Console.WriteLine(options.DeviceId ?? options.DeviceName ?? "<only available device>");
Console.WriteLine("RESOLVED DEVICE:"); Console.WriteLine(device.FriendlyName); Console.WriteLine(device.Id);

if (options.ListFormats)
{
    Console.WriteLine($"FORMAT ENUMERATION device='{device.FriendlyName}' id='{device.Id}' container='{device.ContainerId}'");
    var formats = await backend.EnumerateFormatsAsync(device, default);
    foreach (var format in formats) Console.WriteLine($"FORMAT id='{format.Id}' display='{format.Display}'");
    var normallyVisible = formats.Count(format => format.Format is { } native && CaptureFormatCatalog.IsNormallyVisible(native));
    Console.WriteLine($"RESULT selectableFormats={formats.Count} recommendedOrSdFormats={normallyVisible} captureStarted=false mediaSaved=false");
    return 0;
}

InputSignalInfo? preferredSource = null;
if (options.SourceId is not null)
{
    var sourceOption = InputSignalOptions.Common.FirstOrDefault(candidate => string.Equals(candidate.Id, options.SourceId, StringComparison.Ordinal));
    if (sourceOption?.Signal is null) { Console.Error.WriteLine($"SOURCE ERROR: Unknown manual source option '{options.SourceId}'."); return 4; }
    preferredSource = sourceOption.Signal;
}
FieldReconstructionOptions? reconstruction = null;
if (options.ReconstructFields)
{
    if (options.ModeId is null) { Console.Error.WriteLine("INTERPRETATION ERROR: --reconstruct-fields requires an explicit progressive --mode."); return 4; }
    var ratePart = options.ModeId.Split('|');
    if (ratePart.Length < 4 || ratePart[2] != "p" || !TryRate(ratePart[1], out var transportRate) || transportRate is null || (transportRate != Rational.From(25) && transportRate != Rational.From(30_000, 1_001)))
    { Console.Error.WriteLine("INTERPRETATION ERROR: reconstruction currently supports strict 25p or 30000/1001p transport modes."); return 4; }
    reconstruction = new(transportRate.Value, Rational.From(transportRate.Value.Numerator * 2, transportRate.Value.Denominator), options.FieldOrder == "bottom" ? FieldOrder.BottomFirst : FieldOrder.TopFirst);
}

Console.WriteLine("REQUESTED MODE:"); Console.WriteLine(options.ModeId ?? "Auto");
Console.WriteLine("INPUT INTERPRETATION:"); Console.WriteLine(reconstruction is null ? "Native progressive frames" : $"{reconstruction.CapturedProgressiveRate.Value:0.000}p -> {reconstruction.TargetFieldRate.Value:0.000}i · {reconstruction.FieldOrder}");
ICaptureSession session;
try { session = await backend.OpenAsync(device, new(PreferredNativeFormatId: options.ModeId, PreferredSourceSignal: preferredSource, RequirePreferredNativeFormat: options.ModeId is not null, FieldReconstruction: reconstruction), default); }
catch (Exception ex) { Console.Error.WriteLine("FORMAT NEGOTIATION FAILED"); Console.Error.WriteLine($"Requested: {options.ModeId ?? "Auto"}"); Console.Error.WriteLine(ex.Message); return 5; }
await using var ownedSession = session;
Console.WriteLine("NEGOTIATED MODE:"); Console.WriteLine($"{session.CurrentFormat.Display} {session.CurrentFormat.PixelFormat}");
Console.WriteLine($"NEGOTIATED MODE ID: {WindowsNativeFormatRanker.ModeId(session.CurrentFormat, session.CurrentFormat.PixelFormat)}");

var samples = 0; var audioBlocks = 0; var frames = new ConcurrentQueue<VideoFrame>();
var fiveFrames = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
session.StatusChanged += (_, status) => Console.WriteLine($"STATUS {status.Status}: {status.Message}");
session.VideoSampleReceived += (_, frame) =>
{
    var count = Interlocked.Increment(ref samples); frames.Enqueue(frame);
    if (count <= 6) Console.WriteLine($"FRAME {count}: {frame.Width}x{frame.Height} kind={frame.TemporalImageKind} timestampOrigin={frame.TimestampOrigin} timestamp={frame.Timestamp.Ticks100ns} quality={frame.Timestamp.Quality} source={frame.TimingObservation?.PrimarySource} bytes={frame.Luma.Length}");
    if (count == 5) fiveFrames.TrySetResult();
};
session.AudioSampleReceived += (_, chunk) => { if (Interlocked.Increment(ref audioBlocks) == 1) Console.WriteLine($"AUDIO rate={chunk.SampleRate} channels={chunk.Channels} frames={chunk.FrameCount} quality={chunk.Timestamp.Quality} domain={chunk.Timestamp.ClockDomain}"); };
var stopwatch = Stopwatch.StartNew(); await session.StartAsync(default); await fiveFrames.Task.WaitAsync(TimeSpan.FromSeconds(5));
await Task.Delay(options.AnalyzeSignal || options.TimingDetail ? TimeSpan.FromSeconds(7) : TimeSpan.FromSeconds(1)); stopwatch.Stop();

var captured = frames.ToArray();
var declaredRate = reconstruction?.ReviewTemporalRate ?? (session.CurrentFormat.TemporalImageDuration.TotalSeconds > 0 ? 1 / session.CurrentFormat.TemporalImageDuration.TotalSeconds : 0);
var timing = new FrameTimingAnalyzer().Analyze(captured, declaredRate);
var review = captured.Length == 0 ? null : ReviewTimelineIntegrity.Build(captured, captured[captured.Length / 2].Timestamp, TimeSpan.FromMilliseconds(250), declaredRate);
if (options.TimingDetail) PrintTimingDetail(captured, 100);

Console.WriteLine("DEVICE"); Console.WriteLine($"Requested: {options.DeviceId ?? options.DeviceName ?? "<only available device>"}"); Console.WriteLine($"Resolved: {device.FriendlyName} [{device.Id}]");
Console.WriteLine("FORMAT"); Console.WriteLine($"Requested: {options.ModeId ?? "Auto"}"); Console.WriteLine($"Negotiated: {session.CurrentFormat.Display} {session.CurrentFormat.PixelFormat}");
Console.WriteLine($"Interpretation: {(reconstruction is null ? "native capture cadence" : $"{reconstruction.CapturedProgressiveRate.Value:0.000}p -> {reconstruction.TargetFieldRate.Value:0.000}i {reconstruction.FieldOrder}")}");
if (reconstruction is not null) Console.WriteLine($"Reconstructed field timing: {reconstruction.FieldOrder}; captured sample assumed to represent second field / completed pair; interval={reconstruction.FieldIntervalTicks100ns / 10_000d:0.###} ms");
Console.WriteLine("TIMESTAMPS");
Console.WriteLine($"Primary source: {captured.FirstOrDefault()?.TimingObservation?.PrimarySource.ToString() ?? captured.FirstOrDefault()?.Timestamp.Quality.ToString() ?? "unknown"}");
Console.WriteLine($"Declared rate: {timing.DeclaredTemporalRate:0.###} fps"); Console.WriteLine($"Observed primary rate: {timing.Primary.ObservedRate:0.###} fps"); Console.WriteLine($"Observed arrival rate: {timing.Arrival.ObservedRate:0.###} fps");
Console.WriteLine($"Median interval: {timing.Primary.MedianIntervalMilliseconds:0.###} ms"); Console.WriteLine($"Jitter: {timing.Primary.JitterPercent:0.###}%");
Console.WriteLine($"Duplicate timestamps: {timing.Primary.DuplicateCount}"); Console.WriteLine($"Backwards timestamps: {timing.Primary.BackwardCount}"); Console.WriteLine($"Large gaps: {timing.Primary.LargeGapCount}");
PrintSource("DeviceTimestamp", timing.DeviceTimestamp); PrintSource("SampleTime", timing.SampleTime); PrintSource("ReaderTimestamp", timing.ReaderTimestamp);
if (review is not null)
{
    var center = captured[captured.Length / 2].Timestamp.Ticks100ns;
    var offsets = review.Frames.Select(frame => (frame.Timestamp.Ticks100ns - center) / 10_000d).ToArray();
    Console.WriteLine("REVIEW TIMELINE");
    Console.WriteLine($"Frames in 500 ms window: {review.Frames.Count} (raw {review.RawFrameCount})");
    Console.WriteLine($"Median adjacent spacing: {review.Analysis.Primary.MedianIntervalMilliseconds:0.###} ms");
    Console.WriteLine($"First/last offsets: {(offsets.Length == 0 ? 0 : offsets[0]):0.###} / {(offsets.Length == 0 ? 0 : offsets[^1]):0.###} ms");
}
Console.WriteLine("CONTENT"); Console.WriteLine($"Near-identical consecutive samples: {timing.NearIdenticalConsecutiveImages}"); Console.WriteLine("Near-identical content is diagnostic only; distinct valid timestamps are retained.");
if (timing.Content.PairedRepeatDetected) Console.WriteLine($"REPEATED FRAME PAIRS DETECTED: {timing.Primary.ObservedRate:0.###} timestamped fps; approximately {timing.Content.EstimatedUniqueImageRate:0.###} unique images/sec. Capture device may be deinterlacing or frame-rate converting the source.");
if (options.AnalyzeSignal)
{
    var signal = new ObservedSignalAnalyzer().Analyze(captured);
    Console.WriteLine("SOURCE ANALYSIS");
    Console.WriteLine($"estimate='{InputSignalFormatter.Format(signal.Signal)}' authority={signal.Signal.Authority} nearIdenticalFraction={signal.NearIdenticalFraction:P1} sceneActivity={signal.SceneActivitySufficient} pairedRepeat={signal.PairedRepeatDetected} uniqueRate={signal.EstimatedUniqueImageRate?.ToString("0.###") ?? "unknown"} repeat='{signal.RepeatPattern}' interlaceEvidence={signal.InterlaceEvidence:0.000}");
}
var result = !timing.TimingValid ? "FAIL" : timing.DeclaredVsObservedErrorPercent >= 3 ? "WARNING" : "PASS";
Console.WriteLine("RESULT"); Console.WriteLine(result); Console.WriteLine($"SUMMARY payloadFrames={samples} audioBlocks={audioBlocks} elapsedMs={stopwatch.ElapsedMilliseconds} mediaSaved=false");
return result == "FAIL" ? 5 : 0;

static void PrintSource(string name, CadenceStatistics statistics) => Console.WriteLine($"{name}: samples={statistics.Samples} rate={statistics.ObservedRate:0.###} fps median={statistics.MedianIntervalMilliseconds:0.###} ms duplicates={statistics.DuplicateCount} backwards={statistics.BackwardCount}");
static void PrintTimingDetail(IReadOnlyList<VideoFrame> frames, int maximumRows)
{
    Console.WriteLine("TIMING DETAIL (milliseconds; bounded)"); Console.WriteLine("#\tprimary Δ\tdevice Δ\tsample Δ\treader Δ\tarrival Δ\timage MAD");
    for (var index = 0; index < Math.Min(frames.Count, maximumRows); index++)
    {
        if (index == 0) { Console.WriteLine("1\t-\t-\t-\t-\t-\t-"); continue; }
        var prior = frames[index - 1]; var current = frames[index];
        Console.WriteLine($"{index + 1}\t{Delta(prior.Timestamp.Ticks100ns, current.Timestamp.Ticks100ns)}\t{Delta(prior.TimingObservation?.DeviceTimestampTicks100ns, current.TimingObservation?.DeviceTimestampTicks100ns)}\t{Delta(prior.TimingObservation?.SampleTimeTicks100ns, current.TimingObservation?.SampleTimeTicks100ns)}\t{Delta(prior.TimingObservation?.ReaderTimestampTicks100ns, current.TimingObservation?.ReaderTimestampTicks100ns)}\t{ArrivalDelta(prior.TimingObservation, current.TimingObservation)}\t{ImageMeanAbsoluteDifference(prior, current):0.###}");
    }
}
static string Delta(long? first, long? second) => first.HasValue && second.HasValue ? $"{(second.Value - first.Value) / 10_000d:0.###}" : "-";
static string ArrivalDelta(VideoTimingObservation? first, VideoTimingObservation? second) => first is { StopwatchFrequency: > 0 } && second is not null ? $"{(second.ArrivalStopwatchTicks - first.ArrivalStopwatchTicks) * 1000d / first.StopwatchFrequency:0.###}" : "-";
static double ImageMeanAbsoluteDifference(VideoFrame first, VideoFrame second)
{
    var width = Math.Min(first.Width, second.Width); var height = Math.Min(first.Height, second.Height); long total = 0; var count = 0;
    var stepX = Math.Max(1, width / 160); var stepY = Math.Max(1, height / 90);
    for (var y = 0; y < height; y += stepY) for (var x = 0; x < width; x += stepX)
    {
        var a = y * first.EffectiveStride + x; var b = y * second.EffectiveStride + x;
        if (a >= first.Luma.Length || b >= second.Luma.Length) continue;
        total += Math.Abs(first.Luma[a] - second.Luma[b]); count++;
    }
    return count == 0 ? 0 : total / (double)count;
}
static bool TryRate(string text, out Rational? rate)
{
    rate = null; var parts = text.Split('/');
    if (!int.TryParse(parts[0], out var numerator) || (parts.Length > 1 && !int.TryParse(parts[1], out _))) return false;
    var denominator = parts.Length > 1 ? int.Parse(parts[1]) : 1;
    rate = Rational.From(numerator, denominator); return true;
}
static string Usage() => """
Kairix Quick A/V Sync HardwareProbe

Canonical usage:
  --device "<friendly name>" [--mode "<native mode id>"] [--reconstruct-fields] [--field-order top|bottom] [--analyze-signal] [--timing-detail]

Options:
  --device <name>       Exact friendly-name match; ambiguity requires --device-id.
  --device-id <id>      Exact stable device ID.
  --mode <id>           Strict native mode; unavailable/rejected/mismatched negotiation fails.
  --source <id>         Manual physical-source option used only by Auto mode.
  --reconstruct-fields  Split each supported progressive transport frame into two bobbed temporal fields.
  --field-order <order> Field order for reconstruction: top (default) or bottom.
  --list-formats        List native modes without starting capture.
  --analyze-signal      Run bounded passive signal analysis.
  --timing-detail       Print at most the first 100 timing rows.
  --help                Show this help.

One positional friendly name remains supported for backward compatibility.
""";
sealed class ConsoleSink : IDiagnosticSink { public void Write(string category, string message) => Console.WriteLine($"[{category}] {message}"); }
