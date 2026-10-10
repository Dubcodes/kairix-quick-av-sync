# Kairix Quick A/V Sync v0.1.1-alpha

This is the recommended public alpha for Windows 10/11 x64. The primary download is a self-contained executable and does not require a separate .NET installation.

## Included workflow

- native capture-device and format selection;
- paired capture audio;
- continuous automatic A/V detection with Human Clap or Flash / Test Signal;
- waveform, timeline, marker, and manual-result review;
- bounded in-memory result history and persisted application preferences.

## Physical validation

The release candidate was physically validated with a Logitech HD Pro Webcam C920 at 1920×1080 30p NV12 and its paired microphone. Live video and audio, repeated Flash / Test Signal detections, ARMED continuity, history, manual review, reconnect, clean shutdown, and immediate restart were exercised for this release.

## Known limitations

- Broad capture-device and driver compatibility remains under development.
- Magewell USB Capture SDI testing has shown unresolved intermittent long-run capture/stall behaviour in the current development environment. The C920 validation does not validate the Magewell path.
- Newer clock-integrity and capture-lifecycle work exists separately and is not included in this conservative release.
- This remains alpha software; it is not production-certified or universally compatible.
