# Domain rules

## Timing convention

`SyncResult = visual timestamp - audio timestamp`.

- Positive: **AUDIO LEADS VIDEO**.
- Negative: **AUDIO LAGS VIDEO**.
- Uncorrelated clock domains must refuse a numeric result.

Raw captured timestamps are evidence and are never rewritten to produce a desired result. Video compensation is a separate measurement-layer operation; diagnostics retain raw and compensated values.

## Automatic and manual measurement

Result calculation and whole-timeline integrity are distinct. Whenever the selected audio and video timestamps have comparable clocks, Auto Result, Manual Preview, and Manual Result display their calculated value. An invalid whole timeline remains a separate timing warning and may exclude an automatic result from session history, but it does not replace a calculable displayed value. Existing `SyncResult.Calculate` and `VideoTimingCompensation.Calculate` clock-domain rules always apply.

Automatic detection remains armed after an accepted result. Automatic admission depends only on Auto Detect, Auto Spike, Hold, and whether automatic analysis is already running; a displayed review is not an admission gate. A pending automatic event replaces the displayed event only after an accepted visual candidate. Rejection preserves the displayed review, waveform, frame, markers, thumbnail, and history. Manual Clap enters Hold and opens explicit review. Hold suppresses new automatic events without stopping capture or rolling buffers, and returning to ARMED accepts the next event immediately.

Kairix uses specialized visual detector engines rather than assuming one universal visual signature. **Human Clap** is the default and requires localized pre-contact approach followed by contact/settle. Electronic/generated cues should use **Flash / Test Signal**, which selects the first qualified luminance onset. Only the selected engine runs for an event; sensitivity changes acceptance, not confidence evidence.

A strong impulsive audio transient with no accepted visual match may become an **Audio Candidate**. It preserves its bounded waveform, audio mark, and review frames for manual recovery, but has no sync number and is not a confirmed Session History result unless the operator subsequently selects and commits a comparable visual point. A weak transient with no visual match is discarded without changing the displayed event.

## Capture interpretation

Authoritative capture metadata describes what the backend delivered. The selected interpretation is the sole runtime truth for woven-field reconstruction; reconstructed format choices create two review positions at field cadence without fabricating native transport samples. Raw capture timing, interpreted field timing, and measurement compensation remain separate layers.

Settings persist per applicable device/profile. Media is bounded in RAM, session history is limited to three results, and no captured media is written to disk.
