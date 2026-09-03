# Changelog

All notable changes to this project are documented here.

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
- Add high-resolution startup timing and application/UI event diagnostics so slow startup and runtime behavior can be measured instead of guessed.
- Move completed updater diagnostics to `logs/upgrade.log` and force UTF-8 console output to prevent localized .NET/MSBuild text from being mojibake in CMD.
- Compact the output selector row so duration information shares the same row as Subtitles / Video / Audio and remove the unnecessary vertical gap before the From / To controls.
- Normalize edited time fields when Enter is pressed and when focus is committed by clicking the form background, in addition to the existing focus-leave normalization.
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
