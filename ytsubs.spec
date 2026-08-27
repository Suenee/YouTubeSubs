# -*- mode: python ; coding: utf-8 -*-
# Keep this spec intentionally minimal. PyInstaller's normal analysis and
# package hooks collect the imports required by ytsubs_app.py. Avoid
# collect_all(), which pulls every yt-dlp/youtube-transcript-api submodule,
# data file, and binary into the one-file archive.

a = Analysis(
    ["ytsubs_app.py"],
    pathex=[],
    binaries=[],
    datas=[("assets/ytsubs.ico", "assets")],
    hiddenimports=[],
    hookspath=[],
    hooksconfig={},
    runtime_hooks=[],
    excludes=[
        "pytest",
        "unittest",
        "pydoc",
        "doctest",
        "tkinter.test",
    ],
    noarchive=False,
    optimize=1,
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
    upx=False,
    console=False,
    icon=["assets/ytsubs.ico"],
)
