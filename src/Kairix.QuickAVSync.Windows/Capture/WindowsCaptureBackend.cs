using System.Runtime.InteropServices;
using Kairix.QuickAVSync.Capture;
using Kairix.QuickAVSync.Models;
using Kairix.QuickAVSync.Services;

namespace Kairix.QuickAVSync.Windows.Capture;

public sealed class WindowsCaptureBackend(IDiagnosticSink? diagnostics = null) : ICaptureBackend
{
    public const string BackendName = "windows-media-foundation";
    private readonly IDiagnosticSink _log = diagnostics ?? NullDiagnosticSink.Instance;
    private readonly WindowsAudioEndpointService _audioEndpoints = new();
    private readonly DevicePairingService _pairing = new();
    public string Id => BackendName;

    public Task<IReadOnlyList<CaptureDeviceDescriptor>> EnumerateDevicesAsync(CancellationToken cancellationToken) => Task.Run<IReadOnlyList<CaptureDeviceDescriptor>>(() =>
    {
        cancellationToken.ThrowIfCancellationRequested(); var results = new List<CaptureDeviceDescriptor>(); MediaFoundationNative.MFStartup(0x00020070, 1).ThrowIfFailed();
        try
        {
            using var attributes = MediaFoundationNative.CreateAttributes(1); attributes.Value.SetGuid(MfGuids.DevSourceType, MfGuids.VideoCaptureSource);
            MediaFoundationNative.MFEnumDeviceSources(attributes.Value, out var array, out var count).ThrowIfFailed();
            try
            {
                for (var i = 0; i < count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested(); var ptr = Marshal.ReadIntPtr(array, i * IntPtr.Size); var activate = (IMFActivate)Marshal.GetObjectForIUnknown(ptr);
                    try
                    {
                        var name = activate.TryGetAllocatedString(MfGuids.FriendlyName) ?? "Unnamed video capture device"; var symbolicLink = activate.TryGetAllocatedString(MfGuids.SymbolicLink);
                        if (string.IsNullOrWhiteSpace(symbolicLink)) { _log.Write("device.video", $"Skipping '{name}' because Media Foundation supplied no symbolic link"); continue; }
                        var container = WindowsDeviceProperties.TryGetContainerId(symbolicLink)?.ToString("D"); var kind = Classify(name, symbolicLink);
                        var properties = new Dictionary<string, string> { ["symbolicLink"] = symbolicLink, ["transport"] = symbolicLink.Contains("USB", StringComparison.OrdinalIgnoreCase) ? "USB" : "unknown" };
                        var descriptor = new CaptureDeviceDescriptor(symbolicLink, name, kind, BackendName, container, Properties: properties); results.Add(descriptor);
                        _log.Write("device.video", $"name='{name}' id='{symbolicLink}' container='{container ?? "unavailable"}' kind={kind}");
                    }
                    finally { Marshal.ReleaseComObject(activate); Marshal.Release(ptr); }
                }
            }
            finally { Marshal.FreeCoTaskMem(array); }
            try
            {
                foreach (var endpoint in _audioEndpoints.Enumerate()) _log.Write("device.audio", $"name='{endpoint.FriendlyName}' id='{endpoint.Id}' container='{endpoint.ContainerId ?? "unavailable"}' active={endpoint.IsActive} default={endpoint.IsDefaultMicrophone}");
            }
            catch (Exception ex) { _log.Write("device.audio", $"Enumeration failed: {ex.Message}"); }
        }
        finally { MediaFoundationNative.MFShutdown(); }
        return results;
    }, cancellationToken);

    public Task<ICaptureSession> OpenAsync(CaptureDeviceDescriptor device, CaptureOpenOptions options, CancellationToken cancellationToken) => Task.Run<ICaptureSession>(() =>
    {
        MediaFoundationNative.CoInitializeEx(IntPtr.Zero, 0);
        try
        {
        if (device.BackendId != BackendName) throw new ArgumentException("Descriptor does not belong to the Windows Media Foundation backend.", nameof(device));
        IReadOnlyList<AudioEndpointDescriptor> endpoints;
        try { endpoints = _audioEndpoints.Enumerate(); } catch (Exception ex) { _log.Write("device.audio", $"Enumeration during open failed: {ex.Message}"); endpoints = []; }
        var pairing = _pairing.Pair(device, endpoints); _log.Write("device.pairing", $"video='{device.FriendlyName}' audio='{pairing.Endpoint?.FriendlyName ?? "none"}' confidence={pairing.Confidence} reason='{pairing.Reason}'");
        return MediaFoundationCaptureSession.Open(device, options, pairing, _log, cancellationToken);
        }
        finally { MediaFoundationNative.CoUninitialize(); }
    }, cancellationToken);

    private static CaptureDeviceKind Classify(string name, string id)
    {
        var text = $"{name} {id}".ToLowerInvariant();
        var capture = new[] { "capture", "hdmi", "sdi", "uvc", "video input", "cam link", "decklink", "usb video" }.Any(text.Contains);
        var integrated = new[] { "integrated", "front camera", "webcam", "internal camera" }.Any(text.Contains);
        if (capture || (id.Contains("USB", StringComparison.OrdinalIgnoreCase) && !integrated)) return CaptureDeviceKind.ExternalCapture;
        return integrated ? CaptureDeviceKind.IntegratedCamera : CaptureDeviceKind.Unknown;
    }
}

internal static class WindowsDeviceProperties
{
    private static readonly DevPropKey ContainerId = new(new("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C"), 2);
    public static Guid? TryGetContainerId(string interfacePath)
    {
        uint type = 0, size = 16; var bytes = new byte[size]; var key = ContainerId;
        var result = CM_Get_Device_Interface_Property(interfacePath, ref key, out type, bytes, ref size, 0);
        if (result == 0 && size >= 16) return new Guid(bytes.AsSpan(0, 16));
        return TryGetContainerIdViaSetupApi(interfacePath);
    }
    private static Guid? TryGetContainerIdViaSetupApi(string interfacePath)
    {
        var set = SetupDiCreateDeviceInfoList(IntPtr.Zero, IntPtr.Zero); if (set == new IntPtr(-1)) return null;
        try
        {
            var interfaceData = new DeviceInterfaceData { Size = (uint)Marshal.SizeOf<DeviceInterfaceData>() };
            if (!SetupDiOpenDeviceInterface(set, interfacePath, 0, ref interfaceData)) return null;
            var deviceInfo = new DeviceInfoData { Size = (uint)Marshal.SizeOf<DeviceInfoData>() }; SetupDiGetDeviceInterfaceDetail(set, ref interfaceData, IntPtr.Zero, 0, out var required, ref deviceInfo);
            var detail = Marshal.AllocHGlobal((int)Math.Max(required, 8));
            try
            {
                Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6); if (!SetupDiGetDeviceInterfaceDetail(set, ref interfaceData, detail, required, out _, ref deviceInfo)) return null;
                var buffer = new byte[16]; var property = ContainerId; return SetupDiGetDeviceProperty(set, ref deviceInfo, ref property, out _, buffer, (uint)buffer.Length, out var returned, 0) && returned >= 16 ? new Guid(buffer) : null;
            }
            finally { Marshal.FreeHGlobal(detail); }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
    }
    [StructLayout(LayoutKind.Sequential)] private readonly record struct DevPropKey(Guid FormatId, uint PropertyId);
    [StructLayout(LayoutKind.Sequential)] private struct DeviceInterfaceData { public uint Size; public Guid InterfaceClassGuid; public uint Flags; public UIntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)] private struct DeviceInfoData { public uint Size; public Guid ClassGuid; public uint DevInst; public UIntPtr Reserved; }
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, EntryPoint = "CM_Get_Device_Interface_PropertyW")]
    private static extern int CM_Get_Device_Interface_Property([MarshalAs(UnmanagedType.LPWStr)] string deviceInterface, ref DevPropKey propertyKey, out uint propertyType, [Out] byte[] propertyBuffer, ref uint propertyBufferSize, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)] private static extern IntPtr SetupDiCreateDeviceInfoList(IntPtr classGuid, IntPtr hwndParent);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SetupDiOpenDeviceInterfaceW")] private static extern bool SetupDiOpenDeviceInterface(IntPtr deviceInfoSet, string devicePath, uint openFlags, ref DeviceInterfaceData deviceInterfaceData);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SetupDiGetDeviceInterfaceDetailW")] private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet, ref DeviceInterfaceData deviceInterfaceData, IntPtr detailData, uint detailDataSize, out uint requiredSize, ref DeviceInfoData deviceInfoData);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SetupDiGetDevicePropertyW")] private static extern bool SetupDiGetDeviceProperty(IntPtr deviceInfoSet, ref DeviceInfoData deviceInfoData, ref DevPropKey propertyKey, out uint propertyType, [Out] byte[] propertyBuffer, uint propertyBufferSize, out uint requiredSize, uint flags);
    [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);
}

public sealed class MediaFoundationCaptureSession : ICaptureSession
{
    private readonly CaptureDeviceDescriptor _device; private readonly CaptureOpenOptions _options; private readonly DevicePairing _pairing; private readonly IDiagnosticSink _log;
    private IMFSourceReader? _reader; private CancellationTokenSource? _cts; private Task? _readTask; private WasapiCaptureWorker? _audioWorker; private bool _mfStarted; private int _temporalIndex; private SourceFormat _sourceFormat;
    public CaptureFormat CurrentFormat { get; private set; }
    public TimingQuality TimingQuality { get; private set; } = TimingQuality.StreamTimestamp;
    public event EventHandler<VideoFrame>? VideoSampleReceived;
    public event EventHandler<AudioChunk>? AudioSampleReceived;
    public event EventHandler<CaptureStatusChangedEventArgs>? StatusChanged;

    private MediaFoundationCaptureSession(CaptureDeviceDescriptor device, CaptureOpenOptions options, DevicePairing pairing, IDiagnosticSink log, IMFSourceReader reader, CaptureFormat format, SourceFormat sourceFormat)
    { _device = device; _options = options; _pairing = pairing; _log = log; _reader = reader; CurrentFormat = format; _sourceFormat = sourceFormat; }

    internal static MediaFoundationCaptureSession Open(CaptureDeviceDescriptor device, CaptureOpenOptions options, DevicePairing pairing, IDiagnosticSink log, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); MediaFoundationNative.MFStartup(0x00020070, 1).ThrowIfFailed(); IMFSourceReader? reader = null;
        try
        {
            var activate = FindActivation(device.Id); if (activate is null) throw new InvalidOperationException("The selected capture device is no longer available.");
            try
            {
                var iid = MfGuids.MediaSource; activate.ActivateObject(ref iid, out var source).ThrowIfFailed();
                try { MediaFoundationNative.MFCreateSourceReaderFromMediaSource(source, null, out reader).ThrowIfFailed(); }
                finally { Marshal.ReleaseComObject(source); }
            }
            finally { Marshal.ReleaseComObject(activate); }
            var selection = SelectFormat(reader); reader.SetCurrentMediaType(MediaFoundationNative.SourceReaderFirstVideoStream, IntPtr.Zero, selection.MediaType).ThrowIfFailed();
            Marshal.ReleaseComObject(selection.MediaType); reader.SetStreamSelection(MediaFoundationNative.SourceReaderFirstVideoStream, 1).ThrowIfFailed();
            log.Write("capture.format", $"device='{device.FriendlyName}' format={selection.Format.Display} pixel={selection.Source.PixelFormat} stride={selection.Source.Stride} fields={selection.Format.FieldOrder}");
            var format = pairing.Endpoint is null ? selection.Format with { AudioSampleRate = 0 } : selection.Format;
            var session = new MediaFoundationCaptureSession(device, options, pairing, log, reader, format, selection.Source) { _mfStarted = true }; reader = null; return session;
        }
        catch { if (reader is not null) Marshal.ReleaseComObject(reader); MediaFoundationNative.MFShutdown(); throw; }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_readTask is not null) return Task.CompletedTask; _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); StatusChanged?.Invoke(this, new(CaptureStatus.Starting, "Opening Media Foundation video stream"));
        _readTask = Task.Run(() => ReadLoop(_cts.Token), _cts.Token);
        if (_options.IncludeAudio && _pairing.Endpoint is not null) { _audioWorker = new(_pairing.Endpoint, _log); _audioWorker.AudioSampleReceived += ForwardAudio; _audioWorker.StatusChanged += ForwardStatus; _ = _audioWorker.StartAsync(_cts.Token); }
        return Task.CompletedTask;
    }

    private void ForwardAudio(object? sender, AudioChunk chunk) => AudioSampleReceived?.Invoke(this, chunk);
    private void ForwardStatus(object? sender, CaptureStatusChangedEventArgs status) => StatusChanged?.Invoke(this, status);

    private void ReadLoop(CancellationToken cancellationToken)
    {
        MediaFoundationNative.CoInitializeEx(IntPtr.Zero, 0);
        StatusChanged?.Invoke(this, new(CaptureStatus.Running, _pairing.Endpoint is null ? "Video running; paired audio not found" : $"Video running; paired audio: {_pairing.Endpoint.FriendlyName}"));
        try
        {
            while (!cancellationToken.IsCancellationRequested && _reader is not null)
            {
                var hr = _reader.ReadSample(MediaFoundationNative.SourceReaderFirstVideoStream, 0, out _, out var flags, out var readerTimestamp, out var sample); hr.ThrowIfFailed();
                if ((flags & MediaFoundationNative.EndOfStream) != 0) throw new InvalidOperationException("Capture device ended the video stream.");
                if ((flags & MediaFoundationNative.MediaTypeChanged) != 0) RefreshCurrentFormat();
                if (sample is null) continue;
                try
                {
                    sample.GetSampleTime(out var sampleTime); var deviceTime = sample.TryGetUInt64(MfGuids.DeviceTimestamp);
                    var timestamp = deviceTime is { } qpc
                        ? new MediaTimestamp(qpc, TimingQuality.DeviceHardware, "windows-qpc-100ns", qpc)
                        : new MediaTimestamp(sampleTime != 0 ? sampleTime : readerTimestamp, TimingQuality.StreamTimestamp, $"mf-stream:{_device.Id}", sampleTime);
                    if (timestamp.Quality != TimingQuality) { TimingQuality = timestamp.Quality; _log.Write("capture.timing", $"source={timestamp.Quality} domain='{timestamp.ClockDomain}' raw={timestamp.RawValue}"); }
                    sample.ConvertToContiguousBuffer(out var buffer).ThrowIfFailed();
                    try
                    {
                        var luma = ExtractAnalysisLuma(buffer, _sourceFormat, _options.PreferredAnalysisWidth, _options.PreferredAnalysisHeight);
                        VideoSampleReceived?.Invoke(this, new(timestamp, _options.PreferredAnalysisWidth, _options.PreferredAnalysisHeight, luma, Interlocked.Increment(ref _temporalIndex), Stride: _options.PreferredAnalysisWidth));
                    }
                    finally { Marshal.ReleaseComObject(buffer); }
                }
                finally { Marshal.ReleaseComObject(sample); }
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _log.Write("capture.failure", $"device='{_device.FriendlyName}' error='{ex.Message}'"); StatusChanged?.Invoke(this, new(CaptureStatus.Failed, ex.Message, ex));
        }
        finally { MediaFoundationNative.CoUninitialize(); }
    }

    private void RefreshCurrentFormat()
    {
        if (_reader is null) return; _reader.GetCurrentMediaType(MediaFoundationNative.SourceReaderFirstVideoStream, out var type).ThrowIfFailed();
        try { (CurrentFormat, _sourceFormat) = ParseFormat(type); _log.Write("capture.format", $"Dynamic format change: {CurrentFormat.Display}"); }
        finally { Marshal.ReleaseComObject(type); }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_cts is null) return; StatusChanged?.Invoke(this, new(CaptureStatus.Stopping, "Stopping Windows capture")); _cts.Cancel();
        if (_audioWorker is not null) { _audioWorker.AudioSampleReceived -= ForwardAudio; _audioWorker.StatusChanged -= ForwardStatus; await _audioWorker.DisposeAsync(); _audioWorker = null; }
        try { _reader?.Flush(MediaFoundationNative.SourceReaderFirstVideoStream); } catch { }
        try { if (_readTask is not null) await _readTask.WaitAsync(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false); } catch (OperationCanceledException) { } catch (TimeoutException) { _log.Write("capture.shutdown", "Read loop did not exit within three seconds"); }
        _readTask = null; _cts.Dispose(); _cts = null; StatusChanged?.Invoke(this, new(CaptureStatus.Stopped, "Media Foundation capture stopped"));
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false); if (_reader is not null) { Marshal.ReleaseComObject(_reader); _reader = null; }
        if (_mfStarted) { MediaFoundationNative.MFShutdown(); _mfStarted = false; }
    }

    private static IMFActivate? FindActivation(string id)
    {
        using var attributes = MediaFoundationNative.CreateAttributes(1); attributes.Value.SetGuid(MfGuids.DevSourceType, MfGuids.VideoCaptureSource); MediaFoundationNative.MFEnumDeviceSources(attributes.Value, out var array, out var count).ThrowIfFailed();
        try
        {
            for (var i = 0; i < count; i++)
            {
                var ptr = Marshal.ReadIntPtr(array, i * IntPtr.Size); var activate = (IMFActivate)Marshal.GetObjectForIUnknown(ptr); Marshal.Release(ptr);
                if (activate.TryGetAllocatedString(MfGuids.SymbolicLink) == id) return activate; Marshal.ReleaseComObject(activate);
            }
            return null;
        }
        finally { Marshal.FreeCoTaskMem(array); }
    }

    private static (IMFMediaType MediaType, CaptureFormat Format, SourceFormat Source) SelectFormat(IMFSourceReader reader)
    {
        var candidates = new List<(IMFMediaType Type, CaptureFormat Format, SourceFormat Source)>();
        for (var i = 0; ; i++)
        {
            var hr = reader.GetNativeMediaType(MediaFoundationNative.SourceReaderFirstVideoStream, i, out var type); if (hr == MediaFoundationNative.NoMoreTypes) break; hr.ThrowIfFailed();
            try { var parsed = ParseFormat(type); candidates.Add((type, parsed.Format, parsed.Source)); }
            catch { Marshal.ReleaseComObject(type); }
        }
        if (candidates.Count == 0) throw new InvalidOperationException("Capture device reported no usable video media types.");
        var chosenFormat = CaptureFormatSelector.Select(candidates.Select(c => c.Format))!; var chosen = candidates.First(c => c.Format == chosenFormat);
        foreach (var item in candidates) if (!ReferenceEquals(item.Type, chosen.Type)) Marshal.ReleaseComObject(item.Type);
        return chosen;
    }

    private static (CaptureFormat Format, SourceFormat Source) ParseFormat(IMFMediaType type)
    {
        var size = type.TryGetUInt64(MfGuids.FrameSize) ?? throw new InvalidOperationException("Media type has no frame size."); var rate = type.TryGetUInt64(MfGuids.FrameRate) ?? (30L << 32 | 1);
        var width = (int)((ulong)size >> 32); var height = (int)(size & uint.MaxValue); var numerator = (int)((ulong)rate >> 32); var denominator = (int)(rate & uint.MaxValue);
        var subtype = type.TryGetGuid(MfGuids.Subtype) ?? Guid.Empty; var pixel = subtype == MfGuids.Nv12 ? VideoPixelFormat.Nv12 : subtype == MfGuids.Yuy2 ? VideoPixelFormat.Yuy2 : subtype == MfGuids.Rgb32 ? VideoPixelFormat.Bgra32 : VideoPixelFormat.Unknown;
        if (pixel == VideoPixelFormat.Unknown) throw new NotSupportedException($"Unsupported native pixel subtype {subtype}");
        var interlace = type.TryGetUInt32(MfGuids.InterlaceMode) ?? 2; var scan = interlace == 2 ? ScanMode.Progressive : ScanMode.Interlaced; var order = interlace == 3 || interlace == 5 ? FieldOrder.TopFirst : interlace == 4 || interlace == 6 ? FieldOrder.BottomFirst : FieldOrder.Unknown;
        var defaultStride = type.TryGetUInt32(MfGuids.DefaultStride); var stride = defaultStride is { } s ? s : pixel switch { VideoPixelFormat.Yuy2 => width * 2, VideoPixelFormat.Bgra32 => width * 4, _ => width };
        return (new(width, height, Rational.From(numerator, Math.Max(1, denominator)), scan, order, PixelFormat: pixel), new(width, height, stride, pixel));
    }

    private static unsafe byte[] ExtractAnalysisLuma(IMFMediaBuffer buffer, SourceFormat source, int targetWidth, int targetHeight)
    {
        buffer.Lock(out var data, out _, out var length).ThrowIfFailed();
        try
        {
            var output = new byte[targetWidth * targetHeight]; var pointer = (byte*)data;
            for (var y = 0; y < targetHeight; y++)
            {
                var sy = Math.Min(source.Height - 1, y * source.Height / targetHeight); var row = pointer + sy * source.Stride;
                for (var x = 0; x < targetWidth; x++)
                {
                    var sx = Math.Min(source.Width - 1, x * source.Width / targetWidth); var offset = source.PixelFormat switch { VideoPixelFormat.Nv12 => sx, VideoPixelFormat.Yuy2 => sx * 2, VideoPixelFormat.Bgra32 => sx * 4, _ => sx };
                    if (sy * source.Stride + offset >= length) continue;
                    output[y * targetWidth + x] = source.PixelFormat == VideoPixelFormat.Bgra32
                        ? (byte)Math.Clamp((row[offset + 2] * 54 + row[offset + 1] * 183 + row[offset] * 19) >> 8, 0, 255)
                        : row[offset];
                }
            }
            return output;
        }
        finally { buffer.Unlock(); }
    }
    private readonly record struct SourceFormat(int Width, int Height, int Stride, VideoPixelFormat PixelFormat);
}
