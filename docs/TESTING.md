# Testing

## Automated suites

```powershell
dotnet restore Kairix.QuickAVSync.sln
dotnet build src/Kairix.QuickAVSync.Core/Kairix.QuickAVSync.Core.csproj -c Release
dotnet build Kairix.QuickAVSync.sln -c Release
dotnet test tests/Kairix.QuickAVSync.Core.Tests -c Release
dotnet test tests/Kairix.QuickAVSync.Windows.Tests -c Release
```

The Core tests target plain `net10.0`. They cover rolling buffers, sync convention/wording, incomparable clocks, rational/progressive/full-frame/single-field timing, reconstructed 50i/59.94i ordering, exact automatic capture-delay offsets, pair-wide corrected cadence, raw metadata preservation, profile isolation, detector expected-position shifting, fixed-reference review, manual preview/commit modes, history finalization, waveform placement, configurable synthetic processing rasters, visual analysis, pairing, clock mapping, and complete synthetic measurement paths. Detector fixtures include close and distant claps, varied contrasts/positions/cadences, approach/contact/drop, broad motion, flashes, sparse noise, and no motion. An architecture test rejects Windows Desktop assembly references.

Windows tests cover settings and debounced persistence, processing-raster propagation, themes/panel state, native-format ranking, authoritative formatting, Media Foundation metadata, exact memory accounting, YUV-to-BGR presentation, direct-luma equivalence for NV12/YUY2/UYVY/RGB24/BGRA32, reusable scaling plans, progressive/top/bottom conversion, HardwareProbe arguments, and read-only device enumeration. They do not open hardware or prove capture functionality.

On this mapped workspace drive, `dotnet` project graph operations can intermittently fail without diagnostics under parallel MSBuild. Use `-m:1` for a deterministic local full-solution build; GitHub Actions uses a normal local runner filesystem.

## Capture-free application test

1. Start the app and select **Synthetic A/V test · expected +60 ms (audio leads video)**.
2. Confirm live motion and `READY TO CLAP`.
3. Let clap 1 complete and confirm `+60 ms` / **AUDIO LEADS VIDEO BY 60 ms** remains displayed while the detector reads ARMED.
4. Without Resume Live, let clap 2 complete and confirm it replaces clap 1; let clap 3 replace clap 2. Confirm the newest three finalized events remain bounded in history.
5. Click **Auto Candidate** and confirm navigation returns to that event's candidate without discarding the event.
6. Scrub and verify the result changes to `MANUAL PREVIEW` and follows the white playhead; press Enter and verify it changes to `MANUAL RESULT`.
7. Drag the yellow AUDIO marker or click Pre A/Nxt A and verify 1.0 ms Audio/result changes while the event window, waveform, visual playhead, Auto candidate, and thumbnail remain intact.
8. Press H across the next clap and confirm no new event appears while capture, preview, and rolling buffers continue; press H again and confirm automatic replacement resumes.
9. Use Manual Clap on a fixture/window with an invalid timeline. Confirm the integrity warning remains visible, moving the playhead updates the manual A/V number, moving Audio updates it again, and uncorrelated selected clocks still show no number.
10. Click/drag the timeline and confirm the review playhead and manual-preview result change without changing the stored Auto candidate. Confirm marker labels remain readable when timestamps coincide.
11. Enable reconstruction and choose a field order, reconnect/restart, and verify both per-device preferences persist.
12. Press R, Space, and F5 to verify resume, manual capture, and reconnect.
13. Close/reopen and verify preferences persist while history/media do not.
14. Confirm Settings collapses to a narrow reopen rail; switch AP/DET/REV/CAP/KEY/? pages and verify engineering detail appears only in About.
15. Switch all seven themes, all six waveform styles, and all four amplitude modes without restarting. Verify readable settings navigation, controls, history, marker labels, outlined video playhead, and result overlay; confirm display changes do not move any marker or result. Centered modes use the drawing-area midpoint; Peak modes use its bottom as zero and extend upward. Auto Gain should make a useful snapshot peak reach about 90% height while near-silence remains small.

Both existing win-x64 publish profiles must pass `scripts/smoke-test-windows.ps1`. It requires a live process with the exact title, a non-zero visible maximized top-level window handle, and a clean close.

The shared distribution packager can be exercised locally with a traceable test identity:

```powershell
./scripts/package-windows.ps1 -BuildId dev-20261004-ee1e442 -SourceCommit ee1e442 -BuildChannel Development
```

It must produce two non-empty identified EXEs, a ZIP containing both variants plus README/LICENSE, and a three-entry `SHA256SUMS.txt` under `artifacts/package/`. On a `main` push, Windows CI creates the date component in the project's Pacific/Auckland timezone, invokes the packager only after all builds and tests succeed, and uploads the result under an artifact name containing the full commit SHA. The development-release workflow is triggered by that completed CI run, downloads only that run's exact artifact, rechecks identity and checksums, and creates an idempotent public prerelease. A `v*` tag continues to be the only recommended-release trigger.

## Synthetic stability observation

The capture-free probe deliberately uses the same bounded Core samples and synthetic session contract as the application. It writes CSV diagnostics only; it records no media and does not prove physical-device driver behavior.

```powershell
dotnet run --project tools/Kairix.QuickAVSync.StabilityProbe -c Release -- --duration 00:10:00 --sample-interval 00:00:30
dotnet run --project tools/Kairix.QuickAVSync.StabilityProbe -c Release -- --duration 00:02:00 --detection 1280x720 --sample-interval 00:00:30
dotnet run --project tools/Kairix.QuickAVSync.StabilityProbe -c Release -- --reconnect-cycles 100 --reconnect-pause 00:00:00.250
```

Review consecutive steady-state samples rather than startup allocation: video/audio counts must remain at their configured capacities (250/1000 in the probe), active tasks return to zero at completion, and thread/handle counts should settle rather than climb per sample or reconnect. Managed and LOH values are expected to move with GC; a single high value is not a leak verdict. CI runs the one-minute version as an early regression check. The icon source is `assets/icon/kairix-quick-av-sync.svg`; regenerate its tested multi-size ICO with `./scripts/generate-icon.ps1`.

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

For a physical reconstructed A/V check, record all three values separately: the raw interpreted result, the automatic one-field offset shown in Settings, and the corrected primary result. A 25p→50i profile must show +20.000 ms automatic even for bottom-first order. A normal 50p profile must show 0.000 ms unless that exact profile has a manual override. Do not edit raw timestamps or reconstruction phase to force an expected answer.

## UI, contact, and resolution regressions

The Windows suite copies `MainWindow.xaml` and every theme dictionary into its output. Contract tests verify the named left navigation and six settings pages, collapsed reopen rail, Appearance/audio controls, visual/audio Detection controls, About/Shortcuts content, compact result panel, removed top-left viewer popup, Mark Visual review gating, and exact review-button order. Theme tests compute sRGB relative luminance and require at least 4.5:1 for primary/secondary/muted panel text, normal/hover/pressed control text, accent text, history text, and the outlined video playhead in Graphite, Midnight, Light, High Contrast, Synthwave, Terminal, and Solar Flare.

Core contact fixtures cover: approach→contact/settle (contact, not peak), fast post-peak contact, slow motion without contact (no candidate), broad camera movement rejection, reconstructed Top-approach/Bottom-contact, and progressive equivalent phase. One 1280×720 source sequence is downsampled to 320×180, 640×360, 960×540, and 1280×720; all must select the same physical contact index while confidence and score may differ.

Separate invariant tests prove each analysis raster preserves media timestamp/raw value, temporal index, native-sample identity, field identity, timing observation, and compensation result. Synthetic same-resolution reconnect and changed-resolution reconnect produce the same phase. These tests distinguish **detector selection shift** from clock drift: raster may change evidence/confidence, but only a genuinely different chosen temporal position may change the result. Physical testing must additionally compare timestamp cadence/faults, audio continuity, conversion average/max, analysis time, selected field/index, and corrected result across at least 320×180, 640×360, and 960×540; never tune offsets per raster.
