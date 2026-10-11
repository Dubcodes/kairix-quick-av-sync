# v1.0.0 release audit

This focused audit distinguishes verified findings from broader assumptions. It did not change Media Foundation/WASAPI acquisition, capture timing mathematics, field reconstruction, detector algorithms, or the browser test signal.

## Corrected

- Interpretation choices could include rates unrelated to the selected transport. After selection or negotiation, the list is now restricted to direct and supported reconstruction choices for that exact transport.
- Diagnostic logging performed synchronous filesystem appends and was reachable from capture diagnostics. Logging now enters a bounded queue and a single background writer performs batched I/O; disposal flushes completed work.
- The public packager produced a different asset contract and staged outside the repository. It now creates exactly the stable EXE, ZIP, independent canonical HTML, and checksum manifest, with temporary staging under `artifacts/`.
- The new history trend view derives lightweight numeric points from existing bounded history. It creates no second snapshot or media-retention path.

## Reviewed and retained

- Video preview delivery is coalesced; discarded previews do not create WPF bitmaps.
- Live audio presentation is coalesced to 25 Hz and remains independent of automatic-event admission.
- Event analysis, passive analysis, reconnect work, and capture lifetimes have cancellation/generation guards; retiring capture callbacks are detached before shared media is cleared.
- Rolling buffers and 3/5/10 session history are bounded. Historical snapshots clone processed review-window media and do not reference mutable rolling buffers.
- Settings writes are debounced and the bounded logger uses one owned writer task.
- The trend control uses weak collection-change subscription and has no timer or independent history collection.

## Deferred evidence

- Broad capture-device/driver compatibility still requires hardware-specific validation.
- Reconstructed-field phase and device-specific delivery compensation remain explicit operating assumptions rather than universal facts.
- Long-duration physical-device drift and recovery behavior remain owner-test items; capture-free stability tests do not prove driver behavior.
