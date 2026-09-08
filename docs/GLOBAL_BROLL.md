# Global BROLL workflow

YouTubeSubs 2.20 adds two GUI-only global BROLL launch modes for automation callers such as AutoHotkey SlideMarker.

```cmd
ytsubs.exe --broll:play
ytsubs.exe --broll:loop
```

`--broll:play` downloads one MP4 with video and audio. `--broll:loop` downloads a silent MP4. Both modes open a dedicated YouTubeSubs GUI where the operator enters the YouTube URL or video ID, reviews the generated clip name, and optionally adjusts the From/To range.

Global BROLL media is stored in the first available root listed in `config\broll.json`. The file is created automatically on first use with these defaults:

```json
{
  "roots": [
    "D:\\WORK\\Sueneé Universe\\BROLL",
    "N:\\WORK\\Sueneé Universe\\BROLL"
  ],
  "max_id": 9999,
  "id_min_digits": 3,
  "clip_name_max_words": 4,
  "unique_name_max_words": 8
}
```

The application does not create a missing global media root. It uses the first configured root that already exists and is accessible. Reorder `roots` if a different drive should win when more than one path is available.

Files are numbered with the smallest free positive ID. With `001`, `002`, `003`, and `005` already present, the next file receives ID `4` and is saved with a minimum three-digit prefix, for example `004 - Mars wheel Curiosity.mp4`. The numeric ID is authoritative. YouTubeSubs also tries to make the generated textual name more distinctive by adding further words from the original YouTube title when the shorter suggestion already exists, but duplicate text is not treated as an error.

The ID allocation is re-evaluated during finalization. If another YouTubeSubs process claims the same ID while a download is running, the completed temporary file is finalized under a newly allocated free ID instead of overwriting the other clip.

## AutoHotkey result file

Automation callers can provide their own temporary INI path:

```cmd
ytsubs.exe --broll:play --result-file="C:\Users\Name\AppData\Local\Temp\SlideMarker-YTSubs-12345.ini"
```

The caller owns the location and cleanup of this file. `%TEMP%` is recommended for SlideMarker. YouTubeSubs writes the result atomically after a completed operation. The INI file is UTF-16 so AutoHotkey can read it directly through its native INI functions.

Successful example:

```ini
[result]
status=success
mode=play
id=17
file=D:\WORK\Sueneé Universe\BROLL\017 - Mars wheel Curiosity.mp4
message=
```

Error example:

```ini
[result]
status=error
mode=loop
id=
file=
message=No configured global BROLL directory is available. Check config\broll.json.
```

If the operator closes the global BROLL GUI before a successful download, `status=cancelled` is written when possible.

Global BROLL mode intentionally runs as a dedicated GUI process rather than being forwarded to the normal single-instance YouTubeSubs window. This allows an AutoHotkey caller to use `RunWait` on the exact `ytsubs.exe` process and then read the result INI after that process exits.
