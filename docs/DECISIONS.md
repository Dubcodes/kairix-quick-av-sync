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
| Automatic workflow | Detection is continuous; the newest accepted automatic result remains displayed and replaces the previous event. |
| Manual workflow | Manual review is explicitly entered and its audio/video points are independently editable. |
| Integrity vs measurement | Whole-timeline invalidity may reject automatic measurement while comparable manually selected points remain measurable; the warning stays visible. |
| Hold | Suppresses new automatic events but does not stop capture or buffering. |
| Persistence | Capture and presentation preferences persist per device/profile; Hold, media, and session history do not. |
| History | Keep the newest three finalized results. |
| Privacy | No telemetry, accounts, database, cloud dependency, or media persistence. |
| Browser source | Ship one offline-capable HTML A/V sync test signal with the repository. |
| Releases | Recommended `v*` releases are deliberate promotions; validated `main` commits produce separate development prereleases. |
