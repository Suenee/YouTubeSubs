# Changelog

All notable changes to this project are documented here.

## 2.00 - 28.08.2026

- Preserve the last verified Python implementation, version 1.13, in branch `ALFA` before replacing the development implementation.
- Replace the experimental Python/Nuitka development line with a clean .NET 10 Windows Forms port.
- Keep the port intentionally 1:1 with the verified Python application instead of redesigning the workflow or adding unrelated features.
- Preserve GUI mode without arguments and CLI mode with arguments.
- Preserve tolerant YouTube video ID extraction, automatic 500 ms analysis, clickable video title, invalid-video inline status, language selection, manual-over-auto preference, output formats `.srt`, `.sub`, `.txt`, `.vtt`, remembered GUI format, exit codes, stdout/stderr behavior, single-instance activation, adaptive modal progress, cancellation behavior, post-save open prompt, local configuration, and `off` / `single` / `all` logging modes.
- Use `YoutubeExplode` 6.6.2 for YouTube video metadata and closed-caption discovery/download.
- Publish as a self-contained single-file `win-x64` .NET 10 application named `ytsubs.exe`.
- Integrate the existing YouTubeSubs icon into the .NET executable through `ApplicationIcon`; WinForms windows reuse the executable icon.
- Keep the tracked icon in text-safe `assets/ytsubs.ico.b64`; `upgrade.cmd` reconstructs and validates `assets\ytsubs.ico` before build.
- Replace Python/Nuitka build logic in `upgrade.cmd` with .NET 10 restore, Release build, self-contained single-file publish, candidate validation, portable smoke test, executable-size reporting, and CLI cold-start measurement.
- `upgrade.cmd` automatically installs the current Microsoft .NET 10 SDK through `winget` when SDK 10.x is missing, instead of introducing an older runtime or SDK.
- Remove obsolete Python project files and Python build requirements from the `devel` branch.

## Python history

The complete verified Python implementation and its historical changelog through version 1.13 are preserved in branch `ALFA`.

Versions 1.14 through 1.16 were experimental build-system attempts on `devel` and are intentionally not carried forward into the .NET implementation.
