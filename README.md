# Kairix Quick A/V Sync

Kairix Quick A/V Sync is a small Windows 10/11 utility for measuring the timing difference between a sharp audio transient and its visible event. It is designed for broadcast technicians testing capture cards, wireless cameras, microphones, transmitters, and converter chains.

> Current V1 status: the full UI and capture-free synthetic workflow run today. Native Windows Media Foundation video streaming, WASAPI audio capture, and conservative device pairing are implemented. The native path still needs validation against representative physical capture cards and drivers. See [CURRENT_STATE.md](CURRENT_STATE.md) for the precise boundary.

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

1. Select a video source and choose **Reconnect**.
2. With all three automatic modes enabled, clap in front of the camera.
3. Review the waveform, automatic candidate thumbnail, confidence, and plain-language result.
4. Use Left/Right to inspect temporal images and Enter to replace the automatic visual mark.
5. Click the waveform to replace Audio Zero when Auto Spike chose the wrong point.
6. Clap again; the previous result moves into the three-item session history.

**Manual Clap** (Space) freezes a work window around the latest rolling-buffer position. Capture and rolling buffers continue while reviewing. Holding H pauses new automatic detections without stopping capture.

## Keyboard shortcuts

| Key | Action |
|---|---|
| Space | Manual clap/current-buffer capture |
| H (hold) | Pause Auto Detect; release to resume |
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

## Design notes and limitations

- `Kairix.QuickAVSync.Core` targets plain `net10.0` and contains no WPF, Win32, Media Foundation, or WASAPI references. The WPF application composes that portable domain/analysis layer with `Kairix.QuickAVSync.Windows`.
- Timing models preserve 100 ns media time, clock domains, raw values, and device/QPC, stream timestamp, and arrival-fallback quality separately. Results are refused when clocks are not demonstrably comparable.
- Frame rates are rational; interlaced formats model two temporal fields and field order.
- Visual analysis is deterministic, downscaled-luma motion analysis constrained to the audio-centered work window. It is intentionally conservative and always overridable.
- The Windows backend uses Media Foundation source readers for video and shared-mode WASAPI for audio. Pairing prefers exact device Container IDs, then hardware parents, and uses a unique-name fallback only when unambiguous; it never silently substitutes the default microphone.
- Native video is reduced directly from locked NV12, YUY2, or RGB32 buffers to bounded 320×180 luma frames. Audio is normalized from float32 or PCM16 to owned float samples.
- Physical hardware behavior remains driver-dependent and is not yet certified. Deinterlacing/field extraction, broader native formats, reconnect after hot-unplug, and capture-card validation remain V1 work.
- The coffee control remains disabled until the repository owner sets `AppConstants.BuyMeACoffeeUrl`.

See [architecture](docs/ARCHITECTURE.md), [capture backends](docs/CAPTURE_BACKENDS.md), [testing](docs/TESTING.md), and [contribution guidelines](CONTRIBUTING.md).

## License

MIT. The repository owner must replace the marked copyright-holder placeholder in [LICENSE](LICENSE) before the first public release.
