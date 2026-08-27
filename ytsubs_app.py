#!/usr/bin/env python3
"""YouTubeSubs standalone application entry point."""
from __future__ import annotations

import argparse
import ctypes
import io
import os
import socket
import sys
import threading
import time
import webbrowser
from pathlib import Path

import ytsubs as core

VERSION = core.VERSION
FORMATS = ("srt", "sub", "txt", "vtt")


def _bind_windows_stdio() -> None:
    """Restore inherited/parent stdio for a windowed PyInstaller EXE used as CLI."""
    if os.name != "nt" or not getattr(sys, "frozen", False):
        return
    try:
        import msvcrt
        from ctypes import wintypes
        kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
        kernel32.AttachConsole.argtypes = [wintypes.DWORD]
        kernel32.GetStdHandle.argtypes = [wintypes.DWORD]
        kernel32.GetStdHandle.restype = wintypes.HANDLE
        kernel32.GetCurrentProcess.restype = wintypes.HANDLE
        kernel32.DuplicateHandle.argtypes = [wintypes.HANDLE, wintypes.HANDLE, wintypes.HANDLE, ctypes.POINTER(wintypes.HANDLE), wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
        kernel32.DuplicateHandle.restype = wintypes.BOOL
        kernel32.AttachConsole(0xFFFFFFFF)
        try: kernel32.SetConsoleOutputCP(65001)
        except Exception: pass
        current = kernel32.GetCurrentProcess()
        def bind(name: str, std_id: int, write: bool) -> None:
            handle = kernel32.GetStdHandle(std_id & 0xFFFFFFFF)
            if not handle or int(handle) == -1: return
            duplicate = wintypes.HANDLE()
            if not kernel32.DuplicateHandle(current, handle, current, ctypes.byref(duplicate), 0, True, 2): return
            flags = os.O_WRONLY if write else os.O_RDONLY
            fd = msvcrt.open_osfhandle(duplicate.value, flags)
            raw = os.fdopen(fd, "wb" if write else "rb", buffering=0)
            setattr(sys, name, io.TextIOWrapper(raw, encoding="utf-8", errors="replace", line_buffering=write))
        if sys.stdin is None: bind("stdin", -10, False)
        if sys.stdout is None: bind("stdout", -11, True)
        if sys.stderr is None: bind("stderr", -12, True)
    except Exception: pass


def _snippets(transcript): return list(transcript)


def format_transcript(transcript, fmt: str) -> str:
    items = _snippets(transcript)
    if fmt == "txt": return "\n".join(item.text for item in items)
    if fmt == "srt": return core.SRTFormatter().format_transcript(transcript)
    def stamp(seconds: float, vtt: bool = False) -> str:
        ms = max(0, round(seconds * 1000)); h, rem = divmod(ms, 3_600_000); m, rem = divmod(rem, 60_000); s, milli = divmod(rem, 1000)
        return f"{h:02}:{m:02}:{s:02}{'.' if vtt else ':'}{milli:03}"
    if fmt == "vtt":
        blocks = ["WEBVTT", ""]
        for item in items: blocks.extend([f"{stamp(item.start, True)} --> {stamp(item.start + item.duration, True)}", item.text, ""])
        return "\n".join(blocks)
    blocks = []
    for item in items: blocks.extend([f"{stamp(item.start)},{stamp(item.start + item.duration)}", item.text.replace("\n", "[br]"), ""])
    return "\n".join(blocks)


def fetch_text(info: core.VideoInfo, fmt: str, lang: str | None, phase=None, cancel=None) -> str:
    engine = core.Engine(phase, cancel); engine._phase("download"); transcript = engine.select_track(info, lang).fetch(); engine._check_cancel(); engine._phase("format")
    return format_transcript(transcript, fmt)


def cli(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(prog="ytsubs", description="Download original YouTube subtitles.")
    parser.add_argument("video", nargs="?", help="YouTube URL or 11-character video ID"); parser.add_argument("--format", choices=FORMATS, default="txt"); parser.add_argument("--lang", help="Subtitle language code, e.g. en or cs"); parser.add_argument("-o", "--output"); parser.add_argument("--version", action="version", version=f"%(prog)s {VERSION}")
    args = parser.parse_args(argv)
    if not args.video: parser.error("video is required")
    try:
        info = core.Engine().analyze(args.video); core.write_output(fetch_text(info, args.format, args.lang), args.output); return core.EXIT_OK
    except ValueError as exc: print(f"ytsubs: {exc}", file=sys.stderr); return core.EXIT_USAGE
    except LookupError as exc: print(f"ytsubs: {exc}", file=sys.stderr); return core.EXIT_NO_SUBS
    except OSError as exc: print(f"ytsubs: unable to write output: {exc}", file=sys.stderr); return core.EXIT_OUTPUT
    except Exception as exc: print(f"ytsubs: unable to retrieve subtitles: {exc}", file=sys.stderr); return core.EXIT_API


def gui() -> int:
    import tkinter as tk
    from tkinter import filedialog, messagebox, ttk
    if os.name == "nt":
        try: ctypes.windll.shell32.SetCurrentProcessExplicitAppUserModelID(core.APP_ID)
        except Exception: pass
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    try: sock.bind(("127.0.0.1", core.GUI_PORT))
    except OSError:
        try:
            with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as sender: sender.sendto(b"ACTIVATE", ("127.0.0.1", core.GUI_PORT))
        except OSError: pass
        return 0
    config = core.load_config(); phase_stats = config["phase_seconds"]; saved_fmt = str(config.get("last_format", "srt")).lower()
    if saved_fmt not in FORMATS: saved_fmt = "srt"
    root = tk.Tk(); root.title(f"YouTubeSubs {VERSION}"); root.resizable(False, False); core.apply_window_icon(root)
    url_var = tk.StringVar(); lang_var = tk.StringVar(value="Auto"); fmt_var = tk.StringVar(value="." + saved_fmt)
    state = {"info": None, "timer": None, "lang_map": {}, "dialog": None, "closing": False}

    def front():
        if state["closing"]: return
        try:
            root.deiconify(); root.lift(); root.attributes("-topmost", True); root.focus_force(); root.after(250, lambda: root.attributes("-topmost", False) if root.winfo_exists() else None)
            dialog = state.get("dialog")
            if dialog and dialog.window.winfo_exists(): dialog.window.lift(); dialog.window.focus_force()
        except tk.TclError: pass

    def listener():
        while not state["closing"]:
            try:
                sock.settimeout(0.5); data, _ = sock.recvfrom(64)
                if data == b"ACTIVATE": root.after(0, front)
            except socket.timeout: continue
            except OSError: return
    threading.Thread(target=listener, daemon=True).start()

    frame = ttk.Frame(root, padding=14); frame.grid(); frame.columnconfigure(0, weight=1)
    ttk.Label(frame, text="YouTube URL / Video ID").grid(row=0, column=0, columnspan=2, sticky="w")
    entry = ttk.Entry(frame, textvariable=url_var, width=66); entry.grid(row=1, column=0, columnspan=2, sticky="ew", pady=(2, 10))
    ttk.Label(frame, text="Language").grid(row=2, column=0, sticky="w")
    lang = ttk.Combobox(frame, textvariable=lang_var, state="readonly", values=["Auto"]); lang.grid(row=3, column=0, sticky="ew", pady=(2, 10), padx=(0, 8))
    fmt = ttk.Combobox(frame, textvariable=fmt_var, state="readonly", width=5, values=["." + x for x in FORMATS]); fmt.grid(row=3, column=1, sticky="e", pady=(2, 10))
    status = tk.Label(frame, text="", justify="center", wraplength=500); status.grid(row=4, column=0, columnspan=2, sticky="ew", pady=(2, 8))
    buttons = ttk.Frame(frame); buttons.grid(row=5, column=0, columnspan=2, sticky="e", pady=(8, 0))

    def close():
        state["closing"] = True; d = state.get("dialog")
        if d: d.cancel_event.set()
        try: sock.close()
        except OSError: pass
        try: root.destroy()
        except tk.TclError: pass

    def clear(show_invalid=False):
        state["info"] = None; state["lang_map"] = {}; lang["values"] = ["Auto"]; lang_var.set("Auto"); download.state(["disabled"])
        status.configure(text="Invalid Video ID. Please try again..." if show_invalid else "", fg="#c00000", cursor="", font=("TkDefaultFont", 9, "normal")); status.unbind("<Button-1>")

    def center_child(win):
        win.update_idletasks(); root.update_idletasks(); x = root.winfo_rootx() + (root.winfo_width() - win.winfo_width()) // 2; y = root.winfo_rooty() + (root.winfo_height() - win.winfo_height()) // 2; win.geometry(f"+{x}+{y}")

    class Progress:
        def __init__(self, title, phases, on_cancel):
            self.cancel_event = threading.Event(); self.on_cancel = on_cancel; self.phases = phases; self.phase_name = None; self.phase_start = 0.0; self.completed = 0.0
            self.window = tk.Toplevel(root); state["dialog"] = self; self.window.title(title); self.window.resizable(False, False); self.window.transient(root); core.apply_window_icon(self.window)
            body = ttk.Frame(self.window, padding=14); body.grid(); self.text = tk.StringVar(value=title + "..."); self.eta = tk.StringVar(value=""); self.progress = tk.DoubleVar(value=1)
            ttk.Label(body, textvariable=self.text, width=52).grid(row=0, column=0, sticky="w"); ttk.Progressbar(body, variable=self.progress, maximum=100, length=400).grid(row=1, column=0, pady=(8, 6)); ttk.Label(body, textvariable=self.eta).grid(row=2, column=0, sticky="w"); ttk.Button(body, text="Cancel", command=self.cancel).grid(row=3, column=0, sticky="e", pady=(8, 0))
            self.window.protocol("WM_DELETE_WINDOW", close); center_child(self.window); self.window.grab_set(); self.window.focus_force(); self.animate()
        def phase(self, name):
            now = time.monotonic()
            if self.phase_name:
                duration = max(0.01, now - self.phase_start); previous = float(phase_stats.get(self.phase_name, core.DEFAULT_STATS.get(self.phase_name, 0.5))); phase_stats[self.phase_name] = round(previous * 0.75 + duration * 0.25, 3); self.completed += previous
            self.phase_name = name; self.phase_start = now; labels = {"metadata":"Reading video information...","transcripts":"Finding available subtitles...","download":"Downloading subtitles...","format":"Creating output...","save":"Saving file..."}; root.after(0, lambda: self.text.set(labels.get(name, name)))
        def animate(self):
            try:
                if self.phase_name:
                    total = sum(float(phase_stats.get(name, core.DEFAULT_STATS.get(name, 0.5))) for name in self.phases); average = float(phase_stats.get(self.phase_name, core.DEFAULT_STATS.get(self.phase_name, 0.5))); elapsed = time.monotonic() - self.phase_start; partial = average * min(0.92, elapsed / max(average, 0.05)); self.progress.set(min(96.0, (self.completed + partial) / max(total, 0.1) * 100))
                    if int(config.get("samples", 0)) >= 3: self.eta.set(f"Estimated time remaining: ~{max(1, round(max(0.0, total - self.completed - min(elapsed, average))))} s")
                self.window.after(80, self.animate)
            except tk.TclError: return
        def cancel(self): self.cancel_event.set(); self.finish(False); self.on_cancel()
        def finish(self, learn=True):
            if learn and self.phase_name:
                duration=max(0.01,time.monotonic()-self.phase_start); average=float(phase_stats.get(self.phase_name,core.DEFAULT_STATS.get(self.phase_name,0.5))); phase_stats[self.phase_name]=round(average*0.75+duration*0.25,3); config["samples"]=int(config.get("samples",0))+1; config["phase_seconds"]=phase_stats; core.save_config(config)
            try: self.window.grab_release(); self.window.destroy()
            except tk.TclError: pass
            state["dialog"] = None

    def analyzed(info,d):
        if d.cancel_event.is_set() or state["closing"]: return
        d.finish(); state["info"]=info; choices=info.language_choices(); state["lang_map"]=dict(choices); lang["values"]=["Auto"]+[label for label,_ in choices]; lang_var.set("Auto"); status.configure(text=info.title,fg="#0563C1",cursor="hand2",font=("TkDefaultFont",9,"underline")); status.bind("<Button-1>",lambda _e:webbrowser.open(core.canonical_url(info.video_id))); download.state(["!disabled"]); front()
    def analyze():
        value=url_var.get().strip()
        if not value or state.get("dialog"): return
        try: core.extract_video_id(value)
        except ValueError: clear(True); return
        clear(False); d=Progress("Analyzing video",["metadata","transcripts"],lambda:clear(False))
        def worker():
            try: info=core.Engine(d.phase,d.cancel_event).analyze(value); root.after(0,lambda:analyzed(info,d))
            except core.CancelledError: pass
            except Exception: root.after(0,lambda:(d.finish(False),clear(True),front()))
        threading.Thread(target=worker,daemon=True).start()
    def schedule(*_):
        if state["timer"]: root.after_cancel(state["timer"])
        clear(False); state["timer"]=root.after(500,analyze)
    def format_changed(*_):
        ext=fmt_var.get().lstrip(".")
        if ext in FORMATS: config["last_format"]=ext; core.save_config(config)

    def save_dialog_centered(**kwargs):
        """Open the native Save As dialog centered on the active monitor work area, not on the small main window."""
        if os.name != "nt": return filedialog.asksaveasfilename(parent=root, **kwargs)
        from ctypes import wintypes
        user32 = ctypes.windll.user32
        MONITOR_DEFAULTTONEAREST = 2
        class RECT(ctypes.Structure): _fields_ = [("left",ctypes.c_long),("top",ctypes.c_long),("right",ctypes.c_long),("bottom",ctypes.c_long)]
        class MONITORINFO(ctypes.Structure): _fields_ = [("cbSize",wintypes.DWORD),("rcMonitor",RECT),("rcWork",RECT),("dwFlags",wintypes.DWORD)]
        root.update_idletasks(); hwnd = root.winfo_id(); monitor = user32.MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST); mi = MONITORINFO(); mi.cbSize = ctypes.sizeof(MONITORINFO); user32.GetMonitorInfoW(monitor, ctypes.byref(mi)); work = mi.rcWork
        # A hidden owner spanning the monitor work area makes the native Windows dialog center on that monitor instead of relative to the small app window.
        owner = tk.Toplevel(root); owner.withdraw(); owner.overrideredirect(True); owner.geometry(f"{max(1,work.right-work.left)}x{max(1,work.bottom-work.top)}+{work.left}+{work.top}"); owner.deiconify(); owner.attributes("-alpha",0.0); owner.update_idletasks()
        try: return filedialog.asksaveasfilename(parent=owner, **kwargs)
        finally:
            try: owner.destroy()
            except tk.TclError: pass

    def do_download():
        info=state["info"]
        if not info: return
        ext=fmt_var.get().lstrip("."); config["last_format"]=ext; core.save_config(config); selected=lang_var.get(); code=None if selected=="Auto" else state["lang_map"].get(selected); proposed=core.clean_filename(info.title)+"."+ext
        path=save_dialog_centered(initialfile=proposed,defaultextension="."+ext,filetypes=[(ext.upper(),"*."+ext),("All files","*.*")])
        if not path: return
        d=Progress("Downloading subtitles",["download","format","save"],lambda:download.state(["!disabled"])); download.state(["disabled"])
        def worker():
            try:
                text=fetch_text(info,ext,code,d.phase,d.cancel_event); d.phase("save"); Path(path).write_text(text,encoding="utf-8")
                if d.cancel_event.is_set(): Path(path).unlink(missing_ok=True); return
                def done():
                    d.finish(); open_file=messagebox.askyesno("YouTubeSubs","Subtitles saved successfully.\n\nOpen the file?",parent=root)
                    if open_file:
                        try: os.startfile(path)
                        except OSError as exc: messagebox.showerror("YouTubeSubs",f"Unable to open the file:\n{exc}",parent=root)
                    close()
                root.after(0,done)
            except core.CancelledError: pass
            except Exception as exc: root.after(0,lambda:(d.finish(False),download.state(["!disabled"]),messagebox.showerror("YouTubeSubs",str(exc),parent=root)))
        threading.Thread(target=worker,daemon=True).start()

    download=ttk.Button(buttons,text="Download",command=do_download); download.grid(row=0,column=0,padx=(0,8)); download.state(["disabled"]); ttk.Button(buttons,text="Cancel",command=close).grid(row=0,column=1)
    root.protocol("WM_DELETE_WINDOW",close); url_var.trace_add("write",schedule); fmt.bind("<<ComboboxSelected>>",format_changed); root.update_idletasks(); core.center_window(root); entry.focus_set(); root.after(50,front); root.mainloop(); return 0


def main() -> int:
    config=core.load_config(); core.setup_logging(str(config.get("logging","off")))
    if len(sys.argv)==1: return gui()
    _bind_windows_stdio(); return cli(sys.argv[1:])

if __name__ == "__main__": raise SystemExit(main())
