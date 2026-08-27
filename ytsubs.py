#!/usr/bin/env python3
"""YouTubeSubs 1.16 core services."""

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

import requests
from youtube_transcript_api import YouTubeTranscriptApi
from youtube_transcript_api.formatters import SRTFormatter, TextFormatter

VERSION = "1.16"
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
    return Path(getattr(sys, "_MEIPASS", project_dir())) / "assets" / name

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
    logging.basicConfig(filename=app_dir() / "ytsubs.log", filemode="w" if mode == "single" else "a", level=logging.DEBUG, format="%(asctime)s %(levelname)s %(message)s")

def extract_video_id(value: str) -> str:
    value = value.strip()
    if VIDEO_ID_RE.fullmatch(value):
        return value
    for pattern in (
        r"(?:[?&]v=)([A-Za-z0-9_-]{11})",
        r"(?:youtu\.be/)([A-Za-z0-9_-]{11})",
        r"(?:youtube(?:-nocookie)?\.com/(?:shorts|embed|live)/)([A-Za-z0-9_-]{11})",
    ):
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
            parts = [p for p in parsed.path.split("/") if p]
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
            if any(not t.generated for t in tracks):
                kinds.append("manual")
            if any(t.generated for t in tracks):
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
        response = requests.get(
            "https://www.youtube.com/oembed",
            params={"url": canonical_url(video_id), "format": "json"},
            timeout=8,
        )
        response.raise_for_status()
        self._check_cancel()
        return response.json()

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
        tracks = [Track(i.language, i.language_code, bool(i.is_generated)) for i in items]
        original = next((t.code for t in tracks if t.generated), tracks[0].code)
        return VideoInfo(video_id, str(metadata.get("title") or video_id), tracks, original)

    def select_track(self, info: VideoInfo, lang: str | None):
        self._check_cancel()
        code = lang or info.original_code
        matches = [i for i in self.api.list(info.video_id) if i.language_code.lower() == code.lower()]
        self._check_cancel()
        if not matches:
            raise LookupError(f"No subtitle track is available for language '{code}'.")
        return next((i for i in matches if not i.is_generated), matches[0])

def write_output(text: str, output: str | None) -> None:
    if output:
        Path(output).write_text(text, encoding="utf-8")
    else:
        sys.stdout.write(text)
        if text and not text.endswith("\n"):
            sys.stdout.write("\n")

def center_window(window) -> None:
    window.update_idletasks()
    width, height = window.winfo_width(), window.winfo_height()
    screen_w, screen_h = window.winfo_screenwidth(), window.winfo_screenheight()
    window.geometry(f"+{max(0, (screen_w - width) // 2)}+{max(0, (screen_h - height) // 2)}")

def apply_window_icon(window) -> None:
    icon = asset_path("ytsubs.ico")
    if icon.exists():
        try:
            window.iconbitmap(default=str(icon))
        except Exception:
            logging.exception("Unable to set window icon")
