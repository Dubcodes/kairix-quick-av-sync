# Kairix Quick A/V Sync

Kairix Quick A/V Sync is a small Windows 10/11 utility for measuring the timing difference between a sharp audio transient and its visible event. It is designed for broadcast technicians testing capture cards, wireless cameras, microphones, transmitters, and converter chains.

> Current V1 status: Windows Media Foundation video capture, paired WASAPI audio, native format selection, comparable timing, and automatic/manual clap review are implemented. The native path has been physically validated on representative USB HDMI capture and UVC webcam hardware; broad device/driver compatibility remains under testing. See [CURRENT_STATE.md](CURRENT_STATE.md) for the precise boundary.

## Download

### Recommended

**Kairix Quick A/V Sync v0.1.0-alpha** — [self-contained Windows x64 EXE](https://github.com/Dubcodes/kairix-quick-av-sync/releases/download/v0.1.0-alpha/Kairix.QuickAVSync-v0.1.0-alpha-win-x64.exe) — **recommended for most users**.

This is the deliberately conservative public download. It remains recommended while newer builds receive practical testing.

### Latest development

**Latest Development Build** — [open the newest public development prerelease](https://github.com/Dubcodes/kairix-quick-av-sync/releases) and choose the self-contained `dev-YYYYMMDD-<commit>-win-x64.exe` asset.

Development builds are built from validated current `main`, identify their exact source commit, and contain the newest changes. They may be less stable or include recently changed or unfinished functionality. Use the recommended release above when stability is more important. Both the self-contained EXE and a smaller framework-dependent EXE are provided publicly; at least the five newest successful development builds remain available for rollback.

The self-contained EXE needs no separate .NET installation or installer. Administrator rights are not normally expected, although Windows privacy settings and capture-device drivers still control hardware access. This is an unsigned public alpha, so Windows SmartScreen may show an **Unknown Publisher** warning. Download builds only from this repository's Releases page. Hardware compatibility remains under active testing.

First run:

1. Download and run the self-contained EXE.
2. Select the capture device and confirm that **Detected Capture** matches the Media Foundation mode actually delivered.
3. Choose **Resolution** and **Format** under **Input**, with **Pixel format** under **Settings → Capture**. These controls resolve to one exact native capture mode and are remembered per device.
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

When no working hardware backend is available, the app selects **Synthetic A/V test source**. It creates a transient and matching visual event every five seconds, allowing the complete detection, timeline, review, override, and history workflow to be exercised without a capture card.

## Workflow

1. Select a video source. **Detected Capture** is read-only and always describes the authoritative Media Foundation output using the real raster, numeric rate/scan, and pixel format.
2. Choose the interpretation **Resolution**, **Format**, and **Pixel format**. A valid combination reconnects to the matching exact native mode; unavailable combinations are not offered and explicit selection never silently falls back.
3. For woven capture such as `1920×1080 · 25.000p · YUY2` containing a 50i source, enable **Reconstruct interlaced fields** and select `25.000p → 50.000i · Top first` or `Bottom first`. This changes review cadence, not the authoritative detected capture metadata.
4. Enable **Automatic detection**, **Audio trigger**, and **Visual clap match** under **Settings → Detection**. The main state reads **ARMED**, **HOLD**, or **AUTO OFF** truthfully.
5. Review the waveform, automatic candidate thumbnail, confidence, and plain-language **AUTO RESULT**.
6. Click or drag the timeline to scrub to the nearest real temporal image. **Pre V/Nxt V**, mouse wheel, and Left/Right step one frame or field; Shift+Left/Right step five. The result becomes **MANUAL PREVIEW** and follows the playhead immediately.
7. Drag the yellow **AUDIO** marker or use **Pre A/Nxt A** (Ctrl+Left/Ctrl+Right) for deterministic 1.0 ms audio adjustments. Audio changes never move the visual playhead.
8. Press Enter to commit the playhead as **MANUAL RESULT**. The automatic candidate remains available from its thumbnail for comparison.
9. Clap again; the previous event moves into the three-item session history once.

**Manual Clap** (Space) freezes a work window around the latest rolling-buffer position. Capture and rolling buffers continue while reviewing. Press H once to toggle Hold for new automatic detections; press it again to resume. Hold never stops capture or buffering and resets off at launch.

The left side is split into a collapsible tabbed **Settings** column and an always-visible operational capture column beside the video. Its vertical rail groups Appearance, Detection, Review, Capture, Shortcuts, and About; a narrow rail remains when collapsed. Engineering timing/reconstruction data is confined to About. Detection and review rasters never change native capture or timestamp math. Graphite, Midnight, Light, High Contrast, Synthwave, Terminal, and Solar Flare switch immediately and persist. Mirrored, Filled, and Line waveform styles are presentation-only and never modify samples, detection, or timing.

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
- Native capture and temporal interpretation are separate. Enabling reconstruction on a valid 25p or 29.97p progressive transport produces 50i or 59.94i field positions while **Detected Capture** remains the real progressive Media Foundation mode.
- Visual analysis is deterministic multi-scale downscaled-luma motion analysis. Coarse evidence rejects broad camera/exposure changes and fine cells retain localized motion. Selection separates the rapid approach peak from a bounded one-to-three-position contact/settle search, chooses the earliest credible contact, and keeps expected-time proximity from forcing an approaching-hand frame. Sensitivity changes acceptance without bypassing rejection gates.
- The Windows backend uses Media Foundation source readers for video and shared-mode WASAPI for audio. Pairing prefers exact device Container IDs, then hardware parents, and uses a unique-name fallback only when unambiguous; it never silently substitutes the default microphone.
- Native capture has been physically validated on an XI100DUSB HDMI capture device and Logitech C920 UVC webcam. It is designed for standard Windows Media Foundation/UVC capture devices; hardware and driver compatibility may vary.
- Native video is reduced directly from locked NV12, YUY2, UYVY, BGRA32, or RGB24 buffers to configurable luma-analysis and BGRA-review rasters (defaults 640×360 and 160×90). Packed and planar YUV analysis reads luma bytes directly without YUV-to-RGB conversion; reusable scaling maps avoid rebuilding source coordinates per frame. For woven reconstruction, even/odd source rows are selected before either downscale and bobbed independently. Luma remains the detector input; colour is used only for preview/review/thumbnail presentation. Audio is normalized from float32 or PCM16 to owned float samples.
- Reconstructed timestamps use exact rational field intervals and preserve the captured clock domain and timing quality. The current, deliberately explicit phase assumption is that the capture timestamp represents the second field/completed woven pair, so the first field is `T - one field interval` and the second is `T`. This phase has **not** been physically calibrated.
- Physical hardware behavior remains driver-dependent and is not yet certified. A Magewell 1080p25 Weave run produced two genuinely different parity-derived images per native sample at a measured 50-position/s, 20 ms review cadence. A comparison 1080p50 run retained its valid timestamps and exposed the expected A-A/B-B repeated pairs at about 25 unique images/s. Native WPF visual stepping still requires owner inspection before release.
- The unobtrusive coffee control opens `https://buymeacoffee.com/dubcodes` only after an explicit click.

See [architecture](docs/ARCHITECTURE.md), [capture backends](docs/CAPTURE_BACKENDS.md), [testing](docs/TESTING.md), and [contribution guidelines](CONTRIBUTING.md).

## License

MIT © 2026 Dubcodes. See [LICENSE](LICENSE).
