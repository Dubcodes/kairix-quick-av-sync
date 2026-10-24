# Kairix Quick A/V Sync v1.0.0-beta.1

Kairix Quick A/V Sync v1.0.0-beta.1 is the first public Beta for Windows 10/11 x64. The normal download is the self-contained EXE; it does not require a separate .NET installation.

## Highlights

- Media Foundation video capture with paired shared-mode WASAPI audio and automatic audio-transient detection.
- Auto capture-mode selection or a strict exact native capture mode, with separate **Driver capture**, **Observed delivery**, and **Source cadence** reporting.
- Human Clap and Flash / Test Signal visual detectors, plus explicit manual review and fallback when an automatic visual match is unavailable.
- A live mono/stereo audio meter showing the actual sample rate and channel layout, including during Hold and historical review.
- Bounded RAM-only session history configurable to 3, 5, 10, or 20 results. Clickable cards and the interactive trend graph reopen the retained review in Hold; graph points support signed-value hover, click-to-review, and a compact Y-axis reference.
- Woven-field reconstruction for supported transports such as 25p to 50i, with explicit field order and separate measurement timing compensation.
- Multiple persistent themes and a standalone browser A/V test signal supplied independently and inside the ZIP.
- No telemetry, account, cloud service, database, or captured-media upload. Captured review/history media remains bounded in process memory and is discarded when the app closes.

## Beta limitations

- Capture-card and driver compatibility varies. Current physical evidence is strongest for USB Capture SDI, not every Windows capture path.
- The executable is unsigned, so Windows SmartScreen may show an **Unknown Publisher** warning.
- Human Clap is substantially improved but remains under practical tuning. The current 104-event real-clap fixture evidence (62 confirmed, 6 false positives, no duplicates, and 13 of 38 manually resolved contacts within one temporal position) is useful validation, not universal detection certification.
- Reconstructed-field timestamp phase and compensation assumptions are documented but are not universally physically calibrated across hardware.
- Media Foundation or device-specific stalls may depend on the particular hardware, driver, USB path, and signal state.

This Beta is not production certification. For critical measurements, verify the negotiated capture mode, repeat the cue, and compare against a known external reference.
