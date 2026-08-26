# Changelog

All notable changes to this project are documented here.

## 1.00 - 27.08.2026

- Initial development version.
- Added CLI mode for YouTube URL or video ID input.
- Added TXT and SRT output.
- Added stdout output suitable for pipes and redirection.
- Added optional `--lang` and `--output` parameters.
- Added GUI mode when started without parameters.
- Added automatic discovery of available subtitle tracks.
- Added deterministic original-language heuristic without automatic translation.
- Added automatic file-name suggestion from the YouTube video title.
- Added adaptive progress estimation based on locally learned phase timings.
- Added configurable file logging modes: `off`, `single`, and `all`.
