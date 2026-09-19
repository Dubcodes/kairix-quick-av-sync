using System.Runtime.InteropServices;
using Kairix.QuickAVSync.Models;

namespace Kairix.QuickAVSync.Services;

public interface ICaptureSession : IAsyncDisposable
{
    event Action<VideoFrame>? VideoFrameReady;
    event Action<AudioChunk>? AudioReady;
    CaptureFormat Format { get; }
    TimingQuality TimingQuality { get; }
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync();
}

public sealed class DevicePairingService
{
    public DevicePairing Pair(CaptureDevice video, IReadOnlyList<AudioEndpoint> audio)
    {
        if (!string.IsNullOrWhiteSpace(video.ContainerId))
        {
            var exact = audio.Where(a => string.Equals(a.ContainerId, video.ContainerId, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (exact.Length == 1) return new(exact[0], true, "Shared Windows device Container ID");
            if (exact.Length > 1) return new(exact.OrderByDescending(a => NameSimilarity(video.FriendlyName, a.FriendlyName)).First(), true, "Shared Container ID; closest endpoint name");
        }
        var safe = audio.Where(a => !a.IsDefaultMicrophone && NameSimilarity(video.FriendlyName, a.FriendlyName) >= 2).ToArray();
        return safe.Length == 1 ? new(safe[0], false, "Unique capture-device name match; Container ID unavailable") : new(null, false, "Paired audio endpoint not found with sufficient certainty");
    }
    private static int NameSimilarity(string a, string b)
    {
        var ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "audio", "video", "device", "capture", "input", "usb" };
        var left = a.Split([' ', '-', '_', '(', ')'], StringSplitOptions.RemoveEmptyEntries).Where(x => !ignored.Contains(x)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return b.Split([' ', '-', '_', '(', ')'], StringSplitOptions.RemoveEmptyEntries).Count(left.Contains);
    }
}

public static class CaptureFormatSelector
{
    public static CaptureFormat? Select(IEnumerable<CaptureFormat> formats)
    {
        return formats.OrderByDescending(f => f.Width == 1920 && f.Height == 1080)
            .ThenByDescending(f => RatePriority(f.FrameRate.Value))
            .ThenByDescending(f => f.Width * f.Height)
            .ThenByDescending(f => f.FrameRate.Value).FirstOrDefault();
    }
    private static int RatePriority(double rate) => Math.Abs(rate - 50) < .02 ? 4 : Math.Abs(rate - 25) < .02 ? 3 : Math.Abs(rate - 59.94) < .02 ? 2 : 1;
}

public sealed class MediaTimingService
{
    private long? _firstDevice, _firstStream;
    public MediaTimestamp Normalize(long streamTicks, long? deviceQpcTicks = null)
    {
        if (deviceQpcTicks is { } qpc)
        {
            _firstDevice ??= qpc;
            return new(qpc - _firstDevice.Value, TimingQuality.DeviceQpc);
        }
        _firstStream ??= streamTicks;
        return new(streamTicks - _firstStream.Value, TimingQuality.StreamTimestamp);
    }
    public static MediaTimestamp ArrivalNow(long stopwatchTicks) => new((long)(stopwatchTicks * (double)TimeSpan.TicksPerSecond / System.Diagnostics.Stopwatch.Frequency), TimingQuality.ArrivalFallback);
}

public sealed class CaptureDeviceService
{
    private readonly AppLogger _log;
    public CaptureDeviceService(AppLogger log) => _log = log;

    public IReadOnlyList<CaptureDevice> Enumerate()
    {
        var results = new List<CaptureDevice>();
        try
        {
            MediaFoundationNative.MFStartup(0x00020070, 1);
            using var attributes = MediaFoundationNative.CreateAttributes(1);
            attributes.Value.SetGUID(MediaFoundationNative.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE, MediaFoundationNative.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_GUID);
            MediaFoundationNative.MFEnumDeviceSources(attributes.Value, out var ptr, out var count).ThrowIfFailed();
            try
            {
                for (var i = 0; i < count; i++)
                {
                    var activatePtr = Marshal.ReadIntPtr(ptr, i * IntPtr.Size);
                    var activate = (IMFActivate)Marshal.GetObjectForIUnknown(activatePtr);
                    try
                    {
                        var name = activate.GetString(MediaFoundationNative.MF_DEVSOURCE_ATTRIBUTE_FRIENDLY_NAME);
                        var id = activate.GetString(MediaFoundationNative.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_SYMBOLIC_LINK);
                        var lower = name.ToLowerInvariant();
                        var captureWords = new[] { "capture", "hdmi", "sdi", "uvc", "video input", "cam link", "decklink" };
                        var integratedWords = new[] { "integrated", "front", "facetime", "webcam" };
                        var external = captureWords.Any(lower.Contains) || id.Contains("USB", StringComparison.OrdinalIgnoreCase);
                        var rank = external ? 100 : 20;
                        if (captureWords.Any(lower.Contains)) rank += 50;
                        if (integratedWords.Any(lower.Contains)) rank -= 40;
                        results.Add(new(id, name, null, external, rank));
                    }
                    finally { Marshal.ReleaseComObject(activate); Marshal.Release(activatePtr); }
                }
            }
            finally { Marshal.FreeCoTaskMem(ptr); MediaFoundationNative.MFShutdown(); }
        }
        catch (Exception ex) { _log.Write($"Device enumeration failed: {ex.Message}"); }
        _log.Write($"Enumerated {results.Count} video capture device(s)");
        return results.OrderByDescending(d => d.Rank).ThenBy(d => d.FriendlyName).ToArray();
    }

    public CaptureDevice? SelectBest(IReadOnlyList<CaptureDevice> devices, AppSettings settings)
    {
        var exact = devices.FirstOrDefault(d => d.Id == settings.LastDeviceId);
        if (exact is not null) return exact;
        // Friendly-name fallback is intentionally accepted only if it is unique and still ranks as external.
        var named = devices.Where(d => d.IsLikelyExternal && d.FriendlyName == settings.LastDeviceName).ToArray();
        return named.Length == 1 ? named[0] : devices.FirstOrDefault(d => d.IsLikelyExternal);
    }
}

// Deterministic capture-free source used for development, demos, and end-to-end UI verification.
public sealed class SyntheticCaptureSession : ICaptureSession
{
    private CancellationTokenSource? _cts;
    private Task? _task;
    private readonly Random _random = new(31415);
    public event Action<VideoFrame>? VideoFrameReady;
    public event Action<AudioChunk>? AudioReady;
    public CaptureFormat Format { get; } = new(1920, 1080, Rational.From(50), ScanMode.Progressive, FieldOrder.Unknown);
    public TimingQuality TimingQuality => TimingQuality.StreamTimestamp;
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _task = Task.Run(() => RunAsync(_cts.Token), _cts.Token); return Task.CompletedTask;
    }
    private async Task RunAsync(CancellationToken ct)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew(); var frame = 0; var nextClap = 3.0;
        while (!ct.IsCancellationRequested)
        {
            var now = clock.Elapsed; var w = 320; var h = 180; var luma = new byte[w * h];
            var phase = now.TotalSeconds;
            var clapDistance = Math.Abs(phase - nextClap);
            for (var y = 0; y < h; y++) for (var x = 0; x < w; x++)
            {
                var baseValue = 22 + x * 22 / w + y * 12 / h;
                var hands = clapDistance < .18 && Math.Abs(x - w / 2) < (int)(12 + clapDistance * 350) && Math.Abs(y - h / 2) < 34;
                luma[y * w + x] = (byte)(hands ? 205 : baseValue);
            }
            var ts = MediaTimestamp.FromTimeSpan(now, TimingQuality.StreamTimestamp);
            VideoFrameReady?.Invoke(new(ts, w, h, luma, frame++));
            var samples = new float[960 * 2];
            for (var i = 0; i < samples.Length; i++) samples[i] = (float)(_random.NextDouble() - .5) * .006f;
            if (now.TotalSeconds >= nextClap && now.TotalSeconds < nextClap + .02)
                for (var i = 0; i < 80; i++) samples[i * 2] = samples[i * 2 + 1] = (float)Math.Exp(-i / 16d) * .9f;
            AudioReady?.Invoke(new(ts, samples, 48000, 2));
            if (now.TotalSeconds > nextClap + 2) nextClap += 5;
            await Task.Delay(20, ct);
        }
    }
    public async Task StopAsync() { if (_cts is null) return; _cts.Cancel(); try { if (_task is not null) await _task; } catch (OperationCanceledException) { } _cts.Dispose(); _cts = null; }
    public async ValueTask DisposeAsync() => await StopAsync();
}

internal static class HResultExtensions { public static void ThrowIfFailed(this int hr) { if (hr < 0) Marshal.ThrowExceptionForHR(hr); } }

internal static class MediaFoundationNative
{
    public static readonly Guid MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE = new("C60AC5FE-252A-478F-A0EF-BC8FA5F7CAD3");
    public static readonly Guid MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_GUID = new("8AC3587A-4AE7-42D8-99E0-0A6013EEF90F");
    public static readonly Guid MF_DEVSOURCE_ATTRIBUTE_FRIENDLY_NAME = new("60DDCBA0-2651-4B69-8B6C-7B1C78B8F369");
    public static readonly Guid MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_SYMBOLIC_LINK = new("58F0AAD8-22BF-4F8A-BB3D-D2C4978C6E2F");
    [DllImport("mfplat.dll")] public static extern int MFStartup(int version, int flags);
    [DllImport("mfplat.dll")] public static extern int MFShutdown();
    [DllImport("mfplat.dll")] private static extern int MFCreateAttributes(out IntPtr attributes, int initialSize);
    [DllImport("mf.dll")] public static extern int MFEnumDeviceSources(IMFAttributes attributes, out IntPtr devices, out int count);
    public static ComReleaser<IMFAttributes> CreateAttributes(int count) { MFCreateAttributes(out var ptr, count).ThrowIfFailed(); return new((IMFAttributes)Marshal.GetObjectForIUnknown(ptr), ptr); }
}

internal sealed class ComReleaser<T>(T value, IntPtr ptr) : IDisposable where T : class
{
    public T Value { get; } = value;
    public void Dispose() { Marshal.ReleaseComObject(Value); Marshal.Release(ptr); }
}

[ComImport, Guid("2CD2D921-C447-44A7-A13C-4ADABFC247E3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFAttributes
{
    int GetItem([In] ref Guid key, IntPtr value); int GetItemType([In] ref Guid key, out int type); int CompareItem([In] ref Guid key, IntPtr value, out int result); int Compare(IMFAttributes theirs, int matchType, out int result); int GetUINT32([In] ref Guid key, out int value); int GetUINT64([In] ref Guid key, out long value); int GetDouble([In] ref Guid key, out double value); int GetGUID([In] ref Guid key, out Guid value); int GetStringLength([In] ref Guid key, out int length); int GetString([In] ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder value, int size, out int length); int GetAllocatedString([In] ref Guid key, out IntPtr value, out int length); int GetBlobSize([In] ref Guid key, out int size); int GetBlob([In] ref Guid key, IntPtr buffer, int size, out int blobSize); int GetAllocatedBlob([In] ref Guid key, out IntPtr buffer, out int size); int GetUnknown([In] ref Guid key, [In] ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object value); int SetItem([In] ref Guid key, IntPtr value); int DeleteItem([In] ref Guid key); int DeleteAllItems(); int SetUINT32([In] ref Guid key, int value); int SetUINT64([In] ref Guid key, long value); int SetDouble([In] ref Guid key, double value); int SetGUID([In] ref Guid key, [In] ref Guid value); int SetString([In] ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] string value); int SetBlob([In] ref Guid key, IntPtr buffer, int size); int SetUnknown([In] ref Guid key, [MarshalAs(UnmanagedType.IUnknown)] object value); int LockStore(); int UnlockStore(); int GetCount(out int count); int GetItemByIndex(int index, out Guid key, IntPtr value); int CopyAllItems(IMFAttributes destination);
}

internal static class MfAttributeHelpers
{
    public static void SetGUID(this IMFAttributes a, Guid key, Guid value) => a.SetGUID(ref key, ref value).ThrowIfFailed();
    public static string GetString(this IMFAttributes a, Guid key) { a.GetAllocatedString(ref key, out var ptr, out _).ThrowIfFailed(); try { return Marshal.PtrToStringUni(ptr) ?? ""; } finally { Marshal.FreeCoTaskMem(ptr); } }
}

[ComImport, Guid("7FEE9E9A-4A89-47A6-899C-B6A53A70FB67"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFActivate : IMFAttributes { }
