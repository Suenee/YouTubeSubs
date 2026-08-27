# Changelog

All notable changes to this project are documented here.

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
- Kept single-instance activation behavior and strengthened foreground activation.
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
