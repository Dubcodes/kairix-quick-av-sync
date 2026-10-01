# Kairix Quick A/V Sync v0.1.0-alpha

This is the first public alpha for Windows 10/11 x64. It is intended for testing with real capture hardware; compatibility and detection quality will vary by device, driver, signal chain, and scene.

## Highlights

- Self-contained single-file Windows executable; no separate .NET installation or installer required.
- Media Foundation video capture with paired WASAPI audio and device/QPC timing where hardware exposes comparable timestamps.
- Automatic and manual clap measurement, waveform/timeline review, Audio marker correction, manual visual results, sensitivity control, colour preview, and RAM status.
- Native capture-format selection with a concise recommended broadcast-format list plus **Show all formats** for every supported driver mode.
- Separate physical Input/Source and Capture Output reporting, per-device manual source declarations, passive estimated source cadence, and source-aware Auto capture selection for authoritative or user-declared sources.
- Deterministic application shutdown even when the window is closed during device startup or reconnect.
- No telemetry, cloud processing, accounts, or saved audio/video media.

## First run

1. Download `Kairix.QuickAVSync-v0.1.0-alpha-win-x64.exe` from this release.
2. Run the executable and select the capture device.
3. Leave **Capture Format** on **Auto** initially.
4. If the physical source is known but unavailable from the device, optionally declare it under **Physical Input**, then press **Reconnect** so Auto can prefer a matching capture output.
5. Clap and review the reported sync result. Manual timeline scrubbing and Audio/Visual corrections remain available.

## Alpha limitations

- Capture-card compatibility varies by driver. Direct NV12, YUY2, UYVY, RGB32, and RGB24 paths are supported; devices that expose only compressed MJPEG/H.264 modes are not yet supported.
- The physical connector signal may remain Unknown. Passive cadence and interlace results are estimates and cannot always recover information after device conversion or deinterlacing.
- Automatic visual clap detection may require sensitivity tuning and manual review.
- True interlaced-field extraction has not yet been physically validated on hardware that exposes actual interlaced samples.
- Hot-unplug and automatic recovery remain limited; manual Refresh/Reconnect may be required.
- The executable is currently unsigned. Windows SmartScreen may show an **Unknown Publisher** warning. Download builds only from the official [Kairix Quick A/V Sync releases](https://github.com/Dubcodes/kairix-quick-av-sync/releases).
