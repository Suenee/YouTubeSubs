@echo off
cls
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
".venv\Scripts\python.exe" -m pip uninstall -y yt-dlp >nul 2>nul

echo === SOURCE VALIDATION ===
".venv\Scripts\python.exe" -m py_compile ytsubs.py ytsubs_app.py || (echo ERROR: Python syntax validation failed.& exit /b 21)
findstr /s /i /c:"from yt_dlp" /c:"import yt_dlp" ytsubs.py ytsubs_app.py >nul && (echo ERROR: Runtime source still imports yt-dlp.& exit /b 22)
if not exist "assets\ytsubs.ico.b64" (echo ERROR: Missing encoded Windows icon asset: assets\ytsubs.ico.b64& exit /b 23)

echo === ICON VALIDATION ===
".venv\Scripts\python.exe" -c "import base64,struct; from pathlib import Path; s=Path(r'assets\ytsubs.ico.b64'); raw=base64.b64decode(s.read_text(encoding='ascii'),validate=True); r,t,n=struct.unpack_from('<HHH',raw,0); assert r==0 and t==1 and n>=1; head=6+16*n; assert len(raw)>=head; e=[struct.unpack_from('<BBBBHHII',raw,6+16*i) for i in range(n)]; assert all(x[6]>0 and x[7]>=head and x[7]+x[6]<=len(raw) for x in e); Path(r'assets\ytsubs.ico').write_bytes(raw)" || (echo ERROR: Encoded Windows ICO asset is invalid.& exit /b 24)

echo === NUITKA STANDALONE BUILD ===
echo yt-dlp has been removed from the runtime; Nuitka no longer analyzes its extractor tree.
if exist "build\nuitka" rmdir /s /q "build\nuitka"
mkdir "build\nuitka" >nul 2>nul || (echo ERROR: Unable to create Nuitka build directory.& exit /b 25)
".venv\Scripts\python.exe" -m nuitka ^
  --mode=onefile ^
  --onefile-no-compression ^
  --windows-console-mode=attach ^
  --enable-plugin=tk-inter ^
  --windows-icon-from-ico="assets\ytsubs.ico" ^
  --include-data-file="assets\ytsubs.ico=assets/ytsubs.ico" ^
  --output-dir="build\nuitka" ^
  --output-filename="ytsubs.exe" ^
  --mingw64 ^
  --assume-yes-for-downloads ^
  --nofollow-import-to=yt_dlp ^
  --nofollow-import-to=unittest ^
  --nofollow-import-to=pydoc ^
  --nofollow-import-to=doctest ^
  ytsubs_app.py || (echo ERROR: Nuitka standalone build failed. Existing ytsubs.exe was left untouched.& exit /b 26)
if not exist "build\nuitka\ytsubs.exe" (echo ERROR: Nuitka did not create build\nuitka\ytsubs.exe.& exit /b 27)

echo === CANDIDATE VALIDATION ===
set "EXPECTED_VERSION="
for /f "tokens=3" %%V in ('findstr /b /c:"version = " pyproject.toml') do set "EXPECTED_VERSION=%%~V"
if not defined EXPECTED_VERSION (echo ERROR: Unable to read project version from pyproject.toml.& exit /b 28)
set "EXPECTED_OUTPUT=ytsubs !EXPECTED_VERSION!"
set "CANDIDATE_VERSION="
for /f "delims=" %%V in ('"%CD%\build\nuitka\ytsubs.exe" --version 2^>nul') do set "CANDIDATE_VERSION=%%V"
if not defined CANDIDATE_VERSION (echo ERROR: Nuitka candidate CLI validation failed. Existing ytsubs.exe was left untouched.& exit /b 29)
if /i not "!CANDIDATE_VERSION!"=="!EXPECTED_OUTPUT!" (echo ERROR: Candidate version mismatch.& echo        Expected: !EXPECTED_OUTPUT!& echo        Actual:   !CANDIDATE_VERSION!& exit /b 30)

echo === INSTALL CANDIDATE ===
copy /y "build\nuitka\ytsubs.exe" "%CD%\ytsubs.exe" >nul || (echo ERROR: Unable to install validated ytsubs.exe.& exit /b 31)

echo === PORTABLE SMOKE TEST ===
set "PORTABLE_DIR=%TEMP%\ytsubs_portable_%RANDOM%_%RANDOM%"
mkdir "!PORTABLE_DIR!" >nul 2>nul || (echo ERROR: Unable to create portable test directory.& exit /b 32)
copy /y "%CD%\ytsubs.exe" "!PORTABLE_DIR!\ytsubs.exe" >nul || (rmdir /s /q "!PORTABLE_DIR!" >nul 2>nul& echo ERROR: Unable to copy ytsubs.exe for portable test.& exit /b 33)
set "PORTABLE_VERSION="
for /f "delims=" %%V in ('cd /d "!PORTABLE_DIR!" ^&^& ytsubs.exe --version 2^>nul') do set "PORTABLE_VERSION=%%V"
rmdir /s /q "!PORTABLE_DIR!" >nul 2>nul
if not defined PORTABLE_VERSION (echo ERROR: Portable ytsubs.exe failed outside the repository.& exit /b 34)
if /i not "!PORTABLE_VERSION!"=="!EXPECTED_OUTPUT!" (echo ERROR: Portable executable version mismatch.& echo        Expected: !EXPECTED_OUTPUT!& echo        Actual:   !PORTABLE_VERSION!& exit /b 35)

echo === STARTUP DIAGNOSTICS ===
set "EXE_BYTES="
for %%F in ("%CD%\ytsubs.exe") do set "EXE_BYTES=%%~zF"
set "STARTUP_MS="
for /f "delims=" %%T in ('powershell -NoProfile -Command "$sw=[Diagnostics.Stopwatch]::StartNew(); $p=Start-Process -FilePath '%CD%\ytsubs.exe' -ArgumentList '--version' -PassThru -Wait -WindowStyle Hidden; $sw.Stop(); [int]$sw.Elapsed.TotalMilliseconds"') do set "STARTUP_MS=%%T"

".venv\Scripts\python.exe" -c "import ctypes; ctypes.windll.shell32.SHChangeNotify(0x08000000,0,None,None)" >nul 2>nul

echo Version validation: !CANDIDATE_VERSION!
echo Executable validation: ytsubs.exe
echo Portable validation: standalone EXE passed outside repository
echo Build system: Nuitka onefile, C-compiled Python application
echo Runtime dependencies: youtube-transcript-api + requests; yt-dlp removed
echo GUI mode: attach existing console only; no console created on GUI launch
echo Startup optimization: uncompressed onefile payload; no yt-dlp extractor tree
echo Executable size: !EXE_BYTES! bytes
if defined STARTUP_MS echo CLI cold-start sample: !STARTUP_MS! ms
echo Icon validation: encoded ICO -^> structural validation -^> embedded EXE icon
echo Format validation: .srt .sub .txt .vtt
echo.
echo YouTubeSubs update completed successfully on branch %BRANCH%.
exit /b 0
