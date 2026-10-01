using System.Diagnostics;
using System.Collections.Concurrent;
using Kairix.QuickAVSync.Capture;
using Kairix.QuickAVSync.Models;
using Kairix.QuickAVSync.Services;
using Kairix.QuickAVSync.Windows.Capture;

var sink = new ConsoleSink();
var backend = new WindowsCaptureBackend(sink);
var devices = await backend.EnumerateDevicesAsync(default);
var modeIndex = Array.FindIndex(args, argument => string.Equals(argument, "--mode", StringComparison.OrdinalIgnoreCase));
var preferredMode = modeIndex >= 0 && modeIndex + 1 < args.Length ? args[modeIndex + 1] : null;
var listFormats = args.Any(argument => string.Equals(argument, "--list-formats", StringComparison.OrdinalIgnoreCase));
var analyzeSignal = args.Any(argument => string.Equals(argument, "--analyze-signal", StringComparison.OrdinalIgnoreCase));
var targetArguments = args.Where((argument, index) => !string.Equals(argument, "--list-formats", StringComparison.OrdinalIgnoreCase) && !string.Equals(argument, "--analyze-signal", StringComparison.OrdinalIgnoreCase) && index != modeIndex && index != modeIndex + 1).ToArray();
var target = targetArguments.Length == 0 ? null : string.Join(' ', targetArguments);
var device = target is null ? devices.FirstOrDefault() : devices.FirstOrDefault(candidate => candidate.FriendlyName.Equals(target, StringComparison.OrdinalIgnoreCase));
if (device is null) throw new InvalidOperationException($"Capture device was not found. Available: {string.Join(", ", devices.Select(candidate => candidate.FriendlyName))}");

if (listFormats)
{
    Console.WriteLine($"FORMAT ENUMERATION device='{device.FriendlyName}' id='{device.Id}' container='{device.ContainerId}'");
    var formats = await backend.EnumerateFormatsAsync(device, default);
    foreach (var format in formats) Console.WriteLine($"FORMAT id='{format.Id}' display='{format.Display}'");
    Console.WriteLine($"RESULT selectableFormats={formats.Count} captureStarted=false mediaSaved=false");
    return;
}

Console.WriteLine($"PROBE device='{device.FriendlyName}' id='{device.Id}' container='{device.ContainerId}' requestedMode='{preferredMode ?? "Auto"}'");
await using var session = await backend.OpenAsync(device, new(PreferredNativeFormatId: preferredMode), default);
var samples = 0; var audioBlocks = 0; long firstTimestamp = 0; long lastTimestamp = 0;
var analysisFrames = new ConcurrentQueue<VideoFrame>();
var fiveFrames = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
session.StatusChanged += (_, status) => Console.WriteLine($"STATUS {status.Status}: {status.Message}");
session.VideoSampleReceived += (_, frame) =>
{
    var count = Interlocked.Increment(ref samples); if (count == 1) firstTimestamp = frame.Timestamp.Ticks100ns; lastTimestamp = frame.Timestamp.Ticks100ns;
    if (analyzeSignal) analysisFrames.Enqueue(frame);
    if (count <= 5) Console.WriteLine($"FRAME {count}: {frame.Width}x{frame.Height} timestamp={frame.Timestamp.Ticks100ns} quality={frame.Timestamp.Quality} domain={frame.Timestamp.ClockDomain} bytes={frame.Luma.Length}");
    if (count == 5) fiveFrames.TrySetResult();
};
session.AudioSampleReceived += (_, chunk) => { if (Interlocked.Increment(ref audioBlocks) == 1) Console.WriteLine($"AUDIO rate={chunk.SampleRate} channels={chunk.Channels} frames={chunk.FrameCount} quality={chunk.Timestamp.Quality} domain={chunk.Timestamp.ClockDomain}"); };
var stopwatch = Stopwatch.StartNew(); await session.StartAsync(default); await fiveFrames.Task.WaitAsync(TimeSpan.FromSeconds(5)); await Task.Delay(analyzeSignal ? TimeSpan.FromSeconds(7) : TimeSpan.FromSeconds(1)); stopwatch.Stop();
var intervalMs = samples > 1 ? (lastTimestamp - firstTimestamp) / 10_000d / (samples - 1) : 0;
Console.WriteLine($"RESULT backend=MediaFoundation format='{session.CurrentFormat.Display}' payloadFrames={samples} audioBlocks={audioBlocks} averageTimestampIntervalMs={intervalMs:0.###} timing={session.TimingQuality} elapsedMs={stopwatch.ElapsedMilliseconds}");
if (analyzeSignal)
{
    var analysis = new ObservedSignalAnalyzer().Analyze(analysisFrames.ToArray());
    Console.WriteLine("CAPTURE OUTPUT");
    Console.WriteLine($"declared='{session.CurrentFormat.Display}' observedDeliveredCadence={analysis.ObservedFrameRate:0.###}Hz medianInterval={analysis.MedianIntervalMilliseconds:0.###}ms jitter={analysis.JitterPercent:0.##}%");
    Console.WriteLine("SOURCE ANALYSIS");
    Console.WriteLine($"estimate='{InputSignalFormatter.Format(analysis.Signal)}' authority={analysis.Signal.Authority} duplicateFraction={analysis.DuplicateFraction:P1} repeat='{analysis.RepeatPattern}' interlaceEvidence={analysis.InterlaceEvidence:0.000}");
    Console.WriteLine($"framesAnalyzed={analysis.FramesAnalyzed} duration={analysis.DurationSeconds:0.###}s mediaSaved=false");
}

sealed class ConsoleSink : IDiagnosticSink { public void Write(string category, string message) => Console.WriteLine($"[{category}] {message}"); }
