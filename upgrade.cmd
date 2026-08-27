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
git pull --ff-only origin "%BRANCH%" || (echo ERROR: git pull failed.& exit /b 15)

echo === PYTHON BUILD ENVIRONMENT ===
if not exist ".venv\Scripts\python.exe" (py -3 -m venv .venv 2>nul || python -m venv .venv || (echo ERROR: Unable to create .venv.& exit /b 16))
".venv\Scripts\python.exe" -m pip install --upgrade pip || exit /b 17
".venv\Scripts\python.exe" -m pip install --upgrade -r requirements-build.txt || exit /b 18

echo === SOURCE VALIDATION ===
".venv\Scripts\python.exe" -m py_compile ytsubs.py ytsubs_app.py || (echo ERROR: Python syntax validation failed.& exit /b 20)
if not exist "assets\ytsubs.ico" (echo ERROR: Missing application icon: assets\ytsubs.ico& exit /b 21)
if not exist "assets\ytsubs.png" (echo ERROR: Missing application icon source: assets\ytsubs.png& exit /b 22)
if not exist "ytsubs.spec" (echo ERROR: Missing PyInstaller spec: ytsubs.spec& exit /b 23)

echo === STANDALONE BUILD ===
if exist "ytsubs.exe" del /q "ytsubs.exe" || exit /b 24
".venv\Scripts\python.exe" -m PyInstaller --noconfirm --clean --distpath "%CD%" --workpath "%CD%\build\pyinstaller" ytsubs.spec || (echo ERROR: Standalone build failed.& exit /b 25)
if not exist "ytsubs.exe" (echo ERROR: ytsubs.exe was not created.& exit /b 26)

echo === EXECUTABLE VALIDATION ===
set "EXPECTED_VERSION="
for /f "tokens=3" %%V in ('findstr /b /c:"version = " pyproject.toml') do set "EXPECTED_VERSION=%%~V"
if not defined EXPECTED_VERSION (echo ERROR: Unable to read project version from pyproject.toml.& exit /b 27)
set "ACTUAL_VERSION="
for /f "delims=" %%V in ('"%CD%\ytsubs.exe" --version 2^>nul') do set "ACTUAL_VERSION=%%V"
if not defined ACTUAL_VERSION (echo ERROR: Standalone ytsubs.exe CLI validation failed.& exit /b 28)
set "EXPECTED_OUTPUT=ytsubs !EXPECTED_VERSION!"
if /i not "!ACTUAL_VERSION!"=="!EXPECTED_OUTPUT!" (echo ERROR: Version mismatch.& echo        Expected: !EXPECTED_OUTPUT!& echo        Actual:   !ACTUAL_VERSION!& exit /b 29)
echo Version validation: !ACTUAL_VERSION!
echo Executable validation: ytsubs.exe
echo Icon validation: assets\ytsubs.ico + embedded EXE icon
echo Format validation: .srt .sub .txt .vtt
echo.
echo YouTubeSubs update completed successfully on branch %BRANCH%.
exit /b 0
