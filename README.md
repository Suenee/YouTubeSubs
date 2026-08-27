# YouTubeSubs

YouTubeSubs is a standalone Windows GUI and CLI utility for downloading manual or auto-generated YouTube subtitles without automatic translation.

The current `devel` branch is the .NET 10 implementation. The last verified Python implementation is preserved separately in branch `ALFA` as version 1.13.

## Design goal

The .NET port is intentionally a 1:1 functional replacement of the verified Python application. It does not redesign the workflow or add unrelated features.

## Features

- Accepts a full YouTube URL, a damaged/incomplete URL with one recoverable 11-character video ID, or a plain 11-character video ID.
- One portable `ytsubs.exe` for distribution.
- Double-click / no arguments: WinForms GUI.
- Arguments supplied: CLI mode with stdout/stderr suitable for pipes and redirection.
- Output formats: `.srt`, `.sub` (SubViewer), `.txt`, `.vtt`.
- Discovers real subtitle tracks exposed by YouTube; automatic translation is never used.
- `Auto` prefers the first auto-generated language as the likely spoken/original language; if none exists, the first manual track is used.
- Within a selected language, manual subtitles are preferred over auto-generated subtitles.
- Remembers the last GUI output format; default is `.srt`.
- Adaptive modal progress estimation stores learned timings locally.
- Single-instance GUI behavior with activation of the existing window.
- Native Save As dialog positioned on the work area of the monitor containing the application.
- Embedded YouTubeSubs application icon in the final executable and GUI windows.
- Optional file logging modes: `off`, `single`, `all`.

## Technology

- .NET 10
- Windows Forms
- `YoutubeExplode` 6.6.2 for YouTube video metadata and closed-caption tracks
- self-contained, single-file `win-x64` publishing

## Build / update

Clone the repository and switch to `devel`:

```cmd
git clone https://github.com/Suenee/YouTubeSubs.git
cd YouTubeSubs
git switch devel
upgrade.cmd
```

`upgrade.cmd` always self-updates first. It then updates the branch, checks for the current .NET 10 SDK, installs the Microsoft .NET 10 SDK through `winget` when necessary, restores dependencies, validates sources, reconstructs and validates the tracked Base64 icon, publishes a self-contained single-file executable, validates the candidate before replacing the current executable, and performs a portable smoke test outside the repository.

The final distributable is:

```text
ytsubs.exe
```

No .NET runtime, source files, CMD files, or SDK are required beside the distributed executable.

## GUI

Run:

```cmd
ytsubs.exe
```

Enter a YouTube URL or video ID. Analysis starts automatically after 500 ms. A valid video shows its title as a clickable link and exposes available subtitle languages. Invalid or unavailable input shows:

```text
Invalid Video ID. Please try again...
```

The language selector starts with `Auto`. The format selector is on the same row and offers `.srt`, `.sub`, `.txt`, `.vtt`.

Analysis and download use modal progress dialogs centered relative to the main application window. Cancel returns to the application; closing a progress dialog with its X exits the application. After saving, YouTubeSubs asks whether the saved file should be opened using the Windows file association.

## CLI

```cmd
ytsubs.exe VIDEO_ID
ytsubs.exe "https://www.youtube.com/watch?v=VIDEO_ID"
ytsubs.exe VIDEO_ID --format srt
ytsubs.exe VIDEO_ID --format sub
ytsubs.exe VIDEO_ID --format txt
ytsubs.exe VIDEO_ID --format vtt
ytsubs.exe VIDEO_ID --lang en
ytsubs.exe VIDEO_ID -o subtitles.srt --format srt
ytsubs.exe --version
```

Default CLI format is plain text to stdout. Automatic translation is never used.

## Exit codes

| Code | Meaning |
| ---: | --- |
| 0 | Success |
| 2 | Invalid argument, URL, or video ID |
| 3 | No usable subtitle track / requested language unavailable |
| 4 | YouTube, network, or caption API failure |
| 5 | Output file write failure |

## Local configuration

Runtime configuration, logging settings, last GUI format, and learned progress timings are stored in:

```text
%LOCALAPPDATA%\YouTubeSubs\config.json
```

The optional log file is:

```text
%LOCALAPPDATA%\YouTubeSubs\ytsubs.log
```

See [MANUAL.md](MANUAL.md) and [CHANGELOG.md](CHANGELOG.md) for additional details.
