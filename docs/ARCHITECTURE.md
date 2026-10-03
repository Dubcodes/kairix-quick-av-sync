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

Core samples safely own their arrays. Backends must not mutate arrays after publishing them. The Windows backend extracts independently configurable analysis luma and BGRA presentation rasters (defaults 640×360 and 160×90) before crossing the boundary. Luma alone feeds automatic visual analysis. Every `VideoFrame` identifies its native source sample, temporal position, progressive/field kind, and whether its review timestamp is direct or reconstructed.

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

In reconstruction mode the capture worker emits two field frames sharing one `NativeSampleIndex`. Review navigation and visual detection use the interpreted field rate. `FrameTimingAnalyzer` analyzes derived media timestamps at that review cadence, but groups arrival/device/sample/reader observations by native identity and reports the factual transport cadence separately. Audio remains unchanged on the WASAPI QPC timeline.

Measurement compensation is a separate fourth layer. `VideoTimingCompensation` never mutates frames or observations. For explicit reconstruction its automatic offset is the exact target field interval; corrected visual time is `raw interpreted visual time - effective video offset`. The same offset is applied to every field in a pair, so 20 ms raw spacing remains 20 ms corrected spacing. `EventReviewState` retains raw candidate, manual mark, and playhead timestamps while calculating Auto, Manual Preview, and Manual Result through this helper. Detector proximity and video-window selection are centered at `audio + effective offset`, because detection still examines raw captured frames.

`VideoTimingProfileKey` is the single stable key for manual overrides and includes device identity, native mode identity, reconstruction state, field order, and target field rate. Session history snapshots corrected result, raw result, effective offset, and automatic/manual provenance so later settings changes cannot rewrite prior events.

## Processing and presentation performance

`CaptureOpenOptions` keeps three rasters distinct: the native capture raster selected from Media Foundation, the detection raster stored as `VideoFrame.Luma`, and the review raster stored as `PresentationBgra`. Changing either processing raster reconnects the same exact native mode and interpretation. A reusable conversion plan precomputes progressive/top/bottom source maps. NV12, YUY2, and UYVY use direct luma-byte reads for analysis; only presentation performs YUV-to-BGR conversion.

The live path queues the newest `VideoFrame` and creates a WPF bitmap only when that frame reaches the dispatcher. Periodic passive analysis snapshots the rolling buffer rather than retaining a second full-frame queue. Detector cell density scales with input resolution and rents a bounded set of scratch arrays. Rolling buffers retain references and grow as captured frames arrive; the UI estimates analysis plus presentation image bytes from raster, temporal rate, and requested duration.

WPF colors are semantic `DynamicResource` values supplied by Graphite, Midnight, Light, High Contrast, Synthwave, Terminal, or Solar Flare dictionaries. Runtime replacement of the active dictionary updates standard and custom-drawn controls. Settings writes are debounced for interactive controls and synchronously flushed at shutdown.

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

## Operator UI and diagnostics

The WPF shell separates routine operation from engineering detail. The normal result column binds only the result, evidence-only confidence, and three session-history summaries. Settings uses a right-edge vertical rail with Appearance, Detection, Review, Capture, Shortcuts, and About pages. About is the sole normal home for transport/reconstruction/phase, memory, raw-versus-corrected, and last-event detector diagnostics. Collapsing Settings retains a narrow reopen rail.

Waveform rendering consumes the same immutable amplitude array for Mirrored, Filled, and Line styles. Style changes invalidate drawing only; they never rebuild audio samples or detector timestamps. ARMED/HOLD is a session latch over the persisted Automatic detection master setting and never stops capture or buffering.

## Visual contact selection and resolution isolation

`MotionVisualClapDetector` derives spatial evidence per temporal transition, robust baseline/spread, and then performs two explicit stages. Stage 1 locates the first qualified localized rapid approach. Stage 2 searches one to three temporal positions forward for the earliest sharp reduction that remains lower briefly. A no-lead-in onset path handles test patterns and extremely fast contact without adding a fixed frame delay. Expected timing has only weak score influence; it cannot replace contact evidence.

`VisualDetectionTrace` retains approach/contact indices, advancement, and evidence. The WPF layer combines that trace with raw audio/expected/chosen timestamps, temporal/native/field identity, raw/compensated/corrected values, score/confidence, analysis time, backend conversion average/maximum, timestamp faults, and audio-continuity faults in one bounded log/UI diagnostic.

Detection resolution enters only native-buffer-to-luma conversion and spatial analysis. `VideoFrame.Timestamp`, `NativeSampleIndex`, `TemporalIndex`, `TimingObservation`, reconstruction position, and measurement compensation are assigned independently of output raster dimensions. A resolution change currently reconnects because conversion plans are session-scoped, but deterministic same-resolution and changed-resolution reconnect fixtures retain phase. No settling gate is justified by current deterministic evidence; physical device evidence would be required before adding one.
