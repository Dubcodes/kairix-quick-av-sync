# Decisions

This concise log records durable project choices. Evidence and open questions belong in `CURRENT_STATE.md` and `docs/TESTING.md`.

| Decision | Current choice |
|---|---|
| V1 platform | Windows-first, implemented in C#/.NET/WPF. |
| Portable domain | Timing, buffers, analysis, review, and capture contracts remain in platform-neutral Core. |
| Windows capture | Media Foundation video with paired shared-mode WASAPI audio. |
| Media ownership | Bounded RAM rolling buffers; no recording library or saved media. |
| Audio pairing | Prefer same-device Container ID, then cautious hardware/name evidence; never silently use the default microphone. |
| Review cadence | Review is field-aware. Explicit reconstructed fields remain distinct from native transport samples. |
| Automatic workflow | Detection is continuous and transactional; a rejected pending event changes no displayed event or history, while the newest accepted event atomically replaces the previous event. |
| Manual workflow | Manual Clap enters Hold and opens explicit review; its audio/video points are independently editable. Live View clears the review without changing Hold/ARMED. |
| Integrity vs measurement | Every calculable Auto Result, Manual Preview, and Manual Result stays visible. Whole-timeline invalidity is a separate warning; it never upgrades trust and does not exclude an accepted automatic result from history. |
| Hold | Suppresses new automatic events but does not stop capture or buffering. Once returned to ARMED, the next automatic event is immediately admissible even if a review is displayed. |
| Persistence | Capture and presentation preferences persist per device/profile; Hold, media, and session history do not. |
| Reconstruction selection | The selected interpretation is the sole runtime reconstruction truth; reconstructed rate/order choices are formats, not a second checkbox state. |
| Capture-mode selection | Auto retains backend ranking/fallback. Selecting a named native mode makes one strict exact request; the simplified catalog hides advanced modes unless Show all is enabled, while a selected advanced mode always remains visible. |
| Capture truth labels | Driver-negotiated mode, observed application delivery, and physical-source cadence are shown separately. Passive cadence is explicitly estimated; authoritative wording requires authoritative hardware/provider metadata. |
| Live audio meter | Display peak dBFS from incoming `AudioChunk` samples at a coalesced 25 Hz UI rate. Metering continues during Hold and historical review without changing detector admission or capture behavior. |
| Visual detector engines | Run one selected lightweight deterministic engine per event: Human Clap by default, or Flash / Test Signal for electronic cues. Capture, timing, reconstruction, review, and history remain shared. |
| Audio-only fallback | A strong impulsive transient without a visual match is retained as a reviewable Audio Candidate, not a confirmed sync result or normal history entry; weaker unmatched transients are discarded. |
| History | Keep the newest 3, 5, 10, or 20 finalized automatic results (default 3) as immutable processed review-window snapshots in bounded RAM. Derive the interactive trend graph from that same collection without another media store. History depth persists; captured history media does not. |
| Privacy | No telemetry, accounts, database, cloud dependency, or media persistence. |
| Browser source | Ship one offline-capable HTML A/V sync test signal with the repository. |
| Releases | Stable `v*` releases are deliberate promotions. Public assets are exactly one self-contained x64 EXE, one ZIP, one independent canonical browser-signal HTML file, and SHA256SUMS; validated `main` commits produce separate development prereleases with the same shape. |
