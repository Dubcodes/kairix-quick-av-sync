# Kairix agent guide

Read, in order, before changing the repository:

1. `AGENTS.md`
2. `docs/PROJECT_BRIEF.md`
3. `docs/DOMAIN_RULES.md`
4. `docs/DECISIONS.md`
5. `CURRENT_STATE.md`
6. the relevant architecture and testing documents

Keep Kairix simple. Distinguish evidence from assumptions and never silently alter timing mathematics. Core must remain platform-neutral; do not casually modify capture infrastructure. Prefer small, reviewable changes plus focused automated and physical tests over broad rewrites.

Automatic detection is continuous: the latest automatic result remains displayed while the detector stays armed, and the next accepted clap replaces it. Manual review is explicit. Hold suppresses new automatic events but never stops capture or rolling buffers. Settings persist; media and session history do not. Keep state, queues, tasks, buffers, and history bounded.
