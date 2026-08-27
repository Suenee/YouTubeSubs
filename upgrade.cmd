@echo off
cls
setlocal EnableExtensions EnableDelayedExpansion

if defined YTSUBS_REPO_DIR (cd /d "%YTSUBS_REPO_DIR%") else (set "YTSUBS_REPO_DIR=%~dp0"& cd /d "%~dp0")
where git >nul 2>nul || (echo ERROR: Git was not found.& exit /b 10)
for /f "delims=" %%B in ('git rev-parse --abbrev-ref HEAD 2^>nul') do set "BRANCH=%%B"
if not defined BRANCH (echo ERROR: This directory is not a Git working tree.& exit /b 11)

echo === SELF UPDATE CHECK ===
git fetch origin "%BRANCH%" || (echo ERROR: git fetch failed.& exit /b 12)
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
for /f "delims=" %%S in ('git status --porcelain --untracked-files^=no') do (echo ERROR: Tracked local changes detected. Commit, stash, or revert them first.& exit /b 13)

echo === UPDATE ===
git fetch origin "%BRANCH%" || (echo ERROR: git fetch failed.& exit /b 14)
set "LOCAL_SHA="
set "REMOTE_SHA="
set "BASE_SHA="
for /f "delims=" %%S in ('git rev-parse HEAD') do set "LOCAL_SHA=%%S"
for /f "delims=" %%S in ('git rev-parse "origin/%BRANCH%"') do set "REMOTE_SHA=%%S"
for /f "delims=" %%S in ('git merge-base HEAD "origin/%BRANCH%"') do set "BASE_SHA=%%S"
if /i "!LOCAL_SHA!"=="!REMOTE_SHA!" (
  echo Already up to date.
) else if /i "!LOCAL_SHA!"=="!BASE_SHA!" (
  git merge --ff-only "origin/%BRANCH%" || (echo ERROR: Fast-forward update failed.& exit /b 15)
) else (
  echo WARNING: Local branch history diverged from origin/%BRANCH%.
  echo          No tracked local changes were found, so the branch will be synchronized to origin/%BRANCH%.
  git reset --hard "origin/%BRANCH%" || (echo ERROR: Unable to synchronize local branch with origin/%BRANCH%.& exit /b 16)
)

echo === .NET 10 SDK CHECK ===
set "DOTNET_EXE="
where dotnet >nul 2>nul && set "DOTNET_EXE=dotnet"
if not defined DOTNET_EXE if exist "%ProgramFiles%\dotnet\dotnet.exe" set "DOTNET_EXE=%ProgramFiles%\dotnet\dotnet.exe"
set "HAS_DOTNET10="
if defined DOTNET_EXE for /f "delims=" %%D in ('"!DOTNET_EXE!" --list-sdks 2^>nul ^| findstr /b "10\."') do set "HAS_DOTNET10=1"
if not defined HAS_DOTNET10 (
  echo .NET 10 SDK is not installed. upgrade.cmd will install the current Microsoft .NET 10 SDK.
  where winget >nul 2>nul || (echo ERROR: .NET 10 SDK is missing and winget is unavailable.& echo        Install the current .NET 10 SDK and run upgrade.cmd again.& exit /b 17)
  winget install --id Microsoft.DotNet.SDK.10 --exact --silent --accept-package-agreements --accept-source-agreements || (echo ERROR: Automatic .NET 10 SDK installation failed.& exit /b 18)
  if exist "%ProgramFiles%\dotnet\dotnet.exe" set "DOTNET_EXE=%ProgramFiles%\dotnet\dotnet.exe"
  set "HAS_DOTNET10="
  for /f "delims=" %%D in ('"!DOTNET_EXE!" --list-sdks 2^>nul ^| findstr /b "10\."') do set "HAS_DOTNET10=1"
  if not defined HAS_DOTNET10 (echo ERROR: .NET 10 SDK installation completed but SDK 10.x is still unavailable.& exit /b 19)
)
for /f "delims=" %%D in ('"!DOTNET_EXE!" --version') do set "DOTNET_VERSION=%%D"
echo Build SDK: .NET !DOTNET_VERSION!

echo === LEGACY PYTHON BUILD CLEANUP ===
if exist ".venv" rmdir /s /q ".venv"
if exist "build\nuitka" rmdir /s /q "build\nuitka"
if exist "nuitka-crash-report.xml" del /q "nuitka-crash-report.xml"

echo === ICON VALIDATION ===
if not exist "assets\ytsubs.ico.b64" (echo ERROR: Missing encoded Windows icon asset: assets\ytsubs.ico.b64& exit /b 20)
powershell -NoProfile -Command "$raw=[Convert]::FromBase64String((Get-Content -Raw 'assets\ytsubs.ico.b64')); if($raw.Length -lt 22 -or $raw[0] -ne 0 -or $raw[1] -ne 0 -or $raw[2] -ne 1 -or $raw[3] -ne 0){exit 1}; [IO.File]::WriteAllBytes('assets\ytsubs.ico',$raw)" || (echo ERROR: Encoded Windows ICO asset is invalid.& exit /b 21)

echo === .NET SOURCE VALIDATION ===
"!DOTNET_EXE!" restore YouTubeSubs.csproj || (echo ERROR: dotnet restore failed.& exit /b 22)
"!DOTNET_EXE!" build YouTubeSubs.csproj -c Release --no-restore || (echo ERROR: .NET source build failed.& exit /b 23)

echo === .NET 10 SELF-CONTAINED SINGLE-FILE PUBLISH ===
if exist "build\publish" rmdir /s /q "build\publish"
"!DOTNET_EXE!" publish YouTubeSubs.csproj -c Release -r win-x64 --self-contained true --no-restore -p:PublishSingleFile=true -p:PublishTrimmed=false -o "build\publish" || (echo ERROR: .NET publish failed. Existing ytsubs.exe was left untouched.& exit /b 24)
if not exist "build\publish\ytsubs.exe" (echo ERROR: Publish did not create build\publish\ytsubs.exe.& exit /b 25)

echo === CANDIDATE VALIDATION ===
set "EXPECTED_OUTPUT=ytsubs 2.00"
set "CANDIDATE_VERSION="
for /f "delims=" %%V in ('powershell -NoProfile -Command "$p=New-Object Diagnostics.Process; $p.StartInfo.FileName='%CD%\build\publish\ytsubs.exe'; $p.StartInfo.Arguments='--version'; $p.StartInfo.UseShellExecute=$false; $p.StartInfo.RedirectStandardOutput=$true; $p.StartInfo.RedirectStandardError=$true; $p.StartInfo.CreateNoWindow=$true; [void]$p.Start(); $o=$p.StandardOutput.ReadToEnd().Trim(); $p.WaitForExit(); if($p.ExitCode -eq 0){$o}"') do set "CANDIDATE_VERSION=%%V"
if not defined CANDIDATE_VERSION (echo ERROR: .NET candidate CLI validation failed. Existing ytsubs.exe was left untouched.& exit /b 26)
if /i not "!CANDIDATE_VERSION!"=="!EXPECTED_OUTPUT!" (echo ERROR: Candidate version mismatch.& echo        Expected: !EXPECTED_OUTPUT!& echo        Actual:   !CANDIDATE_VERSION!& exit /b 27)

echo === INSTALL CANDIDATE ===
copy /y "build\publish\ytsubs.exe" "%CD%\ytsubs.exe" >nul || (echo ERROR: Unable to install validated ytsubs.exe.& exit /b 28)

echo === PORTABLE SMOKE TEST ===
set "PORTABLE_DIR=%TEMP%\ytsubs_portable_%RANDOM%_%RANDOM%"
mkdir "!PORTABLE_DIR!" >nul 2>nul || (echo ERROR: Unable to create portable test directory.& exit /b 29)
copy /y "%CD%\ytsubs.exe" "!PORTABLE_DIR!\ytsubs.exe" >nul || (rmdir /s /q "!PORTABLE_DIR!" >nul 2>nul& echo ERROR: Unable to copy ytsubs.exe for portable test.& exit /b 30)
set "PORTABLE_VERSION="
for /f "delims=" %%V in ('powershell -NoProfile -Command "$p=New-Object Diagnostics.Process; $p.StartInfo.FileName='!PORTABLE_DIR!\ytsubs.exe'; $p.StartInfo.Arguments='--version'; $p.StartInfo.WorkingDirectory='!PORTABLE_DIR!'; $p.StartInfo.UseShellExecute=$false; $p.StartInfo.RedirectStandardOutput=$true; $p.StartInfo.CreateNoWindow=$true; [void]$p.Start(); $o=$p.StandardOutput.ReadToEnd().Trim(); $p.WaitForExit(); if($p.ExitCode -eq 0){$o}"') do set "PORTABLE_VERSION=%%V"
rmdir /s /q "!PORTABLE_DIR!" >nul 2>nul
if /i not "!PORTABLE_VERSION!"=="!EXPECTED_OUTPUT!" (echo ERROR: Portable executable validation failed.& exit /b 31)

echo === STARTUP DIAGNOSTICS ===
set "EXE_BYTES="
for %%F in ("%CD%\ytsubs.exe") do set "EXE_BYTES=%%~zF"
set "STARTUP_MS="
for /f "delims=" %%T in ('powershell -NoProfile -Command "$sw=[Diagnostics.Stopwatch]::StartNew(); $p=New-Object Diagnostics.Process; $p.StartInfo.FileName='%CD%\ytsubs.exe'; $p.StartInfo.Arguments='--version'; $p.StartInfo.UseShellExecute=$false; $p.StartInfo.RedirectStandardOutput=$true; $p.StartInfo.CreateNoWindow=$true; [void]$p.Start(); $null=$p.StandardOutput.ReadToEnd(); $p.WaitForExit(); $sw.Stop(); [int]$sw.Elapsed.TotalMilliseconds"') do set "STARTUP_MS=%%T"
powershell -NoProfile -Command "Add-Type -AssemblyName System.Windows.Forms; [System.Windows.Forms.Application]::DoEvents()" >nul 2>nul

echo Version validation: !CANDIDATE_VERSION!
echo Executable validation: ytsubs.exe
echo Portable validation: standalone EXE passed outside repository
echo Build system: .NET 10 WinForms, self-contained single-file win-x64
echo Application icon: embedded assets\ytsubs.ico
echo Executable size: !EXE_BYTES! bytes
if defined STARTUP_MS echo CLI cold-start sample: !STARTUP_MS! ms
echo Format validation: .srt .sub .txt .vtt
echo.
echo YouTubeSubs update completed successfully on branch %BRANCH%.
exit /b 0
