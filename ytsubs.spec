# -*- mode: python ; coding: utf-8 -*-
from PyInstaller.utils.hooks import collect_all

yt_dlp_datas, yt_dlp_binaries, yt_dlp_hidden = collect_all("yt_dlp")
yta_datas, yta_binaries, yta_hidden = collect_all("youtube_transcript_api")

a = Analysis(
    ["ytsubs_app.py"],
    pathex=[],
    binaries=yt_dlp_binaries + yta_binaries,
    datas=[("assets/ytsubs.ico", "assets")] + yt_dlp_datas + yta_datas,
    hiddenimports=yt_dlp_hidden + yta_hidden,
    hookspath=[],
    hooksconfig={},
    runtime_hooks=[],
    excludes=[],
    noarchive=False,
    optimize=0,
)
pyz = PYZ(a.pure)

exe = EXE(
    pyz,
    a.scripts,
    a.binaries,
    a.datas,
    [],
    name="ytsubs",
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=True,
    console=False,
    icon=["assets/ytsubs.ico"],
)
