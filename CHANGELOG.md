# Changelog

All notable changes to this project are documented here.

## 1.16 - 27.08.2026

- Pin the Nuitka build environment to 64-bit Python 3.12 when using MinGW64.
- Detect Python 3.12 before dependency installation or compilation and fail early with a clear message when it is unavailable.
- Automatically recreate the local `.venv` with Python 3.12 when an older build environment uses Python 3.13 or another version.
- Validate the build interpreter and `.venv` Python version before invoking Nuitka.
- Keep the existing validated `ytsubs.exe` untouched when the required build runtime is unavailable or compilation fails.
- Preserve the Nuitka onefile build, uncompressed payload, portable smoke test, icon validation, CLI validation, and startup diagnostics.

## 1.15 - 27.08.2026

- Removed `yt-dlp` from the runtime and build dependency graph after Nuitka spent excessive time analyzing the full extractor tree.
- Replace `yt-dlp` metadata lookup with YouTube's lightweight oEmbed endpoint via `requests`.
- Preserve subtitle retrieval through `youtube-transcript-api`; automatic translation remains disabled.
- Simplify automatic original-language detection: prefer the first auto-generated transcript language, otherwise use the first manual track returned by YouTube.
- Keep manual subtitles preferred over auto-generated subtitles within the selected language.
- `upgrade.cmd` now explicitly uninstalls stale `yt-dlp` from the local build environment and fails validation if runtime source imports it again.
- Keep Nuitka onefile, uncompressed payload, console attach mode, candidate validation, portable smoke test, icon validation, and startup diagnostics.

## 1.14 - 27.08.2026

- Replaced the PyInstaller production build with Nuitka onefile compilation for faster application startup while preserving a single portable `ytsubs.exe`.
- Build with an uncompressed onefile payload to prioritize startup latency over executable size.
- Use Nuitka Windows console mode `attach`: GUI launch does not create a console, while CLI launch can attach to an existing terminal.
- Enable Nuitka's `tk-inter` plugin and embed the validated application ICO into the executable and onefile payload.
- Keep the existing `yt-dlp` lazy import optimization.
- `upgrade.cmd` builds into a staging directory and validates the Nuitka candidate before replacing the existing working `ytsubs.exe`.
- Preserve the portable smoke test outside the repository and startup diagnostics.
- Remove the obsolete PyInstaller build specification and PyInstaller build dependency.

## 1.13 - 27.08.2026

- Center the native Windows Save As dialog on the work area of the monitor containing the main YouTubeSubs window instead of centering it relative to the small application window.
- Keep the save dialog on the active monitor and reduce the risk of it extending outside the visible desktop.

## 1.12 - 27.08.2026

- Lazy-load `yt-dlp` only when video metadata is actually requested instead of importing its large module tree during normal application startup.
- Keep GUI startup and lightweight CLI operations such as `--version` free from the runtime cost of importing `yt-dlp`.
- Preserve `yt-dlp` as the metadata/fallback implementation and keep the existing subtitle behavior unchanged.
- Keep the single portable `ytsubs.exe`, native windowed GUI, embedded dependencies, and portable smoke test.

## 1.11 - 27.08.2026

- Optimized the PyInstaller one-file bundle for faster startup while preserving the single portable `ytsubs.exe` distribution model.
- Removed broad `collect_all()` calls for `yt_dlp` and `youtube_transcript_api`; rely on normal PyInstaller dependency analysis and package hooks instead of forcibly bundling every package submodule, data file, and binary.
- Disabled UPX for the executable to avoid an additional runtime decompression stage.
- Enabled Python bytecode optimization level 1 for the frozen application.
- Excluded development/test-only standard modules from the bundle where safe.
- Added executable-size and CLI cold-start timing diagnostics to `upgrade.cmd` so future startup changes can be measured rather than guessed.
- Preserved the portable smoke test and native windowed/no-console GUI build.

## 1.10 - 27.08.2026

- Build `ytsubs.exe` as a native Windows windowed executable (`console=False`) so GUI launch never creates or flashes a console window.
- Keep CLI support through the existing explicit parent-console stdio binding when arguments are supplied.
- Added a portable smoke test to `upgrade.cmd`: the finished `ytsubs.exe` is copied to a new empty temporary directory outside the repository and `--version` is validated there.
- The portable smoke test ensures the distributed executable does not depend on repository Python sources, CMD/PowerShell launchers, `.venv`, or adjacent project files.
- Keep the final distribution artifact as a single `ytsubs.exe` with the application icon and Python runtime dependencies embedded.

## 1.09 - 27.08.2026

- Removed the PNG-to-ICO conversion workflow from `upgrade.cmd`.
- Removed Pillow from the build toolchain.
- Store the validated Windows icon as text-safe `assets/ytsubs.ico.b64`; `upgrade.cmd` only decodes it and validates the ICO container structure before PyInstaller starts.
- Removed the obsolete PNG icon asset from the repository.
- PyInstaller now receives the final Windows ICO directly instead of converting image formats during the build.
- Changed the executable to a console-enabled hybrid build with PyInstaller `hide_console="hide-early"`: GUI launch hides its own console while CLI launch from an existing console keeps normal stdout/stderr behavior.
- Refresh the Windows shell icon cache notification after a successful build.
- Keep `upgrade.cmd` starting with `cls`.

## 1.08 - 27.08.2026

- Replaced the corrupted icon source with a verified 256 × 256 RGBA PNG derived directly from the original YouTubeSubs artwork.
- Strengthened `upgrade.cmd` icon validation to run Pillow `verify()`, reopen the image, and fully decode it before any ICO generation or PyInstaller work begins.
- Added the same full-stream verification for the generated Windows ICO.
- Kept `upgrade.cmd` starting with `cls` and preserved the generated multi-size ICO workflow.

## 1.07 - 27.08.2026

- Corrected the icon assets: `assets/ytsubs.png` is now the canonical, real PNG source image.
- Removed the incorrectly tracked `assets/ytsubs.ico`; the ICO is now generated locally during `upgrade.cmd` and ignored by Git.
- Restored the complete `ytsubs.py` core after detecting an accidental partial overwrite during version maintenance.
- `upgrade.cmd` now starts with `cls`.
- Strengthened icon validation to verify the actual PNG format and minimum source dimensions before generating the Windows ICO.
- Kept Pillow as a build-only dependency and generate the 16, 24, 32, 48, 64, 128, and 256 pixel ICO variants before PyInstaller starts.

## 1.06 - 27.08.2026

- Added Pillow to the standalone build toolchain.
- `upgrade.cmd` now regenerates a standards-compliant multi-size Windows ICO from the canonical PNG source before every standalone build.
- Added pre-build PNG/ICO validation so icon problems fail before the expensive PyInstaller packaging stage.
- Kept the generated ICO sizes at 16, 24, 32, 48, 64, 128, and 256 pixels for Windows shell, title-bar, taskbar, and shortcut use.

## 1.05 - 27.08.2026

- Converted YouTubeSubs into a standalone Windows application built as a single `ytsubs.exe`.
- Removed the obsolete `ytsubs.cmd` launcher.
- Added a PyInstaller one-file/windowed build specification with the application icon embedded into the executable.
- Added build-only requirements in `requirements-build.txt`.
- `upgrade.cmd` now builds `ytsubs.exe` automatically in the project root and validates the executable version.
- Preserved GUI mode on double-click and CLI mode when arguments are supplied.
- Added Windows stdio binding for the windowed executable so CLI stdout/stderr and shell redirection continue to work.
- Restored adaptive learned progress estimation in modal dialogs.
- Preserved `.srt`, `.sub`, `.txt`, and `.vtt` output formats and remembered GUI format selection.

## 1.04 - 27.08.2026

- Center modal analysis and download dialogs relative to the main application window.
- Show `Invalid Video ID. Please try again...` in red for invalid or unavailable video input while keeping Download disabled.
- Shorten the automatic language choice label to `Auto`.
- Replace TXT/SRT radio buttons with a compact format selector on the same row as Language.
- Add `.srt`, `.sub`, `.txt`, and `.vtt` output choices in alphabetical order.
- Remember the last GUI output format in local configuration; default to `.srt` when no previous choice exists.
- Add WebVTT output and SubViewer-compatible SUB output without additional dependencies.
- Keep the Language selector stretched across the remaining row width while the format selector stays compact.

## 1.03 - 27.08.2026

- Added the YouTubeSubs application icon assets for GUI windows, the Windows taskbar, and future executable builds.
- Improved initial GUI activation so a normal launch is brought to the foreground.
- Kept single-instance GUI behavior with foreground activation.
- Invalid or unavailable video-like input now fails quietly in the GUI and keeps Download disabled.
- Removed the `Ready:` status prefix.
- Added the valid video title as a centered clickable link that opens the canonical YouTube URL in the default browser.
- Added tolerant video ID extraction from damaged or incomplete YouTube URLs and surrounding text.

## 1.02 - 27.08.2026

- Centered the main GUI window on the desktop.
- Added single-instance GUI behavior with activation of the existing window on repeated launch.
- Moved analysis progress into a modal dialog.
- Moved download progress into a modal dialog.
- Added Cancel support for analysis and download workflows.
- Closing a progress dialog with the window close button exits the application.
- Added adaptive progress and ETA display to modal progress dialogs.
- Added post-save Yes / No prompt asking whether the saved file should be opened.
- Opening a saved file is delegated to the default Windows file association.
- The application exits after the post-save choice.

## 1.01 - 27.08.2026

- Added `ytsubs.cmd` launcher in the project root.
- `ytsubs` can now be started directly from the repository root without changing `PATH` or activating `.venv`.
- Updated `upgrade.cmd` validation to test the root launcher.

## 1.00 - 27.08.2026

- Initial development version.
- Added CLI mode for YouTube URL or video ID input.
- Added TXT and SRT output.
- Added stdout output suitable for pipes and redirection.
- Added optional `--lang` and `--output` parameters.
- Added GUI mode when started without parameters.
- Added automatic discovery of available subtitle tracks.
- Added deterministic original-language heuristic without automatic translation.
- Added automatic file-name suggestion from the YouTube video title.
- Added adaptive progress estimation based on locally learned phase timings.
- Added configurable file logging modes: `off`, `single`, and `all`.
