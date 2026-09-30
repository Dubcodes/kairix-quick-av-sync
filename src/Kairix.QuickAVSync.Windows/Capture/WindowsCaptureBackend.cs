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
    private const int ValidationFrameCount = 3;
    private static readonly TimeSpan ValidationTimeout = TimeSpan.FromSeconds(3);
    private readonly CaptureDeviceDescriptor _device; private readonly CaptureOpenOptions _options; private readonly DevicePairing _pairing; private readonly IDiagnosticSink _log;
    private IMFSourceReader? _reader; private CancellationTokenSource? _cts; private Task? _readTask; private WasapiCaptureWorker? _audioWorker; private bool _mfStarted; private int _temporalIndex; private int _validationStreak; private int _emptySampleCount; private int _audioState; private SourceFormat _sourceFormat;
    private TaskCompletionSource _firstVideoSample = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public CaptureFormat CurrentFormat { get; private set; }
    public TimingQuality TimingQuality { get; private set; } = TimingQuality.StreamTimestamp;
    public event EventHandler<VideoFrame>? VideoSampleReceived;
    public event EventHandler<AudioChunk>? AudioSampleReceived;
    public event EventHandler<CaptureStatusChangedEventArgs>? StatusChanged;

    private MediaFoundationCaptureSession(CaptureDeviceDescriptor device, CaptureOpenOptions options, DevicePairing pairing, IDiagnosticSink log, IMFSourceReader reader, CaptureFormat format, SourceFormat sourceFormat)
    { _device = device; _options = options; _pairing = pairing; _log = log; _reader = reader; CurrentFormat = format; _sourceFormat = sourceFormat; }

    internal static MediaFoundationCaptureSession Open(CaptureDeviceDescriptor device, CaptureOpenOptions options, DevicePairing pairing, IDiagnosticSink log, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); IMFSourceReader? reader = null; IntPtr source = IntPtr.Zero;
        log.Write("capture.coordinator", $"Trying Media Foundation for '{device.FriendlyName}'");
        log.Write("capture.open", $"Opening device name='{device.FriendlyName}' link='{device.Id}' container='{device.ContainerId ?? "unavailable"}'");
        var startupHr = MediaFoundationNative.MFStartup(0x00020070, 1); log.Write("capture.open", $"MFStartup {DescribeHr(startupHr)}"); startupHr.ThrowIfFailed();
        try
        {
            log.Write("capture.open", "Locating IMFActivate for selected symbolic link");
            var activate = FindActivation(device.Id); if (activate is null) throw new InvalidOperationException("The selected capture device is no longer available.");
            try
            {
                var iid = MfGuids.MediaSource; var activateHr = activate.ActivateObject(ref iid, out source); log.Write("capture.open", $"IMFActivate::ActivateObject(IMFMediaSource) {DescribeHr(activateHr)}"); activateHr.ThrowIfFailed();
                var readerHr = MediaFoundationNative.MFCreateSourceReaderFromMediaSource(source, null, out reader); log.Write("capture.open", $"MFCreateSourceReaderFromMediaSource {DescribeHr(readerHr)}"); readerHr.ThrowIfFailed();
            }
            finally { if (source != IntPtr.Zero) { Marshal.Release(source); source = IntPtr.Zero; } Marshal.ReleaseComObject(activate); }
            log.Write("capture.open", "Enumerating native video media types");
            var selection = SelectAndSetFormat(reader, log);
            Marshal.ReleaseComObject(selection.MediaType); reader.SetStreamSelection(MediaFoundationNative.SourceReaderFirstVideoStream, 1).ThrowIfFailed();
            log.Write("capture.format", $"device='{device.FriendlyName}' format={selection.Format.Display} pixel={selection.Source.PixelFormat} stride={selection.Source.Stride} fields={selection.Format.FieldOrder}");
            var format = pairing.Endpoint is null ? selection.Format with { AudioSampleRate = 0 } : selection.Format;
            var session = new MediaFoundationCaptureSession(device, options, pairing, log, reader, format, selection.Source) { _mfStarted = true }; reader = null; return session;
        }
        catch (Exception ex) { log.Write("capture.open.failure", ex.ToString()); if (source != IntPtr.Zero) Marshal.Release(source); if (reader is not null) Marshal.ReleaseComObject(reader); MediaFoundationNative.MFShutdown(); throw; }
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_readTask is not null) { await _firstVideoSample.Task.WaitAsync(cancellationToken).ConfigureAwait(false); return; }
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); StatusChanged?.Invoke(this, new(CaptureStatus.Starting, "Waiting for first video frame"));
        _readTask = Task.Run(() => ReadLoop(_cts.Token), _cts.Token);
        if (_options.IncludeAudio && _pairing.Endpoint is not null) { _audioWorker = new(_pairing.Endpoint, _log); _audioWorker.AudioSampleReceived += ForwardAudio; _audioWorker.StatusChanged += ForwardStatus; _ = _audioWorker.StartAsync(_cts.Token); }
        try
        {
            await _firstVideoSample.Task.WaitAsync(ValidationTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            var message = $"Fewer than {ValidationFrameCount} video frames with pixel data arrived within {ValidationTimeout.TotalSeconds:0} seconds ({Volatile.Read(ref _temporalIndex)} payload, {Volatile.Read(ref _emptySampleCount)} timestamp-only).";
            _log.Write("capture.first-sample", message); _log.Write("capture.coordinator", $"Media Foundation rejected for '{_device.FriendlyName}': {message}"); StatusChanged?.Invoke(this, new(CaptureStatus.Failed, message)); throw new TimeoutException(message);
        }
    }

    private void ForwardAudio(object? sender, AudioChunk chunk) => AudioSampleReceived?.Invoke(this, chunk);
    private void ForwardStatus(object? sender, CaptureStatusChangedEventArgs status)
    {
        if (status.Status == CaptureStatus.Running)
        {
            Volatile.Write(ref _audioState, 1);
            if (_firstVideoSample.Task.IsCompletedSuccessfully) StatusChanged?.Invoke(this, new(CaptureStatus.Running, LiveStatus()));
            return;
        }
        if (status.Status == CaptureStatus.Failed)
        {
            Volatile.Write(ref _audioState, -1); _log.Write("capture.audio", $"Video capture remains independent of audio failure: {status.Message}");
            if (_firstVideoSample.Task.IsCompletedSuccessfully) StatusChanged?.Invoke(this, new(CaptureStatus.Running, LiveStatus()));
        }
    }

    private string LiveStatus() => WindowsCaptureReadiness.Describe(_firstVideoSample.Task.IsCompletedSuccessfully, _pairing.Endpoint is not null, Volatile.Read(ref _audioState) > 0, Volatile.Read(ref _audioState) < 0, TimingQuality);

    private void ReadLoop(CancellationToken cancellationToken)
    {
        MediaFoundationNative.CoInitializeEx(IntPtr.Zero, 0);
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
                    var bufferCountHr = sample.GetBufferCount(out var bufferCount);
                    if (_temporalIndex == 0 && Volatile.Read(ref _emptySampleCount) == 0) _log.Write("capture.sample", $"first sample timestamp={timestamp.Ticks100ns} quality={timestamp.Quality} raw={timestamp.RawValue?.ToString() ?? "unavailable"}; GetBufferCount {DescribeHr(bufferCountHr)} count={bufferCount}");
                    bufferCountHr.ThrowIfFailed();
                    if (bufferCount == 0)
                    {
                        Volatile.Write(ref _validationStreak, 0);
                        var emptyCount = Interlocked.Increment(ref _emptySampleCount);
                        if (emptyCount == 1 || emptyCount % 250 == 0) _log.Write("capture.sample", $"Media Foundation delivered timestamp-only samples without video data; count={emptyCount}");
                        continue;
                    }
                    var contiguousHr = sample.ConvertToContiguousBuffer(out var buffer);
                    if (contiguousHr < 0 && bufferCount == 1)
                    {
                        _log.Write("capture.sample", $"ConvertToContiguousBuffer {DescribeHr(contiguousHr)}; falling back to the sample's sole native buffer");
                        sample.GetBufferByIndex(0, out buffer).ThrowIfFailed();
                    }
                    else
                    {
                        contiguousHr.ThrowIfFailed();
                    }
                    try
                    {
                        if (_temporalIndex == 0)
                        {
                            buffer.GetCurrentLength(out var currentLength).ThrowIfFailed();
                            buffer.GetMaxLength(out var maximumLength).ThrowIfFailed();
                            _log.Write("capture.sample", $"first buffer currentLength={currentLength} maximumLength={maximumLength} expectedMinimum={Math.Abs(_sourceFormat.Stride) * _sourceFormat.Height}");
                        }
                        var luma = ExtractAnalysisLuma(buffer, _sourceFormat, _options.PreferredAnalysisWidth, _options.PreferredAnalysisHeight);
                        var sampleNumber = Interlocked.Increment(ref _temporalIndex);
                        var validationStreak = Interlocked.Increment(ref _validationStreak);
                        VideoSampleReceived?.Invoke(this, new(timestamp, _options.PreferredAnalysisWidth, _options.PreferredAnalysisHeight, luma, sampleNumber, Stride: _options.PreferredAnalysisWidth));
                        if (sampleNumber == 1)
                        {
                            _log.Write("capture.first-sample", $"Video payload received timestamp={timestamp.Ticks100ns} raw={timestamp.RawValue?.ToString() ?? "unavailable"} deviceTimestamp={(deviceTime is null ? "unavailable" : "available")} format='{CurrentFormat.Display}'");
                        }
                        if (validationStreak == ValidationFrameCount && !_firstVideoSample.Task.IsCompleted)
                        {
                            _firstVideoSample.TrySetResult();
                            _log.Write("capture.validation", $"Media Foundation accepted after {ValidationFrameCount} consecutive payload-bearing frames");
                            _log.Write("capture.coordinator", $"Media Foundation accepted for '{_device.FriendlyName}'");
                            StatusChanged?.Invoke(this, new(CaptureStatus.Running, LiveStatus()));
                        }
                        else if (sampleNumber % 250 == 0) _log.Write("capture.samples", $"delivered={sampleNumber} latestTimestamp={timestamp.Ticks100ns}");
                    }
                    finally { Marshal.ReleaseComObject(buffer); }
                }
                finally { Marshal.ReleaseComObject(sample); }
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _firstVideoSample.TrySetException(ex); _log.Write("capture.failure", $"device='{_device.FriendlyName}' exception={ex}"); StatusChanged?.Invoke(this, new(CaptureStatus.Failed, ex.Message, ex));
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
        var reader = _reader;
        if (reader is not null)
        {
            try
            {
                await Task.Run(() => reader.Flush(MediaFoundationNative.SourceReaderFirstVideoStream), CancellationToken.None)
                    .WaitAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException) { _log.Write("capture.shutdown", "Source-reader flush did not exit within two seconds"); }
            catch { }
        }
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

    private static (IMFMediaType MediaType, CaptureFormat Format, SourceFormat Source) SelectAndSetFormat(IMFSourceReader reader, IDiagnosticSink log)
    {
        (CaptureFormat Format, SourceFormat Source)? current = null;
        var currentHr = reader.GetCurrentMediaType(MediaFoundationNative.SourceReaderFirstVideoStream, out var currentType);
        if (currentHr >= 0)
        {
            try { current = ParseFormat(currentType); log.Write("capture.current-format", $"Source Reader default/current native type: {Describe(current.Value.Format, current.Value.Source)}"); }
            catch (Exception ex) { log.Write("capture.current-format", $"Default/current type is not directly supported: {ex.Message}"); }
            finally { Marshal.ReleaseComObject(currentType); }
        }
        else log.Write("capture.current-format", $"GetCurrentMediaType {DescribeHr(currentHr)}; ranking native capabilities without a current-mode hint");
        var candidates = new List<(int Index, IMFMediaType Type, CaptureFormat Format, SourceFormat Source)>(); var suppressedFormats = 0; var nativeTypeCount = 0;
        for (var i = 0; ; i++)
        {
            var hr = reader.GetNativeMediaType(MediaFoundationNative.SourceReaderFirstVideoStream, i, out var type); if (hr == MediaFoundationNative.NoMoreTypes) break; hr.ThrowIfFailed();
            nativeTypeCount++;
            try { var parsed = ParseFormat(type); candidates.Add((i, type, parsed.Format, parsed.Source)); if (i < 40) log.Write("capture.native-format", $"index={i} supported=true {Describe(parsed.Format, parsed.Source)}"); else suppressedFormats++; }
            catch (Exception ex) { if (i < 40) log.Write("capture.native-format", $"index={i} supported=false subtype='{type.TryGetGuid(MfGuids.Subtype)?.ToString() ?? "unavailable"}' reason='{ex.Message}'"); else suppressedFormats++; Marshal.ReleaseComObject(type); }
        }
        if (suppressedFormats > 0) log.Write("capture.native-format", $"suppressed={suppressedFormats} additional native media-type entries; total={nativeTypeCount} supported={candidates.Count}");
        if (candidates.Count == 0) throw new InvalidOperationException("Capture device reported no usable video media types.");
        (int Index, IMFMediaType Type, CaptureFormat Format, SourceFormat Source)? chosen = null;
        try
        {
            var ranked = WindowsNativeFormatRanker.Rank(candidates.Select(candidate => new WindowsNativeFormatCandidate(candidate.Index, candidate.Format, candidate.Source.PixelFormat, current is { } hint && SameVideoMode(candidate.Format, candidate.Source, hint.Format, hint.Source))));
            foreach (var rankedCandidate in ranked)
            {
                var candidate = candidates.First(item => item.Index == rankedCandidate.NativeIndex);
                var setTypeHr = reader.SetCurrentMediaType(MediaFoundationNative.SourceReaderFirstVideoStream, IntPtr.Zero, candidate.Type);
                log.Write("capture.negotiation", $"attempt index={candidate.Index} candidate='{Describe(candidate.Format, candidate.Source)}' {DescribeHr(setTypeHr)}");
                if (setTypeHr >= 0) { chosen = candidate; break; }
            }
            if (chosen is null) throw new InvalidOperationException($"Media Foundation rejected all {candidates.Count} supported native video formats.");
            log.Write("capture.open", $"Selected native media type index={chosen.Value.Index} candidate='{Describe(chosen.Value.Format, chosen.Value.Source)}'");
            return (chosen.Value.Type, chosen.Value.Format, chosen.Value.Source);
        }
        finally
        {
            foreach (var item in candidates)
            {
                if (chosen is null || !ReferenceEquals(item.Type, chosen.Value.Type)) Marshal.ReleaseComObject(item.Type);
            }
        }
    }

    private static string Describe(CaptureFormat format, SourceFormat source) => $"{format.Width}x{format.Height} rate={format.FrameRate} subtype={source.PixelFormat} scan={format.ScanMode} field={format.FieldOrder} stride={source.Stride}";
    private static bool SameVideoMode(CaptureFormat left, SourceFormat leftSource, CaptureFormat right, SourceFormat rightSource) => left.Width == right.Width && left.Height == right.Height && left.FrameRate == right.FrameRate && left.ScanMode == right.ScanMode && leftSource.PixelFormat == rightSource.PixelFormat;
    private static string DescribeHr(int hr) => $"HRESULT=0x{hr:X8} ({(hr >= 0 ? "S_OK" : Marshal.GetExceptionForHR(hr)?.Message ?? "unknown")})";

    private static (CaptureFormat Format, SourceFormat Source) ParseFormat(IMFMediaType type)
    {
        var size = type.TryGetUInt64(MfGuids.FrameSize) ?? throw new InvalidOperationException("Media type has no frame size."); var rate = type.TryGetUInt64(MfGuids.FrameRate) ?? (30L << 32 | 1);
        var width = (int)((ulong)size >> 32); var height = (int)(size & uint.MaxValue); var numerator = (int)((ulong)rate >> 32); var denominator = (int)(rate & uint.MaxValue);
        var subtype = type.TryGetGuid(MfGuids.Subtype) ?? Guid.Empty; var pixel = subtype == MfGuids.Nv12 ? VideoPixelFormat.Nv12 : subtype == MfGuids.Yuy2 ? VideoPixelFormat.Yuy2 : subtype == MfGuids.Uyvy ? VideoPixelFormat.Uyvy : subtype == MfGuids.Rgb32 ? VideoPixelFormat.Bgra32 : subtype == MfGuids.Rgb24 ? VideoPixelFormat.Bgr24 : VideoPixelFormat.Unknown;
        if (pixel == VideoPixelFormat.Unknown) throw new NotSupportedException($"Unsupported native pixel subtype {subtype}");
        var interlace = type.TryGetUInt32(MfGuids.InterlaceMode) ?? 2; var scan = interlace == 2 ? ScanMode.Progressive : ScanMode.Interlaced; var order = interlace == 3 || interlace == 5 ? FieldOrder.TopFirst : interlace == 4 || interlace == 6 ? FieldOrder.BottomFirst : FieldOrder.Unknown;
        var defaultStride = type.TryGetUInt32(MfGuids.DefaultStride); var stride = defaultStride is { } s ? s : pixel switch { VideoPixelFormat.Yuy2 or VideoPixelFormat.Uyvy => width * 2, VideoPixelFormat.Bgra32 => width * 4, VideoPixelFormat.Bgr24 => width * 3, _ => width };
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
                var sy = Math.Min(source.Height - 1, y * source.Height / targetHeight); var sourceRow = source.Stride < 0 ? source.Height - 1 - sy : sy; var row = pointer + sourceRow * Math.Abs(source.Stride);
                for (var x = 0; x < targetWidth; x++)
                {
                    var sx = Math.Min(source.Width - 1, x * source.Width / targetWidth); var offset = source.PixelFormat switch { VideoPixelFormat.Nv12 => sx, VideoPixelFormat.Yuy2 => sx * 2, VideoPixelFormat.Uyvy => sx * 2 + 1, VideoPixelFormat.Bgra32 => sx * 4, VideoPixelFormat.Bgr24 => sx * 3, _ => sx };
                    var bytesNeeded = source.PixelFormat switch { VideoPixelFormat.Bgra32 => 4, VideoPixelFormat.Bgr24 => 3, _ => 1 };
                    if (sy * Math.Abs(source.Stride) + offset + bytesNeeded > length) continue;
                    output[y * targetWidth + x] = source.PixelFormat is VideoPixelFormat.Bgra32 or VideoPixelFormat.Bgr24
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
