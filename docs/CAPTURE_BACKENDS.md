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

The format catalog retains every directly supported native mode. The normal operator list shows common 720p/1080p/2K/UHD rates from 23.976 through 60 plus conventional 720×480/576 SD; uncommon computer rasters and low rates remain available under **Show all formats**. A saved advanced selection remains visible even with Show All off. When capture is Auto, authoritative or user-declared source metadata can move an exact supported temporal/resolution match to the front of negotiation; explicit capture-mode selection always wins, and estimated/unknown source metadata leaves generic ranking unchanged.

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
