# Changelog

All notable changes to this project are documented here.

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
