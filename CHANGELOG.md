# Changelog

All notable changes to this project are documented here.

## 2.19 - 04.09.2026

- Move project-media ID collision handling from a modal popup into the main project window.
- Detect an occupied project media ID as soon as project mode is applied and show the configured marker in red.
- Replace the normal Download button with inline `Replace` and `Move to XX` actions while the requested ID is occupied; `XX` is always the first free ID above the requested ID.
- Re-check the selected ID immediately before downloading so a newly-created conflicting file is never overwritten silently.
- Keep replacement downloads on the existing temporary-file safety path and keep Move semantics identical to the previous collision dialog.
- Harden Windows taskbar progress by requesting `ITaskbarList3` directly through `CoCreateInstance`, resolving the actual top-level taskbar window, and logging the first taskbar API failure instead of swallowing it silently.
- Preserve version 2.18 as rollback branch `restore/2.18-before-inline-collision`.
- Bump GUI, CLI, assemblies, and updater validation to version 2.19.

## 2.18 - 04.09.2026

- Fix the Windows taskbar progress COM activation introduced in 2.17 by creating the TaskbarList COM object through its CLSID and then querying the `ITaskbarList3` interface.
- Remove the nullable `ControlAdded` warning in `UiDiagnostics` by guarding the event control before recursive attachment.
- Keep the 2.17 taskbar progress, project auto-close, full project label, and clickable marker behavior unchanged.
- Bump GUI, CLI, assemblies, and updater validation to version 2.18.

## 2.17 - 04.09.2026

- Mirror the existing learned overall job progress to the Windows taskbar button so minimized downloads remain visible as a percentage/progress overlay.
- Clear taskbar progress on cancellation, completion, and application close.
- Close the project-media GUI automatically after a fully successful `--avid` or `--brollid` job once the media file is finalized and the final marker has been copied to Clipboard.
- Keep the project-media window open on download errors, cancellation, or Clipboard failure so the problem remains visible and recoverable.
- Show the complete resolved project directory name, including its leading `YYYYMMDD` date, in project mode.
- Make the resolved project name and current media marker visually prominent.
- Replace the plain project-media ID text with a clickable marker representation derived from the configured AV/BROLL HTML template.
- Allow clicking the marker to copy the same configured HTML marker to Clipboard without starting a download.
- Preserve version 2.16 as rollback branch `restore/2.16-before-taskbar-ui`.
- Bump GUI, CLI, assemblies, and updater validation to version 2.17.

## 2.16 - 04.09.2026

- Add GUI project-media launch arguments `--avid=<id> --project="<name>"` and `--brollid=<id> --project="<name>"`.
- Define `--avid` as one MP4 containing video and audio, and `--brollid` as a silent video-only MP4; subtitle, language, and output selectors are locked in project mode.
- Add an editable Clip name field in project mode and derive a short initial suggestion from the YouTube title, limited by the configurable `clip_name_max_words` value (four words by default).
- Resolve project output below configurable `editing_root` by exact project name: reuse the single `YYYYMMDD Project` directory when present, create today's directory when absent, and fail explicitly if duplicate dated directories exist for the same project name.
- Create/use the project's `BROLL` directory automatically and save numbered media as `NNN - short clip name.mp4` without opening a Save As or folder-selection dialog.
- Detect numbered clip collisions and offer exactly `Replace`, `Move to XX`, or `Cancel`; `XX` is the first free ID after the requested ID.
- Preserve the existing clip until a replacement download succeeds by downloading project media to a temporary BROLL file and finalizing it only after successful processing.
- Add configurable `av_marker_html` and `broll_marker_html` templates using the `{id}` placeholder. After a successful project-media download, render the template with the final ID and publish it to Clipboard as both text and HTML data.
- Forward project-media launch arguments to an already-running GUI instance through the existing single-instance IPC channel instead of dropping the requested project and ID.
- Add project-media job/output/Clipboard diagnostics to the canonical application log.
- Prevent `ytsubs-cli --version` validation calls from initializing `single` logging and truncating the current application log.
- Restore robust updater safeguards that were accidentally simplified in 2.15: bootstrap CRLF repair, graceful-stop timeout with forced fallback, automatic .NET 10 SDK installation through winget, Python-artifact migration cleanup, legacy-log cleanup, dependency validation, and verified repository synchronization.
- Preserve the exact 2.15 development state on branch `restore/2.15-before-project-mode` as the rollback point for this iteration.
- Bump GUI, CLI, assemblies, and updater validation to version 2.16.

## 2.15 - 03.09.2026

- Standardize application log lines on real wall-clock timestamps in `DD.MM.YYYY HH:mm:ss.fff` format.
- Add lifecycle logging for progress-dialog phases and elapsed durations.
- Keep the canonical repository-local application log at `logs/YouTubeSubs.log`.
- Bump GUI, CLI, assemblies, and updater validation to version 2.15.

## 2.14 - 03.09.2026

- Replace the insufficient direct `--force-keyframes-at-cuts` partial-video path with a two-stage exact-cut workflow.
- Download partial video with up to 10 seconds of preroll before the requested start so FFmpeg has valid reference frames available before the visible cut begins.
- Re-encode only partial video outputs explicitly through FFmpeg H.264 so the resulting MP4 begins on a clean keyframe instead of undecodable P/B-frame dependencies.
- Keep full-video downloads on the existing fast non-transcoding path.
- Preserve output semantics during exact cuts: Video only remains silent MP4, while Video + Audio produces one MP4 with AAC audio.
- Report the explicit FFmpeg exact-cut pass through the existing real progress telemetry.
- Make media-tool maintenance incremental: yt-dlp now checks its stable channel and only replaces itself when needed, while FFmpeg compares the installed version with Gyan's small `.ver` endpoint before downloading the essentials archive.
- Keep full validation of yt-dlp, FFmpeg, and ffprobe after every dependency check even when no download is required.
- Add project-standard application logging modes `off`, `single`, and `all` using the single canonical `logs/YouTubeSubs.log` file. `single` truncates the file for each run; `all` appends subsequent sessions to the same file.
- Make `single` the default logging mode for new configurations.
- Keep persistent development state inside the repository: configuration now lives in `config/config.json`, while application and updater logs remain under `logs/`; only genuinely temporary processing data may use the system TEMP directory.
- Add high-resolution startup timing and application/UI event diagnostics so slow startup and runtime behavior can be measured instead of guessed.
- Move completed updater diagnostics to `logs/upgrade.log` and force UTF-8 console output to prevent localized .NET/MSBuild text from being mojibake in CMD.
- Include the application version in final updater SUCCESS, WARNING, and FAILED status lines.
- Compact the output selector row so duration information shares the same row as Subtitles / Video / Audio and remove the unnecessary vertical gap before the From / To controls.
- Normalize edited time fields when Enter is pressed and when focus is committed by clicking the form background, in addition to the existing focus-leave normalization.
- Disable the subtitle format selector whenever Subtitles is unavailable or unchecked while preserving the selected format.
- Show `Enter a YouTube URL or Video ID...` in the title/status area until a video is loaded.
- Wrap the main content in an explicit padded layout container so the left and right window margins are structurally symmetrical instead of depending on WinForms Form.Padding or root-control Margin behavior.
- Restore the `TimeTextBox` and `DialogPositioning` helper classes after the UI hotfix build regression.
- Harden updater bootstrap handling against false local-change detection around `upgrade.cmd` self-update and line-ending normalization.
- Keep GUI and CLI assemblies and upgrade validation on version 2.14 for these same-version hotfixes.

## 2.13 - 02.09.2026

- Replace the single ambiguous progress indicator with two stacked progress bars: current-stage progress and learned overall job progress.
- Stretch progress bars across the usable width of the progress dialog and show the active operation, percentage, download speed, ETA, and FFmpeg processing progress when available.
- Read yt-dlp machine-oriented download progress and FFmpeg `-progress` telemetry instead of relying on cosmetic simulated percentages when real progress data is available.
- Keep a small local timing history per processing phase and progressively refine overall progress and remaining-time estimates from completed runs.
- Change media output semantics: Video only creates a silent MP4, Audio only creates an MP3, and Video + Audio creates one MP4 containing both streams.
- On the first Video selection in each application run, also select Audio once; after the user turns Audio off, do not offer it again during that run.
- Prefer a separate best-video stream for silent MP4 output so an audio stream cannot be included accidentally.
- Add `--force-keyframes-at-cuts` for partial video downloads to avoid cuts beginning on undecodable inter-frame data and the resulting picture jumps at the start of a clip.
- Add range-aware subtitle cutting. SRT, VTT, and SUB cues are filtered, clipped to the selected boundaries, and shifted so the cut begins at 00:00:00; TXT contains only captions overlapping the selected cut.
- Append `-cut` to automatically suggested/generated filenames when the selected range is shorter than the full video.
- Simplify the time row to `From` / `To`, center it, reduce field width, limit input to nine characters, filter invalid pasted characters, and select the whole value when entering a time field.
- Normalize resolved time expressions directly back into the From and To fields on focus loss or Download, silently clamp to video bounds, and silently swap reversed endpoints.
- Update raw no-colon time parsing: 1-3 digits are seconds; valid four-digit values are MMSS otherwise seconds; valid five/six-digit values are HHMMSS otherwise seconds.
- Highlight invalid time fields directly in red and disable Download rather than showing a separate range error line.
- Move duration feedback to the output-selector row. Full jobs show `[full]`; cuts show `[clip / full]` with the clip duration highlighted.
- Replace the monolithic batch upgrader with the shared `upgrade.cmd -> temporary current upgrade.ps1` protocol, add single-run `upgrade.log`, exact-path Git safe-directory recovery for mapped/UNC repositories, explicit phases/status, deterministic build/deploy validation, and restart of the app when it was running before upgrade.
- Remove known obsolete local Python artifacts such as `__pycache__`, `.venv`, and `.pytest_cache` during upgrade without broad untracked-file cleanup.
- Add `.gitattributes` rules enforcing CRLF for Windows CMD/BAT/PowerShell scripts.
- Bump GUI and CLI assemblies and upgrade validation to version 2.13.

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
- Keep the CLI project physically isolated in `cli/` with its own intermediate build directories and apphosts.
- Preserve the strict publish validation requiring GUI subsystem 2 and CLI subsystem 3 before installation.
- Bump both GUI and CLI assemblies and upgrade validation to version 2.10.

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
- Preserve tolerant YouTube video ID extraction, automatic 500 ms analysis, clickable video title, invalid-video inline status, language selection, manual-over-auto preference, output formats `.srt`, `.sub`, `.txt`, `.vtt`, remembered GUI format, exit codes, stdout/stderr behavior, single-instance activation, adaptive modal progress, cancellation behavior, post-save open prompt, local configuration, logging, and `off` / `single` / `all` logging modes.
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