# Testing

## Automated suites

```powershell
dotnet restore Kairix.QuickAVSync.sln
dotnet build src/Kairix.QuickAVSync.Core/Kairix.QuickAVSync.Core.csproj -c Release
dotnet build Kairix.QuickAVSync.sln -c Release
dotnet test tests/Kairix.QuickAVSync.Core.Tests -c Release
dotnet test tests/Kairix.QuickAVSync.Windows.Tests -c Release
```

The Core tests target plain `net10.0`. They cover rolling buffers, sync convention/wording, incomparable clocks, rational/progressive/full-frame/single-field timing, reconstructed 50i/59.94i ordering and exact interval math, timing-origin/clock-domain preservation, field-level clap selection, transient detection, fixed-reference event review, timeline-to-nearest-frame mapping, manual preview/commit modes, idempotent history finalization, Hold gating, waveform placement, bounded 640×360 synthetic luma and 160×90 colour presentation, multi-scale localized visual analysis, startup device ranking, physical pairing, clock mapping/discontinuities, supersession, and complete synthetic audio-to-measurement paths for signed 5–120 ms offsets at multiple frame cadences. Detector fixtures include close and distant claps, low/high contrast, varied positions and cadences, approach/contact/drop, steady movement, whole-frame movement, flashes, sparse noise, and no motion. Source-signal fixtures cover native 25/50/59.94, repeated A-A/B-B/C-C content, all-fresh progressive 50, woven 25, gross 50-vs-60 mismatch, static and sensor-noise restraint, and alternating-line interlace evidence. Sensitivity tests prove that weak-clap acceptance changes while confidence remains evidence-derived and global/noise rejection remains active. An architecture test rejects Windows Desktop assembly references.

Windows tests cover settings and debounced persistence, processing-raster propagation, themes/panel state, native-format ranking, authoritative formatting, Media Foundation metadata, exact memory accounting, YUV-to-BGR presentation, direct-luma equivalence for NV12/YUY2/UYVY/RGB24/BGRA32, reusable scaling plans, progressive/top/bottom conversion, HardwareProbe arguments, and read-only device enumeration. They do not open hardware or prove capture functionality.

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
11. Confirm the Settings column collapses/reopens, all four toggles include their function plus `ON`/`OFF`, and the operational controls remain visible.
12. Switch Graphite, Midnight, Light, and High Contrast without restarting. Verify readable ComboBox/TextBox/Button states and that waveform, sync, and RAM controls update.

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

Exercise strict woven-field reconstruction (still without saving media):

```powershell
dotnet run --project tools/Kairix.QuickAVSync.HardwareProbe -c Release -- --device "USB Capture SDI" --mode "1920x1080|25/1|p|Yuy2" --reconstruct-fields --field-order top --analyze-signal --timing-detail
```

Compare detector conversion cost without changing the native mode or interpretation:

```powershell
dotnet run --project tools/Kairix.QuickAVSync.HardwareProbe -c Release -- --device "USB Capture SDI" --mode "1920x1080|25/1|p|Yuy2" --reconstruct-fields --field-order top --detection-resolution 320x180 --review-resolution 160x90 --analyze-signal
dotnet run --project tools/Kairix.QuickAVSync.HardwareProbe -c Release -- --device "USB Capture SDI" --mode "1920x1080|25/1|p|Yuy2" --reconstruct-fields --field-order top --detection-resolution 640x360 --review-resolution 160x90 --analyze-signal
dotnet run --project tools/Kairix.QuickAVSync.HardwareProbe -c Release -- --device "USB Capture SDI" --mode "1920x1080|25/1|p|Yuy2" --reconstruct-fields --field-order top --detection-resolution 960x540 --review-resolution 160x90 --analyze-signal
```

The timing report must show approximately 50 reconstructed review positions/s but 25 native transport samples/s. Arrival/device/sample/reader observations must each be counted once per `NativeSampleIndex`, never shifted to field rate.

Canonical strict device/mode validation with bounded clock and content detail:

```powershell
dotnet run --project tools/Kairix.QuickAVSync.HardwareProbe -c Release --no-build -- --device "USB Capture SDI" --mode "1920x1080|50/1|p|Yuy2" --analyze-signal --timing-detail
```

The reconstruction report keeps the authoritative 25p capture mode separate from the interpreted 50i review cadence, prints field identity/timestamp origin/phase assumption, and reports native-sample versus temporal-position counts. The 50p comparison keeps declared/observed capture output separate from estimated unique cadence and repeat pattern. Paired repeats require an active scene and stable evidence.

Current Magewell evidence with a 1080i50 camera and card Weave mode:

- strict 1080p25 YUY2 reconstruction: 183 native samples produced 366 temporal field positions; 50 positions/s, 20 ms median spacing, zero duplicate/backwards/gap faults, 25 positions per 500 ms, and distinct content on parity transitions;
- strict 1080p50 YUY2 comparison: 50.001 fps and 20 ms timestamps, stable A-A/B-B content, and approximately 25 unique images/s.
- 2026-10-03 strict 1080p25 YUY2/top-first conversion comparison with fixed 160×90 review: 320×180 detection averaged 0.519 ms (5.143 ms max), 640×360 averaged 1.320 ms (5.927 max), and 960×540 averaged 2.424 ms (6.762 max). All three reported 50.000 fields/s review and 25.000 samples/s transport. Treat these figures as host-specific evidence, not guarantees.

These results validate extraction and cadence, not phase. Native UI inspection must still confirm visible Top/Bottom motion stepping, and an external reference must calibrate whether the hardware capture timestamp truly corresponds to the second field.

Passing enumeration tests or seeing a picture is not enough to claim calibrated A/V timing.
