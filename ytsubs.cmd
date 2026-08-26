@echo off
setlocal
set "YTSUBS_ROOT=%~dp0"
if not exist "%YTSUBS_ROOT%.venv\Scripts\ytsubs.exe" (
  echo ERROR: YouTubeSubs virtual environment is not installed. Run upgrade.cmd first. 1>&2
  exit /b 11
)
"%YTSUBS_ROOT%.venv\Scripts\ytsubs.exe" %*
exit /b %ERRORLEVEL%
