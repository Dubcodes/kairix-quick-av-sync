# Testing

## Automated suites

```powershell
dotnet restore Kairix.QuickAVSync.sln
dotnet build src/Kairix.QuickAVSync.Core/Kairix.QuickAVSync.Core.csproj -c Release
dotnet build Kairix.QuickAVSync.sln -c Release
dotnet test tests/Kairix.QuickAVSync.Core.Tests -c Release
dotnet test tests/Kairix.QuickAVSync.Windows.Tests -c Release
```

The Core tests target plain `net10.0`. They cover rolling buffers, sync convention/wording, incomparable clocks, rational/interlaced timing, transient detection, work windows, waveform placement, history, visual analysis, startup device ranking, physical pairing, clock mapping/discontinuities, supersession, and known positive/negative synthetic offsets. An architecture test rejects Windows Desktop assembly references.

Windows tests cover settings allow-list persistence and execute real read-only MMDevice and Media Foundation enumeration on the test host. They do not open hardware or prove capture functionality.

On this mapped workspace drive, `dotnet` project graph operations can intermittently fail without diagnostics under parallel MSBuild. Use `-m:1` for a deterministic local full-solution build; GitHub Actions uses a normal local runner filesystem.

## Capture-free application test

1. Start the app and select **Synthetic A/V test source (+60 ms video)**.
2. Confirm live motion and `READY TO CLAP`.
3. Wait for the generated event and confirm a positive audio-leads result near +60 ms.
4. Confirm the automatic thumbnail remains fixed while Left/Right changes the main viewer.
5. Press Enter and verify only the effective visual mark/result changes.
6. Click the waveform or press A and verify Audio Zero/result changes.
7. Hold H across the next event and confirm no new test appears while preview continues.
8. Press R, Space, and F5 to verify resume, manual capture, and reconnect.
9. Close/reopen and verify preferences persist while history/media do not.

## Concurrency regression

Run the Core suite repeatedly after changing event analysis:

```powershell
1..10 | ForEach-Object { dotnet test tests/Kairix.QuickAVSync.Core.Tests -c Release --no-build }
```

The analysis-generation test proves the acceptance rule independently; application smoke testing should also trigger closely spaced synthetic/manual events and confirm no old thumbnail/result returns.

## Physical hardware

Use the checklist in `CURRENT_STATE.md`. Save diagnostic text only. For each format record the card/driver identity, paired endpoint, Container IDs, pixel subtype, frame-rate rational, scan/field metadata, active video timestamp source, audio QPC status, discontinuities, reconnect behavior, and an external known-delay comparison.

Passing enumeration tests or seeing a picture is not enough to claim calibrated A/V timing.
