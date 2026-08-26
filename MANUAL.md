# YouTubeSubs User Manual

YouTubeSubs is a small Windows-focused Python application for downloading subtitle tracks that YouTube actually exposes for a video. It can be used either through a simple graphical interface or as a command-line tool suitable for scripts, pipes, and redirection.

Automatic subtitle translation is intentionally not used. When both manual and auto-generated subtitles are available in the selected language, YouTubeSubs prefers the manual track.

## 1. Requirements

- Windows 10 or Windows 11
- Python 3.10 or newer
- Git
- Internet access

Tkinter is included with the standard Python installation for Windows.

## 2. Installation

Clone the stable branch:

```cmd
git clone https://github.com/Suenee/YouTubeSubs.git
cd YouTubeSubs
upgrade.cmd
```

The project uses its own `.venv` directory. It does not modify the global Windows `PATH`.

The command is launched from the repository root through:

```text
ytsubs.cmd
```

This launcher automatically uses the executable installed inside `.venv`.

To verify the installation:

```cmd
ytsubs --version
```

## 3. Updating

From the repository root run:

```cmd
upgrade.cmd
```

`upgrade.cmd` performs the complete local update workflow:

1. Detects the currently checked-out Git branch.
2. Checks whether a newer `upgrade.cmd` exists remotely and runs the newer copy first when needed.
3. Refuses to overwrite tracked local changes.
4. Updates the current branch using fast-forward only.
5. Creates `.venv` if it does not exist.
6. Updates Python dependencies.
7. Installs the local YouTubeSubs package into `.venv`.
8. Validates Python syntax.
9. Runs the root `ytsubs` launcher.
10. Verifies that the version returned by `ytsubs --version` exactly matches the project version in `pyproject.toml`.

Any failed step returns a non-zero exit code and prints a clear error message.

## 4. GUI mode

Start YouTubeSubs from the repository root without parameters:

```cmd
ytsubs
```

The main window opens centered on the desktop.

Only one GUI instance is allowed. If YouTubeSubs is already running and `ytsubs` is started again, the second process exits and the existing application is brought to the foreground.

### 4.1 Entering a video

Paste either a complete YouTube URL or an 11-character video ID.

Examples:

```text
https://www.youtube.com/watch?v=dQw4w9WgXcQ
```

```text
dQw4w9WgXcQ
```

Supported URL forms include normal watch URLs, `youtu.be`, Shorts, embed URLs, and live URLs.

### 4.2 Video analysis

After a short input pause, YouTubeSubs automatically analyzes the video.

Analysis runs in a modal progress dialog. The main window cannot be changed while analysis is active.

The dialog shows the current phase and an approximate progress bar. After several successful operations, the program also estimates the remaining time using timing data learned locally on that computer.

Press **Cancel** to stop the analysis and return to the main window.

Closing the progress window using the Windows close button exits YouTubeSubs completely.

### 4.3 Language selection

After analysis, the language list contains only subtitle languages actually exposed by YouTube for that video.

Typical examples are:

```text
Auto / Original
English (en) — manual + auto
Czech (cs) — manual
German (de) — auto
```

`Auto / Original` uses the application's deterministic original-language heuristic.

If both manual and auto-generated tracks exist for the same language, the manual track is used.

YouTube automatic translation is never requested.

### 4.4 TXT and SRT

Choose one output format:

- **TXT** — transcript text without timestamps.
- **SRT** — standard SubRip subtitles with timestamps.

### 4.5 Downloading

Press **Download** and select the destination file.

YouTubeSubs proposes a filename based on the YouTube video title and automatically uses the selected `.txt` or `.srt` extension.

Download, formatting, and saving run in a second modal progress dialog.

Press **Cancel** to stop the operation and return to the main window. A cancelled operation is not intended to leave a partial output file.

### 4.6 Opening the saved file

After a successful save, YouTubeSubs asks:

```text
Open the file?
```

- **Yes** — the file is passed to Windows and opened using the current default application associated with `.txt` or `.srt`.
- **No** — the application exits without opening the file.

YouTubeSubs exits after either choice.

## 5. CLI mode

Providing a video argument switches the application directly into CLI mode. No GUI is opened.

### 5.1 Basic TXT output

```cmd
ytsubs VIDEO_ID
```

or:

```cmd
ytsubs "https://www.youtube.com/watch?v=VIDEO_ID"
```

The transcript is written to `stdout`.

### 5.2 Redirecting output to a file

```cmd
ytsubs VIDEO_ID > transcript.txt
```

For SRT:

```cmd
ytsubs VIDEO_ID --format srt > transcript.srt
```

### 5.3 Pipes

Because transcript data is written to `stdout` and errors are written to `stderr`, output can be piped directly into another command:

```cmd
ytsubs VIDEO_ID --format txt | another-command
```

### 5.4 Explicit output file

```cmd
ytsubs VIDEO_ID --format srt -o transcript.srt
```

### 5.5 Selecting a language

```cmd
ytsubs VIDEO_ID --lang en
```

```cmd
ytsubs VIDEO_ID --lang cs
```

The requested language must exist as a real subtitle track exposed by YouTube. Automatic translation is not used as a fallback.

### 5.6 Version

```cmd
ytsubs --version
```

## 6. Original-language selection

YouTube does not expose one universally reliable original-language field for every video through the APIs used by this project.

YouTubeSubs therefore uses this deterministic heuristic:

1. If `yt-dlp` reports a language and that language matches an available real subtitle track, use that language.
2. Otherwise use the language of the first auto-generated subtitle track, because it normally corresponds to the spoken audio.
3. If no auto-generated track exists, use the first manual subtitle track returned by YouTube.
4. Within the selected language, prefer a manual track over an auto-generated track.

This works well for typical videos but cannot guarantee the real original language in every unusual case. For videos with several manually uploaded language tracks and insufficient metadata, select the desired language explicitly.

## 7. Exit codes

| Code | Meaning |
| ---: | --- |
| 0 | Success |
| 2 | Invalid argument, URL, or video ID |
| 3 | No usable subtitle track or requested language unavailable |
| 4 | YouTube, network, or transcript API failure |
| 5 | Output file write failure |

This makes YouTubeSubs suitable for batch files and other automation.

## 8. Local configuration

Runtime configuration is stored in:

```text
%LOCALAPPDATA%\YouTubeSubs\config.json
```

The file contains application settings and aggregate timing statistics used by the adaptive progress estimator.

It does not intentionally store downloaded transcript contents, YouTube URLs, video IDs, or video titles as history.

## 9. Logging

The configuration supports three logging modes:

```json
"logging": "off"
```

Available values:

- `off` — no log file.
- `single` — one log for the current application run; previous log content is replaced.
- `all` — append activity across multiple runs.

When enabled, the log is stored in:

```text
%LOCALAPPDATA%\YouTubeSubs\ytsubs.log
```

## 10. Development branches

- `main` — stable releases.
- `devel` — active development.

Users should normally stay on `main`.

Developers and testers can switch to the development branch with:

```cmd
git switch devel
upgrade.cmd
```

To return to the stable version:

```cmd
git switch main
upgrade.cmd
```

## 11. Dependencies

YouTubeSubs primarily uses:

- `youtube-transcript-api` for subtitle discovery and retrieval.
- `yt-dlp` for video metadata such as title and language hints.

YouTube changes its internal behavior frequently. Running `upgrade.cmd` keeps dependencies within the compatible ranges defined by the project.

## 12. Known limitations

- Availability of subtitles depends entirely on what YouTube exposes for the selected video.
- YouTube can rate-limit or block automated requests from some IP addresses.
- The original language cannot be identified with absolute certainty for every possible video.
- Cancel can stop the workflow between network operations, but an HTTP request already in progress may need to return before the worker thread fully terminates.
- The GUI workflow is primarily designed and tested for Windows.
