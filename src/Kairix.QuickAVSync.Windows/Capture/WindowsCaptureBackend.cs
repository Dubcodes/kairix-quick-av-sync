using System.Diagnostics;
using System.Runtime.InteropServices;
using Kairix.QuickAVSync.Capture;
using Kairix.QuickAVSync.Models;
using Kairix.QuickAVSync.Services;

namespace Kairix.QuickAVSync.Windows.Capture;

public sealed class WindowsCaptureBackend(IDiagnosticSink? diagnostics = null) : ICaptureBackend, ICaptureFormatProvider
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

    public Task<IReadOnlyList<CaptureFormatOption>> EnumerateFormatsAsync(CaptureDeviceDescriptor device, CancellationToken cancellationToken) => Task.Run<IReadOnlyList<CaptureFormatOption>>(() =>
    {
        MediaFoundationNative.CoInitializeEx(IntPtr.Zero, 0);
        try
        {
            if (device.BackendId != BackendName) throw new ArgumentException("Descriptor does not belong to the Windows Media Foundation backend.", nameof(device));
            return MediaFoundationCaptureSession.EnumerateNativeFormatOptions(device, _log, cancellationToken);
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

public sealed class MediaFoundationCaptureSession : ICaptureSession, ICapturePerformanceDiagnostics
{
    private const int ValidationFrameCount = 3;
    private static readonly TimeSpan ValidationTimeout = TimeSpan.FromSeconds(3);
    private readonly CaptureDeviceDescriptor _device; private readonly CaptureOpenOptions _options; private readonly DevicePairing _pairing; private readonly IDiagnosticSink _log;
    private readonly object _stopGate = new();
    private IMFSourceReader? _reader; private CancellationTokenSource? _cts; private Task? _readTask; private Task? _stopTask; private Task? _flushTask; private WasapiCaptureWorker? _audioWorker; private bool _mfStarted; private int _temporalIndex; private int _nativeSampleCount; private int _validationStreak; private int _emptySampleCount; private int _consecutiveEmptySamples; private int _audioState; private int _stopAttempts; private int _workerThreadId; private SourceFormat _sourceFormat;
    private string _stopState = "running";
    private long _audioChunkCount, _lastNativePayloadStopwatchTicks, _lastReadSampleReturnStopwatchTicks, _lastReadSampleEnteredStopwatchTicks, _lastAudioStopwatchTicks;
    private Dictionary<TemporalImageKind, NativeVideoConversionPlan> _conversionPlans = [];
    private long _conversionTicks; private long _conversionMaximumTicks; private int _conversionSamples;
    private long _readTicks, _readMaximumTicks, _processingTicks, _processingMaximumTicks, _callbackTicks, _callbackMaximumTicks, _bufferHoldTicks, _bufferHoldMaximumTicks;
    private int _readSamples, _processingSamples, _callbackSamples, _bufferHoldSamples, _readSlowBucket, _processingSlowBucket, _callbackSlowBucket, _bufferSlowBucket;
    private TaskCompletionSource _firstVideoSample = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public CaptureFormat CurrentFormat { get; private set; }
    public TimingQuality TimingQuality { get; private set; } = TimingQuality.StreamTimestamp;
    public event EventHandler<VideoFrame>? VideoSampleReceived;
    public event EventHandler<AudioChunk>? AudioSampleReceived;
    public event EventHandler<CaptureStatusChangedEventArgs>? StatusChanged;
    public CaptureProcessingDiagnostics GetProcessingDiagnostics()
    {
        var samples = Volatile.Read(ref _conversionSamples); var ticks = Interlocked.Read(ref _conversionTicks); var maximum = Interlocked.Read(ref _conversionMaximumTicks);
        return new(samples, samples == 0 ? 0 : ticks * 1000d / Stopwatch.Frequency / samples, maximum * 1000d / Stopwatch.Frequency);
    }
    public CaptureRuntimeDiagnostics GetRuntimeDiagnostics() => new(Volatile.Read(ref _nativeSampleCount), Volatile.Read(ref _temporalIndex), Interlocked.Read(ref _audioChunkCount), Interlocked.Read(ref _lastNativePayloadStopwatchTicks), Interlocked.Read(ref _lastReadSampleReturnStopwatchTicks), Interlocked.Read(ref _lastAudioStopwatchTicks), Volatile.Read(ref _consecutiveEmptySamples));
    public CaptureLoopDiagnostics GetLoopDiagnostics() => new(
        Volatile.Read(ref _workerThreadId), _readTask?.Status ?? TaskStatus.RanToCompletion, _flushTask?.Status, Volatile.Read(ref _stopState), Volatile.Read(ref _stopAttempts),
        Volatile.Read(ref _processingSamples), AverageMilliseconds(_readTicks, _readSamples), TicksToMilliseconds(Interlocked.Read(ref _readMaximumTicks)),
        AverageMilliseconds(_processingTicks, _processingSamples), TicksToMilliseconds(Interlocked.Read(ref _processingMaximumTicks)),
        AverageMilliseconds(_callbackTicks, _callbackSamples), TicksToMilliseconds(Interlocked.Read(ref _callbackMaximumTicks)),
        AverageMilliseconds(_bufferHoldTicks, _bufferHoldSamples), TicksToMilliseconds(Interlocked.Read(ref _bufferHoldMaximumTicks)));

    private MediaFoundationCaptureSession(CaptureDeviceDescriptor device, CaptureOpenOptions options, DevicePairing pairing, IDiagnosticSink log, IMFSourceReader reader, CaptureFormat format, SourceFormat sourceFormat)
    { _device = device; _options = options; _pairing = pairing; _log = log; _reader = reader; CurrentFormat = format; _sourceFormat = sourceFormat; BuildConversionPlans(); }

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
            var selection = SelectAndSetFormat(reader, options, log);
            Marshal.ReleaseComObject(selection.MediaType); reader.SetStreamSelection(MediaFoundationNative.SourceReaderFirstVideoStream, 1).ThrowIfFailed();
            log.Write("capture.format", $"device='{device.FriendlyName}' format={selection.Format.Display} pixel={selection.Source.PixelFormat} stride={selection.Source.Stride} fields={selection.Format.FieldOrder}");
            var format = pairing.Endpoint is null ? selection.Format with { AudioSampleRate = 0 } : selection.Format;
            ValidateFieldReconstruction(format, options.FieldReconstruction);
            var session = new MediaFoundationCaptureSession(device, options, pairing, log, reader, format, selection.Source) { _mfStarted = true }; reader = null; return session;
        }
        catch (Exception ex) { log.Write("capture.open.failure", ex.ToString()); if (source != IntPtr.Zero) Marshal.Release(source); if (reader is not null) Marshal.ReleaseComObject(reader); MediaFoundationNative.MFShutdown(); throw; }
    }

    internal static IReadOnlyList<CaptureFormatOption> EnumerateNativeFormatOptions(CaptureDeviceDescriptor device, IDiagnosticSink log, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); IMFSourceReader? reader = null; IntPtr source = IntPtr.Zero;
        MediaFoundationNative.MFStartup(0x00020070, 1).ThrowIfFailed();
        try
        {
            var activate = FindActivation(device.Id) ?? throw new InvalidOperationException("The selected capture device is no longer available.");
            try
            {
                var iid = MfGuids.MediaSource; activate.ActivateObject(ref iid, out source).ThrowIfFailed();
                MediaFoundationNative.MFCreateSourceReaderFromMediaSource(source, null, out reader).ThrowIfFailed();
            }
            finally { if (source != IntPtr.Zero) { Marshal.Release(source); source = IntPtr.Zero; } Marshal.ReleaseComObject(activate); }

            var unique = new Dictionary<string, CaptureFormatOption>(StringComparer.Ordinal);
            var currentHr = reader.GetCurrentMediaType(MediaFoundationNative.SourceReaderFirstVideoStream, out var currentType);
            if (currentHr >= 0)
            {
                try { var current = ParseFormat(currentType); log.Write("capture.format-list", $"current/default {Describe(current.Format, current.Source)}"); }
                catch (Exception ex) { log.Write("capture.format-list", $"current/default unsupported reason='{ex.Message}'"); }
                finally { Marshal.ReleaseComObject(currentType); }
            }
            else log.Write("capture.format-list", $"current/default GetCurrentMediaType {DescribeHr(currentHr)}");
            for (var index = 0; ; index++)
            {
                cancellationToken.ThrowIfCancellationRequested(); var hr = reader.GetNativeMediaType(MediaFoundationNative.SourceReaderFirstVideoStream, index, out var type);
                if (hr == MediaFoundationNative.NoMoreTypes) break; hr.ThrowIfFailed();
                try
                {
                    var parsed = ParseFormat(type); var candidate = new WindowsNativeFormatCandidate(index, parsed.Format, parsed.Source.PixelFormat); log.Write("capture.format-list", $"index={index} supported=true {Describe(parsed.Format, parsed.Source)}");
                    unique.TryAdd(WindowsNativeFormatRanker.ModeId(candidate), new(WindowsNativeFormatRanker.ModeId(candidate), CaptureFormatFormatter.Format(parsed.Format), parsed.Format));
                }
                catch (Exception ex) { log.Write("capture.format-list", $"Skipped native format index={index}: {ex.Message}"); }
                finally { Marshal.ReleaseComObject(type); }
            }
            var ordered = unique.Values.OrderByDescending(option => option.Format!.ScanMode == ScanMode.Progressive).ThenByDescending(option => option.Format!.Width <= 1920 && option.Format.Height <= 1080).ThenByDescending(option => option.Format!.Width * option.Format.Height).ThenByDescending(option => option.Format!.FrameRate.Value).ThenBy(option => option.Display, StringComparer.Ordinal).ToArray();
            log.Write("capture.format-list", $"device='{device.FriendlyName}' exposed {ordered.Length} selectable native modes");
            return ordered;
        }
        finally
        {
            if (source != IntPtr.Zero) Marshal.Release(source);
            if (reader is not null) Marshal.ReleaseComObject(reader);
            MediaFoundationNative.MFShutdown();
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_readTask is not null) { await _firstVideoSample.Task.WaitAsync(cancellationToken).ConfigureAwait(false); return; }
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); StatusChanged?.Invoke(this, new(CaptureStatus.Starting, "Waiting for first video frame"));
        Volatile.Write(ref _stopState, "running"); _readTask = Task.Run(() => ReadLoop(_cts.Token), _cts.Token);
        if (_options.IncludeAudio && _pairing.Endpoint is not null) { _audioWorker = new(_pairing.Endpoint, _log); _audioWorker.AudioSampleReceived += ForwardAudio; _audioWorker.StatusChanged += ForwardStatus; _ = _audioWorker.StartAsync(_cts.Token); }
        try
        {
            await _firstVideoSample.Task.WaitAsync(ValidationTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            var message = $"Fewer than {ValidationFrameCount} video frames with pixel data arrived within {ValidationTimeout.TotalSeconds:0} seconds ({Volatile.Read(ref _nativeSampleCount)} payload, {Volatile.Read(ref _emptySampleCount)} timestamp-only).";
            _log.Write("capture.first-sample", message); _log.Write("capture.coordinator", $"Media Foundation rejected for '{_device.FriendlyName}': {message}"); StatusChanged?.Invoke(this, new(CaptureStatus.Failed, message)); throw new TimeoutException(message);
        }
    }

    private void ForwardAudio(object? sender, AudioChunk chunk)
    {
        Interlocked.Increment(ref _audioChunkCount); Interlocked.Exchange(ref _lastAudioStopwatchTicks, Stopwatch.GetTimestamp());
        if (CurrentFormat.AudioSampleRate != chunk.SampleRate) CurrentFormat = CurrentFormat with { AudioSampleRate = chunk.SampleRate };
        AudioSampleReceived?.Invoke(this, chunk);
    }
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
        Volatile.Write(ref _workerThreadId, Environment.CurrentManagedThreadId);
        MediaFoundationNative.CoInitializeEx(IntPtr.Zero, 0);
        try
        {
            while (!cancellationToken.IsCancellationRequested && _reader is not null)
            {
                var readStarted = Stopwatch.GetTimestamp(); Interlocked.Exchange(ref _lastReadSampleEnteredStopwatchTicks, readStarted);
                var hr = _reader.ReadSample(MediaFoundationNative.SourceReaderFirstVideoStream, 0, out _, out var flags, out var readerTimestamp, out var sample); var arrivalStopwatchTicks = Stopwatch.GetTimestamp(); Interlocked.Exchange(ref _lastReadSampleReturnStopwatchTicks, arrivalStopwatchTicks); RecordDuration("ReadSample", arrivalStopwatchTicks - readStarted, ref _readTicks, ref _readMaximumTicks, ref _readSamples, ref _readSlowBucket); hr.ThrowIfFailed();
                if ((flags & MediaFoundationNative.EndOfStream) != 0) throw new InvalidOperationException("Capture device ended the video stream.");
                if ((flags & MediaFoundationNative.MediaTypeChanged) != 0) RefreshCurrentFormat();
                if (sample is null) continue;
                try { ProcessSample(sample, readerTimestamp, arrivalStopwatchTicks); }
                finally { Marshal.ReleaseComObject(sample); }
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _firstVideoSample.TrySetException(ex); _log.Write("capture.failure", $"device='{_device.FriendlyName}' exception={ex}"); StatusChanged?.Invoke(this, new(CaptureStatus.Failed, ex.Message, ex));
        }
        finally { MediaFoundationNative.CoUninitialize(); }
    }

    private void ProcessSample(IMFSample sample, long readerTimestamp, long arrivalStopwatchTicks)
    {
        var processingStarted = arrivalStopwatchTicks;
        try
        {
            var sampleTimeHr = sample.GetSampleTime(out var sampleTime); var deviceTime = sample.TryGetUInt64(MfGuids.DeviceTimestamp);
            var hasSampleTime = sampleTimeHr >= 0;
            var primarySource = deviceTime is not null ? VideoPrimaryTimestampSource.DeviceTimestamp : hasSampleTime ? VideoPrimaryTimestampSource.SampleTime : VideoPrimaryTimestampSource.ReaderTimestamp;
            var timestamp = deviceTime is { } qpc ? new MediaTimestamp((long)qpc, TimingQuality.DeviceHardware, "windows-qpc-100ns", (long)qpc) : new MediaTimestamp(hasSampleTime ? sampleTime : readerTimestamp, TimingQuality.StreamTimestamp, $"mf-stream:{_device.Id}", hasSampleTime ? sampleTime : readerTimestamp);
            var timingObservation = new VideoTimingObservation(primarySource, arrivalStopwatchTicks, Stopwatch.Frequency, deviceTime is { } device ? (long)device : null, hasSampleTime ? sampleTime : null, readerTimestamp);
            if (timestamp.Quality != TimingQuality) { TimingQuality = timestamp.Quality; _log.Write("capture.timing", $"source={timestamp.Quality} domain='{timestamp.ClockDomain}' raw={timestamp.RawValue}"); }
            var bufferCountHr = sample.GetBufferCount(out var bufferCount);
            if (_temporalIndex == 0 && Volatile.Read(ref _emptySampleCount) == 0) _log.Write("capture.sample", $"first sample timestamp={timestamp.Ticks100ns} quality={timestamp.Quality} raw={timestamp.RawValue?.ToString() ?? "unavailable"}; GetBufferCount {DescribeHr(bufferCountHr)} count={bufferCount}");
            bufferCountHr.ThrowIfFailed();
            if (bufferCount == 0)
            {
                Volatile.Write(ref _validationStreak, 0); var emptyCount = Interlocked.Increment(ref _emptySampleCount); var consecutiveEmpty = Interlocked.Increment(ref _consecutiveEmptySamples);
                if (consecutiveEmpty == 1 || consecutiveEmpty % 250 == 0) _log.Write("capture.sample", $"Media Foundation delivered timestamp-only samples without video data; total={emptyCount} consecutive={consecutiveEmpty}");
                return;
            }
            var contiguousHr = sample.ConvertToContiguousBuffer(out var buffer);
            if (contiguousHr < 0 && bufferCount == 1) { _log.Write("capture.sample", $"ConvertToContiguousBuffer {DescribeHr(contiguousHr)}; falling back to the sample's sole native buffer"); sample.GetBufferByIndex(0, out buffer).ThrowIfFailed(); }
            else contiguousHr.ThrowIfFailed();
            var bufferHoldStarted = Stopwatch.GetTimestamp();
            try
            {
                if (_temporalIndex == 0) { buffer.GetCurrentLength(out var currentLength).ThrowIfFailed(); buffer.GetMaxLength(out var maximumLength).ThrowIfFailed(); _log.Write("capture.sample", $"first buffer currentLength={currentLength} maximumLength={maximumLength} expectedMinimum={Math.Abs(_sourceFormat.Stride) * _sourceFormat.Height}"); }
                var nativeSampleNumber = Interlocked.Increment(ref _nativeSampleCount); Interlocked.Exchange(ref _lastNativePayloadStopwatchTicks, arrivalStopwatchTicks); Volatile.Write(ref _consecutiveEmptySamples, 0); var validationStreak = Interlocked.Increment(ref _validationStreak);
                if (_options.FieldReconstruction is { } reconstruction)
                {
                    var positions = ReconstructedFieldTimestampModel.Reconstruct(timestamp, timingObservation, reconstruction); var converted = ExtractFrames(buffer, positions.Select(position => position.Kind));
                    for (var index = 0; index < positions.Count; index++) PublishFrame(converted[index], positions[index], nativeSampleNumber);
                    if (nativeSampleNumber <= 3) _log.Write("field.reconstruction", $"sample={nativeSampleNumber} assumption='capture timestamp represents second field/completed pair' intervalMs={reconstruction.FieldIntervalTicks100ns / 10_000d:0.###} first={positions[0].Kind}@{positions[0].Timestamp.Ticks100ns} second={positions[1].Kind}@{positions[1].Timestamp.Ticks100ns}");
                }
                else { var converted = ExtractFrames(buffer, [TemporalImageKind.ProgressiveFrame])[0]; PublishFrame(converted, new(TemporalImageKind.ProgressiveFrame, timestamp, TimestampOrigin.DirectCapture, timingObservation), nativeSampleNumber); }
                if (nativeSampleNumber == 1) _log.Write("capture.first-sample", $"Video payload received timestamp={timestamp.Ticks100ns} raw={timestamp.RawValue?.ToString() ?? "unavailable"} deviceTimestamp={(deviceTime is null ? "unavailable" : "available")} format='{CurrentFormat.Display}'");
                if (validationStreak == ValidationFrameCount && !_firstVideoSample.Task.IsCompleted) { _firstVideoSample.TrySetResult(); _log.Write("capture.validation", $"Media Foundation accepted after {ValidationFrameCount} consecutive payload-bearing frames"); _log.Write("capture.coordinator", $"Media Foundation accepted for '{_device.FriendlyName}'"); StatusChanged?.Invoke(this, new(CaptureStatus.Running, LiveStatus())); }
                else if (nativeSampleNumber % 250 == 0) { _log.Write("capture.samples", $"native={nativeSampleNumber} temporal={Volatile.Read(ref _temporalIndex)} latestTimestamp={timestamp.Ticks100ns}"); LogConversionSummary("periodic", reset: true); }
            }
            finally { RecordDuration("native-buffer-hold", Stopwatch.GetTimestamp() - bufferHoldStarted, ref _bufferHoldTicks, ref _bufferHoldMaximumTicks, ref _bufferHoldSamples, ref _bufferSlowBucket); Marshal.ReleaseComObject(buffer); }
        }
        finally { RecordDuration("sample-processing", Stopwatch.GetTimestamp() - processingStarted, ref _processingTicks, ref _processingMaximumTicks, ref _processingSamples, ref _processingSlowBucket); }
    }

    private void RefreshCurrentFormat()
    {
        if (_reader is null) return; _reader.GetCurrentMediaType(MediaFoundationNative.SourceReaderFirstVideoStream, out var type).ThrowIfFailed();
        try { var parsed = ParseFormat(type); CurrentFormat = parsed.Format with { AudioSampleRate = CurrentFormat.AudioSampleRate }; _sourceFormat = parsed.Source; BuildConversionPlans(); _log.Write("capture.format", $"Dynamic capture-mode change: {CurrentFormat.Display}"); }
        finally { Marshal.ReleaseComObject(type); }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Task operation;
        lock (_stopGate)
        {
            if (_stopTask is null)
            {
                Interlocked.Increment(ref _stopAttempts); Volatile.Write(ref _stopState, "stopping");
                _stopTask = StopCoreAsync();
            }
            else _log.Write("capture.shutdown", $"Reusing terminal stop operation state={Volatile.Read(ref _stopState)} task={_stopTask.Status}; no additional Flush was started.");
            operation = _stopTask;
        }
        return cancellationToken.CanBeCanceled ? operation.WaitAsync(cancellationToken) : operation;
    }

    private async Task StopCoreAsync()
    {
        var cts = _cts; var readTask = _readTask;
        if (cts is null || readTask is null) { Volatile.Write(ref _stopState, "stopped"); return; }
        StatusChanged?.Invoke(this, new(CaptureStatus.Stopping, "Stopping Windows capture")); _firstVideoSample.TrySetCanceled(cts.Token); cts.Cancel();
        Exception? audioFailure = null;
        if (_audioWorker is not null)
        {
            _audioWorker.AudioSampleReceived -= ForwardAudio; _audioWorker.StatusChanged -= ForwardStatus;
            try { await _audioWorker.DisposeAsync().ConfigureAwait(false); _audioWorker = null; }
            catch (CaptureWorkerTerminationException ex) { audioFailure = ex; }
        }
        var reader = _reader;
        var flushCompleted = true;
        if (reader is not null)
        {
            _flushTask ??= Task.Run(() => reader.Flush(MediaFoundationNative.SourceReaderFirstVideoStream).ThrowIfFailed(), CancellationToken.None);
            try
            {
                await _flushTask.WaitAsync(TimeSpan.FromSeconds(2), CancellationToken.None).ConfigureAwait(false);
            }
            catch (TimeoutException) { flushCompleted = false; _log.Write("capture.shutdown", "Source-reader flush did not exit within two seconds"); }
            catch (Exception ex) { _log.Write("capture.shutdown", $"Source-reader flush completed with failure: {ex.Message}"); }
        }
        var readCompleted = true;
        try { await readTask.WaitAsync(TimeSpan.FromSeconds(3), CancellationToken.None).ConfigureAwait(false); }
        catch (TimeoutException) { readCompleted = false; }
        if (!flushCompleted || !readCompleted)
        {
            var message = $"Media Foundation stop did not complete safely; retaining SourceReader and MF lifetime until process shutdown (readTask={readTask.Status}, flushTask={_flushTask?.Status.ToString() ?? "none"}).";
            Volatile.Write(ref _stopState, "quarantined");
            _log.Write("capture.shutdown", message); StatusChanged?.Invoke(this, new(CaptureStatus.Failed, message));
            throw new CaptureWorkerTerminationException(message);
        }
        // Only this completed path transfers ownership away from ReadLoop. It is
        // now safe for DisposeAsync to release the reader and call MFShutdown.
        _readTask = null; _cts = null; cts.Dispose();
        if (audioFailure is not null) throw audioFailure;
        Volatile.Write(ref _stopState, "stopped");
        StatusChanged?.Invoke(this, new(CaptureStatus.Stopped, "Media Foundation capture stopped"));
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        LogConversionSummary("final", reset: false); if (_reader is not null) { Marshal.ReleaseComObject(_reader); _reader = null; }
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

    private static (IMFMediaType MediaType, CaptureFormat Format, SourceFormat Source) SelectAndSetFormat(IMFSourceReader reader, CaptureOpenOptions options, IDiagnosticSink log)
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
        var candidates = new List<(int Index, IMFMediaType Type, CaptureFormat Format, SourceFormat Source)>(); var nativeTypeCount = 0;
        for (var i = 0; ; i++)
        {
            var hr = reader.GetNativeMediaType(MediaFoundationNative.SourceReaderFirstVideoStream, i, out var type); if (hr == MediaFoundationNative.NoMoreTypes) break; hr.ThrowIfFailed();
            nativeTypeCount++;
            try { var parsed = ParseFormat(type); candidates.Add((i, type, parsed.Format, parsed.Source)); log.Write("capture.native-format", $"index={i} supported=true {Describe(parsed.Format, parsed.Source)}"); }
            catch (Exception ex) { log.Write("capture.native-format", $"index={i} supported=false subtype='{type.TryGetGuid(MfGuids.Subtype)?.ToString() ?? "unavailable"}' interlacePresent={type.TryGetUInt32(MfGuids.InterlaceMode) is not null} interlaceRaw={type.TryGetUInt32(MfGuids.InterlaceMode)?.ToString() ?? "missing"} reason='{ex.Message}'"); Marshal.ReleaseComObject(type); }
        }
        log.Write("capture.native-format", $"total={nativeTypeCount} supported={candidates.Count}");
        if (candidates.Count == 0) throw new InvalidOperationException("Capture device reported no usable video media types.");
        (int Index, IMFMediaType Type, CaptureFormat Format, SourceFormat Source)? chosen = null;
        try
        {
            var rankable = candidates.Select(candidate => new WindowsNativeFormatCandidate(candidate.Index, candidate.Format, candidate.Source.PixelFormat, current is { } hint && SameVideoMode(candidate.Format, candidate.Source, hint.Format, hint.Source))).ToArray();
            var ranked = WindowsNativeFormatRanker.Rank(rankable, options.PreferredNativeFormatId, options.PreferredSourceSignal, options.RequirePreferredNativeFormat);
            log.Write("capture.negotiation", $"ranking={(options.RequirePreferredNativeFormat ? "strict-explicit-mode" : string.IsNullOrWhiteSpace(options.PreferredNativeFormatId) ? SourceAwareFormatMatcher.CanAutomaticallyApply(options.PreferredSourceSignal) ? "source-aware-auto" : "generic-auto" : "preferred-mode-with-fallback")} sourceProvenance={options.PreferredSourceSignal?.Provenance.ToString() ?? "none"} fieldReconstruction={options.FieldReconstruction is not null}");
            foreach (var rankedCandidate in ranked)
            {
                var candidate = candidates.First(item => item.Index == rankedCandidate.NativeIndex);
                var setTypeHr = reader.SetCurrentMediaType(MediaFoundationNative.SourceReaderFirstVideoStream, IntPtr.Zero, candidate.Type);
                var requested = !string.IsNullOrWhiteSpace(options.PreferredNativeFormatId) && string.Equals(WindowsNativeFormatRanker.ModeId(rankedCandidate), options.PreferredNativeFormatId, StringComparison.Ordinal);
                log.Write("capture.negotiation", $"attempt index={candidate.Index} requested={requested} candidate='{Describe(candidate.Format, candidate.Source)}' {DescribeHr(setTypeHr)}");
                if (setTypeHr >= 0) { chosen = candidate; break; }
            }
            if (chosen is null) throw new InvalidOperationException(options.RequirePreferredNativeFormat
                ? $"REQUESTED CAPTURE FORMAT NOT ACCEPTED: Media Foundation rejected '{options.PreferredNativeFormatId}'."
                : $"Media Foundation rejected all {candidates.Count} supported native video formats.");
            var negotiatedFormat = chosen.Value.Format; var negotiatedSource = chosen.Value.Source;
            var negotiatedHr = reader.GetCurrentMediaType(MediaFoundationNative.SourceReaderFirstVideoStream, out var negotiatedType);
            if (negotiatedHr >= 0)
            {
                try
                {
                    var parsed = ParseFormat(negotiatedType); negotiatedFormat = parsed.Format; negotiatedSource = parsed.Source;
                    log.Write("capture.negotiated-format", $"selectedIndex={chosen.Value.Index} {Describe(negotiatedFormat, negotiatedSource)}");
                    var requestedCandidate = new WindowsNativeFormatCandidate(chosen.Value.Index, chosen.Value.Format, chosen.Value.Source.PixelFormat);
                    if (options.RequirePreferredNativeFormat && !WindowsNativeFormatVerifier.Matches(requestedCandidate, negotiatedFormat, negotiatedSource.PixelFormat))
                        throw new InvalidOperationException($"REQUESTED CAPTURE FORMAT NOT ACCEPTED: requested '{Describe(chosen.Value.Format, chosen.Value.Source)}' but the driver negotiated '{Describe(negotiatedFormat, negotiatedSource)}'.");
                }
                catch (Exception ex) when (!options.RequirePreferredNativeFormat) { log.Write("capture.negotiated-format", $"selectedIndex={chosen.Value.Index} current type unsupported after negotiation: {ex.Message}"); }
                finally { Marshal.ReleaseComObject(negotiatedType); }
            }
            else if (options.RequirePreferredNativeFormat) throw new InvalidOperationException($"REQUESTED CAPTURE FORMAT NOT ACCEPTED: unable to verify the negotiated media type ({DescribeHr(negotiatedHr)}).");
            else log.Write("capture.negotiated-format", $"selectedIndex={chosen.Value.Index} GetCurrentMediaType {DescribeHr(negotiatedHr)}; using requested metadata {Describe(negotiatedFormat, negotiatedSource)}");
            log.Write("capture.open", $"Selected native media type index={chosen.Value.Index} candidate='{Describe(negotiatedFormat, negotiatedSource)}'");
            return (chosen.Value.Type, negotiatedFormat, negotiatedSource);
        }
        finally
        {
            foreach (var item in candidates)
            {
                if (chosen is null || !ReferenceEquals(item.Type, chosen.Value.Type)) Marshal.ReleaseComObject(item.Type);
            }
        }
    }

    private static string Describe(CaptureFormat format, SourceFormat source) => $"{format.Width}x{format.Height} rate={format.FrameRate} subtype={source.PixelFormat} stride={source.Stride} interlacePresent={source.InterlaceAttributePresent} interlaceRaw={source.RawInterlaceMode?.ToString() ?? "missing"} scan={format.ScanMode} layout={format.InterlaceLayout} field={format.FieldOrder}";
    private static bool SameVideoMode(CaptureFormat left, SourceFormat leftSource, CaptureFormat right, SourceFormat rightSource) => left.Width == right.Width && left.Height == right.Height && left.FrameRate == right.FrameRate && left.ScanMode == right.ScanMode && left.InterlaceLayout == right.InterlaceLayout && left.FieldOrder == right.FieldOrder && leftSource.PixelFormat == rightSource.PixelFormat;
    private static string DescribeHr(int hr) => $"HRESULT=0x{hr:X8} ({(hr >= 0 ? "S_OK" : Marshal.GetExceptionForHR(hr)?.Message ?? "unknown")})";

    private static (CaptureFormat Format, SourceFormat Source) ParseFormat(IMFMediaType type)
    {
        var size = type.TryGetUInt64(MfGuids.FrameSize) ?? throw new InvalidOperationException("Media type has no frame size."); var rate = type.TryGetUInt64(MfGuids.FrameRate) ?? (30L << 32 | 1);
        var width = (int)((ulong)size >> 32); var height = (int)(size & uint.MaxValue); var numerator = (int)((ulong)rate >> 32); var denominator = (int)(rate & uint.MaxValue);
        var subtype = type.TryGetGuid(MfGuids.Subtype) ?? Guid.Empty; var pixel = subtype == MfGuids.Nv12 ? VideoPixelFormat.Nv12 : subtype == MfGuids.Yuy2 ? VideoPixelFormat.Yuy2 : subtype == MfGuids.Uyvy ? VideoPixelFormat.Uyvy : subtype == MfGuids.Rgb32 ? VideoPixelFormat.Bgra32 : subtype == MfGuids.Rgb24 ? VideoPixelFormat.Bgr24 : VideoPixelFormat.Unknown;
        if (pixel == VideoPixelFormat.Unknown) throw new NotSupportedException($"Unsupported native pixel subtype {subtype}");
        var interlace = WindowsInterlaceMetadata.Parse(type.TryGetUInt32(MfGuids.InterlaceMode));
        var defaultStride = type.TryGetUInt32(MfGuids.DefaultStride); var stride = defaultStride is { } s ? s : pixel switch { VideoPixelFormat.Yuy2 or VideoPixelFormat.Uyvy => width * 2, VideoPixelFormat.Bgra32 => width * 4, VideoPixelFormat.Bgr24 => width * 3, _ => width };
        return (new(width, height, Rational.From(numerator, Math.Max(1, denominator)), interlace.ScanMode, interlace.FieldOrder, PixelFormat: pixel, InterlaceLayout: interlace.Layout), new(width, height, stride, pixel, interlace.AttributePresent, interlace.RawValue));
    }

    private void PublishFrame(ConvertedVideoFrame converted, ReconstructedFieldPosition position, long nativeSampleIndex)
    {
        var temporalIndex = Interlocked.Increment(ref _temporalIndex);
        var callbackStarted = Stopwatch.GetTimestamp();
        try
        {
            VideoSampleReceived?.Invoke(this, new(position.Timestamp, _options.PreferredAnalysisWidth, _options.PreferredAnalysisHeight, converted.Luma, temporalIndex,
                TemporalImageKind: position.Kind, TimestampOrigin: position.TimestampOrigin, Stride: _options.PreferredAnalysisWidth,
                PresentationBgra: converted.Bgra, PresentationWidth: _options.PreferredPresentationWidth, PresentationHeight: _options.PreferredPresentationHeight,
                PresentationStride: _options.PreferredPresentationWidth * 4, TimingObservation: position.TimingObservation, NativeSampleIndex: nativeSampleIndex));
        }
        finally { RecordDuration("VideoSampleReceived-callback", Stopwatch.GetTimestamp() - callbackStarted, ref _callbackTicks, ref _callbackMaximumTicks, ref _callbackSamples, ref _callbackSlowBucket); }
    }

    private void RecordDuration(string operation, long ticks, ref long totalTicks, ref long maximumTicks, ref int samples, ref int slowBucket)
    {
        Interlocked.Add(ref totalTicks, ticks); Interlocked.Increment(ref samples);
        var priorMaximum = Interlocked.Read(ref maximumTicks);
        while (ticks > priorMaximum && Interlocked.CompareExchange(ref maximumTicks, ticks, priorMaximum) != priorMaximum) priorMaximum = Interlocked.Read(ref maximumTicks);
        var milliseconds = TicksToMilliseconds(ticks); var bucket = milliseconds >= 50 ? 4 : milliseconds >= 20 ? 3 : milliseconds >= 10 ? 2 : milliseconds >= 5 ? 1 : 0;
        var priorBucket = Volatile.Read(ref slowBucket);
        while (bucket > priorBucket)
        {
            var observed = Interlocked.CompareExchange(ref slowBucket, bucket, priorBucket);
            if (observed == priorBucket) { _log.Write("capture.slow-path", $"operation={operation} elapsedMs={milliseconds:0.###} thresholdMs={(bucket == 4 ? 50 : bucket == 3 ? 20 : bucket == 2 ? 10 : 5)}"); break; }
            priorBucket = observed;
        }
    }

    private static double AverageMilliseconds(long ticks, int samples) => samples <= 0 ? 0 : TicksToMilliseconds(Interlocked.Read(ref ticks)) / samples;
    private static double TicksToMilliseconds(long ticks) => ticks * 1000d / Stopwatch.Frequency;

    private unsafe IReadOnlyList<ConvertedVideoFrame> ExtractFrames(IMFMediaBuffer buffer, IEnumerable<TemporalImageKind> kinds)
    {
        buffer.Lock(out var data, out _, out var length).ThrowIfFailed();
        try
        {
            var started = Stopwatch.GetTimestamp();
            var bytes = new ReadOnlySpan<byte>((void*)data, length);
            var converted = new List<ConvertedVideoFrame>();
            foreach (var kind in kinds) converted.Add(NativeVideoFrameConverter.Convert(bytes, _conversionPlans[kind]));
            var elapsed = Stopwatch.GetTimestamp() - started;
            _conversionTicks += elapsed; _conversionMaximumTicks = Math.Max(_conversionMaximumTicks, elapsed); _conversionSamples++;
            return converted;
        }
        finally { buffer.Unlock(); }
    }

    private void BuildConversionPlans()
    {
        var layout = new NativeFrameLayout(_sourceFormat.Width, _sourceFormat.Height, _sourceFormat.Stride, _sourceFormat.PixelFormat);
        var kinds = _options.FieldReconstruction is null
            ? new[] { TemporalImageKind.ProgressiveFrame }
            : new[] { TemporalImageKind.TopField, TemporalImageKind.BottomField };
        _conversionPlans = kinds.ToDictionary(kind => kind, kind => NativeVideoFrameConverter.CreatePlan(layout,
            _options.PreferredAnalysisWidth, _options.PreferredAnalysisHeight, _options.PreferredPresentationWidth, _options.PreferredPresentationHeight, kind));
    }

    private void LogConversionSummary(string reason, bool reset)
    {
        if (_conversionSamples == 0) return;
        var averageMs = _conversionTicks * 1000d / Stopwatch.Frequency / _conversionSamples;
        var maximumMs = _conversionMaximumTicks * 1000d / Stopwatch.Frequency;
        _log.Write("capture.conversion", $"reason={reason} samples={_conversionSamples} analysis={_options.PreferredAnalysisWidth}x{_options.PreferredAnalysisHeight} presentation={_options.PreferredPresentationWidth}x{_options.PreferredPresentationHeight} reconstruction={_options.FieldReconstruction is not null} averageMs={averageMs:0.###} maximumMs={maximumMs:0.###}");
        if (reset) { _conversionTicks = 0; _conversionMaximumTicks = 0; _conversionSamples = 0; }
    }

    private static void ValidateFieldReconstruction(CaptureFormat format, FieldReconstructionOptions? reconstruction)
    {
        if (reconstruction is null) return;
        if (format.ScanMode != ScanMode.Progressive || format.FrameRate != reconstruction.CapturedProgressiveRate)
            throw new InvalidOperationException("FIELD RECONSTRUCTION NOT ACCEPTED: negotiated transport must be the selected progressive frame rate.");
        var exactDoubledRate = Rational.From(format.FrameRate.Numerator * 2, format.FrameRate.Denominator);
        if (reconstruction.TargetFieldRate != exactDoubledRate)
            throw new InvalidOperationException("FIELD RECONSTRUCTION NOT ACCEPTED: target field rate must be exactly twice the captured progressive rate.");
        if (reconstruction.FieldOrder is not (FieldOrder.TopFirst or FieldOrder.BottomFirst))
            throw new InvalidOperationException("FIELD RECONSTRUCTION NOT ACCEPTED: field order must be Top first or Bottom first.");
    }

    private readonly record struct SourceFormat(int Width, int Height, int Stride, VideoPixelFormat PixelFormat, bool InterlaceAttributePresent, int? RawInterlaceMode);
}
