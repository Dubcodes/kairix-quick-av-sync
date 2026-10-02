# Capture backends

## Portable contract

Every platform implements `ICaptureBackend` to enumerate `CaptureDeviceDescriptor` values and open an `ICaptureSession`. Sessions publish safely owned `VideoFrame` and `AudioChunk` values plus status changes. No contract exposes native samples, handles, WPF images, or platform device objects.

A backend must:

- preserve the strongest source timestamps and label their clock domain;
- explicitly report when audio/video domains are not comparable;
- bound retained data and never queue capture indefinitely;
- keep capture independent from UI and analysis;
- support cancellation, clean stop, and disposal;
- publish scan mode, field order, pixel/source format, rate rational, and audio format diagnostics;
- never substitute an arbitrary microphone for a capture card's audio function.

## Windows backend

The current implementation uses Media Foundation for video and MMDevice/WASAPI for the paired capture endpoint. Video device symbolic links are stable backend identities. PnP Container IDs associate separate USB video/audio functions where drivers publish them.

The format catalog retains every directly supported native mode. The UI projects that catalog into separate Resolution, Format, and Pixel format controls and resolves the selection back to one stable native mode ID. Pixel formats are filtered to the selected raster and exact rational transport rate. Initial unsaved capture may use generic Auto ranking; an explicit operator selection always wins.

Explicit capture-format requests are strict: unavailable, rejected, unverifiable, or post-negotiation-mismatched modes fail without trying the next candidate. Auto selection retains ranked fallback behavior.

**Detected Capture** always shows the final negotiated Media Foundation output. **Input Interpretation** may additionally reconstruct woven fields for supported progressive pair-rate transports: 25p to 50i or 30000/1001p to 60000/1001i, with explicit top-first or bottom-first ordering. Kairix does not change card firmware or claim that Media Foundation negotiated the interpreted interlaced rate. Shared content analysis still reports repeated pairs only when regular timestamps, adequate scene activity, and a stable alternating near-identical/changed pattern agree. Distinct timestamped samples remain in the review timeline.

### Woven-field reconstruction

When reconstruction is enabled, each Media Foundation sample is locked once and its native rows are mapped twice before any downscale. Top selects even source rows and Bottom selects odd source rows. The converter supports NV12, YUY2, UYVY, BGRA32, and RGB24; configurable luma analysis and BGRA presentation images are generated independently for each parity. Nearest-line bob fills the full output height without blending fields.

One native sample therefore emits two `VideoFrame` positions with distinct image arrays and field identity but the same native-sample ID and factual transport observations. Only the first review timestamp is derived as `T - field interval`; device/sample/reader/arrival values are never shifted. The target field rate is exactly twice the progressive transport rate. Validation rejects other relationships rather than creating conversions such as 25p to 60i. The session's `CurrentFormat` remains the negotiated progressive transport.

### Processing rasters and conversion diagnostics

Native capture, detector processing, and review presentation are independent. Defaults are 640×360 luma and 160×90 BGRA. Settings are aspect-preserving and never upscale above the selected native raster. Conversion plans precompute X/Y maps for progressive and both field parities, while YUV analysis reads the native luma component directly. Every 250 native samples, and once when a session closes, bounded `capture.conversion` diagnostics report both processing rasters, reconstruction state, average milliseconds, and maximum milliseconds.

The timestamp calculation is centralized in `ReconstructedFieldTimestampModel`. Under the current `CaptureTimestampRepresentsSecondField` assumption, the configured first field is timestamped `T - one exact rational field interval` and the second field is timestamped `T`. Both keep the original timing quality and clock domain. Metadata distinguishes the derived first timestamp from the captured timestamp being treated as the second field. The assumption is diagnostic and replaceable; it has not been physically phase-calibrated.

The Windows backend records DeviceTimestamp, `IMFSample::GetSampleTime`, Source Reader timestamp, and `Stopwatch.GetTimestamp` arrival for each bounded frame. DeviceTimestamp remains primary when present to preserve the WASAPI QPC relationship; the other clocks are diagnostic comparisons and are not silently substituted.

Timing order:

1. Media Foundation device timestamp (`MFSampleExtension_DeviceTimestamp`) → `DeviceHardware`, domain `windows-qpc-100ns`.
2. Media Foundation sample time (`IMFSample::GetSampleTime`) → `StreamTimestamp`, a device-specific stream domain.
3. WASAPI `IAudioCaptureClient::GetBuffer` QPC position → `PlatformCaptureClock`, domain `windows-qpc-100ns`.
4. WASAPI timestamp-error fallback → `ArrivalFallback` in QPC time.

Microsoft documents both the Media Foundation device timestamp and WASAPI QPC position as QPC-derived 100 ns values. Core compares them only when the domain labels match. Relevant references: [Media Foundation device timestamp](https://learn.microsoft.com/en-us/windows/win32/medfound/mfsampleextension-devicetimestamp), [Source Reader processing](https://learn.microsoft.com/en-us/windows/win32/medfound/processing-media-data-with-the-source-reader), [WASAPI capture buffer timestamps](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudiocaptureclient-getbuffer), and [Windows device Container IDs](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/devpkey-device-containerid).

### Generic source-status finding

Media Foundation Source Reader native/current media types and DirectShow [`IAMStreamConfig`](https://learn.microsoft.com/en-us/windows/win32/api/strmif/nn-strmif-iamstreamconfig) describe device-to-application output capabilities, not a universal pre-processing connector signal. [Media Foundation advanced video processing](https://learn.microsoft.com/en-us/windows/win32/medfound/mf-source-reader-enable-advanced-video-processing) may insert deinterlacing, scaling, and frame-rate conversion. [Kernel Streaming properties](https://learn.microsoft.com/en-us/windows-hardware/drivers/stream/ks-properties) define extensible property mechanisms, but not one cross-device connector-status property for source lock/timing/scan. [UVC Extension Units](https://learn.microsoft.com/en-us/windows-hardware/drivers/stream/device-requirements-for-usb-video-class-extension-units) are vendor-defined private controls identified by vendor GUIDs. Kairix therefore does not add DirectShow, probe undocumented extension units, or reinterpret output formats as physical input metadata. The Windows standard provider remains optional/unavailable unless a device exposes documented standard source status.

Future Magewell, DeckLink, AJA, Elgato, or other adapters can implement `IInputSignalProvider` in Windows-side optional modules. Missing SDKs/runtimes must leave Media Foundation/WASAPI capture and passive analysis fully operational.

## Synthetic backend

The synthetic backend is portable and uses a shared synthetic clock. Its default video event is 60 ms after the audio impulse. It is the CI/development reference for the end-to-end contract and both lead/lag directions are fixture-tested.

## Future macOS backend

AVFoundation is the likely capture implementation. It must translate Core Media timestamps into an explicit monotonic/correlated domain and preserve device/host-time evidence in diagnostics. No implementation exists yet.

## Future Linux backend

PipeWire, V4L2, ALSA, or another combination may be appropriate. Selection is intentionally deferred until timestamp semantics and device association are researched. No implementation exists yet.
