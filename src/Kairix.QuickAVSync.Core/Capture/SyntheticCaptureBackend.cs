using System.Diagnostics;
using Kairix.QuickAVSync.Models;

namespace Kairix.QuickAVSync.Capture;

public sealed class SyntheticCaptureBackend(TimeSpan? audioToVideoOffset = null) : ICaptureBackend
{
    public const string BackendName = "synthetic";
    public const string DeviceId = "synthetic://development";
    private readonly TimeSpan _offset = audioToVideoOffset ?? TimeSpan.FromMilliseconds(60);
    public string Id => BackendName;
    public Task<IReadOnlyList<CaptureDeviceDescriptor>> EnumerateDevicesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<CaptureDeviceDescriptor>>([
        new(DeviceId, "Synthetic A/V test source (+60 ms video)", CaptureDeviceKind.Synthetic, BackendName, "synthetic")
    ]);
    public Task<ICaptureSession> OpenAsync(CaptureDeviceDescriptor device, CaptureOpenOptions options, CancellationToken cancellationToken)
    {
        if (device.BackendId != BackendName) throw new ArgumentException("Descriptor does not belong to the synthetic backend.", nameof(device));
        return Task.FromResult<ICaptureSession>(new SyntheticCaptureSession(_offset));
    }
}

public sealed class SyntheticCaptureSession(TimeSpan audioToVideoOffset) : ICaptureSession
{
    private CancellationTokenSource? _cts; private Task? _task; private readonly Random _random = new(31415);
    public CaptureFormat CurrentFormat { get; } = new(320, 180, Rational.From(50), ScanMode.Progressive, FieldOrder.Unknown, 48000, VideoPixelFormat.Luma8);
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
        var clock = Stopwatch.StartNew(); var frame = 0; var nextAudioClap = 3.0;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var now = clock.Elapsed; var visualClap = nextAudioClap + audioToVideoOffset.TotalSeconds;
                VideoSampleReceived?.Invoke(this, CreateFrame(now, visualClap, frame++));
                AudioSampleReceived?.Invoke(this, CreateAudio(now, nextAudioClap, _random));
                if (now.TotalSeconds > nextAudioClap + Math.Max(2, audioToVideoOffset.TotalSeconds + 1)) nextAudioClap += 5;
                await Task.Delay(20, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally { StatusChanged?.Invoke(this, new(CaptureStatus.Stopped, "Synthetic source stopped")); }
    }
    public static VideoFrame CreateFrame(TimeSpan now, double visualClapSeconds, int frame)
    {
        const int width = 320, height = 180, presentationWidth = 160, presentationHeight = 90; var luma = new byte[width * height]; var bgra = new byte[presentationWidth * presentationHeight * 4]; var distance = Math.Abs(now.TotalSeconds - visualClapSeconds);
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
        {
            var background = 22 + x * 22 / width + y * 12 / height;
            var hands = distance < .18 && Math.Abs(x - width / 2) < (int)(12 + distance * 350) && Math.Abs(y - height / 2) < 34;
            luma[y * width + x] = (byte)(hands ? 205 : background);
        }
        for (var y = 0; y < presentationHeight; y++) for (var x = 0; x < presentationWidth; x++)
        {
            var hands = distance < .18 && Math.Abs(x - presentationWidth / 2) < (int)(6 + distance * 175) && Math.Abs(y - presentationHeight / 2) < 17;
            var offset = (y * presentationWidth + x) * 4;
            bgra[offset] = hands ? (byte)114 : (byte)(68 + x * 20 / presentationWidth);
            bgra[offset + 1] = hands ? (byte)164 : (byte)(44 + y * 20 / presentationHeight);
            bgra[offset + 2] = hands ? (byte)224 : (byte)(24 + x * 26 / presentationWidth);
            bgra[offset + 3] = 255;
        }
        return new(MediaTimestamp.FromTimeSpan(now, TimingQuality.StreamTimestamp, "synthetic-common"), width, height, luma, frame, Stride: width, PresentationBgra: bgra, PresentationWidth: presentationWidth, PresentationHeight: presentationHeight, PresentationStride: presentationWidth * 4);
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
        // Keep the synthetic contact visible past the analysis window so the
        // deterministic fixture has one unambiguous visual onset.
        var contactDuration = TimeSpan.FromSeconds(1);
        for (var frame = 0; ; frame++)
        {
            var now = TimeSpan.FromTicks(cadence.Ticks * frame); if (now > TimeSpan.FromSeconds(2)) break;
            var pixels = new byte[width * height]; Array.Fill(pixels, (byte)30);
            if (now >= visualAt && now < visualAt + contactDuration) Array.Fill(pixels, (byte)230, 1_000, 1_500);
            video.Add(new(MediaTimestamp.FromTimeSpan(now, TimingQuality.StreamTimestamp, "synthetic-common"), width, height, pixels, frame, Stride: width));
        }
        return (audio, video, MediaTimestamp.FromTimeSpan(audioAt, TimingQuality.StreamTimestamp, "synthetic-common"), MediaTimestamp.FromTimeSpan(visualAt, TimingQuality.StreamTimestamp, "synthetic-common"));
    }
}
