@echo off
setlocal EnableExtensions EnableDelayedExpansion

if defined YTSUBS_REPO_DIR (
  cd /d "%YTSUBS_REPO_DIR%"
) else (
  set "YTSUBS_REPO_DIR=%~dp0"
  cd /d "%~dp0"
)

where git >nul 2>nul || (echo ERROR: Git was not found.& exit /b 10)
where py >nul 2>nul || where python >nul 2>nul || (echo ERROR: Python was not found.& exit /b 11)

for /f "delims=" %%B in ('git rev-parse --abbrev-ref HEAD 2^>nul') do set "BRANCH=%%B"
if not defined BRANCH (echo ERROR: This directory is not a Git working tree.& exit /b 12)

echo === SELF UPDATE CHECK ===
git fetch origin "%BRANCH%" || (echo ERROR: git fetch failed.& exit /b 13)
set "REMOTE_UPGRADE=%TEMP%\ytsubs_upgrade_%RANDOM%.cmd"
git show "origin/%BRANCH%:upgrade.cmd" > "%REMOTE_UPGRADE%" 2>nul
if exist "%REMOTE_UPGRADE%" (
  fc /b "%~f0" "%REMOTE_UPGRADE%" >nul 2>nul
  if errorlevel 1 if not defined YTSUBS_REMOTE_UPGRADE_RUNNING (
    echo A newer upgrade.cmd was found. Running it first...
    set "YTSUBS_REMOTE_UPGRADE_RUNNING=1"
    call "%REMOTE_UPGRADE%"
    set "RC=!ERRORLEVEL!"
    del "%REMOTE_UPGRADE%" >nul 2>nul
    exit /b !RC!
  )
)
del "%REMOTE_UPGRADE%" >nul 2>nul

echo === WORKTREE CHECK ===
for /f "delims=" %%S in ('git status --porcelain --untracked-files^=no') do (
  echo ERROR: Tracked local changes detected. Commit, stash, or revert them first.
  exit /b 14
)

echo === UPDATE ===
git pull --ff-only origin "%BRANCH%" || (echo ERROR: git pull failed.& exit /b 15)

echo === PYTHON ENVIRONMENT ===
if not exist ".venv\Scripts\python.exe" (
  py -3 -m venv .venv 2>nul || python -m venv .venv || (echo ERROR: Unable to create .venv.& exit /b 16)
)

".venv\Scripts\python.exe" -m pip install --upgrade pip || exit /b 17
".venv\Scripts\python.exe" -m pip install --upgrade -r requirements.txt || exit /b 18
".venv\Scripts\python.exe" -m pip install --upgrade -e . || exit /b 19

echo === VALIDATION ===
".venv\Scripts\python.exe" -m py_compile ytsubs.py || (echo ERROR: Python syntax validation failed.& exit /b 20)
call "%CD%\ytsubs.cmd" --version || (echo ERROR: Root ytsubs launcher validation failed.& exit /b 21)

echo.
echo YouTubeSubs update completed successfully on branch %BRANCH%.
exit /b 0
