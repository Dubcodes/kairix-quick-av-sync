# Current state

Updated: 2026-09-29

## Verified

- `Kairix.QuickAVSync.Core` targets plain `net10.0`, builds independently, has no Windows Desktop reference, P/Invoke, COM, WPF, or Windows-native types, and is guarded by an assembly-reference test plus a source leakage audit.
- The Windows WPF application and all three production projects build in Release with zero compiler warnings.
- 44 portable Core tests pass. They cover the original buffer/timing/detection/history behavior plus device ranking, physical pairing policy, clock correlation/discontinuities, unrelated-domain rejection, supersession, waveform building, and full synthetic audio-leads/audio-lags measurements.
- 6 Windows tests pass, including deterministic native-format ranking/fallback coverage plus real MMDevice audio endpoint enumeration and real Media Foundation video device enumeration on this computer.
- The synthetic backend implements the same `ICaptureBackend`/`ICaptureSession` contracts as Windows, generates a known +60 ms video offset, and drives the existing live/review UI.
- The published no-window startup failure was traced to the RAM `ProgressBar`'s default two-way binding against read-only `SystemFraction`. It is explicitly one-way now. WPF startup also explicitly creates, assigns, shows, and activates `MainWindow` before device/capture initialization. Both published variants pass `scripts/smoke-test-windows.ps1`, which verifies a live process, non-zero main-window handle, visible top-level window, title, and clean close.
- Existing product behavior remains: bounded rolling buffers, automatic transient/visual analysis, current-event waveform, independent automatic/effective/playhead markers, fixed auto-candidate thumbnail, manual overrides, Hold, keyboard controls, RAM status, settings, three-result session history, reconnect, refresh, and privacy boundaries.
- Preview dispatch is latest-frame coalesced. Waveform/work-window construction and visual analysis run off the WPF dispatcher. Analysis generations reject stale results after event supersession.

## Physical XI100DUSB-HDMI observations

- The original open failure was caused by an incorrect hand-written `IMFMediaType` IID (`45BC8A7B-AC88-46D8-9A1C-125B799B2A38` instead of `44AE0FA8-EA31-4109-8D2E-4CAE4997C555`). Before the correction, `GetNativeMediaType` failed with `E_NOINTERFACE (0x80004002)` after source activation and Source Reader creation had succeeded.
- On the connected `XI100DUSB-HDMI Video`, Media Foundation startup, source activation, and Source Reader creation now return `S_OK`. The device exposes native progressive YUY2 formats including 1920×1080 at 60/1, 60000/1001, 50/1, 30/1, 30000/1001, 25/1, and 15/1, plus lower resolutions/rates.
- Negotiation selected native index 2, 1920×1080 at 50/1 progressive YUY2 with stride 3840, and `SetCurrentMediaType` returned `S_OK`.
- The exact-Container-ID audio endpoint `Digital Audio Interface (XI100DUSB-HDMI Audio)` opened successfully and delivered 48 kHz stereo float32 blocks with WASAPI/QPC timestamps.
- The physical video run did not produce a payload-bearing video sample. It produced 399 device-timestamped, zero-buffer samples over the eight-second first-frame window. The previous path passed such samples to `ConvertToContiguousBuffer`, which failed with `E_INVALIDARG (0x80070057)`; bufferless samples are now skipped and logged in bounded form. Because the API evidence does not distinguish missing HDMI signal from a driver-specific payload condition, no `NO SIGNAL` claim is made and the card is not yet claimed as video-supported.

## Implemented

- Media Foundation video devices are enumerated into platform-neutral descriptors with stable symbolic-link identity, friendly name, Windows transport metadata, classification, and Container ID lookup.
- `DEVPKEY_Device_ContainerId` lookup uses Config Manager first and a SetupAPI device-interface fallback.
- Active/disabled/unplugged Windows capture audio endpoints are enumerated through MMDevice, including endpoint ID, friendly name, default-microphone state, active state, and Container ID.
- Pairing is exact Container ID first, then hardware parent when available, then a cautious unique name match. The Windows default microphone is excluded from fallback pairing.
- Selected video devices are activated through Media Foundation and opened through `MFCreateSourceReaderFromMediaSource`/`IMFSourceReader` on a background worker.
- Native format negotiation ranks and attempts every supported candidate until one is accepted, with per-attempt HRESULT diagnostics. Direct native extraction supports NV12, YUY2, UYVY, RGB32, and RGB24 while preserving rational rate, interlace mode, and field order metadata.
- Video samples use `IMFSample::GetSampleTime`; `MFSampleExtension_DeviceTimestamp` is preferred when supplied. The latter is represented in the shared Windows QPC/MFTIME 100 ns domain.
- Native video buffers are sampled directly while locked into a bounded 320×180 luma image; full 1080 frame arrays are not retained in Core.
- The matched endpoint is opened through shared-mode WASAPI. Float32 and 16-bit PCM mix formats are normalized to floats and delivered with `IAudioCaptureClient::GetBuffer` QPC timestamps. Discontinuity and timestamp-error flags are logged.
- Device-timestamped video and WASAPI audio use the common `windows-qpc-100ns` domain. If video has only stream-relative time, Core refuses to label the streams comparable instead of returning false millisecond precision.
- Reconnect disposes event subscriptions, cancels video/audio workers, flushes the source reader, and opens a new session.
- A hardware session remains in `WAITING FOR FIRST VIDEO FRAME` until pixel data is received. An eight-second payload timeout fails opening rather than presenting a false live state. Audio open/failure is independent: live video can continue with an embedded-audio-not-found/unavailable status.
- Reconnect/open clears the prior preview, format, waveform, thumbnail, marks, review state, and current result before touching the new source, preventing stale synthetic media from being presented as hardware output.

## Not yet implemented

- MJPEG/H.264 capture formats and Media Foundation decoder/converter negotiation; current direct native capture supports NV12, YUY2, UYVY, RGB32, and RGB24 only.
- Field extraction/bob display and field-by-field samples for interlaced input. Interlace metadata and cadence models exist, but each source-reader sample currently produces one luma image.
- A long-running clock drift estimator. Current logic establishes explicit common domains, normalizes observations, detects jumps/backwards clocks, and degrades on uncertainty.
- Dedicated Auto Spike post-trigger refinement beyond the adaptive detector's short-window timestamp.
- Automatic capture recovery after device loss; the inline failure state and manual Reconnect path exist.
- Three separately proportioned visual segments in the compact RAM bar (numeric System/Kairix/Available values are real).

## Known limitations and assumptions

- Some drivers expose only compressed formats, omit device timestamps, misreport stride/interlace metadata, return timestamp-only samples, or block synchronous source-reader reads during unplug. Those cases need additional real-device observation.
- Container ID may be absent from either the video interface or endpoint property store. Name fallback deliberately prefers no audio over an unrelated microphone.
- WASAPI uses the endpoint's shared-mode mix format, which might apply Windows audio processing. The timing remains endpoint QPC-based, but format/processing behavior needs capture-card testing.
- The Media Foundation implementation intentionally keeps only downscaled luma in the rolling buffer. A future presentation path may retain a separate bounded native preview surface without changing Core.
- Visual detection remains a lightweight global sparse-luma motion heuristic. It is fast and overridable but is not robust to every background or hand pose.
- The smoke script verifies a top-level window through Windows process/window APIs; it does not inspect the rendered visual content.

## Next hardware test checklist

1. Plug in a USB HDMI/SDI capture card carrying embedded HDMI audio.
2. Launch the app and confirm the external card ranks above the integrated webcam.
3. Inspect `%LOCALAPPDATA%\Kairix\QuickAVSync\logs\current.log` for video ID, Container ID, endpoint list, and pairing confidence.
4. Confirm the paired endpoint belongs to the card and the laptop microphone was not chosen.
5. Reconnect and confirm the negotiated resolution, rational frame rate, pixel format, scan mode, and field order.
6. Confirm live preview appears and remains low-latency.
7. Inspect whether timing reports device/QPC, stream-only, or degraded arrival timing.
8. Clap and confirm PCM transient detection and immediate review transition.
9. Confirm the automatic visual thumbnail remains fixed while scrubbing.
10. Step to the true contact image/field, press Enter, and compare the manual result.
11. Repeat several claps and confirm only the newest three numeric results remain.
12. Hold H across a clap and confirm capture continues without a new event.
13. Unplug/replug, use Refresh Devices, then use Reconnect; check for duplicate callbacks or stale frames.
14. Test driver stall/reconnect and camera privacy denied behavior.
15. Test 1080p25 and 1080p50.
16. Test 1080i50 metadata; field-accurate review is expected to remain incomplete.
17. Compare reported delay with a known external delay/reference before treating the measurement as calibrated.

## Next priorities

1. Run the checklist with at least two capture-card chipsets and retain metadata-only diagnostic logs.
2. Add compressed-format conversion and harden source-reader cancellation/device-loss recovery.
3. Implement top/bottom field extraction and bob review for 1080i50.
4. Add clock-drift observation across longer runs and confidence transitions.
5. Tune audio and visual detection from real-room fixtures without committing private media.
