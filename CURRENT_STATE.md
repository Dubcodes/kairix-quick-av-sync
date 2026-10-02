# Current state

Updated: 2026-10-02

## Verified

- `Kairix.QuickAVSync.Core` targets plain `net10.0`, builds independently, has no Windows Desktop reference, P/Invoke, COM, WPF, or Windows-native types, and is guarded by an assembly-reference test plus a source leakage audit.
- The Windows WPF application and all three production projects build in Release with zero compiler warnings.
- 155 portable Core tests pass. In addition to the original coverage, they validate exact timestamp-derived 23.976/24/25/29.97/30/50/59.94/60 timelines, independent primary/arrival cadence, duplicate/backwards/gap faults, a strict 25-sample 50p review window, and repeated-content retention.
- 48 Windows tests pass, including deterministic HardwareProbe parsing/device resolution, strict native-mode selection and post-negotiation comparison, Windows SDK COM regression checks, source-aware Auto behavior, settings, RAM, colour/readiness, and real device enumeration.
- The synthetic backend implements the same `ICaptureBackend`/`ICaptureSession` contracts as Windows, generates a known +60 ms video offset, and drives the existing live/review UI.
- The compact RAM bar now draws three truthful partitions: other system use, Kairix process use, and available physical memory. A non-zero subpixel Kairix share receives a one-pixel marker at its true boundary. WPF startup explicitly creates, assigns, shows, and activates a normally resizable maximized `MainWindow` before device/capture initialization. Both published variants pass `scripts/smoke-test-windows.ps1`, which verifies a live process, non-zero main-window handle, visible maximized top-level window, title, and clean close.
- Event review now uses a fixed reference/window with independent Audio mark, Auto visual candidate, Manual visual mark, and video playhead. Scrubbing produces a live Manual Preview result; Enter commits Manual Result. Audio edits update the result live without rebuilding or clearing the event, and each event can enter session history only once.
- Existing product behavior remains: bounded rolling buffers, automatic transient/visual analysis, current-event waveform, fixed auto-candidate thumbnail, manual overrides, a non-persistent Hold latch, keyboard/mouse timeline controls, RAM status, settings, three-result session history, reconnect, refresh, and privacy boundaries.
- Preview dispatch is latest-frame coalesced. Waveform/work-window construction and visual analysis run off the WPF dispatcher. Analysis generations reject stale results after event supersession.

## Physical XI100DUSB-HDMI observations

- The original open failure was caused by an incorrect hand-written `IMFMediaType` IID (`45BC8A7B-AC88-46D8-9A1C-125B799B2A38` instead of `44AE0FA8-EA31-4109-8D2E-4CAE4997C555`). Before the correction, `GetNativeMediaType` failed with `E_NOINTERFACE (0x80004002)` after source activation and Source Reader creation had succeeded.
- The subsequent zero-buffer diagnosis was also caused by handwritten COM interop: `IMFSample` omitted `SetSampleFlags` and had getter/setter methods in the wrong order, shifting all buffer calls onto incorrect vtable slots. The active interfaces were checked against the installed Windows SDK declarations and regression tests now lock the critical IIDs and method order.
- On the connected `XI100DUSB-HDMI Video`, Media Foundation startup, source activation, and Source Reader creation now return `S_OK`. The device exposes native progressive YUY2 formats including 1920×1080 at 60/1, 60000/1001, 50/1, 30/1, 30000/1001, 25/1, and 15/1, plus lower resolutions/rates.
- Source-driven negotiation discovered the Source Reader's current/default mode and selected native index 0, 1920×1080 at 60/1 progressive YUY2 with stride 3840. `SetCurrentMediaType` returned `S_OK`.
- The exact-Container-ID audio endpoint `Digital Audio Interface (XI100DUSB-HDMI Audio)` opened successfully and delivered 48 kHz stereo float32 blocks with WASAPI/QPC timestamps.
- Owner desktop testing verified manual 1920×1080 50p YUY2 capture, colour presentation, paired 48 kHz audio, and DEVICE/QPC timestamps. Hold also behaved correctly as a toggle.
- Physical validation delivered real 4,147,200-byte video buffers and 65 analysis frames in 1.13 seconds, averaging 16.666 ms by device timestamp. Three consecutive payload-bearing frames are required before Media Foundation is accepted. Video and audio both used the `windows-qpc-100ns` domain with device/platform capture timing, and the resulting state was `READY TO CLAP`.

## Physical Logitech C920 observations

- The independently different UVC webcam paired to `Microphone (HD Pro Webcam C920)` by exact Container ID.
- Before the format-selector pass, its Source Reader current/default mode was native index 0, 640×480 at 30/1 progressive YUY2. The device had also previously delivered payload at a native 1920×1080 30/1 NV12 mode. Auto now ranks that quality class ahead of the low-resolution current default; the policy is covered by deterministic native-mode tests.
- Owner desktop testing verified colour presentation at 1920×1080 30p NV12 on the C920.
- Video supplied device/QPC timestamps in the same domain as WASAPI audio and passed the three-consecutive-frame acceptance rule. Observed timestamps averaged 66.668 ms during this run despite the declared 30 fps mode, which may reflect camera exposure/cadence behavior and is reported rather than hidden.

## Physical USB Capture SDI interlace observations

- With the camera configured for 1080i50, the connected `USB Capture SDI` driver reported the Media Foundation interlace attribute on all 133 native modes, always with raw value `2` (progressive). Its current/default mode was 1920×1080 60/1 YUY2, and all seven 1080 modes—60/1, 60000/1001, 50/1, 30/1, 30000/1001, 25/1, and 15/1—were likewise reported progressive.
- An explicit 1920×1080 50/1 YUY2 open succeeded. The final negotiated media type still reported raw interlace value `2`, delivered real 640×360 luma frames at an average 20 ms timestamp interval, paired to `SDI (USB Capture SDI)` at 48 kHz float32, and reached ready state.
- The card/driver therefore appears to expose a progressive/deinterlaced output rather than field-bearing interlaced samples. Kairix truthfully displays this as `50p`; no field extraction is performed because the negotiated hardware metadata does not report an interlaced layout.
- A new blind seven-second generic analysis at the default 1920×1080 60p output measured 60.015 delivered frames/s, 16.663 ms median interval, and 0.19% jitter across 427 frames. The scene had insufficient temporal activity (100% near-duplicate intervals), so the source estimate correctly remained Unknown/EstimatedLow with scan unknown. Only after recording that result was it compared with the operator-known 1080i50 camera setting. The run demonstrates the intentional ambiguity boundary: passive analysis cannot recover source cadence or original scan from a static, already-converted output.
- Final alpha hardware enumeration retained all 133 supported USB Capture SDI modes while the normal catalog classified 30 as Recommended or conventional SD. The normal set keeps common 1080/720/SD rates and excludes 15 fps; Show All retains those low-rate and PC-oriented modes.
- A blind backend negotiation using Auto plus the user-declared `1920x1080-50i` source selected native index 2, 1920×1080 50p YUY2, reached Ready to clap, delivered 54 frames at 20 ms average intervals, and paired 48 kHz audio. Repeating with explicit 1920×1080 60p plus the same declaration selected index 0 at 60p and 16.667 ms, proving the operator's capture selection overrides source-aware Auto.
- The measurement-integrity hotfix physically resolved canonical `--device "USB Capture SDI"` to that exact stable USB device and strictly verified `1920x1080|50/1|p|Yuy2` after negotiation. Across 354 analyzed samples, DeviceTimestamp, sample time, and Source Reader time each measured 50.002 fps with a 19.999 ms median; monotonic arrival measured 50.015 fps. Duplicate timestamps, backwards timestamps, and large gaps were all zero. The exact review builder retained 25 real positions in a 500 ms window at a 20.000 ms median spacing. The static scene produced 353 near-identical consecutive images at distinct valid timestamps, so no upstream repeat-source conclusion is claimed.
- During this pass the XI100DUSB and C920 were not enumerated; only the USB Capture SDI and virtual video devices were available, so no new physical results are claimed for those devices.

## Implemented

- Media Foundation video devices are enumerated into platform-neutral descriptors with stable symbolic-link identity, friendly name, Windows transport metadata, classification, and Container ID lookup.
- `DEVPKEY_Device_ContainerId` lookup uses Config Manager first and a SetupAPI device-interface fallback.
- Active/disabled/unplugged Windows capture audio endpoints are enumerated through MMDevice, including endpoint ID, friendly name, default-microphone state, active state, and Container ID.
- Pairing is exact Container ID first, then hardware parent when available, then a cautious unique name match. The Windows default microphone is excluded from fallback pairing.
- Selected video devices are activated through Media Foundation and opened through `MFCreateSourceReaderFromMediaSource`/`IMFSourceReader` on a background worker.
- The capture-format selector presents Auto plus deduplicated directly supported native modes for the selected Windows device. It persists a mode key per device, reconnects immediately after a manual change, and clears stale media/results before reopening. A saved mode that is absent from a newly enumerated catalog returns to Auto; a currently selected explicit mode is strict and reports failure rather than silently opening another mode.
- The normal capture-format list filters to common 23.976–60 fps HD/UHD/2K rasters and conventional 720×480/576 SD. **Show all formats** exposes the complete supported driver catalog without reconnecting, and a valid saved advanced mode remains present while filtering is active.
- Auto native negotiation ranks supported progressive modes by sensible native resolution, then native rational frame rate and pixel support; the Source Reader default is only a final tie-breaker. It attempts candidates until one is accepted and preserves rational rate, interlace presence/raw value, full-frame/single-field layout, and field order metadata. Missing or unrecognized scan metadata remains unknown instead of defaulting to progressive. Direct extraction supports NV12, YUY2, UYVY, RGB32, and RGB24.
- Auto now uses an exact supported source-aware preference when the physical input is user-declared or otherwise authoritative (for example 1080i50 prefers progressive 1080p50 output). A manually selected capture format always overrides it. Estimated and unknown source states preserve generic ranking and never cause reconnect loops.
- Video samples use `IMFSample::GetSampleTime`; `MFSampleExtension_DeviceTimestamp` is preferred when supplied. The latter is represented in the shared Windows QPC/MFTIME 100 ns domain.
- Native video buffers are sampled directly while locked into bounded 640×360 luma analysis and 160×90 BGRA presentation images; full 1080 frame arrays are not retained in Core. At the default five-second/60-frame-per-second capacity this uses about 69 MB of luma storage plus about 17 MB of BGRA presentation storage.
- The matched endpoint is opened through shared-mode WASAPI. Float32 and 16-bit PCM mix formats are normalized to floats and delivered with `IAudioCaptureClient::GetBuffer` QPC timestamps. Discontinuity and timestamp-error flags are logged.
- Device-timestamped video and WASAPI audio use the common `windows-qpc-100ns` domain. If video has only stream-relative time, Core refuses to label the streams comparable instead of returning false millisecond precision.
- Reconnect disposes event subscriptions, cancels video/audio workers, flushes the source reader, and opens a new session.
- A hardware session remains in `WAITING FOR FIRST VIDEO FRAME` until three consecutive payload-bearing frames arrive. Normal validation is bounded to three seconds. Audio open/failure is independent, and `READY TO CLAP` requires validated video, live paired audio, and comparable device/correlated timing.
- Reconnect/open clears the prior preview, format, waveform, thumbnail, marks, review state, and current result before touching the new source, preventing stale synthetic media from being presented as hardware output.
- `tools/Kairix.QuickAVSync.HardwareProbe` performs physical validation without saving media. Canonical syntax uses exact `--device`/`--device-id`, strict `--mode`, optional `--source`, `--analyze-signal`, and bounded `--timing-detail`; malformed or ambiguous requests fail rather than selecting another device or mode.
- `--analyze-signal` captures for a bounded period and reports declared versus observed output cadence, estimated unique source cadence, repeat pattern, interlace evidence, authority, and provenance without saving media.

## Generic source identification

- Core owns `InputSignalInfo`, provenance/authority/lock models, `IInputSignalProvider`, explicit provider precedence, source-aware supported-format recommendation, UI formatting, and passive `ObservedSignalAnalyzer` logic.
- Windows provides a non-fabricating standard-metadata provider. Official Media Foundation and DirectShow format APIs describe capture output; Kernel Streaming is an extensible property mechanism rather than a universal connector-status contract; UVC extension units are vendor-private. No universal documented Windows API was found that reports connector lock, source size/cadence, and scan state across ordinary devices.
- Passive analysis runs off the UI/capture thread over bounded luma frames. It measures timestamp interval/jitter, robust luma change above a sensor-noise floor, periodic fresh/repeat patterns, and alternating odd/even row temporal disagreement. It estimates cadence only; scan remains unknown unless strong alternating parity evidence exists, and even then remains estimated.
- The per-device Physical Input selector is explicitly user-declared, can return to Auto/Detect, and never reconnects or changes capture. Observed estimates may produce a manual capture recommendation but are not automatically applied. Future vendor providers remain optional Windows-side enhancements; no Magewell or other proprietary runtime is installed or required.

## Not yet implemented

- MJPEG/H.264 capture formats and Media Foundation decoder/converter negotiation; current direct native capture supports NV12, YUY2, UYVY, RGB32, and RGB24 only.
- Field extraction/bob display for a device that actually negotiates full-frame interlaced samples. The metadata/cadence model distinguishes progressive, unknown, full-frame interlaced, and single-field interlaced media, but currently connected hardware exposes progressive output.
- A long-running clock drift estimator. Current logic establishes explicit common domains, normalizes observations, detects jumps/backwards clocks, and degrades on uncertainty.
- Dedicated Auto Spike post-trigger refinement beyond the adaptive detector's short-window timestamp.
- Automatic capture recovery after device loss; the inline failure state and manual Reconnect path exist.

## Known limitations and assumptions

- Some drivers expose only compressed formats, omit device timestamps, misreport stride/interlace metadata, or block synchronous source-reader reads during unplug. Those cases need additional real-device observation.
- Container ID may be absent from either the video interface or endpoint property store. Name fallback deliberately prefers no audio over an unrelated microphone.
- WASAPI uses the endpoint's shared-mode mix format, which might apply Windows audio processing. The timing remains endpoint QPC-based, but format/processing behavior needs capture-card testing.
- The Media Foundation implementation intentionally keeps only downscaled luma in the rolling buffer. A future presentation path may retain a separate bounded native preview surface without changing Core.
- Video rolling-buffer capacity uses the negotiated temporal rate with a deliberate 60-sample/s ceiling to preserve the established RAM bound; captured timestamps, not capacity, define review positions. Rates above 60 therefore retain a shorter history at very small buffer settings. Audio packets use a separate conservative 200-packet/s bound.
- Manual 1080p50 YUY2, YUY2/NV12 colour, and Hold were physically verified. The new fixed-reference timeline interaction and revised detector still require owner desktop retesting before release.
- Visual detection uses separate coarse/global rejection and 32×18 local-cell evidence over bounded 640×360 luma (the historical 16×9 path remains for smaller inputs). Fine cells combine top-percentile change, changed-pixel fraction, adjacent-cell support, and global mean; temporal scoring prefers a measured rise/peak/post-peak-drop contact pattern. Sensitivity changes only score/evidence acceptance thresholds; confidence remains evidence-derived, and high sensitivity still rejects flashes, whole-frame motion, and sparse noise. The representative ten-frame 640×360 test completes in about 32 ms on this machine. Physical distant-clap tuning is not claimed solved yet.
- The displayed capture format is the negotiated Media Foundation output mode. Physical input/source is shown separately with provenance. Conversion inside a device can destroy evidence needed to recover original dimensions or scan, while static/noisy/low-motion scenes can prevent cadence estimation; Unknown is the correct result in those cases.
- The smoke script verifies a top-level window through Windows process/window APIs; it does not inspect the rendered visual content.

## Next hardware test checklist

1. Plug in a USB HDMI/SDI capture card carrying embedded HDMI audio.
2. Launch the app and confirm the external card ranks above the integrated webcam.
3. Inspect `%LOCALAPPDATA%\Kairix\QuickAVSync\logs\current.log` for video ID, Container ID, endpoint list, and pairing confidence.
4. Confirm the paired endpoint belongs to the card and the laptop microphone was not chosen.
5. Reconnect and confirm the negotiated resolution, rational frame rate, pixel format, scan mode, and field order.
6. Confirm live preview appears and remains low-latency.
7. Inspect whether timing reports device/QPC, stream-only, or degraded arrival timing.
8. Clap at close and normal working distances and confirm PCM transient detection and a plausible visual candidate; note confidence and whether contact, rather than final approach, is selected.
9. Confirm the automatic visual thumbnail remains fixed while scrubbing and that Current Result becomes Manual Preview.
10. Drag Audio through the event window and confirm the waveform, frames, Auto candidate, thumbnail, and playhead remain visible while the result updates live.
11. Step to true contact, press Enter, confirm Manual Result, then click the Auto thumbnail and confirm the committed manual result is retained.
12. Repeat several claps and Resume Live transitions; confirm one history item per real event and only the newest three remain.
13. Hold H across a clap and confirm capture continues without a new event.
14. Unplug/replug, use Refresh Devices, then use Reconnect; check for duplicate callbacks or stale frames.
15. Test driver stall/reconnect and camera privacy denied behavior; Resume Live must not claim READY after a non-ready capture state.
16. Test 1080p25 and 1080p50.
17. Test 1080i50 metadata on a driver that reports an interlaced raw mode; confirm full-frame versus single-field layout and field order before enabling any field extraction.
18. Compare reported delay with a known external delay/reference before treating the measurement as calibrated.

## Next priorities

1. Run the checklist with at least two capture-card chipsets and retain metadata-only diagnostic logs.
2. Add compressed-format conversion and harden source-reader cancellation/device-loss recovery.
3. Validate a genuinely interlaced Media Foundation device, then implement top/bottom field extraction and bob review for its reported layout.
4. Add clock-drift observation across longer runs and confidence transitions.
5. Tune audio and visual detection from real-room fixtures without committing private media.
