# Changelog

## Unreleased

- Split the application into a platform-neutral `net10.0` Core library, a Windows capture/infrastructure library, and the WPF presentation project.
- Added native Media Foundation source-reader video capture with negotiated format metadata, sample/device timestamps, direct bounded luma extraction, and clean session shutdown.
- Added shared-mode WASAPI audio capture with float32/PCM16 normalization, QPC timestamps, discontinuity reporting, and conservative Container ID/hardware-parent endpoint pairing.
- Added clock-domain-aware results that refuse uncorrelated A/V timing, capture-session contracts shared by native and synthetic providers, stale-analysis cancellation, and coalesced off-UI-thread preview creation.
- Expanded deterministic Core tests and added Windows integration tests for real device enumeration and settings behavior.
- Updated CI, architecture, backend, testing, current-state, and public build/publish documentation.
- Native hardware streaming remains implemented but capture-card-unverified; see `CURRENT_STATE.md`.
