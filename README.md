# YouTubeSubs

Standalone Windows CLI and GUI tool for downloading manual or auto-generated YouTube subtitles without automatic translation.

Development takes place on the `devel` branch. Stable releases are published to `main`.

## Features

- Accepts a full YouTube URL, a damaged/incomplete URL with a recoverable video ID, or an 11-character video ID.
- Single standalone `ytsubs.exe` application.
- Double-click / no arguments: GUI mode.
- Arguments supplied: CLI mode with stdout/stderr suitable for pipes and redirection.
- Output formats: `.srt`, `.sub` (SubViewer), `.txt`, `.vtt`.
- Discovers real subtitle tracks exposed by YouTube; automatic translation is never used.
- Prefers manual subtitles in the selected/original language, falling back to auto-generated subtitles in the same language.
- Remembers the last GUI output format; the first default is `.srt`.
- Adaptive modal progress estimation learns aggregate timings locally.
- Single-instance GUI behavior with foreground activation.
- Custom application icon embedded into `ytsubs.exe` and used by GUI windows/taskbar.
- Optional logging modes: `off`, `single`, `all`.

## Build / update

Clone the repository and switch to the development branch:

```cmd
git clone https://github.com/Suenee/YouTubeSubs.git
cd YouTubeSubs
git switch devel
upgrade.cmd
```

`upgrade.cmd` self-checks first, updates the current branch, prepares a local `.venv`, installs build dependencies, validates Python sources, builds the one-file Windows executable with PyInstaller, and validates the generated executable version.

The resulting application is created directly in the repository root:

```text
ytsubs.exe
```

No global `PATH` changes are required and `ytsubs.cmd` is no longer used.

## GUI

Run:

```cmd
ytsubs.exe
```

Enter a YouTube URL or video ID. The application analyzes the video, lists subtitle languages actually available, and enables Download only for a usable video. Invalid or unavailable input is shown in red without a disruptive popup.

The default language choice is `Auto`. The format selector is on the same row and offers `.srt`, `.sub`, `.txt`, and `.vtt`. The last selected format is remembered.

Analysis and download use modal progress dialogs centered relative to the main window. Cancel stops the workflow. After a successful save, YouTubeSubs asks whether the file should be opened using the standard Windows file association.

## CLI

Default output is plain text to stdout:

```cmd
ytsubs.exe VIDEO_ID
ytsubs.exe "https://www.youtube.com/watch?v=VIDEO_ID"
```

Select a format:

```cmd
ytsubs.exe VIDEO_ID --format srt
ytsubs.exe VIDEO_ID --format sub
ytsubs.exe VIDEO_ID --format txt
ytsubs.exe VIDEO_ID --format vtt
```

Redirect stdout:

```cmd
ytsubs.exe VIDEO_ID > transcript.txt
ytsubs.exe VIDEO_ID --format srt > transcript.srt
```

Select a real subtitle language exposed by YouTube:

```cmd
ytsubs.exe VIDEO_ID --lang en
ytsubs.exe VIDEO_ID --lang cs
```

Automatic translation is never used.

## Original-language heuristic

1. If `yt-dlp` reports a language matching a real subtitle track, that language is preferred.
2. Otherwise the first auto-generated transcript language is preferred because it normally reflects spoken audio.
3. If no generated transcript exists, the first manual track returned by YouTube is used.
4. Within a selected language, manual subtitles are preferred over auto-generated subtitles.

## Exit codes

| Code | Meaning |
| ---: | --- |
| 0 | Success |
| 2 | Invalid argument, URL, or video ID |
| 3 | No usable subtitle track / requested language unavailable |
| 4 | YouTube, network, or transcript API failure |
| 5 | Output file write failure |

## Local configuration

Runtime configuration, logging settings, the last GUI format, and learned progress timings are stored in:

```text
%LOCALAPPDATA%\YouTubeSubs\config.json
```

For additional details see [MANUAL.md](MANUAL.md) and [CHANGELOG.md](CHANGELOG.md).
