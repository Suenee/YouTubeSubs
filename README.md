# YouTubeSubs

Lightweight Python CLI and GUI tool for downloading manual or auto-generated YouTube subtitles as TXT or SRT, without automatic translation.

YouTubeSubs is designed for two workflows:

- a small Windows GUI for interactive downloads;
- a clean CLI that writes transcript data to `stdout`, making it suitable for redirection, pipes, and automation.

## Highlights

- Accepts a full YouTube URL or an 11-character video ID.
- Downloads real subtitle tracks exposed by YouTube.
- Prefers manual subtitles in the selected/original language and falls back to auto-generated subtitles in the same language.
- Never uses YouTube automatic subtitle translation.
- Supports TXT without timestamps and standard SRT with timestamps.
- GUI discovers available subtitle languages automatically.
- Suggests output filenames from the YouTube video title.
- Adaptive progress dialogs learn aggregate local timings.
- Single-instance GUI: repeated launch activates the existing window.
- CLI sends transcript data to `stdout` and errors to `stderr`.
- No global Windows `PATH` modification is required.

## Stable installation

Requirements: Windows, Git, and Python 3.10 or newer.

```cmd
git clone https://github.com/Suenee/YouTubeSubs.git
cd YouTubeSubs
upgrade.cmd
```

The project creates and maintains its own `.venv` environment. The root launcher `ytsubs.cmd` uses that environment automatically.

Verify the installation:

```cmd
ytsubs --version
```

Update later with:

```cmd
upgrade.cmd
```

`upgrade.cmd` updates itself first when necessary, refuses to overwrite tracked local changes, updates the current branch, maintains the virtual environment and dependencies, validates Python syntax, validates the root launcher, and checks that the running application version matches `pyproject.toml`.

## GUI

Start without parameters:

```cmd
ytsubs
```

Paste a YouTube URL or video ID. YouTubeSubs analyzes the video, discovers the subtitle languages actually available, lets you choose TXT or SRT, proposes a filename, and downloads the selected track.

Analysis and download use modal progress dialogs with **Cancel**. After a successful save, YouTubeSubs asks whether the file should be opened using the default Windows file association.

## CLI

Default TXT transcript to `stdout`:

```cmd
ytsubs VIDEO_ID
```

SRT:

```cmd
ytsubs VIDEO_ID --format srt
```

Redirect output:

```cmd
ytsubs VIDEO_ID > transcript.txt
ytsubs VIDEO_ID --format srt > transcript.srt
```

Pipe the transcript:

```cmd
ytsubs VIDEO_ID | another-command
```

Select a language explicitly:

```cmd
ytsubs VIDEO_ID --lang en
ytsubs VIDEO_ID --lang cs
```

Write directly to a file:

```cmd
ytsubs VIDEO_ID --format srt -o transcript.srt
```

## Documentation

See [MANUAL.md](MANUAL.md) for the complete user manual, including GUI behavior, CLI examples, language-selection rules, exit codes, configuration, logging, updates, development branches, and known limitations.

See [CHANGELOG.md](CHANGELOG.md) for release history.

## Original-language heuristic

YouTube does not expose one universally reliable original-language field for every video. YouTubeSubs therefore uses a deterministic heuristic:

1. Use a language hint reported by `yt-dlp` when it matches a real subtitle track.
2. Otherwise use the first auto-generated track language, which normally follows the spoken audio.
3. If there is no generated track, use the first manual track returned by YouTube.
4. Within the selected language, prefer manual subtitles over auto-generated subtitles.

For unusual videos with several manually uploaded languages and insufficient metadata, use `--lang` or select the language explicitly in the GUI.

## Branches

- `main` — stable releases.
- `devel` — active development.

Users should normally use `main`.

## Dependencies

- `youtube-transcript-api` — subtitle discovery and retrieval.
- `yt-dlp` — video metadata such as title and language hints.

## License

No license has been specified yet.
