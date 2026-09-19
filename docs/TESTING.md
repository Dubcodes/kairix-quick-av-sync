# Testing

## Automated

Run on Windows with the .NET 10 SDK:

```powershell
dotnet restore tests/Kairix.QuickAVSync.Tests/Kairix.QuickAVSync.Tests.csproj
dotnet test tests/Kairix.QuickAVSync.Tests/Kairix.QuickAVSync.Tests.csproj -c Release
```

The deterministic suite covers rolling-buffer capacity/wrap/resize/ranges, sync sign and exact wording, rational rates, interlaced field cadence, transient detection with silence/noise/impulses/echo/multiple rates, work-window extraction, three-item session history, allow-listed settings, and synthetic visual sequences. The visual fixtures validate basic behavior, not real-world hand detection accuracy.

## Capture-free manual test

1. Start the app; select **Synthetic A/V test source** and reconnect.
2. Confirm live motion and `READY TO CLAP`.
3. Wait up to five seconds for the transient.
4. Confirm review appears immediately, then an automatic thumbnail/result appears.
5. Step with arrows, press Enter, and verify the result changes.
6. Click a different waveform point and confirm Audio Zero/result recalculate.
7. Hold H across the next synthetic event and confirm no new result is created while capture continues.
8. Press R, Space, and F5 to verify live resume, manual capture, and reconnect.
9. Close/reopen and confirm preferences persist but history/media do not.

## Hardware matrix (required before claiming capture support)

Record device and driver versions but do not commit captured media. For every tested format, note negotiated subtype, frame-rate rational, scan mode/field order, video sample time, device timestamp availability, audio clock source, measured discontinuities, and reconnect behavior.

Minimum cases:

- integrated webcam (listed, not auto-preferred);
- one USB HDMI/UVC card with embedded audio;
- two simultaneous capture cards;
- 1080p25, 1080i50, and 1080p50;
- unplug/replug, busy device, camera privacy denied, missing paired audio, and stalled stream;
- 1, 5, and 30 second rolling buffers;
- quiet room, ordinary background noise, clap echo, and several claps near the refractory boundary.

Timing confidence must be checked against an external known-delay generator or oscilloscope-style reference. A visually plausible number is not proof of timestamp correctness.
