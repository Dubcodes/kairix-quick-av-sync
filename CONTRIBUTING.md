# Contributing

Issues and focused pull requests are welcome. Before coding, read `CURRENT_STATE.md` and `docs/ARCHITECTURE.md`; hardware work should state the exact device, driver, negotiated format, and timing source tested.

Run:

```powershell
dotnet build Kairix.QuickAVSync.sln -c Release
dotnet test tests/Kairix.QuickAVSync.Tests -c Release
```

Keep capture callbacks bounded and non-blocking. Never write captured media to disk, add telemetry/network dependencies, or silently pair an unrelated microphone. Add deterministic tests for timing, lead/lag wording, buffer behavior, and analysis changes. Do not commit build output, logs, user settings, secrets, or test media containing private content.

By contributing, you agree that your contribution is licensed under the repository's MIT license.
