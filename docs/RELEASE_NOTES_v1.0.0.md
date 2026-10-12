# Kairix Quick A/V Sync v1.0.0

Kairix Quick A/V Sync v1.0.0 is the first stable Windows release. It measures audio/video timing around a clap, flash, or test signal, while keeping capture media and review history in bounded process memory.

## Highlights

- Continuous automatic detection with explicit Armed and Hold states.
- Manual timeline review with independent visual and audio markers.
- Bounded, configurable 3/5/10/20-event session history with immutable in-memory review snapshots.
- Clickable historical results that reopen the exact retained review in Hold.
- Compact session-history trend graph with a signed zero reference and no additional media retention.
- Exact native capture-mode selection and transport-specific interpretation choices, including supported woven-field reconstruction.
- Live mono/stereo input audio metering with actual sample-rate and channel display, continuing during Hold and history review.
- Separate **Driver capture**, **Observed delivery**, and **Source cadence** reporting; source cadence remains Unknown or explicitly estimated unless authoritative evidence exists.
- Editable 0–100 visual and audio sensitivity controls, structured shortcut help, persistent themes, and configurable review presentation.
- Human Clap sensitivity controls acceptance while approach, contact, settle, and broad-motion rejection remain active across the full range.
- A standalone browser A/V test signal supplied independently and inside the ZIP.

## Download

The normal Windows 10/11 x64 download is `Kairix.QuickAVSync-v1.0.0-win-x64.exe`. It is self-contained and does not require a separate .NET installation. The release also contains:

- `Kairix-Quick-AV-Sync-v1.0.0-win-x64.zip`, containing the EXE, browser test signal, README, and license;
- `Kairix-AV-Sync-Test-Signal.html`, byte-identical to the canonical repository test signal;
- `SHA256SUMS.txt`, covering the EXE, ZIP, and independent HTML file.

## Important limitations

- The application is unsigned, so Windows SmartScreen may show an Unknown Publisher warning.
- Capture-device and driver behavior varies. USB Capture SDI has the strongest current field evidence; historical HDMI and Logitech UVC evidence is documented, but this is not universal hardware certification.
- Reconstructed-field phase and automatic video delivery compensation are explicit operating assumptions and can require operator confirmation for a particular device and signal path.
- Source cadence remains Unknown unless authoritative metadata or sufficient passive evidence exists.
- Session media and history are intentionally not persisted. Closing the application discards them.
- There is no telemetry, cloud service, account, or automatic upload.

Use the selected device's exact native transport, verify the negotiated **Driver capture** value, then choose only an **Interpretation** offered for that transport. For critical measurements, repeat the cue and confirm the result trend rather than relying on a single event.
