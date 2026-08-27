# Changelog

All notable changes to this project are documented here.

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
