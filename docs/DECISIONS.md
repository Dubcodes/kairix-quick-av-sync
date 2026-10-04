# Engineering decisions

## Historical field evidence: primary capture-stability baseline

Decision date: 2026-10-05

The primary known-good operational baseline for capture stability is commit `71cecc21bee4df5c0eccfc0be59a1f84c4d6f45c`, commit message `feat: add performance settings and themes`. The exact field executable identified itself as ProductVersion `0.1.0-alpha+71cecc21bee4df5c0eccfc0be59a1f84c4d6f45c`.

The user confirmed that exact build ran in the real broadcast-truck environment for an extended period/effectively the work day without the current recurring video-freeze problem. This is **historical field evidence** that the Media Foundation software path was capable of sustained real-world operation. It is not proof of the first bad commit, and it does not exclude a present environment, driver, USB, source, or device-state contribution.

Commit `1557718019fc24a9b7eda51c1947d87061ec51bd` (`fix: restore generic Media Foundation video frames`) remains useful earlier capture evidence but is not the known-good production/field baseline. Documentation and reports must not describe it as such.

Consequences:

- Treat a software regression after `71cecc21…` as a live hypothesis.
- Do not call the current failure environmental merely because both builds use Media Foundation.
- Run same-hardware detached-worktree A/B testing before choosing between regression and environment/device-state explanations.
- Focus comparisons on capture/session ownership and lifecycle, Source Reader/Flush/disposal/reconnect behavior, conversion/native-buffer ownership, buffers, passive analysis, preview scheduling, event lifetime, settings reconnect, diagnostics/watchdogs, generation handling, and field reconstruction.
- Keep the application-level quarantine and bounded-shutdown protections regardless of the native root cause.

### Present-machine A/B result

On 2026-10-05 the exact `71cecc21…` commit was built and run from a detached worktree on the current PC with the same XI100DUSB-HDMI path, source, and saved 1920×1080 25p YUY2/top-first reconstruction profile. It reached Running, produced a 30-frame analysis at 12:36:25, and then produced zero-frame event windows from 12:36:26 onward while audio transients and the responsive WPF process continued for more than two minutes. It remained below the build's 250-sample logging threshold, so no exact final native-frame count is asserted. The stalled old build did not accept normal window close and was forcibly terminated.

Decision consequence: the present environment, driver, USB, source, or device state is now a more plausible contributor because both the historical baseline and current development reproduce there. This is not a root-cause finding, does not rule out a post-`71cecc21…` regression, and does not override the separately confirmed **historical field evidence** from the broadcast truck.
