using System.Diagnostics;
using System.Runtime.InteropServices;
using Kairix.QuickAVSync.Capture;
using Kairix.QuickAVSync.Models;

namespace Kairix.QuickAVSync.Windows.Capture;

internal sealed class WasapiCaptureWorker(AudioEndpointDescriptor endpoint, IDiagnosticSink log) : IAsyncDisposable
{
    private CancellationTokenSource? _cts; private Task? _task;
    public event EventHandler<AudioChunk>? AudioSampleReceived;
    public event EventHandler<CaptureStatusChangedEventArgs>? StatusChanged;
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_task is not null) return Task.CompletedTask; _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); _task = Task.Run(() => Run(_cts.Token), _cts.Token); return Task.CompletedTask;
    }
    private void Run(CancellationToken cancellationToken)
    {
        IMMDeviceEnumerator? enumerator = null; IMMDevice? device = null; IAudioClient? audioClient = null; IAudioCaptureClient? captureClient = null; IntPtr mixFormat = IntPtr.Zero;
        MediaFoundationNative.CoInitializeEx(IntPtr.Zero, 0);
        try
        {
            enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"), true)!)!;
            enumerator.GetDevice(endpoint.Id, out device).ThrowIfFailed(); var iid = new Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"); device.Activate(ref iid, 23, IntPtr.Zero, out var clientObject).ThrowIfFailed(); audioClient = (IAudioClient)clientObject;
            audioClient.GetMixFormat(out mixFormat).ThrowIfFailed(); var format = Marshal.PtrToStructure<WaveFormatEx>(mixFormat); var encoding = DetermineEncoding(mixFormat, format);
            if (encoding == AudioEncoding.Unsupported) throw new NotSupportedException($"Unsupported WASAPI mix format tag={format.FormatTag}, bits={format.BitsPerSample}");
            audioClient.Initialize(0, 0x00080000, 10_000_000, 0, mixFormat, IntPtr.Zero).ThrowIfFailed();
            var captureIid = new Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317"); audioClient.GetService(ref captureIid, out var captureObject).ThrowIfFailed(); captureClient = (IAudioCaptureClient)captureObject;
            audioClient.Start().ThrowIfFailed(); StatusChanged?.Invoke(this, new(CaptureStatus.Running, $"Paired audio running: {endpoint.FriendlyName}"));
            log.Write("capture.audio", $"endpoint='{endpoint.FriendlyName}' rate={format.SamplesPerSec} channels={format.Channels} bits={format.BitsPerSample} encoding={encoding} timestamp=IAudioCaptureClient.QPC");
            while (!cancellationToken.IsCancellationRequested)
            {
                captureClient.GetNextPacketSize(out var packetFrames).ThrowIfFailed();
                if (packetFrames == 0) { cancellationToken.WaitHandle.WaitOne(3); continue; }
                while (packetFrames > 0)
                {
                    captureClient.GetBuffer(out var data, out var frames, out var flags, out var devicePosition, out var qpc100ns).ThrowIfFailed();
                    try
                    {
                        var samples = ConvertSamples(data, frames, format, encoding, (flags & 2) != 0);
                        var timingError = (flags & 4) != 0; var timestamp = timingError
                            ? new MediaTimestamp(To100ns(Stopwatch.GetTimestamp()), TimingQuality.ArrivalFallback, "windows-qpc-100ns")
                            : new MediaTimestamp((long)qpc100ns, TimingQuality.PlatformCaptureClock, "windows-qpc-100ns", (long)devicePosition);
                        if ((flags & 1) != 0) log.Write("capture.audio", $"Data discontinuity at device frame {devicePosition}");
                        if (timingError) log.Write("capture.audio", "WASAPI timestamp error; using degraded arrival QPC");
                        AudioSampleReceived?.Invoke(this, new(timestamp, samples, (int)format.SamplesPerSec, format.Channels, encoding == AudioEncoding.Float32 ? AudioSampleFormat.Float32 : AudioSampleFormat.SignedPcm16));
                    }
                    finally { captureClient.ReleaseBuffer(frames).ThrowIfFailed(); }
                    captureClient.GetNextPacketSize(out packetFrames).ThrowIfFailed();
                }
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested) { log.Write("capture.audio", $"failure endpoint='{endpoint.FriendlyName}' error='{ex.Message}'"); StatusChanged?.Invoke(this, new(CaptureStatus.Failed, $"Paired audio failed: {ex.Message}", ex)); }
        finally
        {
            try { audioClient?.Stop(); } catch { }
            if (mixFormat != IntPtr.Zero) Marshal.FreeCoTaskMem(mixFormat); if (captureClient is not null) Marshal.ReleaseComObject(captureClient); if (audioClient is not null) Marshal.ReleaseComObject(audioClient); if (device is not null) Marshal.ReleaseComObject(device); if (enumerator is not null) Marshal.ReleaseComObject(enumerator); MediaFoundationNative.CoUninitialize();
        }
    }
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_cts is null) return; _cts.Cancel(); try { if (_task is not null) await _task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false); } catch (OperationCanceledException) { } catch (TimeoutException) { log.Write("capture.audio", "Audio worker did not exit within two seconds"); }
        _task = null; _cts.Dispose(); _cts = null;
    }
    public async ValueTask DisposeAsync() => await StopAsync(CancellationToken.None).ConfigureAwait(false);
    private static long To100ns(long stopwatchTicks) => (long)(stopwatchTicks * (double)TimeSpan.TicksPerSecond / Stopwatch.Frequency);
    private static AudioEncoding DetermineEncoding(IntPtr formatPtr, WaveFormatEx format)
    {
        if (format.FormatTag == 3 && format.BitsPerSample == 32) return AudioEncoding.Float32;
        if (format.FormatTag == 1 && format.BitsPerSample == 16) return AudioEncoding.Pcm16;
        if (format.FormatTag == 0xFFFE && format.ExtraSize >= 22)
        {
            var subFormat = Marshal.PtrToStructure<Guid>(formatPtr + 24); if (subFormat == new Guid("00000003-0000-0010-8000-00AA00389B71")) return AudioEncoding.Float32; if (subFormat == new Guid("00000001-0000-0010-8000-00AA00389B71") && format.BitsPerSample == 16) return AudioEncoding.Pcm16;
        }
        return AudioEncoding.Unsupported;
    }
    private static float[] ConvertSamples(IntPtr data, uint frames, WaveFormatEx format, AudioEncoding encoding, bool silent)
    {
        var count = checked((int)frames * format.Channels); var output = new float[count]; if (silent || data == IntPtr.Zero) return output;
        if (encoding == AudioEncoding.Float32) Marshal.Copy(data, output, 0, count);
        else { var input = new short[count]; Marshal.Copy(data, input, 0, count); for (var i = 0; i < count; i++) output[i] = input[i] / 32768f; }
        return output;
    }
    private enum AudioEncoding { Unsupported, Float32, Pcm16 }
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
internal struct WaveFormatEx { public ushort FormatTag; public ushort Channels; public uint SamplesPerSec; public uint AvgBytesPerSec; public ushort BlockAlign; public ushort BitsPerSample; public ushort ExtraSize; }

[ComImport, Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioClient
{
    [PreserveSig] int Initialize(int shareMode, int streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr sessionGuid); [PreserveSig] int GetBufferSize(out uint frames); [PreserveSig] int GetStreamLatency(out long latency); [PreserveSig] int GetCurrentPadding(out uint padding); [PreserveSig] int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closestMatch); [PreserveSig] int GetMixFormat(out IntPtr format); [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod); [PreserveSig] int Start(); [PreserveSig] int Stop(); [PreserveSig] int Reset(); [PreserveSig] int SetEventHandle(IntPtr eventHandle); [PreserveSig] int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
}
[ComImport, Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioCaptureClient
{
    [PreserveSig] int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpcPosition); [PreserveSig] int ReleaseBuffer(uint frames); [PreserveSig] int GetNextPacketSize(out uint frames);
}
