#!/usr/bin/env python3
"""YouTubeSubs 1.01 - small YouTube subtitle downloader (CLI + GUI)."""

from __future__ import annotations

import argparse
import json
import logging
import os
import re
import sys
import threading
import time
from dataclasses import dataclass
from pathlib import Path
from urllib.parse import parse_qs, urlparse

from youtube_transcript_api import YouTubeTranscriptApi
from youtube_transcript_api.formatters import SRTFormatter, TextFormatter
from yt_dlp import YoutubeDL

VERSION = "1.01"
VIDEO_ID_RE = re.compile(r"^[A-Za-z0-9_-]{11}$")
DEFAULT_STATS = {"metadata": 0.8, "transcripts": 1.0, "download": 0.8, "format": 0.1, "save": 0.1}
EXIT_OK, EXIT_USAGE, EXIT_NO_SUBS, EXIT_API, EXIT_OUTPUT = 0, 2, 3, 4, 5


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
    value = value.strip()
    if VIDEO_ID_RE.fullmatch(value):
        return value
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
    def __init__(self, phase_callback=None):
        self.api = YouTubeTranscriptApi()
        self.phase_callback = phase_callback or (lambda *_: None)

    def _phase(self, name: str) -> None:
        logging.debug("phase=%s", name)
        self.phase_callback(name)

    def metadata(self, video_id: str) -> dict:
        self._phase("metadata")
        opts = {"quiet": True, "no_warnings": True, "skip_download": True}
        with YoutubeDL(opts) as ydl:
            return ydl.extract_info(canonical_url(video_id), download=False) or {}

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
        except Exception as exc:
            logging.warning("Metadata lookup failed: %s", exc)
        self._phase("transcripts")
        items = list(self.api.list(video_id))
        if not items:
            raise LookupError("This video has no available subtitles.")
        tracks = [Track(item.language, item.language_code, bool(item.is_generated)) for item in items]

        # Deterministic original-language heuristic:
        # 1) yt-dlp language metadata when it matches a real track;
        # 2) first generated track, normally generated from spoken audio;
        # 3) first manual track returned by YouTube.
        original = self._match_language_hint(str(metadata.get("language") or ""), tracks)
        if original is None:
            original = next((track.code for track in tracks if track.generated), tracks[0].code)
        return VideoInfo(video_id, str(metadata.get("title") or video_id), tracks, original)

    def select_track(self, info: VideoInfo, lang: str | None):
        code = lang or info.original_code
        matches = [item for item in self.api.list(info.video_id) if item.language_code.lower() == code.lower()]
        if not matches:
            raise LookupError(f"No subtitle track is available for language '{code}'.")
        return next((item for item in matches if not item.is_generated), matches[0])

    def fetch(self, info: VideoInfo, fmt: str, lang: str | None = None) -> str:
        self._phase("download")
        transcript = self.select_track(info, lang).fetch()
        self._phase("format")
        return SRTFormatter().format_transcript(transcript) if fmt == "srt" else TextFormatter().format_transcript(transcript)


def write_output(text: str, output: str | None) -> None:
    if output:
        Path(output).write_text(text, encoding="utf-8")
    else:
        sys.stdout.write(text)
        if text and not text.endswith("\n"):
            sys.stdout.write("\n")


def cli(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(prog="ytsubs", description="Download original YouTube subtitles as TXT or SRT.")
    parser.add_argument("video", help="YouTube URL or 11-character video ID")
    parser.add_argument("--format", choices=("txt", "srt"), default="txt")
    parser.add_argument("--lang", help="Subtitle language code, e.g. en or cs. No automatic translation is used.")
    parser.add_argument("-o", "--output", help="Write output to a file instead of stdout")
    parser.add_argument("--version", action="version", version=f"%(prog)s {VERSION}")
    args = parser.parse_args(argv)
    try:
        engine = Engine()
        info = engine.analyze(args.video)
        text = engine.fetch(info, args.format, args.lang)
    except ValueError as exc:
        print(f"ytsubs: {exc}", file=sys.stderr)
        return EXIT_USAGE
    except LookupError as exc:
        print(f"ytsubs: {exc}", file=sys.stderr)
        return EXIT_NO_SUBS
    except Exception as exc:
        logging.exception("YouTube/API failure")
        print(f"ytsubs: unable to retrieve subtitles: {exc}", file=sys.stderr)
        return EXIT_API
    try:
        write_output(text, args.output)
    except OSError as exc:
        print(f"ytsubs: unable to write output: {exc}", file=sys.stderr)
        return EXIT_OUTPUT
    return EXIT_OK


def gui() -> int:
    import tkinter as tk
    from tkinter import filedialog, messagebox, ttk

    config = load_config()
    phase_stats = config["phase_seconds"]
    root = tk.Tk()
    root.title(f"YouTubeSubs {VERSION}")
    root.resizable(False, False)
    frame = ttk.Frame(root, padding=14)
    frame.grid()

    url_var = tk.StringVar()
    lang_var = tk.StringVar(value="Auto / Original")
    fmt_var = tk.StringVar(value="txt")
    status_var = tk.StringVar(value="Enter a YouTube URL or video ID.")
    eta_var = tk.StringVar(value="")
    progress_var = tk.DoubleVar(value=0)
    state = {"info": None, "timer": None, "phase": None, "phase_start": 0.0, "completed": 0.0, "busy": False, "lang_map": {}}
    phases = ["metadata", "transcripts", "download", "format", "save"]
    labels = {"metadata": "Reading video information...", "transcripts": "Finding available subtitles...", "download": "Downloading subtitles...", "format": "Creating output...", "save": "Saving file..."}

    ttk.Label(frame, text="YouTube URL / Video ID").grid(row=0, column=0, columnspan=2, sticky="w")
    url_entry = ttk.Entry(frame, textvariable=url_var, width=66)
    url_entry.grid(row=1, column=0, columnspan=2, sticky="ew", pady=(2, 10))
    ttk.Label(frame, text="Language").grid(row=2, column=0, sticky="w")
    lang_box = ttk.Combobox(frame, textvariable=lang_var, state="readonly", width=42, values=["Auto / Original"])
    lang_box.grid(row=3, column=0, sticky="w", pady=(2, 10))
    fmt_frame = ttk.Frame(frame)
    fmt_frame.grid(row=3, column=1, sticky="e")
    ttk.Radiobutton(fmt_frame, text="TXT", variable=fmt_var, value="txt").grid(row=0, column=0)
    ttk.Radiobutton(fmt_frame, text="SRT", variable=fmt_var, value="srt").grid(row=0, column=1)
    ttk.Progressbar(frame, variable=progress_var, maximum=100, length=500).grid(row=4, column=0, columnspan=2, sticky="ew", pady=(4, 4))
    ttk.Label(frame, textvariable=status_var).grid(row=5, column=0, columnspan=2, sticky="w")
    ttk.Label(frame, textvariable=eta_var).grid(row=6, column=0, columnspan=2, sticky="w")
    buttons = ttk.Frame(frame)
    buttons.grid(row=7, column=0, columnspan=2, sticky="e", pady=(12, 0))

    def phase(name: str) -> None:
        now = time.monotonic()
        old = state["phase"]
        if old:
            duration = max(0.01, now - state["phase_start"])
            old_avg = float(phase_stats.get(old, DEFAULT_STATS[old]))
            phase_stats[old] = round(old_avg * 0.75 + duration * 0.25, 3)
            state["completed"] += old_avg
        state["phase"] = name
        state["phase_start"] = now
        root.after(0, lambda: status_var.set(labels.get(name, name)))

    def animate() -> None:
        if state["busy"] and state["phase"]:
            total = sum(float(phase_stats.get(name, DEFAULT_STATS[name])) for name in phases)
            avg = float(phase_stats.get(state["phase"], 0.5))
            elapsed = time.monotonic() - state["phase_start"]
            partial = avg * min(0.92, elapsed / max(avg, 0.05))
            progress_var.set(min(96.0, (state["completed"] + partial) / max(total, 0.1) * 100))
            if config.get("samples", 0) >= 3:
                eta = max(0.0, total - state["completed"] - min(elapsed, avg))
                eta_var.set(f"Estimated time remaining: ~{max(1, round(eta))} s")
            root.after(80, animate)

    def finish_stats() -> None:
        if state["phase"]:
            duration = max(0.01, time.monotonic() - state["phase_start"])
            name = state["phase"]
            phase_stats[name] = round(float(phase_stats.get(name, DEFAULT_STATS[name])) * 0.75 + duration * 0.25, 3)
        config["samples"] = int(config.get("samples", 0)) + 1
        config["phase_seconds"] = phase_stats
        save_config(config)

    def analysis_done(info: VideoInfo) -> None:
        state["info"] = info
        state["busy"] = False
        progress_var.set(0)
        eta_var.set("")
        status_var.set(f"Ready: {info.title}")
        choices = info.language_choices()
        state["lang_map"] = dict(choices)
        lang_box["values"] = ["Auto / Original"] + [label for label, _ in choices]
        lang_var.set("Auto / Original")
        download_btn.state(["!disabled"])

    def analysis_failed(error: str) -> None:
        state["info"] = None
        state["busy"] = False
        progress_var.set(0)
        eta_var.set("")
        status_var.set(error)
        lang_box["values"] = ["Auto / Original"]
        download_btn.state(["disabled"])

    def run_analysis(value: str) -> None:
        try:
            info = Engine(phase).analyze(value)
            root.after(0, lambda: analysis_done(info))
        except Exception as exc:
            error = str(exc)
            root.after(0, lambda: analysis_failed(error))

    def analyze_now() -> None:
        value = url_var.get().strip()
        if not value:
            return
        try:
            extract_video_id(value)
        except ValueError:
            return
        state.update({"busy": True, "phase": None, "completed": 0.0})
        progress_var.set(1)
        eta_var.set("")
        threading.Thread(target=run_analysis, args=(value,), daemon=True).start()
        animate()

    def schedule_analysis(*_) -> None:
        if state["timer"]:
            root.after_cancel(state["timer"])
        download_btn.state(["disabled"])
        state["timer"] = root.after(500, analyze_now)

    def download() -> None:
        info: VideoInfo | None = state["info"]
        if not info:
            return
        fmt = fmt_var.get()
        selected_label = lang_var.get()
        selected_lang = None if selected_label == "Auto / Original" else state["lang_map"].get(selected_label)
        proposed = clean_filename(info.title) + "." + fmt
        path = filedialog.asksaveasfilename(initialfile=proposed, defaultextension="." + fmt, filetypes=[(fmt.upper(), "*." + fmt), ("All files", "*.*")])
        if not path:
            return
        download_btn.state(["disabled"])
        state.update({"busy": True, "phase": None, "completed": 0.0})
        progress_var.set(1)
        eta_var.set("")

        def worker() -> None:
            try:
                text = Engine(phase).fetch(info, fmt, selected_lang)
                phase("save")
                Path(path).write_text(text, encoding="utf-8")
                root.after(0, lambda: done(path))
            except Exception as exc:
                error = str(exc)
                root.after(0, lambda: failed(error))

        threading.Thread(target=worker, daemon=True).start()
        animate()

    def done(path: str) -> None:
        finish_stats()
        state["busy"] = False
        progress_var.set(100)
        eta_var.set("")
        status_var.set(f"Saved: {path}")
        download_btn.state(["!disabled"])

    def failed(error: str) -> None:
        state["busy"] = False
        eta_var.set("")
        download_btn.state(["!disabled"])
        status_var.set(error)
        messagebox.showerror("YouTubeSubs", error)

    download_btn = ttk.Button(buttons, text="Download", command=download)
    download_btn.grid(row=0, column=0, padx=(0, 8))
    download_btn.state(["disabled"])
    ttk.Button(buttons, text="Cancel", command=root.destroy).grid(row=0, column=1)
    url_var.trace_add("write", schedule_analysis)
    url_entry.focus_set()
    root.mainloop()
    return EXIT_OK


def main() -> int:
    config = load_config()
    setup_logging(str(config.get("logging", "off")))
    return gui() if len(sys.argv) == 1 else cli(sys.argv[1:])


if __name__ == "__main__":
    raise SystemExit(main())
