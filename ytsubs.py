#!/usr/bin/env python3
"""YouTubeSubs 1.00 - small YouTube subtitle downloader (CLI + GUI)."""

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

VERSION = "1.00"
VIDEO_ID_RE = re.compile(r"^[A-Za-z0-9_-]{11}$")
DEFAULT_STATS = {"metadata": 0.8, "transcripts": 1.0, "download": 0.8, "format": 0.1, "save": 0.1}

EXIT_OK = 0
EXIT_USAGE = 2
EXIT_NO_SUBS = 3
EXIT_API = 4
EXIT_OUTPUT = 5


def app_dir() -> Path:
    base = os.environ.get("LOCALAPPDATA") or str(Path.home() / ".config")
    p = Path(base) / "YouTubeSubs"
    p.mkdir(parents=True, exist_ok=True)
    return p


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
    logfile = app_dir() / "ytsubs.log"
    logging.basicConfig(
        filename=logfile,
        filemode="w" if mode == "single" else "a",
        level=logging.DEBUG,
        format="%(asctime)s %(levelname)s %(message)s",
    )


def extract_video_id(value: str) -> str:
    value = value.strip()
    if VIDEO_ID_RE.fullmatch(value):
        return value
    try:
        u = urlparse(value)
    except ValueError as exc:
        raise ValueError("Invalid YouTube URL or video ID.") from exc
    host = u.netloc.lower().split(":")[0]
    if host in {"youtu.be", "www.youtu.be"}:
        candidate = u.path.strip("/").split("/")[0]
    elif host.endswith("youtube.com"):
        if u.path == "/watch":
            candidate = parse_qs(u.query).get("v", [""])[0]
        else:
            parts = [p for p in u.path.split("/") if p]
            candidate = parts[1] if len(parts) >= 2 and parts[0] in {"shorts", "embed", "live"} else ""
    else:
        candidate = ""
    if not VIDEO_ID_RE.fullmatch(candidate):
        raise ValueError("Invalid YouTube URL or video ID.")
    return candidate


def canonical_url(video_id: str) -> str:
    return f"https://www.youtube.com/watch?v={video_id}"


def clean_filename(name: str) -> str:
    name = re.sub(r'[<>:"/\\|?*\x00-\x1f]', "_", name).strip().rstrip(". ")
    return name[:180] or "youtube_subtitles"


@dataclass(frozen=True)
class Track:
    language: str
    code: str
    generated: bool

    @property
    def label(self) -> str:
        return f"{self.language} ({self.code}) — {'auto' if self.generated else 'manual'}"


@dataclass
class VideoInfo:
    video_id: str
    title: str
    tracks: list[Track]
    original_code: str


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

    def analyze(self, value: str) -> VideoInfo:
        video_id = extract_video_id(value)
        meta = {}
        try:
            meta = self.metadata(video_id)
        except Exception as exc:
            logging.warning("Metadata lookup failed: %s", exc)
        self._phase("transcripts")
        transcript_list = self.api.list(video_id)
        items = list(transcript_list)
        if not items:
            raise LookupError("This video has no available subtitles.")
        tracks = [Track(t.language, t.language_code, bool(t.is_generated)) for t in items]

        # Best deterministic original-language heuristic:
        # 1) yt-dlp language metadata if it matches a real track;
        # 2) first generated track (normally generated from spoken audio);
        # 3) first manual track returned by YouTube.
        meta_lang = str(meta.get("language") or "").lower()
        codes = {t.code.lower(): t.code for t in tracks}
        if meta_lang in codes:
            original = codes[meta_lang]
        else:
            generated = next((t.code for t in tracks if t.generated), None)
            original = generated or tracks[0].code
        title = str(meta.get("title") or video_id)
        return VideoInfo(video_id, title, tracks, original)

    def select_track(self, info: VideoInfo, lang: str | None):
        transcript_list = self.api.list(info.video_id)
        code = lang or info.original_code
        matches = [t for t in transcript_list if t.language_code.lower() == code.lower()]
        if not matches:
            raise LookupError(f"No subtitle track is available for language '{code}'.")
        manual = next((t for t in matches if not t.is_generated), None)
        return manual or matches[0]

    def fetch(self, info: VideoInfo, fmt: str, lang: str | None = None) -> str:
        self._phase("download")
        fetched = self.select_track(info, lang).fetch()
        self._phase("format")
        if fmt == "srt":
            return SRTFormatter().format_transcript(fetched)
        return TextFormatter().format_transcript(fetched)


def write_output(text: str, output: str | None) -> None:
    if output:
        Path(output).write_text(text, encoding="utf-8")
    else:
        sys.stdout.write(text)
        if text and not text.endswith("\n"):
            sys.stdout.write("\n")


def cli(argv: list[str]) -> int:
    p = argparse.ArgumentParser(prog="ytsubs", description="Download original YouTube subtitles as TXT or SRT.")
    p.add_argument("video", help="YouTube URL or 11-character video ID")
    p.add_argument("--format", choices=("txt", "srt"), default="txt")
    p.add_argument("--lang", help="Subtitle language code, e.g. en or cs. No automatic translation is used.")
    p.add_argument("-o", "--output", help="Write output to a file instead of stdout")
    p.add_argument("--version", action="version", version=f"%(prog)s {VERSION}")
    args = p.parse_args(argv)
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
    state = {"info": None, "timer": None, "phase": None, "phase_start": 0.0, "completed": 0.0, "busy": False}
    phases = ["metadata", "transcripts", "download", "format", "save"]
    labels = {
        "metadata": "Reading video information...",
        "transcripts": "Finding available subtitles...",
        "download": "Downloading subtitles...",
        "format": "Creating output...",
        "save": "Saving file...",
    }

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

    def phase(name: str):
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

    def animate():
        if state["busy"] and state["phase"]:
            total = sum(float(phase_stats.get(p, DEFAULT_STATS[p])) for p in phases)
            avg = float(phase_stats.get(state["phase"], 0.5))
            elapsed = time.monotonic() - state["phase_start"]
            partial = avg * min(0.92, elapsed / max(avg, 0.05))
            pct = min(96.0, (state["completed"] + partial) / max(total, 0.1) * 100)
            progress_var.set(pct)
            if config.get("samples", 0) >= 3:
                eta = max(0.0, total - state["completed"] - min(elapsed, avg))
                eta_var.set(f"Estimated time remaining: ~{max(1, round(eta))} s")
            root.after(80, animate)

    def finish_stats():
        if state["phase"]:
            duration = max(0.01, time.monotonic() - state["phase_start"])
            name = state["phase"]
            phase_stats[name] = round(float(phase_stats.get(name, DEFAULT_STATS[name])) * 0.75 + duration * 0.25, 3)
        config["samples"] = int(config.get("samples", 0)) + 1
        config["phase_seconds"] = phase_stats
        save_config(config)

    def run_analysis(value: str):
        try:
            info = Engine(phase).analyze(value)
            root.after(0, lambda: analysis_done(info))
        except Exception as exc:
            root.after(0, lambda: analysis_failed(str(exc)))

    def analysis_done(info: VideoInfo):
        state["info"] = info
        state["busy"] = False
        progress_var.set(0)
        eta_var.set("")
        status_var.set(f"Ready: {info.title}")
        values = ["Auto / Original"] + [t.label for t in info.tracks]
        lang_box["values"] = values
        lang_var.set("Auto / Original")
        download_btn.state(["!disabled"])

    def analysis_failed(error: str):
        state["info"] = None
        state["busy"] = False
        progress_var.set(0)
        eta_var.set("")
        status_var.set(error)
        lang_box["values"] = ["Auto / Original"]
        download_btn.state(["disabled"])

    def schedule_analysis(*_):
        if state["timer"]:
            root.after_cancel(state["timer"])
        download_btn.state(["disabled"])
        state["timer"] = root.after(500, analyze_now)

    def analyze_now():
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

    def chosen_lang() -> str | None:
        if lang_var.get() == "Auto / Original":
            return None
        m = re.search(r"\(([^()]+)\)", lang_var.get())
        return m.group(1) if m else None

    def download():
        info: VideoInfo | None = state["info"]
        if not info:
            return
        ext = fmt_var.get()
        proposed = clean_filename(info.title) + "." + ext
        path = filedialog.asksaveasfilename(initialfile=proposed, defaultextension="." + ext, filetypes=[(ext.upper(), "*." + ext), ("All files", "*.*")])
        if not path:
            return
        download_btn.state(["disabled"])
        state.update({"busy": True, "phase": None, "completed": 0.0})
        progress_var.set(1)
        eta_var.set("")

        def worker():
            try:
                engine = Engine(phase)
                text = engine.fetch(info, ext, chosen_lang())
                phase("save")
                Path(path).write_text(text, encoding="utf-8")
                root.after(0, lambda: done(path))
            except Exception as exc:
                root.after(0, lambda: failed(str(exc)))

        threading.Thread(target=worker, daemon=True).start()
        animate()

    def done(path: str):
        finish_stats()
        state["busy"] = False
        progress_var.set(100)
        eta_var.set("")
        status_var.set(f"Saved: {path}")
        download_btn.state(["!disabled"])

    def failed(error: str):
        state["busy"] = False
        eta_var.set("")
        download_btn.state(["!disabled"])
        messagebox.showerror("YouTubeSubs", error)
        status_var.set(error)

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
    if len(sys.argv) == 1:
        return gui()
    return cli(sys.argv[1:])


if __name__ == "__main__":
    raise SystemExit(main())
