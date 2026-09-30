using System.Diagnostics;
using Kairix.QuickAVSync.Capture;
using Kairix.QuickAVSync.Models;
using Kairix.QuickAVSync.Windows.Capture;

var sink = new ConsoleSink();
var backend = new WindowsCaptureBackend(sink);
var devices = await backend.EnumerateDevicesAsync(default);
var target = args.Length == 0 ? null : string.Join(' ', args);
var device = target is null ? devices.FirstOrDefault() : devices.FirstOrDefault(candidate => candidate.FriendlyName.Equals(target, StringComparison.OrdinalIgnoreCase));
if (device is null) throw new InvalidOperationException($"Capture device was not found. Available: {string.Join(", ", devices.Select(candidate => candidate.FriendlyName))}");

Console.WriteLine($"PROBE device='{device.FriendlyName}' id='{device.Id}' container='{device.ContainerId}'");
await using var session = await backend.OpenAsync(device, new(), default);
var samples = 0; var audioBlocks = 0; long firstTimestamp = 0; long lastTimestamp = 0;
var fiveFrames = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
session.StatusChanged += (_, status) => Console.WriteLine($"STATUS {status.Status}: {status.Message}");
session.VideoSampleReceived += (_, frame) =>
{
    var count = Interlocked.Increment(ref samples); if (count == 1) firstTimestamp = frame.Timestamp.Ticks100ns; lastTimestamp = frame.Timestamp.Ticks100ns;
    if (count <= 5) Console.WriteLine($"FRAME {count}: {frame.Width}x{frame.Height} timestamp={frame.Timestamp.Ticks100ns} quality={frame.Timestamp.Quality} domain={frame.Timestamp.ClockDomain} bytes={frame.Luma.Length}");
    if (count == 5) fiveFrames.TrySetResult();
};
session.AudioSampleReceived += (_, chunk) => { if (Interlocked.Increment(ref audioBlocks) == 1) Console.WriteLine($"AUDIO rate={chunk.SampleRate} channels={chunk.Channels} frames={chunk.FrameCount} quality={chunk.Timestamp.Quality} domain={chunk.Timestamp.ClockDomain}"); };
var stopwatch = Stopwatch.StartNew(); await session.StartAsync(default); await fiveFrames.Task.WaitAsync(TimeSpan.FromSeconds(5)); await Task.Delay(1000); stopwatch.Stop();
var intervalMs = samples > 1 ? (lastTimestamp - firstTimestamp) / 10_000d / (samples - 1) : 0;
Console.WriteLine($"RESULT backend=MediaFoundation format='{session.CurrentFormat.Display}' payloadFrames={samples} audioBlocks={audioBlocks} averageTimestampIntervalMs={intervalMs:0.###} timing={session.TimingQuality} elapsedMs={stopwatch.ElapsedMilliseconds}");

sealed class ConsoleSink : IDiagnosticSink { public void Write(string category, string message) => Console.WriteLine($"[{category}] {message}"); }
