# YouTubeSubs Manual

YouTubeSubs 2.00 is the .NET 10 port of the last verified Python version 1.13. The port intentionally preserves the existing workflow and behavior instead of redesigning the application.

## Distribution

The distributed application is a single file:

```text
ytsubs.exe
```

It is published as a self-contained Windows x64 .NET 10 application. No adjacent runtime, DLL, Python installation, CMD file, or configuration file is required to start it.

The verified Python implementation remains available in branch `ALFA`.

## GUI mode

Start `ytsubs.exe` without arguments.

The main window contains:

- YouTube URL / Video ID input
- Language selector
- compact output-format selector
- centered video-title/status area
- Download and Cancel buttons

The main window starts centered on the desktop. Only one GUI instance is allowed. Starting another instance activates the already running one.

### Video input

Accepted input includes:

- a plain 11-character YouTube video ID
- a normal YouTube watch URL
- `youtu.be` URLs
- Shorts, Embed, and Live URLs
- damaged or surrounding text when exactly one unambiguous 11-character YouTube ID can be recovered

Analysis starts automatically 500 ms after input stops changing.

If the input is invalid or the video cannot be used, the GUI displays:

```text
Invalid Video ID. Please try again...
```

and Download remains disabled.

For a valid video, the title is shown as a blue underlined clickable link to the canonical YouTube watch URL.

## Subtitle language selection

The first option is `Auto`.

Automatic translation is never requested.

The automatic language rule matches the verified Python behavior:

1. Prefer the language of the first auto-generated closed-caption track, because it normally represents the spoken language.
2. If no auto-generated track exists, use the first manual track returned by YouTube.
3. Within the selected language, prefer a manual track over an auto-generated track.

The language list shows the real closed-caption tracks supplied by YouTube, including whether a language is available as manual subtitles, auto-generated subtitles, or both.

## Output formats

The GUI selector contains, in order:

```text
.srt
.sub
.txt
.vtt
```

The last selected GUI format is stored locally. With no saved setting, `.srt` is selected.

`.sub` uses the SubViewer time format and does not depend on FPS.

## Save dialog

The native Windows Save As dialog proposes a filename based on the video title and selected extension.

Its owner is positioned across the working area of the monitor containing the main application, so Windows centers the native dialog on that monitor instead of offsetting it relative to the small YouTubeSubs window.

## Progress and cancellation

Analysis and download use modal progress dialogs centered relative to the main window.

The progress model learns average durations for these phases:

- metadata
- transcripts
- download
- format
- save

After several successful operations it displays an estimated remaining time. Learned values are stored in local configuration.

Pressing Cancel cancels the current workflow and returns to the application. Closing a progress dialog with its window X requests cancellation and exits the application.

## Successful save

After a successful save YouTubeSubs asks:

```text
Subtitles saved successfully.

Open the file?
```

Yes opens the file with its Windows file association and exits. No exits without opening it.

## CLI mode

Supplying arguments runs CLI mode.

Examples:

```cmd
ytsubs.exe VIDEO_ID
ytsubs.exe "https://www.youtube.com/watch?v=VIDEO_ID"
ytsubs.exe VIDEO_ID --format srt
ytsubs.exe VIDEO_ID --format sub
ytsubs.exe VIDEO_ID --format txt
ytsubs.exe VIDEO_ID --format vtt
ytsubs.exe VIDEO_ID --lang cs
ytsubs.exe VIDEO_ID --format srt -o subtitles.srt
ytsubs.exe --version
```

Default CLI output format is TXT. Without `-o` / `--output`, subtitle text is written to stdout. Errors are written to stderr so normal pipes and redirection remain usable.

## Exit codes

| Code | Meaning |
| ---: | --- |
| 0 | Success |
| 2 | Invalid argument, URL, video ID, or option |
| 3 | No usable subtitle track / requested language unavailable |
| 4 | YouTube, network, or caption API failure |
| 5 | Output file write failure |

## Configuration and logging

Configuration is stored at:

```text
%LOCALAPPDATA%\YouTubeSubs\config.json
```

The configuration contains the last selected output format, learned progress timing data, sample count, and logging mode.

Logging modes are:

- `off` — no file logging
- `single` — overwrite the log at application start
- `all` — append across application runs

The log file is:

```text
%LOCALAPPDATA%\YouTubeSubs\ytsubs.log
```

## Development and upgrade

Development is performed on branch `devel`. The preserved Python reference implementation is branch `ALFA`.

Run:

```cmd
upgrade.cmd
```

The updater:

1. checks whether `upgrade.cmd` itself is current and runs the newer copy first when necessary
2. refuses to overwrite tracked local changes
3. synchronizes the current branch with origin
4. verifies that a .NET 10 SDK is available
5. installs the current Microsoft .NET 10 SDK through `winget` when needed
6. removes obsolete local Python/Nuitka build artifacts left by the previous implementation
7. reconstructs and validates `assets\ytsubs.ico` from the tracked Base64 source
8. restores NuGet dependencies
9. builds the sources in Release mode
10. publishes a self-contained single-file `win-x64` candidate
11. validates `ytsubs.exe --version` before replacing the existing executable
12. copies the candidate to the repository root
13. performs a portable smoke test from an empty temporary directory
14. reports executable size and a CLI cold-start sample

The application icon stored as `assets/ytsubs.ico.b64` is embedded directly into the .NET executable during publish and reused by the WinForms windows.
