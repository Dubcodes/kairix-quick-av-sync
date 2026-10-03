# Current state

Updated: 2026-10-04

## Verified

- `Kairix.QuickAVSync.Core` targets plain `net10.0`, builds independently, has no Windows Desktop reference, P/Invoke, COM, WPF, or Windows-native types, and is guarded by an assembly-reference test plus a source leakage audit.
- The Windows WPF application and all three production projects build in Release with zero compiler warnings.
- 201 portable Core tests pass. Coverage includes native timing metadata at every detection raster, reconnect phase invariants, approach/contact selection, reconstructed/progressive phase equivalence, and one high-base-raster scene downsampled to 320×180, 640×360, 960×540, and 1280×720 with the same contact selected.
- 76 Windows tests pass, including tabbed UI contracts, compact result/history content, review-control order, waveform-style persistence, and calculated 4.5:1 contrast checks for important resource pairs across all seven themes.
- The synthetic backend implements the same `ICaptureBackend`/`ICaptureSession` contracts as Windows, generates a known +60 ms video offset, and drives the existing live/review UI.
- The compact RAM bar now draws three truthful partitions: other system use, Kairix process use, and available physical memory. A non-zero subpixel Kairix share receives a one-pixel marker at its true boundary. WPF startup explicitly creates, assigns, shows, and activates a normally resizable maximized `MainWindow` before device/capture initialization. Both published variants pass `scripts/smoke-test-windows.ps1`, which verifies a live process, non-zero main-window handle, visible maximized top-level window, title, and clean close.
- Event review now uses a fixed reference/window with independent Audio mark, Auto visual candidate, Manual visual mark, and video playhead. Scrubbing produces a live Manual Preview result; Enter commits Manual Result. Audio edits update the result live without rebuilding or clearing the event, and each event can enter session history only once.
- Existing product behavior remains: bounded rolling buffers, automatic transient/visual analysis, current-event waveform, fixed auto-candidate thumbnail, manual overrides, a non-persistent Hold latch, keyboard/mouse timeline controls, RAM status, settings, three-result session history, reconnect, refresh, and privacy boundaries.
- Preview dispatch coalesces `VideoFrame` objects before creating a WPF bitmap, so discarded previews allocate no `BitmapSource`. Passive signal checks reuse rolling-buffer snapshots instead of retaining a second 600-frame full-image buffer. Waveform/work-window construction and visual analysis run off the WPF dispatcher. Analysis generations reject stale results after event supersession.
- The operator UI now keeps tabbed settings on the left, concise input plus ARMED/HOLD state, an uncluttered viewer, result/confidence/history on the right, and ordered Pre A / Pre V / Mark Visual / Nxt V / Nxt A / Resume Live controls. Technical capture and raw/corrected details are under About.
- Each detected event records bounded resolution/timestamp/identity/result/score/approach/contact/runtime/conversion/timestamp-health/audio-continuity diagnostics. Deterministic evidence shows analysis raster does not enter capture, reconstruction, or compensation math; the same synthetic contact is selected at all four tested rasters, and reconnecting the deterministic source at the same or another raster retains phase. The reproduced risk is therefore detector selection shift, not clock drift. No per-resolution offset or arbitrary settling delay was added.

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
- The card/driver exposes progressive transports to Media Foundation. Kairix truthfully displays those modes as progressive. Field reconstruction is applied only when the operator explicitly interprets a supported progressive transport as a woven field pair; it never rewrites the detected capture metadata.
- A new blind seven-second generic analysis at the default 1920×1080 60p output measured 60.015 delivered frames/s, 16.663 ms median interval, and 0.19% jitter across 427 frames. The scene had insufficient temporal activity (100% near-duplicate intervals), so the source estimate correctly remained Unknown/EstimatedLow with scan unknown. Only after recording that result was it compared with the operator-known 1080i50 camera setting. The run demonstrates the intentional ambiguity boundary: passive analysis cannot recover source cadence or original scan from a static, already-converted output.
- Final alpha hardware enumeration retained all 133 supported USB Capture SDI modes while the normal catalog classified 30 as Recommended or conventional SD. The normal set keeps common 1080/720/SD rates and excludes 15 fps; Show All retains those low-rate and PC-oriented modes.
- A strict 1920×1080 25p YUY2 Weave run with `25.000p → 50.000i · Top first` reconstruction kept the authoritative negotiated mode at 25p. Historical output delivered 183 native samples and generated 366 field positions with 20 ms derived review spacing. Top and bottom parity-derived images both showed measured content change. The corrected analyzer now groups device/sample/reader/arrival observations by native sample and must report their factual approximately 25 Hz transport cadence, not the earlier misleading 50 Hz duplication. This proves separate field images and review cadence programmatically, but not the assumed phase relative to hardware exposure.
- A strict comparison 1920×1080 50p YUY2 run measured 50.001 fps and 20 ms median spacing with no duplicate/backwards timestamps or large gaps. Content analysis found 177 near-identical transitions, a stable A-A/B-B pattern, and approximately 25 unique images/sec across 363 retained native/temporal frames. The evidence was reported as repeated pairs rather than a timestamp fault.
- The measurement-integrity hotfix physically resolved canonical `--device "USB Capture SDI"` to that exact stable USB device and strictly verified `1920x1080|50/1|p|Yuy2` after negotiation. Across 354 analyzed samples, DeviceTimestamp, sample time, and Source Reader time each measured 50.002 fps with a 19.999 ms median; monotonic arrival measured 50.015 fps. Duplicate timestamps, backwards timestamps, and large gaps were all zero. The exact review builder retained 25 real positions in a 500 ms window at a 20.000 ms median spacing. The static scene produced 353 near-identical consecutive images at distinct valid timestamps, so no upstream repeat-source conclusion is claimed.
- On 2026-10-03, three strict `1920x1080|25/1|p|Yuy2`, top-first reconstruction runs held review at 160×90 while detection changed. Final conversion diagnostics measured: 320×180, 0.519 ms average / 5.143 ms maximum; 640×360, 1.320 / 5.927 ms; 960×540, 2.424 / 6.762 ms. These are one host/card run, not universal performance claims. Every run independently confirmed 50.000 reconstructed fields/s at 20.000 ms and 25.000 native samples/s at approximately 40.000 ms; device, sample, reader, and arrival observations were each counted once per native sample with no fabricated field-rate transport timestamps.
- During the later capture-delay-compensation validation, `USB Capture SDI` was not enumerated, so no new physical clap result is claimed. The +22.7 ms raw / +20.0 ms automatic / +2.7 ms corrected case is deterministic coverage based on the previously reported observation, not a newly measured run.
- During the UI/contact/resolution pass, `USB Capture SDI` was again absent (only virtual NDI/Animaze video sources enumerated), so no new multi-resolution physical clap statistics are claimed. The prior transport/conversion figures remain historical evidence; the new four-raster contact result is deterministic synthetic evidence.
- During this pass the XI100DUSB and C920 were not enumerated; only the USB Capture SDI and virtual video devices were available, so no new physical results are claimed for those devices.

## Implemented

- Media Foundation video devices are enumerated into platform-neutral descriptors with stable symbolic-link identity, friendly name, Windows transport metadata, classification, and Container ID lookup.
- `DEVPKEY_Device_ContainerId` lookup uses Config Manager first and a SetupAPI device-interface fallback.
- Active/disabled/unplugged Windows capture audio endpoints are enumerated through MMDevice, including endpoint ID, friendly name, default-microphone state, active state, and Container ID.
- Pairing is exact Container ID first, then hardware parent when available, then a cautious unique name match. The Windows default microphone is excluded from fallback pairing.
- Selected video devices are activated through Media Foundation and opened through `MFCreateSourceReaderFromMediaSource`/`IMFSourceReader` on a background worker.
- The UI shows an authoritative read-only **Detected Capture** line and separate **Input Interpretation** controls for actual raster, rate/scan interpretation, optional woven-field reconstruction, and pixel format. The controls resolve to a single exact native Media Foundation mode, persist the native mode plus reconstruction/order per device, reconnect immediately, and never relabel a progressive negotiated transport as interlaced.
- Resolution, transport rate, and pixel-format options are built from the selected device's directly supported native modes. Pixel formats are filtered to the chosen raster/rate combination, and an explicit selection is strict rather than silently opening another mode.
- Auto native negotiation ranks supported progressive modes by sensible native resolution, then native rational frame rate and pixel support; the Source Reader default is only a final tie-breaker. It attempts candidates until one is accepted and preserves rational rate, interlace presence/raw value, full-frame/single-field layout, and field order metadata. Missing or unrecognized scan metadata remains unknown instead of defaulting to progressive. Direct extraction supports NV12, YUY2, UYVY, RGB32, and RGB24.
- Initial unsaved capture may use generic Auto ranking to discover a working mode. Interlaced source estimates do not silently drive capture choice; the operator explicitly selects a supported transport and reconstruction interpretation.
- Video samples use `IMFSample::GetSampleTime`; `MFSampleExtension_DeviceTimestamp` is preferred when supplied. The latter is represented in the shared Windows QPC/MFTIME 100 ns domain.
- Native video buffers are sampled while locked into separately configurable luma-analysis and BGRA-review rasters, defaulting to 640×360 and 160×90; full 1080 frame arrays are not retained unless explicitly selected for detection. NV12/YUY2/UYVY use direct luma reads, RGB24/BGRA32 calculate luma, and reusable conversion plans precompute scaling/parity maps. In reconstruction mode the even/odd native rows are converted independently before scaling and bobbing.
- The detector scales its cell grid and sample density with the selected detection raster: 320×180 performs less work, 640×360 preserves the compatibility baseline, and 960×540 and above inspect proportionally more detail. Its hot-path scratch arrays come from bounded pools instead of per-cell `List<double>`/LINQ allocation.
- The WPF layout is **Settings | Operation | Video | Result**. Settings is collapsible and persistent, contains theme, detection/review rasters, sensitivity, work/rolling windows, pixel format, RAM, image-buffer estimate, and support. Graphite (default), Midnight, Light, and High Contrast switch at runtime through semantic resources also consumed by custom-drawn controls.
- Interactive preference changes use a short debounced settings write; capture choices save immediately and shutdown flushes pending changes. Corrupt or stale settings fall back safely.
- Explicit reconstructed-field profiles automatically compensate measurement timing by one exact target-field interval: 20.000 ms for 50i and 16.6833 ms at 100 ns precision for 60000/1001 fields/s. The whole pair shifts only in the effective measurement timeline; raw reconstructed positions and all capture observations remain factual. Per-profile manual overrides distinguish device, native mode, reconstruction state, field order, and target field rate, and Reset restores the automatic value rather than zero.
- Each woven native sample produces two `VideoFrame` positions tagged TopField/BottomField. The review analyzer, rolling-buffer sizing, manual stepping, and automatic visual detector all use the interpreted field cadence rather than the native sample cadence.
- The matched endpoint is opened through shared-mode WASAPI. Float32 and 16-bit PCM mix formats are normalized to floats and delivered with `IAudioCaptureClient::GetBuffer` QPC timestamps. Discontinuity and timestamp-error flags are logged.
- Device-timestamped video and WASAPI audio use the common `windows-qpc-100ns` domain. If video has only stream-relative time, Core refuses to label the streams comparable instead of returning false millisecond precision.
- Reconnect disposes event subscriptions, cancels video/audio workers, flushes the source reader, and opens a new session.
- A hardware session remains in `WAITING FOR FIRST VIDEO FRAME` until three consecutive payload-bearing frames arrive. Normal validation is bounded to three seconds. Audio open/failure is independent, and `READY TO CLAP` requires validated video, live paired audio, and comparable device/correlated timing.
- Reconnect/open clears the prior preview, format, waveform, thumbnail, marks, review state, and current result before touching the new source, preventing stale synthetic media from being presented as hardware output.
- `tools/Kairix.QuickAVSync.HardwareProbe` performs physical validation without saving media. Canonical syntax also accepts independent `--detection-resolution WxH` and `--review-resolution WxH`; malformed or ambiguous requests fail rather than selecting another device or mode.
- `--analyze-signal` captures for a bounded period and reports declared versus observed output cadence, estimated unique source cadence, repeat pattern, interlace evidence, authority, and provenance without saving media.

## Generic source identification

- Core owns `InputSignalInfo`, provenance/authority/lock models, `IInputSignalProvider`, explicit provider precedence, source-aware supported-format recommendation, UI formatting, and passive `ObservedSignalAnalyzer` logic.
- Windows provides a non-fabricating standard-metadata provider. Official Media Foundation and DirectShow format APIs describe capture output; Kernel Streaming is an extensible property mechanism rather than a universal connector-status contract; UVC extension units are vendor-private. No universal documented Windows API was found that reports connector lock, source size/cadence, and scan state across ordinary devices.
- Passive and frozen-timeline analysis share one bounded content classifier. It separates near-identical images from timestamp faults, requires real scene activity before cadence conclusions, detects stable paired repeats only with healthy regular timestamps, estimates unique-image rate, and never removes distinct-time frames. Static pictures and low-level sensor noise remain insufficient evidence.
- Passive source estimates remain diagnostic only. Strong 50 fps/~25 unique fps A-A/B-B evidence suggests half-rate progressive capture plus field reconstruction, but never changes settings automatically. Future vendor providers remain optional Windows-side enhancements; no proprietary runtime is installed or required.

## Not yet implemented

- MJPEG/H.264 capture formats and Media Foundation decoder/converter negotiation; current direct native capture supports NV12, YUY2, UYVY, RGB32, and RGB24 only.
- Direct reconstruction from natively interlaced Media Foundation layouts; this pass reconstructs an operator-selected woven progressive transport such as 25p to 50i.
- A long-running clock drift estimator. Current logic establishes explicit common domains, normalizes observations, detects jumps/backwards clocks, and degrades on uncertainty.
- Dedicated Auto Spike post-trigger refinement beyond the adaptive detector's short-window timestamp.
- Automatic capture recovery after device loss; the inline failure state and manual Reconnect path exist.

## Known limitations and assumptions

- Some drivers expose only compressed formats, omit device timestamps, misreport stride/interlace metadata, or block synchronous source-reader reads during unplug. Those cases need additional real-device observation.
- Container ID may be absent from either the video interface or endpoint property store. Name fallback deliberately prefers no audio over an unrelated microphone.
- WASAPI uses the endpoint's shared-mode mix format, which might apply Windows audio processing. The timing remains endpoint QPC-based, but format/processing behavior needs capture-card testing.
- The Media Foundation implementation intentionally keeps only bounded downscaled luma and presentation images in the rolling buffer, including reconstructed fields.
- Video rolling-buffer capacity uses the negotiated temporal rate with a deliberate 60-sample/s ceiling to preserve the established RAM bound; captured timestamps, not capacity, define review positions. Rates above 60 therefore retain a shorter history at very small buffer settings. Audio packets use a separate conservative 200-packet/s bound.
- Strict 1080p25 YUY2 reconstruction, strict 1080p50 YUY2 paired-repeat comparison, YUY2/NV12 colour, and Hold were physically verified. Both published windows pass lifecycle smoke tests, but this Codex environment exposed no native application surface for interactive Settings collapse/reopen or runtime-theme visual inspection; those interactions still require owner desktop retesting before release.
- Visual detection uses resolution-scaled local-cell evidence plus coarse/global rejection. Temporal selection now identifies approach evidence and then searches one to three positions for the earliest credible contact/settle; a bounded trace exposes both indices. Sensitivity changes acceptance only, and flashes, camera motion, sparse noise, and slow motion without contact remain rejected. The real-clip observation (approach before contact, including Top-approach/Bottom-contact) is represented by deterministic regression fixtures; physical distant-clap tuning is not claimed solved.
- **Detected Capture** is the negotiated Media Foundation output mode; **Input Interpretation** is separate. Conversion inside a device can destroy evidence needed to recover original dimensions or scan, while static/noisy/low-motion scenes can prevent cadence estimation; Unknown is the correct diagnostic result in those cases.
- Reconstructed timestamps currently assume the captured sample timestamp `T` represents the second field/completed woven pair. The first configured field is assigned `T - exact rational field interval`; the second is assigned `T`. Both preserve timing quality and clock domain, while metadata distinguishes the derived first field from the assumed captured second field. This phase assumption is centralized and has not been physically calibrated.
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
17. With a 1080i50 source and card Weave mode, select 1080p25 YUY2 plus `25.000p → 50.000i`, then visually verify that stepping alternates Top/Bottom and shows motion on each approximately 20 ms position.
18. Repeat for both field orders and establish the actual capture timestamp phase with an external time reference before treating field-level sync as calibrated.
19. Compare reported delay with a known external delay/reference before treating the measurement as calibrated.

## Next priorities

1. Run the checklist with at least two capture-card chipsets and retain metadata-only diagnostic logs.
2. Add compressed-format conversion and harden source-reader cancellation/device-loss recovery.
3. Physically inspect reconstructed Top/Bottom stepping and calibrate the timestamp phase assumption on representative capture cards.
4. Add clock-drift observation across longer runs and confidence transitions.
5. Tune audio and visual detection from real-room fixtures without committing private media.
