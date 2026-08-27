#!/usr/bin/env python3
"""YouTubeSubs 1.03 - small YouTube subtitle downloader (CLI + GUI)."""

from __future__ import annotations

import argparse
import ctypes
import json
import logging
import os
import re
import socket
import sys
import threading
import time
import webbrowser
from dataclasses import dataclass
from pathlib import Path
from urllib.parse import parse_qs, urlparse

from youtube_transcript_api import YouTubeTranscriptApi
from youtube_transcript_api.formatters import SRTFormatter, TextFormatter
from yt_dlp import YoutubeDL

VERSION = "1.03"
VIDEO_ID_RE = re.compile(r"^[A-Za-z0-9_-]{11}$")
VIDEO_ID_ANYWHERE_RE = re.compile(r"(?<![A-Za-z0-9_-])([A-Za-z0-9_-]{11})(?![A-Za-z0-9_-])")
DEFAULT_STATS = {"metadata": 0.8, "transcripts": 1.0, "download": 0.8, "format": 0.1, "save": 0.1}
EXIT_OK, EXIT_USAGE, EXIT_NO_SUBS, EXIT_API, EXIT_OUTPUT = 0, 2, 3, 4, 5
GUI_PORT = 45871
APP_ID = "Suenee.YouTubeSubs"


class CancelledError(Exception):
    pass


def project_dir() -> Path:
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

    def fetch(self, info: VideoInfo, fmt: str, lang: str | None = None) -> str:
        self._phase("download")
        transcript = self.select_track(info, lang).fetch()
        self._check_cancel()
        self._phase("format")
        result = SRTFormatter().format_transcript(transcript) if fmt == "srt" else TextFormatter().format_transcript(transcript)
        self._check_cancel()
        return result


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
        info = Engine().analyze(args.video)
        text = Engine().fetch(info, args.format, args.lang)
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


def gui() -> int:
    import tkinter as tk
    from tkinter import filedialog, messagebox, ttk

    if os.name == "nt":
        try:
            ctypes.windll.shell32.SetCurrentProcessExplicitAppUserModelID(APP_ID)
        except Exception:
            pass

    activation_socket = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    try:
        activation_socket.bind(("127.0.0.1", GUI_PORT))
    except OSError:
        try:
            with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as sender:
                sender.sendto(b"ACTIVATE", ("127.0.0.1", GUI_PORT))
        except OSError:
            pass
        return EXIT_OK

    config = load_config()
    phase_stats = config["phase_seconds"]
    root = tk.Tk()
    root.title(f"YouTubeSubs {VERSION}")
    root.resizable(False, False)
    apply_window_icon(root)

    url_var = tk.StringVar()
    lang_var = tk.StringVar(value="Auto / Original")
    fmt_var = tk.StringVar(value="txt")
    state = {"info": None, "timer": None, "lang_map": {}, "dialog": None, "closing": False}

    def bring_to_front() -> None:
        if state["closing"]:
            return
        try:
            root.deiconify()
            root.lift()
            root.attributes("-topmost", True)
            root.focus_force()
            root.after(300, lambda: root.attributes("-topmost", False) if root.winfo_exists() else None)
            dialog = state.get("dialog")
            if dialog and dialog.winfo_exists():
                dialog.lift()
                dialog.focus_force()
        except tk.TclError:
            pass

    def activation_listener() -> None:
        while not state["closing"]:
            try:
                activation_socket.settimeout(0.5)
                data, _ = activation_socket.recvfrom(64)
                if data == b"ACTIVATE":
                    root.after(0, bring_to_front)
            except socket.timeout:
                continue
            except OSError:
                return

    threading.Thread(target=activation_listener, daemon=True).start()

    frame = ttk.Frame(root, padding=14)
    frame.grid()
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

    title_link = tk.Label(frame, text="", fg="#0563C1", cursor="hand2", font=("TkDefaultFont", 9, "underline"), wraplength=500, justify="center")
    title_link.grid(row=4, column=0, columnspan=2, sticky="ew", pady=(2, 8))
    title_link.grid_remove()

    buttons = ttk.Frame(frame)
    buttons.grid(row=5, column=0, columnspan=2, sticky="e", pady=(8, 0))

    def close_app() -> None:
        state["closing"] = True
        dialog = state.get("dialog")
        if dialog and hasattr(dialog, "cancel_event"):
            dialog.cancel_event.set()
        try:
            activation_socket.close()
        except OSError:
            pass
        try:
            root.destroy()
        except tk.TclError:
            pass

    def clear_video_state() -> None:
        state["info"] = None
        state["lang_map"] = {}
        lang_box["values"] = ["Auto / Original"]
        lang_var.set("Auto / Original")
        title_link.configure(text="")
        title_link.grid_remove()
        download_btn.state(["disabled"])

    def open_video_link(_event=None) -> None:
        info = state.get("info")
        if info:
            webbrowser.open(canonical_url(info.video_id))

    title_link.bind("<Button-1>", open_video_link)

    class ProgressDialog:
        def __init__(self, title: str, phases: list[str], labels: dict[str, str], on_cancel):
            self.cancel_event = threading.Event()
            self.phases = phases
            self.labels = labels
            self.phase = None
            self.phase_start = 0.0
            self.completed = 0.0
            self.on_cancel_callback = on_cancel
            self.window = tk.Toplevel(root)
            state["dialog"] = self.window
            self.window.title(title)
            self.window.resizable(False, False)
            self.window.transient(root)
            apply_window_icon(self.window)
            self.window.protocol("WM_DELETE_WINDOW", close_app)
            body = ttk.Frame(self.window, padding=14)
            body.grid()
            self.status = tk.StringVar(value=title + "...")
            self.eta = tk.StringVar(value="")
            self.progress = tk.DoubleVar(value=1)
            ttk.Label(body, textvariable=self.status, width=54).grid(row=0, column=0, sticky="w")
            ttk.Progressbar(body, variable=self.progress, maximum=100, length=420).grid(row=1, column=0, sticky="ew", pady=(8, 6))
            ttk.Label(body, textvariable=self.eta).grid(row=2, column=0, sticky="w")
            ttk.Button(body, text="Cancel", command=self.cancel).grid(row=3, column=0, sticky="e", pady=(10, 0))
            self.window.update_idletasks()
            center_window(self.window)
            self.window.grab_set()
            self.window.attributes("-topmost", True)
            self.window.focus_force()
            self.window.after(250, lambda: self.window.attributes("-topmost", False) if self.window.winfo_exists() else None)
            self.animate()

        def set_phase(self, name: str) -> None:
            now = time.monotonic()
            old = self.phase
            if old:
                duration = max(0.01, now - self.phase_start)
                old_avg = float(phase_stats.get(old, DEFAULT_STATS.get(old, 0.5)))
                phase_stats[old] = round(old_avg * 0.75 + duration * 0.25, 3)
                self.completed += old_avg
            self.phase = name
            self.phase_start = now
            root.after(0, lambda: self.status.set(self.labels.get(name, name)))

        def animate(self) -> None:
            if not self.window.winfo_exists():
                return
            if self.phase:
                total = sum(float(phase_stats.get(name, DEFAULT_STATS.get(name, 0.5))) for name in self.phases)
                avg = float(phase_stats.get(self.phase, DEFAULT_STATS.get(self.phase, 0.5)))
                elapsed = time.monotonic() - self.phase_start
                partial = avg * min(0.92, elapsed / max(avg, 0.05))
                self.progress.set(min(96.0, (self.completed + partial) / max(total, 0.1) * 100))
                if config.get("samples", 0) >= 3:
                    eta = max(0.0, total - self.completed - min(elapsed, avg))
                    self.eta.set(f"Estimated time remaining: ~{max(1, round(eta))} s")
            self.window.after(80, self.animate)

        def cancel(self) -> None:
            self.cancel_event.set()
            self.close()
            self.on_cancel_callback()

        def finish_stats(self) -> None:
            if self.phase:
                duration = max(0.01, time.monotonic() - self.phase_start)
                avg = float(phase_stats.get(self.phase, DEFAULT_STATS.get(self.phase, 0.5)))
                phase_stats[self.phase] = round(avg * 0.75 + duration * 0.25, 3)
            config["samples"] = int(config.get("samples", 0)) + 1
            config["phase_seconds"] = phase_stats
            save_config(config)

        def close(self) -> None:
            if self.window.winfo_exists():
                try:
                    self.window.grab_release()
                except tk.TclError:
                    pass
                self.window.destroy()
            state["dialog"] = None

    def analysis_cancelled() -> None:
        clear_video_state()

    def analysis_done(info: VideoInfo, dialog: ProgressDialog) -> None:
        if dialog.cancel_event.is_set() or state["closing"]:
            return
        dialog.finish_stats()
        dialog.close()
        state["info"] = info
        choices = info.language_choices()
        state["lang_map"] = dict(choices)
        lang_box["values"] = ["Auto / Original"] + [label for label, _ in choices]
        lang_var.set("Auto / Original")
        title_link.configure(text=info.title)
        title_link.grid()
        download_btn.state(["!disabled"])
        bring_to_front()

    def analysis_failed(dialog: ProgressDialog) -> None:
        if dialog.cancel_event.is_set() or state["closing"]:
            return
        dialog.close()
        clear_video_state()
        bring_to_front()

    def analyze_now() -> None:
        value = url_var.get().strip()
        if not value or state.get("dialog"):
            return
        try:
            extract_video_id(value)
        except ValueError:
            clear_video_state()
            return
        clear_video_state()
        labels = {"metadata": "Reading video information...", "transcripts": "Finding available subtitles..."}
        dialog = ProgressDialog("Analyzing video", ["metadata", "transcripts"], labels, analysis_cancelled)

        def worker() -> None:
            try:
                info = Engine(dialog.set_phase, dialog.cancel_event).analyze(value)
                root.after(0, lambda: analysis_done(info, dialog))
            except CancelledError:
                pass
            except Exception:
                logging.exception("Video analysis failed")
                root.after(0, lambda: analysis_failed(dialog))

        threading.Thread(target=worker, daemon=True).start()

    def schedule_analysis(*_) -> None:
        if state["timer"]:
            root.after_cancel(state["timer"])
        clear_video_state()
        state["timer"] = root.after(500, analyze_now)

    def download_cancelled() -> None:
        download_btn.state(["!disabled"])

    def download() -> None:
        info: VideoInfo | None = state["info"]
        if not info:
            return
        fmt = fmt_var.get()
        selected_label = lang_var.get()
        selected_lang = None if selected_label == "Auto / Original" else state["lang_map"].get(selected_label)
        proposed = clean_filename(info.title) + "." + fmt
        path = filedialog.asksaveasfilename(parent=root, initialfile=proposed, defaultextension="." + fmt, filetypes=[(fmt.upper(), "*." + fmt), ("All files", "*.*")])
        if not path:
            return
        labels = {"download": "Downloading subtitles...", "format": "Creating output...", "save": "Saving file..."}
        dialog = ProgressDialog("Downloading subtitles", ["download", "format", "save"], labels, download_cancelled)
        download_btn.state(["disabled"])

        def done() -> None:
            if dialog.cancel_event.is_set() or state["closing"]:
                return
            dialog.finish_stats()
            dialog.close()
            open_file = messagebox.askyesno("YouTubeSubs", "Subtitles saved successfully.\n\nOpen the file?", parent=root)
            if open_file:
                try:
                    os.startfile(path)
                except OSError as exc:
                    messagebox.showerror("YouTubeSubs", f"Unable to open the file:\n{exc}", parent=root)
            close_app()

        def failed(error: str) -> None:
            if dialog.cancel_event.is_set() or state["closing"]:
                return
            dialog.close()
            download_btn.state(["!disabled"])
            messagebox.showerror("YouTubeSubs", error, parent=root)

        def worker() -> None:
            try:
                text = Engine(dialog.set_phase, dialog.cancel_event).fetch(info, fmt, selected_lang)
                if dialog.cancel_event.is_set():
                    raise CancelledError()
                dialog.set_phase("save")
                Path(path).write_text(text, encoding="utf-8")
                if dialog.cancel_event.is_set():
                    try:
                        Path(path).unlink(missing_ok=True)
                    except OSError:
                        pass
                    raise CancelledError()
                root.after(0, done)
            except CancelledError:
                pass
            except Exception as exc:
                logging.exception("Subtitle download failed")
                error = str(exc)
                root.after(0, lambda: failed(error))

        threading.Thread(target=worker, daemon=True).start()

    download_btn = ttk.Button(buttons, text="Download", command=download)
    download_btn.grid(row=0, column=0, padx=(0, 8))
    download_btn.state(["disabled"])
    ttk.Button(buttons, text="Cancel", command=close_app).grid(row=0, column=1)

    root.protocol("WM_DELETE_WINDOW", close_app)
    url_var.trace_add("write", schedule_analysis)
    root.update_idletasks()
    center_window(root)
    url_entry.focus_set()
    root.after(50, bring_to_front)
    root.after(400, lambda: root.lift() if root.winfo_exists() else None)
    root.mainloop()
    return EXIT_OK


def main() -> int:
    config = load_config()
    setup_logging(str(config.get("logging", "off")))
    return gui() if len(sys.argv) == 1 else cli(sys.argv[1:])


if __name__ == "__main__":
    raise SystemExit(main())
