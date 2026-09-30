# Changelog

## Unreleased

- Prepared the public Windows prerelease path: `main`-only Windows CI, tag-driven self-contained x64 packaging/release automation, and a completed MIT copyright holder.
- Added a per-device capture-format selector with `Auto` plus deduplicated native modes. Selecting a mode persists only its stable native mode key, immediately clears stale media/results, and reconnects; unavailable saved modes fall back safely to Auto.
- Auto native-format policy now prefers supported progressive modes by sensible native resolution and native rational frame rate, using the driver default only as a final tie-breaker. This prevents a C920-style 640×480 default from winning over a working 1080p30 native mode. Manual mode attempts first and then falls back through Auto-ranked candidates if the driver refuses it.
- Strengthened automatic visual-clap scoring with robust motion baseline/deviation normalization and short-event scoring. Expanded deterministic end-to-end synthetic tests across signed 5–120 ms offsets, 25/50/29.97 fps cadences, incompatible clock domains, and continuous background motion.
- Corrected the `IMFSample` COM vtable declaration (`SetSampleFlags` was missing and getter/setter order was wrong), restoring real video-buffer delivery. Added Windows SDK IID/vtable regression tests for the active Media Foundation interfaces.
- Changed native-mode selection from broadcast-biased 1080p50 ranking to current/default-mode-first, source-driven ranking while retaining rational frame rates and candidate fallback.
- Media Foundation is now accepted only after three consecutive payload-bearing frames within a three-second validation window. Readiness separately requires comparable timing and live paired audio; bounded logs show the strategy decision and suppress enormous capability dumps.
- Physically validated payload video, same-device audio, and shared QPC timing on both an XI100DUSB HDMI capture device and a Logitech C920 UVC webcam. Added a metadata-only hardware probe under `tools/`.
- Wired the existing subtle coffee control to `https://buymeacoffee.com/dubcodes` without adding startup prompts or banners.
- Corrected the Media Foundation `IMFMediaType` IID that caused physical-device format enumeration to fail with `E_NOINTERFACE`, added stage/HRESULT/native-format/first-sample diagnostics, and made logs bounded.
- Added ranked native-format fallback negotiation plus direct UYVY and RGB24 luma extraction. Capture now requires a payload-bearing first video sample instead of treating COM setup or timestamp-only samples as live.
- Decoupled embedded-audio failure from video health and clear stale preview/format/measurement state before source changes and failed opens.
- The earlier XI100DUSB zero-buffer observation was traced to the `IMFSample` vtable mismatch rather than accepted as a device limitation.
- Fixed the published WPF startup crash caused by the RAM `ProgressBar` attempting a default two-way binding against read-only `SystemFraction`. Startup now explicitly establishes the main window/lifetime before asynchronous capture initialization, logs visible startup failures, and has a visible-window publish smoke test script.
- Split the application into a platform-neutral `net10.0` Core library, a Windows capture/infrastructure library, and the WPF presentation project.
- Added native Media Foundation source-reader video capture with negotiated format metadata, sample/device timestamps, direct bounded luma extraction, and clean session shutdown.
- Added shared-mode WASAPI audio capture with float32/PCM16 normalization, QPC timestamps, discontinuity reporting, and conservative Container ID/hardware-parent endpoint pairing.
- Added clock-domain-aware results that refuse uncorrelated A/V timing, capture-session contracts shared by native and synthetic providers, stale-analysis cancellation, and coalesced off-UI-thread preview creation.
- Expanded deterministic Core tests and added Windows integration tests for real device enumeration and settings behavior.
- Updated CI, architecture, backend, testing, current-state, and public build/publish documentation.
- Native hardware streaming is physically verified on the XI100DUSB HDMI capture device and Logitech C920; see `CURRENT_STATE.md` for formats, timing, and remaining limitations.
