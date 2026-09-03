# YouTubeSubs

YouTubeSubs is a Windows utility for downloading YouTube subtitles and preparing video/audio clips. The current `devel` branch is the .NET 10 implementation. The last verified Python implementation is preserved in branch `ALFA` as version 1.13 at commit `0b7271a717e3c308c9dd2bbaf2a75fdc0a532cd7`.

## Applications

The project publishes two executables with intentionally different Windows subsystems:

- `ytsubs.exe` — WinForms GUI application.
- `ytsubs-cli.exe` — console application for synchronous CLI subtitle workflows.

The GUI remains single-instance. A second GUI launch activates the existing window; project-media launch arguments are forwarded to the existing instance.

## Features

- Accept a YouTube URL or plain 11-character video ID.
- Download manual or auto-generated subtitles without automatic translation.
- Subtitle formats: `.srt`, `.sub`, `.txt`, `.vtt`.
- Optional MP4 video and MP3 audio output.
- Exact partial-video cuts with a clean first keyframe.
- Video-only output creates a silent MP4; Video + Audio creates one MP4 containing both streams.
- Project-media workflow for VoicePrompter/BROLL preparation.
- Mirror overall processing progress to the Windows taskbar so progress remains visible while the GUI is minimized.
- Repository-local configuration and logs, including mapped/network-drive repositories.
- Logging modes `off`, `single`, and `all`.

## Technology

- .NET 10
- Windows Forms
- `YoutubeExplode` 6.6.2
- yt-dlp, FFmpeg, and ffprobe maintained by `tools-update.ps1`
- self-contained, single-file `win-x64` publishing

## Build / update

Clone the repository and switch to `devel`:

```cmd
git clone https://github.com/Suenee/YouTubeSubs.git
cd YouTubeSubs
git switch devel
upgrade.cmd
```

`upgrade.cmd` is a thin self-updating launcher. It retrieves the current `upgrade.ps1` from `origin/devel`, safely synchronizes the repository, supports mapped/UNC repository locations, repairs updater bootstrap line-ending changes, verifies or installs the current .NET 10 SDK, updates media tools, builds both applications, validates their PE subsystems and versions, deploys them, and restarts the GUI when it was running before the update.

## GUI

Run:

```cmd
ytsubs.exe
```

Enter a YouTube URL or video ID. Analysis starts automatically after 500 ms. Select subtitles, video, and/or audio, set the optional From/To range, then download. During analysis and processing, the learned overall progress is also shown on the Windows taskbar button.

## Project media mode

Project media mode is intended for VoicePrompter/news-production workflows and is launched through the GUI executable.

Audio + video MP4:

```cmd
ytsubs.exe --avid=17 --project="Zprávy z exopolitiky 29"
```

Silent BROLL MP4:

```cmd
ytsubs.exe --brollid=17 --project="Zprávy z exopolitiky 29"
```

In project mode:

- `--avid` locks Video + Audio on and disables Subtitles/language selection.
- `--brollid` locks Video on, Audio off, and disables Subtitles/language selection.
- YouTubeSubs proposes a short editable clip name, limited to four words by default.
- Download does not ask for an output directory.
- The application searches the configured editing root for exactly one `YYYYMMDD Project` directory with the requested project name. If none exists, today's directory is created. Multiple dated directories for the same project name are treated as an error.
- The resolved `YYYYMMDD Project` directory name is shown in full in the GUI.
- Output is stored below `BROLL` as `NNN - short clip name.mp4`.
- If the requested ID already exists, the user gets `Replace`, `Move to XX`, or `Cancel`. `XX` is the first free ID after the requested number.
- The current marker is shown prominently as a clickable link. Clicking it copies the configured marker to Clipboard without downloading.
- After a successful download, the marker template for the selected mode is rendered with the final ID and copied to Clipboard.
- After both media finalization and Clipboard update succeed, project mode closes automatically. Errors, cancellation, or Clipboard failure leave the window open.

## Marker templates and project configuration

Configuration is stored beside the application under:

```text
config\config.json
```

Relevant project-media settings are:

```json
{
  "editing_root": "N:\\WORK\\Sueneé Universe\\EDITING",
  "clip_name_max_words": 4,
  "av_marker_html": "VLC AV {id}",
  "broll_marker_html": "VLC LOOP {id}"
}
```

Replace the marker values with the actual HTML fragments required by VoicePrompter. The only required placeholder is `{id}`. YouTubeSubs publishes the rendered marker to Clipboard as both plain text and HTML clipboard data.

## CLI

Use `ytsubs-cli.exe` for console subtitle workflows:

```cmd
ytsubs-cli.exe VIDEO_ID
ytsubs-cli.exe "https://www.youtube.com/watch?v=VIDEO_ID"
ytsubs-cli.exe VIDEO_ID --format srt
ytsubs-cli.exe VIDEO_ID --format sub
ytsubs-cli.exe VIDEO_ID --format txt
ytsubs-cli.exe VIDEO_ID --format vtt
ytsubs-cli.exe VIDEO_ID --lang en
ytsubs-cli.exe VIDEO_ID -o subtitles.srt --format srt
ytsubs-cli.exe --version
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

## Local state

Persistent development/runtime state remains inside the repository/application directory:

```text
config\config.json
logs\YouTubeSubs.log
logs\upgrade.log
```

`%TEMP%` is reserved for disposable processing/bootstrap data only.

See [MANUAL.md](MANUAL.md) and [CHANGELOG.md](CHANGELOG.md) for additional details.
