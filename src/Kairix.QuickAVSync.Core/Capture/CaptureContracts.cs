using Kairix.QuickAVSync.Models;

namespace Kairix.QuickAVSync.Capture;

public interface ICaptureBackend
{
    string Id { get; }
    Task<IReadOnlyList<CaptureDeviceDescriptor>> EnumerateDevicesAsync(CancellationToken cancellationToken);
    Task<ICaptureSession> OpenAsync(CaptureDeviceDescriptor device, CaptureOpenOptions options, CancellationToken cancellationToken);
}

public interface ICaptureSession : IAsyncDisposable
{
    CaptureFormat CurrentFormat { get; }
    TimingQuality TimingQuality { get; }
    event EventHandler<VideoFrame>? VideoSampleReceived;
    event EventHandler<AudioChunk>? AudioSampleReceived;
    event EventHandler<CaptureStatusChangedEventArgs>? StatusChanged;
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}

public interface IVisualClapDetector
{
    Task<VisualCandidate?> DetectAsync(IReadOnlyList<VideoFrame> frames, MediaTimestamp expected, CancellationToken cancellationToken);
}

public interface IDiagnosticSink
{
    void Write(string category, string message);
}

public sealed class NullDiagnosticSink : IDiagnosticSink
{
    public static NullDiagnosticSink Instance { get; } = new();
    private NullDiagnosticSink() { }
    public void Write(string category, string message) { }
}
