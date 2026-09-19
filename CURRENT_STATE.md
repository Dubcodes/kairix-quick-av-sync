# Current state

Updated: 2026-09-20

## Works now

- .NET 10 WPF application builds cleanly for Windows x64 and survives an 8-second launch/synthetic-capture smoke test.
- Deliberately designed, resizable dark UI with settings, large live/review viewer, result panel, current-event timeline, and session-only history.
- Bounded, thread-safe audio/video rolling buffers with live resize, chronological snapshots, and timestamp range queries.
- Capture-free synthetic source supplies 320×180 analysis luma at 50p plus 48 kHz stereo PCM, including a repeatable clap-like audio/visual event.
- Adaptive PCM transient detection uses peak, RMS, noise floor, attack threshold, and a refractory interval. It is independent of the UI thread and supports arbitrary sample rates/channel counts.
- Automatic event snapshot, waveform downsampling, Audio Zero, visual candidate, fixed auto thumbnail, playhead, manual audio correction, manual visual correction, transport, and live result updates.
- Work-window-only deterministic luma motion detector with candidate timestamp/index, confidence, and motion score. New events cancel obsolete analysis.
- Correct operator convention: `visual - audio`; positive means audio leads, negative means audio lags. Blue is lead/early and red is lag/late, with text always present.
- Rational frame-rate and interlaced field-cadence domain models.
- Session history keeps only the three latest replaced results and is not persisted.
- JSON settings persist only allow-listed preferences/device identity under LocalAppData.
- Real process/system/available memory readings; bounded diagnostic logging with no raw media.
- Native Media Foundation video-device enumeration and conservative external/capture-device ranking.
- Container-ID-first device pairing decision service and deterministic capture-format selector exist as testable boundaries.
- Self-contained and framework-dependent win-x64 publish profiles.
- 28 automated tests pass.

## Provisional / incomplete

- Physical Media Foundation source-reader streaming is not connected to `ICaptureSession`; selecting an enumerated hardware device reports this honestly rather than pretending capture is active.
- Windows audio endpoint enumeration and retrieval of video/audio `DEVPKEY_Device_ContainerId` are not implemented. Pairing logic exists, but real endpoints are not yet fed into it.
- Device/QPC normalization exists as a timing service, but `IMFSample::GetSampleTime`, `MFSampleExtension_DeviceTimestamp`, and paired audio-clock observations are not yet collected from live samples.
- Auto Spike currently uses the detector timestamp; a dedicated small-region post-trigger refinement pass should be added for physical PCM.
- The visual detector uses global sparse-luma differences and proximity. It can find the synthetic approach/contact sequence but is not yet robust to arbitrary hands/backgrounds.
- The RAM bar reports all values and visualizes system use, but its compact rendering does not yet use three separately sized labelled segments.
- Automatic thumbnail displays the chosen temporal image. Field-extracted thumbnails await the hardware/deinterlacing path.
- UI was compile-tested and process-smoke-tested, but native screenshot inspection was unavailable in this session.

## Requires capture-card testing

- Enumeration/ranking against integrated cameras and multiple USB HDMI/SDI devices.
- Privacy-denied, unplug/replug, stalled-driver, and reconnect behavior.
- Supported native media-type negotiation for 1080p25, 1080i50, 1080p50, fractional rates, and vendor-specific formats.
- Shared physical Container ID matching and proof that the laptop microphone is never selected.
- Actual video/audio timestamp domains, drift, discontinuities, and device timestamp availability.
- CPU/RAM behavior with long sessions and 1–30 second buffer settings at uncompressed 1080 formats.
- Correct top/bottom field extraction and bob display for 1080i50.
- Real-room transient false positives, echo suppression, and visual detector confidence calibration.

## Next priorities

1. Implement Media Foundation async source-reader video samples, preserving sample and device timestamps and negotiating preferred native formats.
2. Enumerate MMDevice audio endpoints, obtain PnP Container IDs, pair conservatively, and capture timestamped PCM through WASAPI.
3. Correlate video and audio into a common QPC timeline; surface discontinuity/degraded timing status.
4. Add NV12/YUY2-to-preview conversion and interlaced field extraction/bob display without blocking capture.
5. Run the documented hardware matrix, add captured metadata fixtures (not media), and tune recovery/detection behavior.
6. Perform hands-on UI/accessibility review at 960×650, 1366×68, high DPI, and multi-monitor scaling.
