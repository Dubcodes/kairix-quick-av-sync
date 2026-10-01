# Testing

## Automated suites

```powershell
dotnet restore Kairix.QuickAVSync.sln
dotnet build src/Kairix.QuickAVSync.Core/Kairix.QuickAVSync.Core.csproj -c Release
dotnet build Kairix.QuickAVSync.sln -c Release
dotnet test tests/Kairix.QuickAVSync.Core.Tests -c Release
dotnet test tests/Kairix.QuickAVSync.Windows.Tests -c Release
```

The Core tests target plain `net10.0`. They cover rolling buffers, sync convention/wording, incomparable clocks, rational/progressive/full-frame/single-field timing, transient detection, fixed-reference event review, timeline-to-nearest-frame mapping, manual preview/commit modes, idempotent history finalization, Hold gating, waveform placement, bounded 640×360 synthetic luma and 160×90 colour presentation, multi-scale localized visual analysis, startup device ranking, physical pairing, clock mapping/discontinuities, supersession, and complete synthetic audio-to-measurement paths for signed 5–120 ms offsets at multiple frame cadences. Detector fixtures include close and distant claps, low/high contrast, varied positions and cadences, approach/contact/drop, steady movement, whole-frame movement, flashes, sparse noise, and no motion. Source-signal fixtures cover native 25/50/59.94, repeated 25→50 and 30→60, deterministic 50→60 conversion, sensor noise, slow/fast/camera-like motion, alternating-line interlace evidence, high-detail progressive rejection, and already-deinterlaced unknown scan. Sensitivity tests prove that weak-clap acceptance changes while confidence remains evidence-derived and global/noise rejection remains active. An architecture test rejects Windows Desktop assembly references.

Windows tests cover settings allow-list/per-device-format/sensitivity persistence, Auto/manual native-format ranking and fallback, raw Media Foundation interlace mapping and distinct mode identities, exact memory-segment accounting, deterministic YUV-to-BGR colour conversion, and execute real read-only MMDevice and Media Foundation enumeration on the test host. They do not open hardware or prove capture functionality.

On this mapped workspace drive, `dotnet` project graph operations can intermittently fail without diagnostics under parallel MSBuild. Use `-m:1` for a deterministic local full-solution build; GitHub Actions uses a normal local runner filesystem.

## Capture-free application test

1. Start the app and select **Synthetic A/V test source (+60 ms video)**.
2. Confirm live motion and `READY TO CLAP`.
3. Wait for the generated event and confirm a positive audio-leads result near +60 ms.
4. Confirm the automatic thumbnail remains fixed while Left/Right changes the main viewer.
5. Scrub and verify the result changes to `MANUAL PREVIEW` and follows the white playhead; press Enter and verify it changes to `MANUAL RESULT`.
6. Drag the yellow AUDIO marker or press A and verify Audio/result changes live while the event window, waveform, playhead, Auto candidate, and thumbnail remain intact.
7. Press H once across the next event and confirm no new test appears while preview continues; press H again to resume automatic events.
8. Click/drag the timeline and confirm the review playhead and manual-preview result change without changing the stored Auto candidate. Confirm marker labels remain readable when timestamps coincide.
9. Press R, Space, and F5 to verify resume, manual capture, and reconnect.
10. Close/reopen and verify preferences persist while history/media do not.
11. Confirm all four toggles show `ON` when checked and `OFF` when unchecked, and that the window initially opens maximized but can be restored and resized normally.

Both existing win-x64 publish profiles must pass `scripts/smoke-test-windows.ps1`. It requires a live process with the exact title, a non-zero visible maximized top-level window handle, and a clean close.

## Concurrency regression

Run the Core suite repeatedly after changing event analysis:

```powershell
1..10 | ForEach-Object { dotnet test tests/Kairix.QuickAVSync.Core.Tests -c Release --no-build }
```

The analysis-generation test proves the acceptance rule independently; application smoke testing should also trigger closely spaced synthetic/manual events and confirm no old thumbnail/result returns.

## Physical hardware

Use the checklist in `CURRENT_STATE.md`. Save diagnostic text only. For each format record the card/driver identity, paired endpoint, Container IDs, pixel subtype, frame-rate rational, scan/field metadata, active video timestamp source, audio QPC status, discontinuities, reconnect behavior, and an external known-delay comparison.

Enumerate all native modes without starting capture or saving media:

```powershell
dotnet run --project tools/Kairix.QuickAVSync.HardwareProbe -c Release -- --list-formats "USB Capture SDI"
```

Run a bounded blind source analysis without saving media:

```powershell
dotnet run --project tools/Kairix.QuickAVSync.HardwareProbe -c Release -- --analyze-signal "USB Capture SDI"
```

The report keeps declared/observed capture output separate from estimated unique cadence, repeat pattern, interlace evidence, authority, and provenance. Compare an operator-known source only after recording the blind result.

Passing enumeration tests or seeing a picture is not enough to claim calibrated A/V timing.
