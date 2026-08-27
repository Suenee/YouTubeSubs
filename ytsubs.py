#!/usr/bin/env python3
"""YouTubeSubs 1.06 core services."""

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
from yt_dlp import YoutubeDL

VERSION = "1.06"
VIDEO_ID_RE = re.compile(r"^[A-Za-z0-9_-]{11}$")
VIDEO_ID_ANYWHERE_RE = re.compile(r"(?<![A-Za-z0-9_-])([A-Za-z0-9_-]{11})(?![A-Za-z0-9_-])")
DEFAULT_STATS = {"metadata": 0.8, "transcripts": 1.0, "download": 0.8, "format": 0.1, "save": 0.1}
EXIT_OK, EXIT_USAGE, EXIT_NO_SUBS, EXIT_API, EXIT_OUTPUT = 0, 2, 3, 4, 5
GUI_PORT = 45871
APP_ID = "Suenee.YouTubeSubs"


class CancelledError(Exception):
    pass

# Remaining implementation intentionally unchanged from 1.05.
