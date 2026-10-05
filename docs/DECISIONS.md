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
| Integrity vs measurement | Every calculable Auto Result, Manual Preview, and Manual Result stays visible. Whole-timeline invalidity is a separate warning and may exclude an automatic result from history. |
| Hold | Suppresses new automatic events but does not stop capture or buffering. Once returned to ARMED, the next automatic event is immediately admissible even if a review is displayed. |
| Persistence | Capture and presentation preferences persist per device/profile; Hold, media, and session history do not. |
| Reconstruction selection | The selected interpretation is the sole runtime reconstruction truth; reconstructed rate/order choices are formats, not a second checkbox state. |
| History | Keep the newest three finalized results. |
| Privacy | No telemetry, accounts, database, cloud dependency, or media persistence. |
| Browser source | Ship one offline-capable HTML A/V sync test signal with the repository. |
| Releases | Recommended `v*` releases are deliberate promotions; validated `main` commits produce separate development prereleases. |
