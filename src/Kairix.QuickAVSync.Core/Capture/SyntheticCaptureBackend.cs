using Kairix.QuickAVSync.Models;

namespace Kairix.QuickAVSync.Capture;

public sealed class SyntheticCaptureBackend(TimeSpan? audioToVideoOffset = null) : ICaptureBackend
{
    public const string BackendName = "synthetic";
    public const string DeviceId = "synthetic://development";
    private readonly TimeSpan _offset = audioToVideoOffset ?? TimeSpan.FromMilliseconds(60);
    public string Id => BackendName;
    public Task<IReadOnlyList<CaptureDeviceDescriptor>> EnumerateDevicesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<CaptureDeviceDescriptor>>([
        new(DeviceId, DisplayName(_offset), CaptureDeviceKind.Synthetic, BackendName, "synthetic")
    ]);
    public static string DisplayName(TimeSpan offset)
    {
        var milliseconds = offset.TotalMilliseconds;
        if (Math.Abs(milliseconds) < .0005) return "Synthetic A/V test · expected 0 ms (in sync)";
        var relationship = milliseconds > 0 ? "audio leads video" : "audio lags video";
        return $"Synthetic A/V test · expected {milliseconds:+0.###;-0.###;0} ms ({relationship})";
    }
    public Task<ICaptureSession> OpenAsync(CaptureDeviceDescriptor device, CaptureOpenOptions options, CancellationToken cancellationToken)
    {
        if (device.BackendId != BackendName) throw new ArgumentException("Descriptor does not belong to the synthetic backend.", nameof(device));
        return Task.FromResult<ICaptureSession>(new SyntheticCaptureSession(_offset, options));
    }
}

public sealed class SyntheticCaptureSession(TimeSpan audioToVideoOffset, CaptureOpenOptions? options = null) : ICaptureSession
{
    private CancellationTokenSource? _cts; private Task? _task; private readonly Random _random = new(31415);
    public CaptureFormat CurrentFormat { get; } = new(640, 360, Rational.From(50), ScanMode.Progressive, FieldOrder.Unknown, 48000, VideoPixelFormat.Luma8);
    public TimingQuality TimingQuality => TimingQuality.StreamTimestamp;
    public event EventHandler<VideoFrame>? VideoSampleReceived;
    public event EventHandler<AudioChunk>? AudioSampleReceived;
    public event EventHandler<CaptureStatusChangedEventArgs>? StatusChanged;
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_task is not null) return Task.CompletedTask;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); StatusChanged?.Invoke(this, new(CaptureStatus.Starting, "Starting deterministic synthetic source"));
        _task = Task.Run(() => RunAsync(_cts.Token), _cts.Token); return Task.CompletedTask;
    }
    private async Task RunAsync(CancellationToken ct)
    {
        StatusChanged?.Invoke(this, new(CaptureStatus.Running, "Synthetic source running"));
        var frame = 0; var nextAudioClap = 1.0;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var now = TimeSpan.FromMilliseconds(frame * 20d); var visualClap = nextAudioClap + audioToVideoOffset.TotalSeconds;
                var index = frame++;
                VideoSampleReceived?.Invoke(this, CreateFrame(now, visualClap, index,
                    options?.PreferredAnalysisWidth ?? 640, options?.PreferredAnalysisHeight ?? 360,
                    options?.PreferredPresentationWidth ?? 160, options?.PreferredPresentationHeight ?? 90));
                AudioSampleReceived?.Invoke(this, CreateAudio(now, nextAudioClap, _random));
                if (now.TotalSeconds > nextAudioClap + Math.Max(.5, audioToVideoOffset.TotalSeconds + .3)) nextAudioClap += 2;
                await Task.Delay(20, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally { StatusChanged?.Invoke(this, new(CaptureStatus.Stopped, "Synthetic source stopped")); }
    }
    public static VideoFrame CreateFrame(TimeSpan now, double visualClapSeconds, int frame, int width = 640, int height = 360, int presentationWidth = 160, int presentationHeight = 90)
    {
        var luma = new byte[width * height]; var bgra = new byte[presentationWidth * presentationHeight * 4];
        var relativeSeconds = (now.Ticks - TimeSpan.FromSeconds(visualClapSeconds).Ticks) / (double)TimeSpan.TicksPerSecond;
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
        {
            luma[y * width + x] = CueLuma(x, y, width, height, relativeSeconds);
        }
        for (var y = 0; y < presentationHeight; y++) for (var x = 0; x < presentationWidth; x++)
        {
            var value = CueLuma(x, y, presentationWidth, presentationHeight, relativeSeconds);
            var offset = (y * presentationWidth + x) * 4;
            bgra[offset] = value; bgra[offset + 1] = value; bgra[offset + 2] = value;
            bgra[offset + 3] = 255;
        }
        return new(MediaTimestamp.FromTimeSpan(now, TimingQuality.StreamTimestamp, "synthetic-common"), width, height, luma, frame, Stride: width, PresentationBgra: bgra, PresentationWidth: presentationWidth, PresentationHeight: presentationHeight, PresentationStride: presentationWidth * 4, NativeSampleIndex: frame);
    }
    private static byte CueLuma(int x, int y, int width, int height, double relativeSeconds)
    {
        var background = 22 + x * 22 / width + y * 12 / height;
        var flash = relativeSeconds is >= -1e-9 and < .10;
        var baselineGap = width * .24;
        var gap = relativeSeconds switch
        {
            < -.12 => baselineGap,
            < -1e-9 => baselineGap * (-relativeSeconds / .12),
            < .14 => 0,
            _ => baselineGap
        };
        var centerX = width / 2d; var centerY = height / 2d; var blockWidth = Math.Max(3, width * .075); var blockHeight = Math.Max(3, height * .20);
        var leftCenter = centerX - gap / 2 - blockWidth / 2; var rightCenter = centerX + gap / 2 + blockWidth / 2;
        var block = Math.Abs(y - centerY) <= blockHeight / 2
            && (Math.Abs(x - leftCenter) <= blockWidth / 2 || Math.Abs(x - rightCenter) <= blockWidth / 2);
        var flashRegion = x >= width / 5 && x < width * 4 / 5 && y >= height / 5 && y < height * 4 / 5;
        if (flash && flashRegion && !block) background = Math.Min(255, background + 120);
        return (byte)(block ? 220 : background);
    }
    internal static AudioChunk CreateAudio(TimeSpan now, double audioClapSeconds, Random random)
    {
        var samples = new float[960 * 2]; for (var i = 0; i < samples.Length; i++) samples[i] = (float)(random.NextDouble() - .5) * .006f;
        if (now.TotalSeconds >= audioClapSeconds && now.TotalSeconds < audioClapSeconds + .02)
            for (var i = 0; i < 80; i++) samples[i * 2] = samples[i * 2 + 1] = (float)Math.Exp(-i / 16d) * .9f;
        return new(MediaTimestamp.FromTimeSpan(now, TimingQuality.StreamTimestamp, "synthetic-common"), samples, 48000, 2);
    }
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_cts is null) return; StatusChanged?.Invoke(this, new(CaptureStatus.Stopping, "Stopping synthetic source")); _cts.Cancel();
        try { if (_task is not null) await _task.WaitAsync(cancellationToken).ConfigureAwait(false); } catch (OperationCanceledException) { }
        _task = null; _cts.Dispose(); _cts = null;
    }
    public async ValueTask DisposeAsync() => await StopAsync(CancellationToken.None).ConfigureAwait(false);
}

public static class SyntheticFixture
{
    public static (IReadOnlyList<AudioChunk> Audio, IReadOnlyList<VideoFrame> Video, MediaTimestamp ExpectedAudio, MediaTimestamp ExpectedVideo) Create(TimeSpan audioToVideoOffset, Rational? videoFrameRate = null)
    {
        const int rate = 48000, width = 96, height = 54; var random = new Random(7); var audio = new List<AudioChunk>(); var video = new List<VideoFrame>(); var audioAt = TimeSpan.FromSeconds(1); var visualAt = audioAt + audioToVideoOffset;
        for (var i = 0; i < 100; i++)
        {
            var samples = new float[960]; for (var s = 0; s < samples.Length; s++) samples[s] = (float)(random.NextDouble() - .5) * .002f;
            if (i == 50) for (var s = 0; s < 40; s++) samples[s] = .9f * (float)Math.Exp(-s / 10d);
            audio.Add(new(MediaTimestamp.FromTimeSpan(TimeSpan.FromMilliseconds(i * 20), TimingQuality.StreamTimestamp, "synthetic-common"), samples, rate, 1));
        }
        var cadence = (videoFrameRate ?? Rational.From(200)).FrameDuration;
        for (var frame = 0; ; frame++)
        {
            var now = TimeSpan.FromTicks(cadence.Ticks * frame); if (now > TimeSpan.FromSeconds(2)) break;
            video.Add(SyntheticCaptureSession.CreateFrame(now, visualAt.TotalSeconds, frame, width, height, 16, 9) with { PresentationBgra = null, PresentationWidth = 0, PresentationHeight = 0, PresentationStride = 0 });
        }
        return (audio, video, MediaTimestamp.FromTimeSpan(audioAt, TimingQuality.StreamTimestamp, "synthetic-common"), MediaTimestamp.FromTimeSpan(visualAt, TimingQuality.StreamTimestamp, "synthetic-common"));
    }
}
