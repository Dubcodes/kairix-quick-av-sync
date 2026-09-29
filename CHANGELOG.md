# Changelog

## Unreleased

- Corrected the Media Foundation `IMFMediaType` IID that caused physical-device format enumeration to fail with `E_NOINTERFACE`, added stage/HRESULT/native-format/first-sample diagnostics, and made logs bounded.
- Added ranked native-format fallback negotiation plus direct UYVY and RGB24 luma extraction. Capture now requires a payload-bearing first video sample instead of treating COM setup or timestamp-only samples as live.
- Decoupled embedded-audio failure from video health and clear stale preview/format/measurement state before source changes and failed opens.
- Verified the connected XI100DUSB activates, negotiates 1920×1080 50/1 progressive YUY2, and pairs/opens 48 kHz stereo audio. Its observed video samples carried device timestamps but no pixel buffers during the test window, so physical video support remains unconfirmed.
- Fixed the published WPF startup crash caused by the RAM `ProgressBar` attempting a default two-way binding against read-only `SystemFraction`. Startup now explicitly establishes the main window/lifetime before asynchronous capture initialization, logs visible startup failures, and has a visible-window publish smoke test script.
- Split the application into a platform-neutral `net10.0` Core library, a Windows capture/infrastructure library, and the WPF presentation project.
- Added native Media Foundation source-reader video capture with negotiated format metadata, sample/device timestamps, direct bounded luma extraction, and clean session shutdown.
- Added shared-mode WASAPI audio capture with float32/PCM16 normalization, QPC timestamps, discontinuity reporting, and conservative Container ID/hardware-parent endpoint pairing.
- Added clock-domain-aware results that refuse uncorrelated A/V timing, capture-session contracts shared by native and synthetic providers, stale-analysis cancellation, and coalesced off-UI-thread preview creation.
- Expanded deterministic Core tests and added Windows integration tests for real device enumeration and settings behavior.
- Updated CI, architecture, backend, testing, current-state, and public build/publish documentation.
- Native hardware streaming remains implemented but capture-card-unverified; see `CURRENT_STATE.md`.
