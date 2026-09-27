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

Timing order:

1. Media Foundation device timestamp (`MFSampleExtension_DeviceTimestamp`) → `DeviceHardware`, domain `windows-qpc-100ns`.
2. Media Foundation sample time (`IMFSample::GetSampleTime`) → `StreamTimestamp`, a device-specific stream domain.
3. WASAPI `IAudioCaptureClient::GetBuffer` QPC position → `PlatformCaptureClock`, domain `windows-qpc-100ns`.
4. WASAPI timestamp-error fallback → `ArrivalFallback` in QPC time.

Microsoft documents both the Media Foundation device timestamp and WASAPI QPC position as QPC-derived 100 ns values. Core compares them only when the domain labels match. Relevant references: [Media Foundation device timestamp](https://learn.microsoft.com/en-us/windows/win32/medfound/mfsampleextension-devicetimestamp), [Source Reader processing](https://learn.microsoft.com/en-us/windows/win32/medfound/processing-media-data-with-the-source-reader), [WASAPI capture buffer timestamps](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudiocaptureclient-getbuffer), and [Windows device Container IDs](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/devpkey-device-containerid).

## Synthetic backend

The synthetic backend is portable and uses a shared synthetic clock. Its default video event is 60 ms after the audio impulse. It is the CI/development reference for the end-to-end contract and both lead/lag directions are fixture-tested.

## Future macOS backend

AVFoundation is the likely capture implementation. It must translate Core Media timestamps into an explicit monotonic/correlated domain and preserve device/host-time evidence in diagnostics. No implementation exists yet.

## Future Linux backend

PipeWire, V4L2, ALSA, or another combination may be appropriate. Selection is intentionally deferred until timestamp semantics and device association are researched. No implementation exists yet.
