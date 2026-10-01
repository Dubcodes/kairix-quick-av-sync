# Kairix Quick A/V Sync

Kairix Quick A/V Sync is a small Windows 10/11 utility for measuring the timing difference between a sharp audio transient and its visible event. It is designed for broadcast technicians testing capture cards, wireless cameras, microphones, transmitters, and converter chains.

> Current V1 status: Windows Media Foundation video capture, paired WASAPI audio, native format selection, comparable timing, and automatic/manual clap review are implemented. The native path has been physically validated on representative USB HDMI capture and UVC webcam hardware; broad device/driver compatibility remains under testing. See [CURRENT_STATE.md](CURRENT_STATE.md) for the precise boundary.

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

1. Select a video source, then choose **Auto — best native format** or one of its native capture modes. A manual capture-format choice reconnects immediately and is remembered for that device.
2. Leave **Physical Input** at **Auto / Detect**, or declare a known source standard. This declaration is labelled user-declared, is remembered per device, and never changes the capture mode.
3. With all three automatic modes enabled, clap in front of the camera.
4. Review the waveform, automatic candidate thumbnail, confidence, and plain-language **AUTO RESULT**.
5. Click or drag the timeline to scrub to the nearest real temporal image. Mouse wheel and Left/Right step one image, while Shift+Left/Right step five. The result becomes **MANUAL PREVIEW** and follows the playhead immediately.
6. Drag the yellow **AUDIO** marker (or press A at the playhead) to correct Audio Zero inside the fixed event window. The result updates live without clearing the waveform, playhead, automatic candidate, or thumbnail.
7. Press Enter to commit the playhead as **MANUAL RESULT**. The automatic candidate remains available from its thumbnail for comparison.
8. Clap again; the previous event moves into the three-item session history once.

**Manual Clap** (Space) freezes a work window around the latest rolling-buffer position. Capture and rolling buffers continue while reviewing. Press H once to toggle Hold for new automatic detections; press it again to resume. Hold never stops capture or buffering and resets off at launch.

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
- Frame rates are rational. Progressive, interlaced, and unknown scan metadata remain distinct; known interlaced modes preserve full-frame/single-field layout and field order instead of silently assuming progressive timing.
- Visual analysis is deterministic multi-scale downscaled-luma motion analysis. Coarse evidence rejects broad camera/exposure changes, fine cells retain small localized motion, and temporal rise/peak/drop evidence distinguishes likely contact from final approach. A persisted 0–100 sensitivity control changes acceptance thresholds without inflating confidence or bypassing the broad-motion/flash/noise rejection gates. It remains advisory and manually overridable.
- The Windows backend uses Media Foundation source readers for video and shared-mode WASAPI for audio. Pairing prefers exact device Container IDs, then hardware parents, and uses a unique-name fallback only when unambiguous; it never silently substitutes the default microphone.
- Native capture has been physically validated on an XI100DUSB HDMI capture device and Logitech C920 UVC webcam. It is designed for standard Windows Media Foundation/UVC capture devices; hardware and driver compatibility may vary.
- Native video is reduced directly from locked NV12, YUY2, UYVY, RGB32, or RGB24 buffers to bounded 640×360 luma frames plus a separate bounded 160×90 BGRA presentation buffer. Luma remains the detector input; colour is used only for preview/review/thumbnail presentation. Audio is normalized from float32 or PCM16 to owned float samples.
- **Input / Source** and **Capture** are separate. Capture is the authoritative negotiated Media Foundation output. Source information comes from the strongest available provider, a visibly user-declared override, or passive luma/timestamp analysis labelled as an estimate. Kairix never claims universal discovery of the physical connector format.
- Physical hardware behavior remains driver-dependent and is not yet certified. Field extraction is not applied without a driver that actually reports an interlaced layout; the currently tested USB SDI driver reports progressive Media Foundation output for every enumerated mode, including 1080/50. Broader native formats, reconnect after hot-unplug, and broad capture-card compatibility validation remain V1 work.
- The unobtrusive coffee control opens `https://buymeacoffee.com/dubcodes` only after an explicit click.

See [architecture](docs/ARCHITECTURE.md), [capture backends](docs/CAPTURE_BACKENDS.md), [testing](docs/TESTING.md), and [contribution guidelines](CONTRIBUTING.md).

## License

MIT © 2026 Dubcodes. See [LICENSE](LICENSE).
