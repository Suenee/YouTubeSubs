# Changelog

All notable changes to this project are documented here.

## 2.05 - 28.08.2026

- Restore the Windows GUI PE subsystem (`WinExe`) so launching `ytsubs.exe` normally does not create a console window, eliminating the terminal flash introduced in 2.02.
- Remove the post-start console hiding/detaching workaround; the GUI process now starts without a console at the operating-system level.
- In CLI mode, explicitly attach to the parent console when available and bind standard output/error streams for command-line use and redirected execution.
- Replace the misleading interactive `cmd.exe` synchronization validation with a PE subsystem check plus redirected CLI process validation.
- Validate the published executable header and reject candidates that are not Windows GUI-subsystem executables before installation.
- Keep the existing single-file .NET 10 build, icon integration, TXT normalization, CLI arguments, exit codes, and portable smoke test.
- Note the Windows shell limitation: an interactive `cmd.exe` does not natively wait for a GUI-subsystem executable launched directly by name; callers that require guaranteed process waiting must launch it through a waiting process API or batch/script context.

## 2.04 - 28.08.2026

- Replace the fragile inline `cmd.exe /c` synchronization command with a temporary `.cmd` test script.
- Run `ytsubs.exe --version` and the following marker command as two ordinary batch lines, eliminating nested command-line quoting and escaping ambiguity.
- Verify the candidate exit code and compare the captured output byte-for-byte with the required `ytsubs 2.04` / `__AFTER__` sequence.
- Treat any execution failure or output mismatch as a hard validation failure and keep the previously installed executable untouched.
- Keep the verified synchronous CLI implementation and TXT normalization behavior from 2.02 unchanged.

## 2.03 - 28.08.2026

- Fix the `upgrade.cmd` CLI synchronization validation introduced in 2.02.
- Remove the fragile nested PowerShell/cmd.exe quoting that could produce a parser error while still allowing the upgrade to continue.
- Run the synchronization check directly through `cmd.exe`, capture its complete output, and compare it byte-for-byte with the expected command order.
- Treat any execution error or output mismatch as a hard validation failure and leave the previously installed `ytsubs.exe` untouched.
- Keep the verified 2.02 console-subsystem CLI behavior unchanged.

## 2.02 - 28.08.2026

- Fix CLI execution under `cmd.exe` so the shell waits for `ytsubs.exe` to finish before displaying the next prompt.
- Build the executable with the Windows console subsystem so stdout, stderr, pipes, redirection, exit codes, and command sequencing behave synchronously.
- Preserve GUI behavior without arguments by immediately detaching from the console; when Explorer creates a private console window for the process, hide it before the WinForms UI starts.
- Do not hide an existing user console when GUI mode is launched from `cmd.exe` or PowerShell.
- Normalize `.txt` subtitle output by removing leading/trailing blank lines from individual caption segments and collapsing repeated internal blank lines to a single paragraph separator.
- Preserve explicit paragraph breaks inside caption text while eliminating the artificial blank gaps that occurred between ordinary subtitle segments.
- Add an `upgrade.cmd` validation that runs the candidate through `cmd.exe` and verifies that `--version` completes before a following command is executed.

## 2.01 - 28.08.2026

- Fix `.NET 10 SDK` detection in `upgrade.cmd` on Windows.
- Avoid nested `FOR /F` command parsing around a quoted `dotnet.exe` path, which could corrupt the command and falsely report that .NET 10 was missing.
- Resolve the active `dotnet.exe` path explicitly, capture `--list-sdks` output to a temporary file, and detect SDK 10.x from that file.
- Only invoke the `winget` installer when no usable .NET 10 SDK is actually detected.
- Preserve the self-updating `upgrade.cmd` workflow and existing executable on later build failures.

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

The complete verified Python implementation and its historical changelog through version 1.13 are preserved in branch `ALFA` at commit `0b7271a717e3c308c9dd2bbaf2a75fdc0a532cd7`.

Versions 1.14 through 1.16 were experimental build-system attempts on `devel` and are intentionally not carried forward into the .NET implementation.
