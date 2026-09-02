# Changelog

All notable changes to this project are documented here.

## 2.12 - 02.09.2026

- Add optional video and audio downloads while keeping subtitles selected by default.
- Add MP4 video output capped at 1080p and MP3 192 kb/s audio output through yt-dlp and FFmpeg.
- Add fast partial-media downloads using a From / To-or-length range instead of requiring a complete video download first.
- Accept `SS`, `MMSS`, `HHMMSS`, `MM:SS`, and `HH:MM:SS` time input forms.
- Accept `+duration` as a relative end after From and `-duration` as a relative start before To.
- Read YouTube shared timestamp URL parameters (`t` / `start`, including seconds and `1h2m3s` forms) into the initial From value.
- Clamp calculated ranges silently to the actual video duration, swap reversed absolute boundaries automatically, and disable Download for clips shorter than two seconds.
- Show the normalized absolute range and calculated clip duration in the GUI.
- Use Save As with a fixed extension for a single selected output; use folder selection when multiple outputs are selected.
- Remember the last output directory, including mapped/network-drive locations.
- Allow metadata analysis and video/audio downloading even when a video has no subtitle tracks.
- Add real yt-dlp percentage reporting to the existing progress dialog and retain cancellation support.
- Add `tools-update.ps1`; `upgrade.cmd` installs/updates the latest stable yt-dlp executable and FFmpeg essentials build automatically and validates both tools.
- Make `upgrade.cmd` Git operations safe for mapped/UNC repositories without requiring a manual global `safe.directory` entry.
- Keep one-video-at-a-time behavior; no playlists or batch download workflow is introduced.
- Bump GUI and CLI assemblies and upgrade validation to version 2.12.

## 2.11 - 28.08.2026

- Reduce the main WinForms window width by 25 percent while preserving its existing height and layout behavior.
- Reduce the URL input and status display widths from 520 px to 390 px so the auto-sized dialog becomes correspondingly narrower.
- Keep language selection, format selection, buttons, analysis behavior, GUI/CLI split, and PE subsystem validation unchanged.
- Bump both GUI and CLI assemblies and upgrade validation to version 2.11.

## 2.10 - 28.08.2026

- Fix the GUI build after moving the dedicated CLI sources into `cli/` in 2.09.
- Explicitly exclude `cli\**\*.cs` from the root WinForms project's default compile glob so `Program.cs` remains the GUI project's only entry point.
- Keep the CLI project physically isolated in `cli/` with its own intermediate build directories and shared application logic linked from the root.
- Preserve the strict publish validation requiring GUI subsystem 2 and CLI subsystem 3 before installation.
- Bump both executables and upgrade validation to version 2.10.

## 2.09 - 28.08.2026

- Correct the 2.08 diagnosis: .NET 6 and later respect explicit `OutputType=Exe`; `DisableWinExeOutputInference` is obsolete for this case.
- Identify the actual build risk: the GUI and CLI project files lived in the same directory and therefore shared default `obj`/`bin` intermediate directories, allowing the GUI apphost state to contaminate the CLI build.
- Move the CLI project and entry point into the dedicated `cli/` directory so GUI and CLI use physically separate build intermediates and apphosts.
- Keep shared application logic linked from the root sources without duplicating subtitle retrieval or configuration code.
- Remove the obsolete root-level CLI project and entry point.
- Make `upgrade.cmd` delete both GUI and CLI intermediate build directories before restore/build, then build the isolated CLI project from `cli\YouTubeSubs.Cli.csproj`.
- Keep the hard PE validation: `ytsubs.exe` must report subsystem 2 and `ytsubs-cli.exe` must report subsystem 3 before either candidate is installed.
- Bump both executables to version 2.09.

## 2.08 - 28.08.2026

- Attempted to prevent SDK output inference with `DisableWinExeOutputInference=true`; runtime testing showed this did not fix the CLI subsystem because output inference was not the actual cause.
- Keep `ytsubs.exe` as a Windows GUI-subsystem executable and preserve the separate GUI/CLI architecture introduced in 2.06.
- Improve `upgrade.cmd` PE diagnostics so it prints the detected GUI and CLI subsystem numbers before accepting or rejecting the candidates.
- Keep installation blocked unless the GUI reports subsystem 2 and the CLI reports subsystem 3.
- Bump both GUI and CLI assemblies to version 2.08.

## 2.07 - 28.08.2026

- Harden installation of the separated GUI/CLI executable pair introduced in 2.06.
- Require both `ytsubs.exe` and `ytsubs-cli.exe` to exist in the repository root after installation before the upgrade may succeed.
- Execute the installed `ytsubs-cli.exe --version` after copying it into place and require the exact expected output before continuing.
- Fail the upgrade explicitly if either installed executable is missing or the installed CLI does not run correctly.
- Bump both GUI and CLI assemblies to version 2.07 while keeping the clean Windows GUI / Windows console subsystem split unchanged.

## 2.06 - 28.08.2026

- End the experimental single-EXE GUI/CLI subsystem work from 2.02 through 2.05 and separate the two Windows execution models cleanly.
- Keep `ytsubs.exe` as a Windows GUI-subsystem WinForms application, so normal desktop launch never allocates a console window.
- Add `ytsubs-cli.exe` as a true Windows console-subsystem application with synchronous `cmd.exe` behavior, stdout, stderr, pipes, redirection, and exit codes.
- Make `ytsubs-cli.exe` with no arguments launch the adjacent `ytsubs.exe` GUI and exit successfully.
- Keep subtitle retrieval and formatting code shared between GUI and CLI instead of maintaining two implementations.
- Remove console attach/hide/detach workarounds from the GUI entry point.
- Update `upgrade.cmd` to build, publish, validate, and install both self-contained single-file executables.
- Validate that `ytsubs.exe` uses PE subsystem 2 (Windows GUI) and `ytsubs-cli.exe` uses PE subsystem 3 (Windows console) before installation.
- Preserve the existing icon, TXT normalization, subtitle formats, configuration, logging, and CLI exit-code semantics.

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
