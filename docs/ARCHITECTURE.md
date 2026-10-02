# Architecture

Kairix Quick A/V Sync now has three deliberate layers.

```text
Windows Media Foundation + WASAPI ─┐
future macOS AVFoundation ────├─> ICaptureBackend / common samples
future Linux backend ───────┘                 │
                                                   ▼
                                      Kairix.QuickAVSync.Core
                                  timing / buffers / clap analysis
                                                   │
                                                   ▼
                                             sync result
```

## Projects

### `Kairix.QuickAVSync.Core` (`net10.0`)

Portable engine with no Windows Desktop dependency:

- capture/session and detector contracts;
- platform-neutral device, format, audio, video, status, and timestamp models;
- rational frame rates, field identity, and centralized reconstructed-field timestamps;
- bounded rolling buffers and work-window selection;
- adaptive PCM transient detector and waveform builder;
- luminance-only `IVisualClapDetector` with the motion implementation;
- clock-domain correlation, discontinuity detection, and quality propagation;
- device ranking, conservative audio/video pairing, sync wording, and history;
- deterministic synthetic backend and known-offset fixtures;
- analysis-generation guard for stale-result rejection.
- platform-neutral input-signal provenance/authority/lock models, provider coordination, passive cadence analysis, and source-aware format recommendations.
- platform-neutral capture-format classification that keeps common HD and conventional SD modes concise while retaining every driver mode for advanced access.

Core samples safely own their small arrays. Backends must not mutate arrays after publishing them. The Windows backend extracts 640×360 analysis luma plus a separate 160×90 BGRA presentation image before crossing the boundary, avoiding a five-second buffer of full uncompressed 1080 frames. Luma alone is consumed by automatic visual analysis. Every `VideoFrame` identifies a progressive frame, top field, or bottom field and whether its timestamp is direct, reconstructed, or the capture timestamp currently assumed to represent the second field.

### `Kairix.QuickAVSync.Windows` (`net10.0-windows`)

Windows infrastructure:

- Media Foundation activation, native type selection, synchronous source-reader worker, sample/device timestamp extraction, native row-parity field separation, bob, and luma/presentation conversion;
- Config Manager/SetupAPI Container ID lookup;
- MMDevice endpoint/property enumeration;
- WASAPI shared-mode PCM capture with endpoint QPC timestamps;
- Windows memory/process status, bounded local logging, and settings persistence.

Media Foundation native-mode diagnostics retain whether the interlace attribute was present, its raw value, interpreted scan/layout/order, and the final negotiated media type. Missing metadata never silently becomes progressive.

## Source identification

Kairix deliberately separates the authoritative Media Foundation **capture output** from the pre-conversion **physical input signal** and from **observed estimates**. `IInputSignalProvider` permits generic Windows metadata and future optional vendor adapters without putting vendor code in Core. `InputSignalCoordinator` applies explicit precedence: authoritative standard device metadata, authoritative vendor metadata, user declaration, then high/medium/low observed estimates. A provider exception is isolated and never prevents capture.

The generic Windows provider currently returns unavailable because no universal documented Windows contract exposes connector lock, source dimensions, cadence, and scan state across ordinary capture hardware. Passive `ObservedSignalAnalyzer` therefore samples the bounded luma stream in the background. It measures timestamp cadence/jitter, shared consecutive-frame content evidence, periodic paired repeats, and alternating row-parity evidence. Temporal cadence and scan mode remain separate; absence of interlace evidence never proves progressive scan. Static and sensor-noise-only scenes do not satisfy the activity gate needed for a repeat/cadence conclusion.

Passive source estimates never change capture or reconstruction settings. A strong A-A/B-B cadence may suggest a half-rate progressive transport plus field reconstruction, but operator intent is required. No vendor runtime is a hard dependency.

`CaptureOpenOptions` keeps the exact native capture mode separate from optional `FieldReconstructionOptions`. The latter carries the progressive transport rate, target field rate, and field order without changing `ICaptureSession.CurrentFormat`, which remains the authoritative Media Foundation result.

### `Kairix.QuickAVSync` (`net10.0-windows`, WPF)

Views, custom waveform/sync controls, commands, presentation state, WPF image conversion, and composition of the Core and Windows backends. WPF remains the right V1 choice; this pass deliberately did not introduce another UI framework.

## Capture and timing flow

```text
ICaptureSession
  ├─ VideoFrame (frame/field identity + small owned luma + bounded BGRA presentation + timestamp/domain)
  │      ├─ bounded rolling buffer
  │      ├─ coalesced latest preview
  │      └─ cancellable work-window visual analysis
  └─ AudioChunk (float PCM + timestamp/domain)
         ├─ bounded rolling buffer
         └─ adaptive transient detector → Audio Zero

Fixed event reference + independently movable Audio/Auto/Manual/Playhead marks → selected result mode → comparable-domain check → SyncResult
```

Windows video prefers `MFSampleExtension_DeviceTimestamp`, which Microsoft defines as the QPC-epoch MFTIME domain in 100 ns units. WASAPI capture's QPC position is also delivered in 100 ns units. Those observations are comparable. `IMFSample::GetSampleTime` is retained as a stream-relative fallback but is not silently compared with endpoint QPC. See [capture backends](CAPTURE_BACKENDS.md).

Explicit native-mode selection is strict. The Windows backend attempts only the exact stable mode ID and reads the active media type back after `SetCurrentMediaType`; disagreement in dimensions, rational rate, scan/layout/field order, or pixel format fails the open. Auto remains the only fallback path.

For explicit woven-field reconstruction, the authoritative capture mode remains progressive (for example, 1920×1080 25p YUY2) while the interpreted review cadence becomes interlaced (50i). The locked native buffer is converted twice: even or odd source rows are selected before downscale, and each selected parity is expanded independently with deterministic nearest-line bob into both analysis and presentation images. No full woven frame is downscaled and split later, and the two temporal positions do not share image content.

`ReconstructedFieldTimestampModel` derives the exact field interval from the target rational rate. The current phase assumption is `CaptureTimestampRepresentsSecondField`: the configured first field receives `T - field interval`, and the configured second field receives `T`. Bottom-first changes actual temporal ordering, not merely labels. Derived observations retain clock domain and timing quality, while timestamp-origin metadata avoids implying that both fields were timestamped directly by hardware. This phase assumption has not yet been physically calibrated.

Every Windows video frame carries platform-neutral observations for DeviceTimestamp, sample time, Source Reader time, and monotonic host arrival. Core analyzes original capture order before review sorting, collapses exact duplicate primary timestamps to one navigation position, invalidates backwards/duplicate timestamp measurements and gross declared/observed cadence mismatch, and retains distinct-time near-identical images as diagnostic evidence. `FrameContentAnalyzer` is shared by passive and frozen-timeline paths; it may report repeated pairs and estimated unique-image rate but never synthesizes fields or deletes samples.

In reconstruction mode the capture worker emits two field frames per native sample. Rolling-buffer capacity, `FrameTimingAnalyzer`, review navigation, and visual clap detection use the interpreted field rate, while audio remains unchanged on the WASAPI QPC timeline.

The signed calculation remains `visual - audio`: positive is audio leads, negative is audio lags. Core returns `TIMING DOMAINS NOT CORRELATED` instead of a number when domains differ.

`EventReviewState` owns the current event reference and keeps it fixed for the event lifetime. Audio correction, playhead preview, automatic visual selection, and committed manual visual selection are independent state transitions. Only a genuinely new event or Resume Live finalizes the current result for session history, and finalization is idempotent.

## Concurrency and ownership

- Capture workers never wait for WPF rendering or visual analysis.
- The rolling buffers have fixed capacities and replace the oldest owned samples.
- Preview dispatch stores only the newest pending frozen image.
- Work-window/waveform construction and visual analysis run on background tasks.
- Each event receives an increasing generation. Cancellation plus the generation check prevents a completed stale detector from changing current UI state.
- Reconnect unsubscribes callbacks before disposing the prior session.

## Future platforms

- **Windows (current):** WPF UI, Media Foundation video, MMDevice/WASAPI audio.
- **macOS (future):** likely AVFoundation backend implementing the same Core contracts. No current support claim.
- **Linux (future):** likely a combination of PipeWire, V4L2, ALSA, or another appropriate stack after research. No current support claim.
- **Future UI:** Avalonia or another cross-platform UI could consume Core later. WPF is not being replaced for Windows V1.
