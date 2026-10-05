# Domain rules

## Timing convention

`SyncResult = visual timestamp - audio timestamp`.

- Positive: **AUDIO LEADS VIDEO**.
- Negative: **AUDIO LAGS VIDEO**.
- Uncorrelated clock domains must refuse a numeric result.

Raw captured timestamps are evidence and are never rewritten to produce a desired result. Video compensation is a separate measurement-layer operation; diagnostics retain raw and compensated values.

## Automatic and manual measurement

Automatic result validation and manual selected-point measurement are distinct. An invalid whole timeline can prevent a trusted automatic result. It does not prevent an operator from measuring an explicitly selected audio timestamp against an explicitly selected comparable video timestamp. Timeline-integrity warnings remain visible independently. Existing `SyncResult.Calculate` and `VideoTimingCompensation.Calculate` clock-domain rules always apply.

Automatic detection remains armed after an accepted result. The latest automatic event stays displayed until the next accepted event replaces it. Manual review begins only through Manual Clap or an explicit review edit. Hold suppresses new automatic events without stopping capture or rolling buffers.

## Capture interpretation

Authoritative capture metadata describes what the backend delivered. Explicit woven-field reconstruction creates two review positions at field cadence without fabricating native transport samples. Raw capture timing, interpreted field timing, and measurement compensation remain separate layers.

Settings persist per applicable device/profile. Media is bounded in RAM, session history is limited to three results, and no captured media is written to disk.
