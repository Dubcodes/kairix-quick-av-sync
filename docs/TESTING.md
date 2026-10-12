# Testing

## Automated suites

```powershell
dotnet restore Kairix.QuickAVSync.sln
dotnet build src/Kairix.QuickAVSync.Core/Kairix.QuickAVSync.Core.csproj -c Release
dotnet build Kairix.QuickAVSync.sln -c Release
dotnet test tests/Kairix.QuickAVSync.Core.Tests -c Release
dotnet test tests/Kairix.QuickAVSync.Windows.Tests -c Release
```

The Core tests target plain `net10.0`. They cover rolling buffers, sync convention/wording, incomparable clocks, rational/progressive/full-frame/single-field timing, reconstructed 50i/59.94i ordering, exact automatic capture-delay offsets, pair-wide corrected cadence, raw metadata preservation, profile isolation, detector expected-position shifting, fixed-reference review, confirmed/Audio Candidate/rejected automatic outcomes, manual recovery, history finalization, waveform placement, configurable synthetic processing rasters, visual analysis, pairing, clock mapping, complete synthetic measurement paths, separated driver/observed/source status wording, and bounded stereo/mono audio-peak accumulation. Human fixtures cover sustained approach/contact/hold, best-candidate selection, reset rejection, onset-without-approach rejection, broad/steady motion, flashes, sparse noise, reconstruction, and confidence. Flash fixtures cover exact first onset, disappearance rejection, partial-frame cues, gradual change, continuous motion, sparse noise, competing transitions, and sensitivity-independent confidence. An architecture test rejects Windows Desktop assembly references.

Windows tests cover settings and debounced persistence, processing-raster propagation, themes/panel state, native-format ranking, strict exact-mode versus Auto open options, Show-all persistence/visibility, capture-status UI labels, coalesced live-audio presentation during Hold/history, Media Foundation metadata, exact memory accounting, YUV-to-BGR presentation, direct-luma equivalence for NV12/YUY2/UYVY/RGB24/BGRA32, reusable scaling plans, progressive/top/bottom conversion, HardwareProbe arguments, read-only device enumeration, and the ViewModel history path for normal/timing-warning events, immutable restore, Hold, de-duplication, 3/5/10/20 capacity changes, and trend-point command identity. They do not open hardware or prove capture functionality.

On this mapped workspace drive, `dotnet` project graph operations can intermittently fail without diagnostics under parallel MSBuild. Use `-m:1` for a deterministic local full-solution build; GitHub Actions uses a normal local runner filesystem.

## Capture-free application test

1. Start the app and select **Synthetic A/V test · expected +60 ms (audio leads video)**.
2. Confirm live motion and `READY TO CLAP`.
3. Let clap 1 complete and confirm `+60 ms` / **AUDIO LEADS VIDEO BY 60 ms** remains displayed while the detector reads ARMED.
4. Repeat with **Human Clap** and **Flash / Test Signal**; both must select the same synthetic cue and report approximately +60 ms. Changing the engine must not reconnect capture or clear history.
5. Without using Live View, let clap 2 complete and confirm it replaces clap 1; let clap 3 replace clap 2. Confirm the newest three finalized events remain bounded in history. On Settings → Review, change **Session history depth** through 3, 5, 10, and 20 and confirm shrinking takes effect immediately. Hover a trend point to see only its signed value; select it and confirm the same retained review opens in Hold as its history card.
6. Click **Auto Candidate** and confirm navigation returns to that event's candidate without discarding the event.
7. Scrub and verify the result changes to `MANUAL PREVIEW` and follows the white playhead; press Enter and verify it changes to `MANUAL RESULT`.
8. Drag the yellow AUDIO marker or click Pre A/Nxt A and verify 1.0 ms Audio/result changes while the event window, waveform, visual playhead, Auto candidate, and thumbnail remain intact.
9. Press H across the next clap and confirm no new event appears while capture, preview, and rolling buffers continue; press H again and confirm automatic replacement resumes.
10. Produce or load an accepted automatic event whose timeline is marked `TIMING WARNING`. Confirm it remains in history, reopening it restores the warning/result/marks/media and enters Hold, and it does not duplicate when returning to Live View. Use Manual Clap on an invalid timeline to confirm moving the playhead updates the manual A/V number, moving Audio updates it again, and uncorrelated selected clocks still show no number.
11. Click/drag the timeline and confirm the review playhead and manual-preview result change without changing the stored Auto candidate. Confirm marker labels remain readable when timestamps coincide.
12. Select Auto, then an exact named capture mode, reconnect/restart, and verify the per-device capture-mode choice persists. Toggle **Show all capture modes**, select an advanced mode, turn the toggle off, and confirm that selected mode stays visible. Separately select a reconstructed interpretation/field order and confirm it does not change the capture-mode selection; select a native interpretation and confirm reconstruction is off.
13. Verify every listed shortcut: Space, Enter, Left/Right, Shift+Left/Right, Ctrl+Left/Right, A, H, R, and F5.
14. Close/reopen and verify preferences persist while history/media do not.
15. Confirm Settings collapses to a narrow reopen rail; switch AP/DET/REV/CAP/KEY/? pages and verify engineering detail appears only in About.
16. Switch all seven themes, all six waveform styles, and all four amplitude modes without restarting. Verify readable settings navigation, controls, history, marker labels, live audio meter, outlined video playhead, and result overlay; confirm display changes do not move any marker or result. Centered modes use the drawing-area midpoint; Peak modes use its bottom as zero and extend upward. Auto Gain should make a useful snapshot peak reach about 90% height while near-silence remains small.
17. With real incoming audio, verify mono uses one meter and stereo shows independent left/right peaks plus the actual sample rate/channel format. Enter Hold and reopen history; the meter must keep moving and decay toward -60 dBFS when input becomes silent.

Both existing win-x64 publish profiles must pass `scripts/smoke-test-windows.ps1`. It requires a live process with the exact title, a non-zero visible maximized top-level window handle, and a clean close.

The shared distribution packager can be exercised locally with a traceable test identity:

```powershell
./scripts/package-windows.ps1 -BuildId dev-20261004-ee1e442 -SourceCommit ee1e442 -BuildChannel Development
```

It must produce exactly one non-empty identified self-contained EXE, a ZIP containing that EXE plus the byte-identical browser test signal and README/LICENSE, an independent byte-identical `Kairix-AV-Sync-Test-Signal.html`, and a three-entry `SHA256SUMS.txt` under `artifacts/package/`. Temporary packaging staging must remain under `artifacts/staging/` and be removed after packaging. On a `main` push, Windows CI creates the date component in the project's Pacific/Auckland timezone, invokes the packager only after all builds and tests succeed, and uploads the result under an artifact name containing the full commit SHA. The development-release workflow is triggered by that completed CI run, downloads only that run's exact artifact, rechecks identity and checksums, and creates an idempotent public prerelease. A `v*` tag continues to be the only stable-release trigger.

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

The Windows suite copies `MainWindow.xaml` and every theme dictionary into its output. Contract tests verify the named left navigation and six settings pages, collapsed reopen rail, Appearance/audio controls, click-to-point detector sliders, reconstruction represented only by interpretation formats, About/Shortcuts content, every advertised keyboard route, compact result panel, removed top-left viewer popup, Mark Visual review gating, and exact review-button order. Theme tests compute sRGB relative luminance and require at least 4.5:1 for primary/secondary/muted panel text, normal/hover/pressed control text, accent text, history text, and the outlined video playhead in Graphite, Midnight, Light, High Contrast, Synthwave, Terminal, and Solar Flare.

Core contact fixtures cover: approach→contact/settle, sustained-approach contact onset, later clean contact over earlier weak motion, post-hold reset rejection, onset without approach rejection, slow/steady motion without contact (no candidate even at high sensitivity), broad camera movement rejection, reconstructed Top-approach/Bottom-contact, and progressive equivalent phase. The actual Synthetic generator is exercised for five sequential events per engine; Human and Flash must choose the same known visual cue and preserve the approximately +60 ms sign convention. One 1280×720 source sequence is downsampled to 320×180, 640×360, 960×540, and 1280×720; all must select the same physical contact index while confidence and score may differ.

Separate invariant tests prove each analysis raster preserves media timestamp/raw value, temporal index, native-sample identity, field identity, timing observation, and compensation result. Synthetic same-resolution reconnect and changed-resolution reconnect produce the same phase. These tests distinguish **detector selection shift** from clock drift: raster may change evidence/confidence, but only a genuinely different chosen temporal position may change the result. Physical testing must additionally compare timestamp cadence/faults, audio continuity, conversion average/max, analysis time, selected field/index, and corrected result across at least 320×180, 640×360, and 960×540; never tune offsets per raster.
