# Kairix Quick A/V Sync

Kairix Quick A/V Sync is a small Windows 10/11 utility for measuring the timing difference between a sharp audio transient and its visible event. It is designed for broadcast technicians testing capture cards, wireless cameras, microphones, transmitters, and converter chains.

> Current V1 status: Windows Media Foundation video capture, paired WASAPI audio, native format selection, comparable timing, and automatic/manual clap review are implemented. Current physical evidence is strongest for USB Capture SDI; historical HDMI and UVC validation remains documented, while broad device/driver compatibility is still under test. See [CURRENT_STATE.md](CURRENT_STATE.md) for the precise boundary.

## Download

### Recommended

**Kairix Quick A/V Sync v0.1.2-alpha** — [self-contained Windows x64 EXE](https://github.com/Dubcodes/kairix-quick-av-sync/releases/download/v0.1.2-alpha/Kairix.QuickAVSync-v0.1.2-alpha-win-x64.exe) — **RECOMMENDED for most users**.

This is the deliberately conservative, physically tested public alpha download. It was validated with a Logitech HD Pro Webcam C920 at 1920×1080 30p NV12 with its paired microphone. Broad capture-device compatibility remains under development.

### Latest development

**Latest Development Build** — [open the newest public development prerelease](https://github.com/Dubcodes/kairix-quick-av-sync/releases) and choose the self-contained `dev-YYYYMMDD-<commit>-win-x64.exe` asset.

Development builds contain newer experimental work and identify their exact source commit. They may be less reliable or include recently changed or unfinished functionality. Testers can use the releases page above, but ordinary users should choose the recommended release.

The self-contained EXE needs no separate .NET installation or installer. Administrator rights are not normally expected, although Windows privacy settings and capture-device drivers still control hardware access. This is an unsigned public alpha, so Windows SmartScreen may show an **Unknown Publisher** warning. Download builds only from this repository's Releases page. Hardware compatibility remains under active testing.

First run:

1. Download and run the self-contained EXE.
2. Select the capture device and confirm that **Driver capture** matches the Media Foundation mode actually delivered.
3. Leave **Capture mode** on Auto or choose one exact native driver mode. The choice is remembered per device; **Show all capture modes** reveals advanced modes without hiding one that is already selected. Choose **Interpretation** separately when woven-field reconstruction is required.
4. If a progressive capture frame contains two woven fields, enable **Reconstruct interlaced fields** and select the matching top-first or bottom-first interpretation.
5. Clap and review the sync result. Manual timeline scrubbing and Audio/Visual corrections remain available.

## Platform support

**Current:** Windows 10/11.

Kairix Quick A/V Sync is Windows-first while its initial capture and timing implementation is developed and validated. Future releases are intended to support macOS and Linux, but neither is supported today. The synchronization engine, buffering, audio/visual analysis, and timing models live in a platform-neutral Core library; capture and UI integrations are isolated so AVFoundation or suitable Linux backends can be added without rewriting that engine. WPF remains the Windows V1 UI.

## Try it now

Build and run on Windows with the .NET 10 SDK:

```powershell
dotnet run --project src/Kairix.QuickAVSync -c Release
```

When no working hardware backend is available, the app selects **Synthetic A/V test · expected +60 ms (audio leads video)**. Every five seconds it emits an audio transient followed exactly 60 ms later by one unambiguous visual contact onset and brief hold. Kairix defines positive results as audio leading video, so the expected automatic result is `+60 ms` / **AUDIO LEADS VIDEO BY 60 ms**. This deterministic calibration pattern exercises detection, timeline, review, override, and history without a capture card.

## Browser A/V Sync Test Signal

Download [kairix_av_sync_test_signal_for_browser.html](kairix_av_sync_test_signal_for_browser.html) and open it locally in a modern browser for a practical, repeatable test source. It starts with **Sync**, **Clap**, and a **2 second loop**: point the camera or complete system under test at the target, then select known audio- or video-lead offsets to check measurement consistency. Browser autoplay rules may require one click anywhere on the page before the cue is audible; the visual signal starts immediately.

## Workflow

1. Select a video source. **Driver capture** is read-only and describes the final negotiated Media Foundation output. **Observed delivery** is the measured application cadence. **Source cadence** remains Unknown or explicitly estimated unless an authoritative provider reports it.
2. Leave **Capture mode** on Auto for backend ranking/fallback, or choose a named raster/rate/scan/pixel mode for a strict exact request. Choose **Interpretation** independently; reconstruction does not relabel or change the selected driver mode.
3. For woven capture such as `1920×1080 · 25.000p · YUY2` containing a 50i source, enable **Reconstruct interlaced fields** and select `25.000p → 50.000i · Top first` or `Bottom first`. This changes review cadence, not the authoritative detected capture metadata.
4. Enable **Automatic detection**, **Audio trigger**, and **Visual clap match** under **Settings → Detection**. The main state reads **ARMED**, **HOLD**, **TRIGGER OFF**, or **AUTO OFF** truthfully.
5. Review the waveform, automatic candidate thumbnail, confidence, and plain-language **AUTO RESULT**.
6. Click or drag the timeline to scrub to the nearest real temporal image. **Pre V/Nxt V**, mouse wheel, and Left/Right step one frame or field; Shift+Left/Right step five. The result becomes **MANUAL PREVIEW** and follows the playhead immediately.
7. Drag the yellow **AUDIO** marker or use **Pre A/Nxt A** (Ctrl+Left/Ctrl+Right) for deterministic 1.0 ms audio adjustments. Audio changes never move the visual playhead.
8. Press Enter to commit the playhead as **MANUAL RESULT**. The automatic candidate remains available from its thumbnail for comparison.
9. Clap again; the previous event moves into the three-item session history once.

**Manual Clap** (Space) freezes a work window around the latest rolling-buffer position. Capture and rolling buffers continue while reviewing. Press H once to toggle Hold for new automatic detections; press it again to resume. Hold never stops capture or buffering and resets off at launch.

The left side is split into a collapsible **Settings** column and an always-visible operational capture column beside the video. A theme-aware left navigation strip names Appearance, Detection, Review, Capture, Shortcuts, and About; a small chevron replaces the old large hide control and a narrow reopen rail remains when collapsed. Engineering timing/reconstruction data is confined to About. Detection groups the automatic master switches with practical visual and audio sensitivity controls. Detection and review rasters never change native capture or timestamp math. Light is the new-install default; Graphite, Midnight, High Contrast, Synthwave, Terminal, and Solar Flare remain available and persistent. Appearance offers six presentation-only waveform styles: Centered Fill, Centered Bars, Centered Line, Peak Fill, Peak Bars, and Peak Line. Its independent amplitude choices are Auto Gain, Linear, dB · 60 dB, and dB · 96 dB. Auto Gain is calculated once for the displayed review snapshot, targets a useful peak at 90% height, and deliberately leaves signals below -50 dBFS unboosted. Neither setting changes audio samples, detection, timestamps, markers, or results. The default work window is ±300 ms; previously saved values remain unchanged.

## Video timing compensation

When Kairix explicitly reconstructs two fields from one progressive woven frame, it assumes the capture device could not deliver that completed frame until both fields had arrived. The default **Video timing offset** is therefore one exact reconstructed field interval: +20.000 ms for 25p→50i and approximately +16.683 ms for 29.97p→59.94i. A positive setting represents known video delivery delay and is subtracted from the interpreted visual time for measurement. Both fields move earlier together, preserving their cadence; raw frame, field, hardware, and transport timestamps never change.

This is a documented operating assumption based on observed Magewell Weave behavior, not a claim about every capture device. Settings shows the effective value and lets the operator save a manual override per device/native-format/reconstruction/order/rate profile. **Reset to automatic** restores one field interval for explicit reconstruction and 0.000 ms for normal progressive or native-interlaced capture. The primary result and review timeline use corrected timing. Raw/corrected engineering diagnostics remain under **Settings → About**; the normal result column contains only result, visual confidence, and compact session history.

An explicitly selected resolution/rate/pixel-format combination is strict: Kairix either captures that exact negotiated size, rational rate, scan/layout, and pixel format or displays **REQUESTED CAPTURE FORMAT NOT ACCEPTED**. Initial unsaved detection may use Auto to discover a mode.

## Keyboard shortcuts

| Key | Action |
|---|---|
| Space | Manual clap/current-buffer capture |
| H | Toggle ARMED/HOLD when Automatic detection is enabled |
| Left / Right | Previous/next temporal frame or field |
| Shift+Left / Shift+Right | Step five temporal images |
| Ctrl+Left / Ctrl+Right | Move the audio mark earlier/later by 1.0 ms |
| Enter | Set the current playhead as visual contact |
| R | Resume live view |
| F5 | Reconnect selected source |

Shortcuts are ignored while a numeric/text field is being edited.

## Privacy

The application has no telemetry, analytics, accounts, cloud API, database, or network dependency. Audio, video, waveforms, thumbnails, and results remain in bounded process memory and are never written to disk. Closing the app discards all measurement history. Only the selected device and small UI/detection preferences are stored under `%LOCALAPPDATA%\Kairix\QuickAVSync`.

Diagnostic logs contain lifecycle/timing messages only, never media. They are bounded to a current and previous 1 MB file under `%LOCALAPPDATA%\Kairix\QuickAVSync\logs`.

## Build, test, and publish

```powershell
dotnet restore Kairix.QuickAVSync.sln
dotnet build src/Kairix.QuickAVSync.Core -c Release
dotnet build Kairix.QuickAVSync.sln -c Release
dotnet test tests/Kairix.QuickAVSync.Core.Tests -c Release
dotnet test tests/Kairix.QuickAVSync.Windows.Tests -c Release

# Public, self-contained x64 distribution (larger; no installed .NET required)
dotnet publish src/Kairix.QuickAVSync -p:PublishProfile=win-x64-self-contained

# Smaller single-file build (requires .NET 10 Desktop Runtime)
dotnet publish src/Kairix.QuickAVSync -p:PublishProfile=win-x64-framework-dependent
```

Self-contained is the simplest public download but includes the runtime. Framework-dependent is substantially smaller but needs the matching Windows Desktop Runtime. Outputs go to `artifacts/publish/` and are intentionally ignored by Git. `scripts/package-windows.ps1` creates both identified EXEs, a ZIP containing both variants, and `SHA256SUMS.txt`; it fails if an expected asset is missing, empty, or cannot be checksummed.

Every successful `main` push runs the full Windows validation before packaging. Its exact package set is uploaded as a commit-identified Actions artifact and then published as a uniquely tagged `dev-YYYYMMDD-<short-sha>` prerelease titled **Latest Development Build**. Rerunning the workflow cannot create a duplicate release for the same commit, and old development prereleases are retained. Recommended `v*` releases remain an explicit promotion decision and are never created automatically from `main`.

## Implemented and still under testing

- `Kairix.QuickAVSync.Core` targets plain `net10.0` and contains no WPF, Win32, Media Foundation, or WASAPI references. The WPF application composes that portable domain/analysis layer with `Kairix.QuickAVSync.Windows`.
- Timing models preserve 100 ns media time, clock domains, raw values, and device/QPC, stream timestamp, and arrival-fallback quality separately. Results are refused when clocks are not demonstrably comparable.
- Each native video sample has a stable native-sample identity and retains factual device timestamp, sample time, Source Reader timestamp, and monotonic host arrival observations. A reconstructed pair shares those observations. Only its first visual review timestamp is derived. Timing reports therefore distinguish reconstructed review cadence (for example 50 fields/s) from native transport cadence (25 samples/s), and never fabricate field-rate hardware observations.
- Frame rates are rational. Progressive, interlaced, and unknown scan metadata remain distinct; known interlaced modes preserve full-frame/single-field layout and field order instead of silently assuming progressive timing.
- Native capture and temporal interpretation are separate. Enabling reconstruction on a valid 25p or 29.97p progressive transport produces 50i or 59.94i field positions while **Driver capture** remains the real progressive Media Foundation mode.
- Visual analysis is deterministic multi-scale downscaled-luma motion analysis. Coarse evidence rejects broad camera/exposure changes and fine cells retain localized motion. Selection separates the rapid approach peak from a bounded one-to-three-position contact/settle search, chooses the earliest credible contact, and keeps expected-time proximity from forcing an approaching-hand frame. Sensitivity changes acceptance without bypassing rejection gates.
- The Windows backend uses Media Foundation source readers for video and shared-mode WASAPI for audio. Pairing prefers exact device Container IDs, then hardware parents, and uses a unique-name fallback only when unambiguous; it never silently substitutes the default microphone.
- Historical native capture validation includes an XI100DUSB HDMI device and Logitech C920 UVC webcam, and current owner testing provides stronger evidence for USB Capture SDI operation. The present HDMI setup has also shown instability; that observation is not a conclusion that the HDMI card is defective. Hardware and driver compatibility may vary.
- Native video is reduced directly from locked NV12, YUY2, UYVY, BGRA32, or RGB24 buffers to configurable luma-analysis and BGRA-review rasters (defaults 640×360 and 160×90). Packed and planar YUV analysis reads luma bytes directly without YUV-to-RGB conversion; reusable scaling maps avoid rebuilding source coordinates per frame. For woven reconstruction, even/odd source rows are selected before either downscale and bobbed independently. Luma remains the detector input; colour is used only for preview/review/thumbnail presentation. Audio is normalized from float32 or PCM16 to owned float samples.
- Reconstructed timestamps use exact rational field intervals and preserve the captured clock domain and timing quality. The current, deliberately explicit phase assumption is that the capture timestamp represents the second field/completed woven pair, so the first field is `T - one field interval` and the second is `T`. This phase has **not** been physically calibrated.
- Physical hardware behavior remains driver-dependent and is not yet certified. A Magewell 1080p25 Weave run produced two genuinely different parity-derived images per native sample at a measured 50-position/s, 20 ms review cadence. A comparison 1080p50 run retained its valid timestamps and exposed the expected A-A/B-B repeated pairs at about 25 unique images/s. Native WPF visual stepping still requires owner inspection before release.
- The unobtrusive coffee control opens `https://buymeacoffee.com/dubcodes` only after an explicit click.

See [architecture](docs/ARCHITECTURE.md), [capture backends](docs/CAPTURE_BACKENDS.md), [testing](docs/TESTING.md), and [contribution guidelines](CONTRIBUTING.md).

## License

MIT © 2026 Dubcodes. See [LICENSE](LICENSE).
