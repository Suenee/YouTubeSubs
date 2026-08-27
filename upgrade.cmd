@echo off
setlocal EnableExtensions EnableDelayedExpansion

if defined YTSUBS_REPO_DIR (cd /d "%YTSUBS_REPO_DIR%") else (set "YTSUBS_REPO_DIR=%~dp0"& cd /d "%~dp0")
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
for /f "delims=" %%S in ('git status --porcelain --untracked-files^=no') do (echo ERROR: Tracked local changes detected. Commit, stash, or revert them first.& exit /b 14)

echo === UPDATE ===
git fetch origin "%BRANCH%" || (echo ERROR: git fetch failed.& exit /b 15)
set "LOCAL_SHA="
set "REMOTE_SHA="
set "BASE_SHA="
for /f "delims=" %%S in ('git rev-parse HEAD') do set "LOCAL_SHA=%%S"
for /f "delims=" %%S in ('git rev-parse "origin/%BRANCH%"') do set "REMOTE_SHA=%%S"
for /f "delims=" %%S in ('git merge-base HEAD "origin/%BRANCH%"') do set "BASE_SHA=%%S"
if /i "!LOCAL_SHA!"=="!REMOTE_SHA!" (
  echo Already up to date.
) else if /i "!LOCAL_SHA!"=="!BASE_SHA!" (
  git merge --ff-only "origin/%BRANCH%" || (echo ERROR: Fast-forward update failed.& exit /b 16)
) else (
  echo WARNING: Local branch history diverged from origin/%BRANCH%.
  echo          No tracked local changes were found, so the branch will be synchronized to origin/%BRANCH%.
  git reset --hard "origin/%BRANCH%" || (echo ERROR: Unable to synchronize local branch with origin/%BRANCH%.& exit /b 17)
)

echo === PYTHON BUILD ENVIRONMENT ===
if not exist ".venv\Scripts\python.exe" (py -3 -m venv .venv 2>nul || python -m venv .venv || (echo ERROR: Unable to create .venv.& exit /b 18))
".venv\Scripts\python.exe" -m pip install --upgrade pip || exit /b 19
".venv\Scripts\python.exe" -m pip install --upgrade -r requirements-build.txt || exit /b 20

echo === SOURCE VALIDATION ===
".venv\Scripts\python.exe" -m py_compile ytsubs.py ytsubs_app.py || (echo ERROR: Python syntax validation failed.& exit /b 21)
if not exist "assets\ytsubs.png" (echo ERROR: Missing application icon source: assets\ytsubs.png& exit /b 22)
if not exist "ytsubs.spec" (echo ERROR: Missing PyInstaller spec: ytsubs.spec& exit /b 23)

echo === ICON VALIDATION ===
".venv\Scripts\python.exe" -c "from PIL import Image; from pathlib import Path; p=Path(r'assets\ytsubs.png'); im=Image.open(p); im.load(); assert im.format=='PNG'; im=im.convert('RGBA'); im.save(r'assets\ytsubs.ico', format='ICO', sizes=[(16,16),(24,24),(32,32),(48,48),(64,64),(128,128),(256,256)]); chk=Image.open(r'assets\ytsubs.ico'); chk.load(); assert chk.format=='ICO'" || (echo ERROR: Unable to create or validate Windows ICO from assets\ytsubs.png.& exit /b 24)

echo === STANDALONE BUILD ===
if exist "ytsubs.exe" del /q "ytsubs.exe" || exit /b 25
".venv\Scripts\python.exe" -m PyInstaller --noconfirm --clean --distpath "%CD%" --workpath "%CD%\build\pyinstaller" ytsubs.spec || (echo ERROR: Standalone build failed.& exit /b 26)
if not exist "ytsubs.exe" (echo ERROR: ytsubs.exe was not created.& exit /b 27)

echo === EXECUTABLE VALIDATION ===
set "EXPECTED_VERSION="
for /f "tokens=3" %%V in ('findstr /b /c:"version = " pyproject.toml') do set "EXPECTED_VERSION=%%~V"
if not defined EXPECTED_VERSION (echo ERROR: Unable to read project version from pyproject.toml.& exit /b 28)
set "ACTUAL_VERSION="
for /f "delims=" %%V in ('"%CD%\ytsubs.exe" --version 2^>nul') do set "ACTUAL_VERSION=%%V"
if not defined ACTUAL_VERSION (echo ERROR: Standalone ytsubs.exe CLI validation failed.& exit /b 29)
set "EXPECTED_OUTPUT=ytsubs !EXPECTED_VERSION!"
if /i not "!ACTUAL_VERSION!"=="!EXPECTED_OUTPUT!" (echo ERROR: Version mismatch.& echo        Expected: !EXPECTED_OUTPUT!& echo        Actual:   !ACTUAL_VERSION!& exit /b 30)
echo Version validation: !ACTUAL_VERSION!
echo Executable validation: ytsubs.exe
echo Icon validation: PNG source -^> multi-size ICO -^> embedded EXE icon
echo Format validation: .srt .sub .txt .vtt
echo.
echo YouTubeSubs update completed successfully on branch %BRANCH%.
exit /b 0
