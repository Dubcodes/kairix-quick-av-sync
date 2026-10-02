# Kairix Quick A/V Sync

Kairix Quick A/V Sync is a small Windows 10/11 utility for measuring the timing difference between a sharp audio transient and its visible event. It is designed for broadcast technicians testing capture cards, wireless cameras, microphones, transmitters, and converter chains.

> Current V1 status: Windows Media Foundation video capture, paired WASAPI audio, native format selection, comparable timing, and automatic/manual clap review are implemented. The native path has been physically validated on representative USB HDMI capture and UVC webcam hardware; broad device/driver compatibility remains under testing. See [CURRENT_STATE.md](CURRENT_STATE.md) for the precise boundary.

## Download

**Windows 10/11 x64:** download the current alpha from [GitHub Releases](https://github.com/Dubcodes/kairix-quick-av-sync/releases).

The self-contained EXE needs no separate .NET installation or installer. Administrator rights are not normally expected, although Windows privacy settings and capture-device drivers still control hardware access. This is an unsigned public alpha, so Windows SmartScreen may show an **Unknown Publisher** warning. Download builds only from this repository's Releases page. Hardware compatibility remains under active testing.

First run:

1. Download and run the self-contained EXE.
2. Select the capture device and confirm that **Detected Capture** matches the Media Foundation mode actually delivered.
3. Choose **Resolution** and **Format** under **Input Interpretation**, with **Pixel format** in the collapsible **Settings** column. These controls resolve to one exact native capture mode and are remembered per device.
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
4. With all three automatic modes enabled, clap in front of the camera.
5. Review the waveform, automatic candidate thumbnail, confidence, and plain-language **AUTO RESULT**.
6. Click or drag the timeline to scrub to the nearest real temporal image. Mouse wheel and Left/Right step one frame or field, while Shift+Left/Right step five. The result becomes **MANUAL PREVIEW** and follows the playhead immediately.
7. Drag the yellow **AUDIO** marker (or press A at the playhead) to correct Audio Zero inside the fixed event window. The result updates live without clearing the waveform, playhead, automatic candidate, or thumbnail.
8. Press Enter to commit the playhead as **MANUAL RESULT**. The automatic candidate remains available from its thumbnail for comparison.
9. Clap again; the previous event moves into the three-item session history once.

**Manual Clap** (Space) freezes a work window around the latest rolling-buffer position. Capture and rolling buffers continue while reviewing. Press H once to toggle Hold for new automatic detections; press it again to resume. Hold never stops capture or buffering and resets off at launch.

The left side is split into a collapsible **Settings** column and an always-visible operational capture column beside the video. Settings provides independent **Detection resolution** (luma used by the detector) and **Review image resolution** (BGRA used by live preview, stepping, and thumbnails). These never change the native capture raster. Higher detection resolution examines more spatial detail; either higher raster increases rolling-buffer memory as frames arrive. The live image-buffer estimate warns above roughly 512 MB and more strongly above 1 GB without silently reducing quality. Graphite is the default theme; Midnight, Light, and High Contrast switch immediately and persist.

An explicitly selected resolution/rate/pixel-format combination is strict: Kairix either captures that exact negotiated size, rational rate, scan/layout, and pixel format or displays **REQUESTED CAPTURE FORMAT NOT ACCEPTED**. Initial unsaved detection may use Auto to discover a mode.

## Keyboard shortcuts

| Key | Action |
|---|---|
| Space | Manual clap/current-buffer capture |
| H | Toggle Hold Auto Detect |
| Left / Right | Previous/next temporal frame or field |
| Shift+Left / Shift+Right | Step five temporal images |
| Enter | Set the current playhead as visual contact |
| A | Move Audio Zero to the current temporal playhead |
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

Self-contained is the simplest public download but includes the runtime. Framework-dependent is substantially smaller but needs the matching Windows Desktop Runtime. Outputs go to `artifacts/publish/` and are intentionally ignored by Git.

## Implemented and still under testing

- `Kairix.QuickAVSync.Core` targets plain `net10.0` and contains no WPF, Win32, Media Foundation, or WASAPI references. The WPF application composes that portable domain/analysis layer with `Kairix.QuickAVSync.Windows`.
- Timing models preserve 100 ns media time, clock domains, raw values, and device/QPC, stream timestamp, and arrival-fallback quality separately. Results are refused when clocks are not demonstrably comparable.
- Each native video sample has a stable native-sample identity and retains factual device timestamp, sample time, Source Reader timestamp, and monotonic host arrival observations. A reconstructed pair shares those observations. Only its first visual review timestamp is derived. Timing reports therefore distinguish reconstructed review cadence (for example 50 fields/s) from native transport cadence (25 samples/s), and never fabricate field-rate hardware observations.
- Frame rates are rational. Progressive, interlaced, and unknown scan metadata remain distinct; known interlaced modes preserve full-frame/single-field layout and field order instead of silently assuming progressive timing.
- Native capture and temporal interpretation are separate. Enabling reconstruction on a valid 25p or 29.97p progressive transport produces 50i or 59.94i field positions while **Detected Capture** remains the real progressive Media Foundation mode.
- Visual analysis is deterministic multi-scale downscaled-luma motion analysis. Coarse evidence rejects broad camera/exposure changes, fine cells retain small localized motion, and temporal rise/peak/drop evidence distinguishes likely contact from final approach. A persisted 0–100 sensitivity control changes acceptance thresholds without inflating confidence or bypassing the broad-motion/flash/noise rejection gates. It remains advisory and manually overridable.
- The Windows backend uses Media Foundation source readers for video and shared-mode WASAPI for audio. Pairing prefers exact device Container IDs, then hardware parents, and uses a unique-name fallback only when unambiguous; it never silently substitutes the default microphone.
- Native capture has been physically validated on an XI100DUSB HDMI capture device and Logitech C920 UVC webcam. It is designed for standard Windows Media Foundation/UVC capture devices; hardware and driver compatibility may vary.
- Native video is reduced directly from locked NV12, YUY2, UYVY, BGRA32, or RGB24 buffers to configurable luma-analysis and BGRA-review rasters (defaults 640×360 and 160×90). Packed and planar YUV analysis reads luma bytes directly without YUV-to-RGB conversion; reusable scaling maps avoid rebuilding source coordinates per frame. For woven reconstruction, even/odd source rows are selected before either downscale and bobbed independently. Luma remains the detector input; colour is used only for preview/review/thumbnail presentation. Audio is normalized from float32 or PCM16 to owned float samples.
- Reconstructed timestamps use exact rational field intervals and preserve the captured clock domain and timing quality. The current, deliberately explicit phase assumption is that the capture timestamp represents the second field/completed woven pair, so the first field is `T - one field interval` and the second is `T`. This phase has **not** been physically calibrated.
- Physical hardware behavior remains driver-dependent and is not yet certified. A Magewell 1080p25 Weave run produced two genuinely different parity-derived images per native sample at a measured 50-position/s, 20 ms review cadence. A comparison 1080p50 run retained its valid timestamps and exposed the expected A-A/B-B repeated pairs at about 25 unique images/s. Native WPF visual stepping still requires owner inspection before release.
- The unobtrusive coffee control opens `https://buymeacoffee.com/dubcodes` only after an explicit click.

See [architecture](docs/ARCHITECTURE.md), [capture backends](docs/CAPTURE_BACKENDS.md), [testing](docs/TESTING.md), and [contribution guidelines](CONTRIBUTING.md).

## License

MIT © 2026 Dubcodes. See [LICENSE](LICENSE).
