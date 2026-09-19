# Architecture

The application is a single WPF process with no server, database, or recording subsystem. UI, capture callbacks, and expensive visual analysis have deliberately separate responsibilities.

## Data flow

```text
ICaptureSession
  ├─ timestamped PCM → bounded audio buffer → TransientDetector
  │                                      └─ Audio Zero / event snapshot
  └─ timestamped luma/video → bounded video buffer ─┘
                                                   ├─ immediate waveform/review
                                                   └─ cancellable VisualClapDetector
                                                            ↓
Audio mark + auto/manual visual mark → SyncResult → result bar + session history
```

Capture callbacks only append bounded data and schedule the newest preview. They do not wait for drawing, file I/O, or vision work. Starting a newer event cancels the previous visual analysis.

## Main boundaries

- `Models/Domain.cs`: rational rates, scan/field metadata, timestamp quality, capture samples, markers, candidates, results, and persisted configuration.
- `Services/RollingBuffer.cs`: thread-safe fixed-capacity storage and timestamp range snapshots.
- `Services/CaptureServices.cs`: `ICaptureSession`, Media Foundation enumeration, device/audio pairing policy, format preference, timing normalization, and the deterministic synthetic provider.
- `Services/AnalysisServices.cs`: adaptive audio transient detection, work-window selection, sparse luma motion analysis, and session history.
- `Services/InfrastructureServices.cs`: allow-listed JSON settings, actual Windows/process memory, and bounded metadata-only logging.
- `ViewModels/MainViewModel.cs`: orchestration and UI state; it never owns an unbounded media queue.
- `Controls/`: lightweight retained WPF rendering for waveform markers and the lead/lag bar.

## Timing rules

All internal media timestamps are signed 100 ns units. `TimingQuality` distinguishes device/QPC, normalized stream/sample, and arrival fallback domains. Arrival time must never be presented as device-precision timing. `MediaTimingService` normalizes observations without consulting the WPF/UI clock.

`SyncResult` is always `visual timestamp - audio timestamp`:

- positive: sound happened first, so **audio leads video**;
- negative: visible contact happened first, so **audio lags video**;
- zero: in sync.

Frame rates use numerator/denominator. For interlaced material, `TemporalImageDuration` is half of frame duration and field order is explicit rather than inferred.

## Hardware implementation seam

`SyntheticCaptureSession` proves the downstream pipeline independently of hardware. The physical implementation should use an asynchronous Media Foundation source reader for video and WASAPI for decoded PCM, associating functions through PnP Container ID. It should publish only immutable `VideoFrame`/`AudioChunk` objects with the strongest available timestamp quality. Preview conversion should drop stale work; event snapshots should retain only bounded analysis luma and decoded PCM.

The fallback pairing policy refuses the Windows default microphone and requires a unique, meaningful name match when no Container ID is available.

## Persistence and privacy

Only `AppSettings` is serialized. Session results and all media-bearing types have no persistence path. Logging records metadata/lifecycle events and rotates at 1 MB. There are no network calls.
