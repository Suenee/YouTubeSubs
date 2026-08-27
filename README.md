# YouTubeSubs

Lightweight Python CLI and GUI tool for downloading manual or auto-generated YouTube subtitles as TXT or SRT, without automatic translation.

Development takes place on the `devel` branch. Stable releases are published to `main`.

## Features

- Accepts a full YouTube URL, a damaged/incomplete YouTube URL containing a recoverable video ID, or an 11-character video ID.
- GUI mode when started without parameters.
- CLI mode when started with a video argument.
- TXT output without timestamps.
- SRT output with timestamps.
- Output to stdout, a pipe, shell redirection, or an explicit file.
- Discovers real subtitle tracks exposed by YouTube; automatic translation is never used.
- Prefers a manual subtitle track in the selected/original language and falls back to an auto-generated track in the same language.
- Suggests a safe output filename based on the video title.
- Adaptive GUI progress estimation learns aggregate phase timings on the local computer.
- Single-instance GUI behavior; a repeated launch activates the existing window.
- Invalid/unavailable GUI input quietly keeps Download disabled.
- Valid videos are shown as a centered clickable title opening the canonical YouTube watch URL.
- Custom YouTubeSubs icon is used for GUI windows and the Windows taskbar; the same icon asset is ready for future executable builds.
- Optional file logging modes: `off`, `single`, `all`.

## Requirements

- Windows is the primary supported desktop platform for the GUI workflow.
- Python 3.10 or newer.
- Tkinter (included with standard Windows Python installations).

Python dependencies are listed in `requirements.txt` and `pyproject.toml`.

## Installation / update

Clone the repository and switch to the development branch:

```cmd
git clone https://github.com/Suenee/YouTubeSubs.git
cd YouTubeSubs
git switch devel
upgrade.cmd
```

`upgrade.cmd` checks the remote version of itself first, runs a newer remote copy when necessary, refuses to overwrite tracked local changes, updates the current branch, creates `.venv`, updates dependencies, installs the Python package, and validates the root launcher and project version.

No global `PATH` modification is required. The repository contains `ytsubs.cmd`, which launches the executable from `.venv`.

From the repository root:

```cmd
ytsubs --version
```

For detailed usage, see [MANUAL.md](MANUAL.md).

## GUI

Start without arguments from the repository root:

```cmd
ytsubs
```

The main window is centered and is brought to the foreground when launched. Only one GUI instance is allowed. Starting `ytsubs` again while the GUI is already running activates and brings the existing window to the foreground.

Enter a YouTube URL or video ID. YouTubeSubs can also recover an unambiguous 11-character video ID from many damaged or incomplete URL strings. After a short pause, analysis starts in a modal progress dialog. Invalid or unavailable input simply returns to the main window with **Download** disabled.

After successful analysis, the centered video title becomes a clickable link. It always opens the normalized URL form `https://www.youtube.com/watch?v=VIDEO_ID` in the default browser.

The language selector contains only subtitle languages actually found for the video. If both manual and auto-generated tracks exist for one language, they are shown as one language choice and the manual track is preferred.

Press **Download** and choose the output path. Downloading, formatting, and saving run in a modal progress dialog with Cancel support. After a successful save, YouTubeSubs asks whether the file should be opened using the current Windows file association. The application exits after the Yes/No choice.

## CLI

Default output is TXT to stdout:

```cmd
ytsubs VIDEO_ID
ytsubs "https://www.youtube.com/watch?v=VIDEO_ID"
```

Choose a format:

```cmd
ytsubs VIDEO_ID --format txt
ytsubs VIDEO_ID --format srt
```

Redirect stdout:

```cmd
ytsubs VIDEO_ID > transcript.txt
ytsubs VIDEO_ID --format srt > transcript.srt
ytsubs VIDEO_ID --format txt | another-command
```

Write directly to a file:

```cmd
ytsubs VIDEO_ID --format srt -o transcript.srt
```

Override automatic language selection:

```cmd
ytsubs VIDEO_ID --lang en
ytsubs VIDEO_ID --lang cs
```

`--lang` selects only a subtitle track that YouTube actually exposes. YouTube automatic translation is not called.

## Original-language heuristic

YouTube does not expose one universally reliable `original_language` field for every video through `youtube-transcript-api`. YouTubeSubs therefore uses a deterministic heuristic:

1. If `yt-dlp` reports a language and that language matches a real subtitle track, use it.
2. Otherwise prefer the language of the first auto-generated track, because it is normally generated from the spoken audio.
3. If no generated track exists, use the first manual track returned by YouTube.
4. Within the selected language, prefer a manual track over an auto-generated track.

This is intentionally conservative and never treats an automatically translated subtitle as an original track. Videos with several manually uploaded languages and no useful language metadata can still require `--lang` or a manual GUI selection.

## Exit codes

| Code | Meaning |
| ---: | --- |
| 0 | Success |
| 2 | Invalid argument, URL, or video ID |
| 3 | No usable subtitle track / requested language unavailable |
| 4 | YouTube, network, or transcript API failure |
| 5 | Output file write failure |

Errors are written to stderr. Transcript content is written to stdout unless `-o/--output` is used.

## Local configuration

Runtime configuration and learned timing statistics are stored in:

```text
%LOCALAPPDATA%\YouTubeSubs\config.json
```

The default logging mode is `off`. Supported values are `off`, `single`, and `all`. When enabled, `ytsubs.log` is stored in the same local application directory.

## Dependencies

The transcript implementation uses `youtube-transcript-api` as the primary subtitle API. `yt-dlp` is used for video metadata such as the title and language hints.
