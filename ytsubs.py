#!/usr/bin/env python3
"""YouTubeSubs 1.12 core services."""

from __future__ import annotations

import json
import logging
import os
import re
import sys
import threading
from dataclasses import dataclass
from pathlib import Path
from urllib.parse import parse_qs, urlparse

from youtube_transcript_api import YouTubeTranscriptApi
from youtube_transcript_api.formatters import SRTFormatter, TextFormatter

VERSION = "1.12"
VIDEO_ID_RE = re.compile(r"^[A-Za-z0-9_-]{11}$")
VIDEO_ID_ANYWHERE_RE = re.compile(r"(?<![A-Za-z0-9_-])([A-Za-z0-9_-]{11})(?![A-Za-z0-9_-])")
DEFAULT_STATS = {"metadata": 0.8, "transcripts": 1.0, "download": 0.8, "format": 0.1, "save": 0.1}
EXIT_OK, EXIT_USAGE, EXIT_NO_SUBS, EXIT_API, EXIT_OUTPUT = 0, 2, 3, 4, 5
GUI_PORT = 45871
APP_ID = "Suenee.YouTubeSubs"


class CancelledError(Exception):
    pass


def project_dir() -> Path:
    if getattr(sys, "frozen", False):
        return Path(sys.executable).resolve().parent
    return Path(__file__).resolve().parent


def asset_path(name: str) -> Path:
    base = Path(getattr(sys, "_MEIPASS", project_dir()))
    return base / "assets" / name


def app_dir() -> Path:
    base = os.environ.get("LOCALAPPDATA") or str(Path.home() / ".config")
    path = Path(base) / "YouTubeSubs"
    path.mkdir(parents=True, exist_ok=True)
    return path


def load_config() -> dict:
    path = app_dir() / "config.json"
    default = {"logging": "off", "samples": 0, "phase_seconds": DEFAULT_STATS.copy()}
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
        default.update(data)
        default["phase_seconds"] = {**DEFAULT_STATS, **data.get("phase_seconds", {})}
    except (OSError, ValueError, TypeError):
        pass
    return default


def save_config(config: dict) -> None:
    try:
        (app_dir() / "config.json").write_text(json.dumps(config, indent=2), encoding="utf-8")
    except OSError:
        pass


def setup_logging(mode: str) -> None:
    if mode not in {"off", "single", "all"}:
        mode = "off"
    if mode == "off":
        logging.disable(logging.CRITICAL)
        return
    logging.basicConfig(
        filename=app_dir() / "ytsubs.log",
        filemode="w" if mode == "single" else "a",
        level=logging.DEBUG,
        format="%(asctime)s %(levelname)s %(message)s",
    )


def extract_video_id(value: str) -> str:
    """Extract an 11-character YouTube ID, tolerating damaged URLs and surrounding text."""
    value = value.strip()
    if VIDEO_ID_RE.fullmatch(value):
        return value
    patterns = (
        r"(?:[?&]v=)([A-Za-z0-9_-]{11})",
        r"(?:youtu\.be/)([A-Za-z0-9_-]{11})",
        r"(?:youtube(?:-nocookie)?\.com/(?:shorts|embed|live)/)([A-Za-z0-9_-]{11})",
    )
    for pattern in patterns:
        match = re.search(pattern, value, flags=re.IGNORECASE)
        if match:
            return match.group(1)
    candidates = VIDEO_ID_ANYWHERE_RE.findall(value)
    if len(candidates) == 1:
        return candidates[0]
    try:
        parsed = urlparse(value)
    except ValueError as exc:
        raise ValueError("Invalid YouTube URL or video ID.") from exc
    host = parsed.netloc.lower().split(":")[0]
    candidate = ""
    if host in {"youtu.be", "www.youtu.be"}:
        candidate = parsed.path.strip("/").split("/")[0]
    elif host.endswith("youtube.com"):
        if parsed.path == "/watch":
            candidate = parse_qs(parsed.query).get("v", [""])[0]
        else:
            parts = [part for part in parsed.path.split("/") if part]
            if len(parts) >= 2 and parts[0] in {"shorts", "embed", "live"}:
                candidate = parts[1]
    if not VIDEO_ID_RE.fullmatch(candidate):
        raise ValueError("Invalid YouTube URL or video ID.")
    return candidate


def canonical_url(video_id: str) -> str:
    return f"https://www.youtube.com/watch?v={video_id}"


def clean_filename(name: str) -> str:
    cleaned = re.sub(r'[<>:"/\\|?*\x00-\x1f]', "_", name).strip().rstrip(". ")
    return cleaned[:180] or "youtube_subtitles"


@dataclass(frozen=True)
class Track:
    language: str
    code: str
    generated: bool


@dataclass
class VideoInfo:
    video_id: str
    title: str
    tracks: list[Track]
    original_code: str

    def language_choices(self) -> list[tuple[str, str]]:
        grouped: dict[str, list[Track]] = {}
        for track in self.tracks:
            grouped.setdefault(track.code, []).append(track)
        choices = []
        for code, tracks in grouped.items():
            kinds = []
            if any(not track.generated for track in tracks):
                kinds.append("manual")
            if any(track.generated for track in tracks):
                kinds.append("auto")
            choices.append((f"{tracks[0].language} ({code}) — {' + '.join(kinds)}", code))
        return choices


class Engine:
    def __init__(self, phase_callback=None, cancel_event: threading.Event | None = None):
        self.api = YouTubeTranscriptApi()
        self.phase_callback = phase_callback or (lambda *_: None)
        self.cancel_event = cancel_event

    def _check_cancel(self) -> None:
        if self.cancel_event and self.cancel_event.is_set():
            raise CancelledError()

    def _phase(self, name: str) -> None:
        self._check_cancel()
        logging.debug("phase=%s", name)
        self.phase_callback(name)

    def metadata(self, video_id: str) -> dict:
        self._phase("metadata")
        # yt-dlp is intentionally imported only when metadata is actually requested.
        # This keeps normal GUI/--version startup free from yt-dlp's large import tree.
        from yt_dlp import YoutubeDL
        opts = {"quiet": True, "no_warnings": True, "skip_download": True}
        with YoutubeDL(opts) as ydl:
            data = ydl.extract_info(canonical_url(video_id), download=False) or {}
        self._check_cancel()
        return data

    @staticmethod
    def _match_language_hint(hint: str, tracks: list[Track]) -> str | None:
        if not hint:
            return None
        hint = hint.lower().replace("_", "-")
        for track in tracks:
            if track.code.lower().replace("_", "-") == hint:
                return track.code
        base = hint.split("-", 1)[0]
        for track in tracks:
            if track.code.lower().replace("_", "-").split("-", 1)[0] == base:
                return track.code
        return None

    def analyze(self, value: str) -> VideoInfo:
        video_id = extract_video_id(value)
        metadata = {}
        try:
            metadata = self.metadata(video_id)
        except CancelledError:
            raise
        except Exception as exc:
            logging.warning("Metadata lookup failed: %s", exc)
        self._phase("transcripts")
        items = list(self.api.list(video_id))
        self._check_cancel()
        if not items:
            raise LookupError("This video has no available subtitles.")
        tracks = [Track(item.language, item.language_code, bool(item.is_generated)) for item in items]
        original = self._match_language_hint(str(metadata.get("language") or ""), tracks)
        if original is None:
            original = next((track.code for track in tracks if track.generated), tracks[0].code)
        return VideoInfo(video_id, str(metadata.get("title") or video_id), tracks, original)

    def select_track(self, info: VideoInfo, lang: str | None):
        self._check_cancel()
        code = lang or info.original_code
        matches = [item for item in self.api.list(info.video_id) if item.language_code.lower() == code.lower()]
        self._check_cancel()
        if not matches:
            raise LookupError(f"No subtitle track is available for language '{code}'.")
        return next((item for item in matches if not item.is_generated), matches[0])


def write_output(text: str, output: str | None) -> None:
    if output:
        Path(output).write_text(text, encoding="utf-8")
    else:
        sys.stdout.write(text)
        if text and not text.endswith("\n"):
            sys.stdout.write("\n")


def center_window(window) -> None:
    window.update_idletasks()
    width = window.winfo_width()
    height = window.winfo_height()
    screen_w = window.winfo_screenwidth()
    screen_h = window.winfo_screenheight()
    x = max(0, (screen_w - width) // 2)
    y = max(0, (screen_h - height) // 2)
    window.geometry(f"+{x}+{y}")


def apply_window_icon(window) -> None:
    icon = asset_path("ytsubs.ico")
    if icon.exists():
        try:
            window.iconbitmap(default=str(icon))
        except Exception:
            logging.exception("Unable to set window icon")
