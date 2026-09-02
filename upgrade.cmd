@echo off
cls
setlocal EnableExtensions EnableDelayedExpansion

rem Allow Git operations from mapped/UNC network repositories without requiring a manual global safe.directory entry.
set "GIT_CONFIG_COUNT=1"
set "GIT_CONFIG_KEY_0=safe.directory"
set "GIT_CONFIG_VALUE_0=*"

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
set "LOCAL_SHA="& set "REMOTE_SHA="& set "BASE_SHA="
for /f "delims=" %%S in ('git rev-parse HEAD') do set "LOCAL_SHA=%%S"
for /f "delims=" %%S in ('git rev-parse "origin/%BRANCH%"') do set "REMOTE_SHA=%%S"
for /f "delims=" %%S in ('git merge-base HEAD "origin/%BRANCH%"') do set "BASE_SHA=%%S"
if /i "!LOCAL_SHA!"=="!REMOTE_SHA!" (echo Already up to date.) else if /i "!LOCAL_SHA!"=="!BASE_SHA!" (git merge --ff-only "origin/%BRANCH%" || exit /b 15) else (echo WARNING: Synchronizing diverged local branch to origin/%BRANCH%.& git reset --hard "origin/%BRANCH%" || exit /b 16)

echo === .NET 10 SDK CHECK ===
set "DOTNET_EXE="
for /f "delims=" %%D in ('where dotnet 2^>nul') do if not defined DOTNET_EXE set "DOTNET_EXE=%%D"
if not defined DOTNET_EXE if exist "%ProgramFiles%\dotnet\dotnet.exe" set "DOTNET_EXE=%ProgramFiles%\dotnet\dotnet.exe"
set "SDK_LIST_FILE=%TEMP%\ytsubs_dotnet_sdks_%RANDOM%.txt"& set "HAS_DOTNET10="
if defined DOTNET_EXE ("!DOTNET_EXE!" --list-sdks > "!SDK_LIST_FILE!" 2>nul& findstr /b /c:"10." "!SDK_LIST_FILE!" >nul 2>nul && set "HAS_DOTNET10=1")
del "!SDK_LIST_FILE!" >nul 2>nul
if not defined HAS_DOTNET10 (
  echo Installing current Microsoft .NET 10 SDK...
  where winget >nul 2>nul || (echo ERROR: winget is unavailable.& exit /b 17)
  winget install --id Microsoft.DotNet.SDK.10 --exact --silent --accept-package-agreements --accept-source-agreements || exit /b 18
  set "DOTNET_EXE=%ProgramFiles%\dotnet\dotnet.exe"
)
for /f "delims=" %%V in ('"!DOTNET_EXE!" --version') do set "DOTNET_VERSION=%%V"
echo Build SDK: .NET !DOTNET_VERSION!

echo === MEDIA TOOLS UPDATE ===
if not exist "tools-update.ps1" (echo ERROR: tools-update.ps1 is missing.& exit /b 36)
powershell -NoProfile -ExecutionPolicy Bypass -File "%CD%\tools-update.ps1" || (echo ERROR: yt-dlp/FFmpeg update failed.& exit /b 37)
if not exist "tools\yt-dlp.exe" (echo ERROR: yt-dlp.exe is missing.& exit /b 38)
if not exist "tools\ffmpeg.exe" (echo ERROR: ffmpeg.exe is missing.& exit /b 38)

echo === ICON VALIDATION ===
if not exist "assets\ytsubs.ico.b64" (echo ERROR: Missing encoded Windows icon asset.& exit /b 20)
powershell -NoProfile -Command "$raw=[Convert]::FromBase64String((Get-Content -Raw 'assets\ytsubs.ico.b64')); if($raw.Length -lt 22 -or $raw[0] -ne 0 -or $raw[1] -ne 0 -or $raw[2] -ne 1 -or $raw[3] -ne 0){exit 1}; [IO.File]::WriteAllBytes('assets\ytsubs.ico',$raw)" || exit /b 21

echo === CLEAN BUILD STATE ===
if exist "obj" rmdir /s /q "obj"
if exist "bin" rmdir /s /q "bin"
if exist "cli\obj" rmdir /s /q "cli\obj"
if exist "cli\bin" rmdir /s /q "cli\bin"

echo === SOURCE VALIDATION ===
"!DOTNET_EXE!" restore YouTubeSubs.csproj || exit /b 22
"!DOTNET_EXE!" restore cli\YouTubeSubs.Cli.csproj || exit /b 22
"!DOTNET_EXE!" build YouTubeSubs.csproj -c Release --no-restore || (echo ERROR: GUI build failed.& exit /b 23)
"!DOTNET_EXE!" build cli\YouTubeSubs.Cli.csproj -c Release --no-restore || (echo ERROR: CLI build failed.& exit /b 23)

echo === PUBLISH GUI AND CLI ===
if exist "build\publish-gui" rmdir /s /q "build\publish-gui"
if exist "build\publish-cli" rmdir /s /q "build\publish-cli"
"!DOTNET_EXE!" publish YouTubeSubs.csproj -c Release -r win-x64 --self-contained true --no-restore -p:PublishSingleFile=true -p:PublishTrimmed=false -o "build\publish-gui" || exit /b 24
"!DOTNET_EXE!" publish cli\YouTubeSubs.Cli.csproj -c Release -r win-x64 --self-contained true --no-restore -p:PublishSingleFile=true -p:PublishTrimmed=false -o "build\publish-cli" || exit /b 24
if not exist "build\publish-gui\ytsubs.exe" exit /b 25
if not exist "build\publish-cli\ytsubs-cli.exe" exit /b 25

echo === PE SUBSYSTEM VALIDATION ===
powershell -NoProfile -Command "$g=[IO.File]::ReadAllBytes('%CD%\build\publish-gui\ytsubs.exe'); $c=[IO.File]::ReadAllBytes('%CD%\build\publish-cli\ytsubs-cli.exe'); function sub($b){$pe=[BitConverter]::ToInt32($b,0x3c); [BitConverter]::ToUInt16($b,$pe+92)}; $gs=sub $g; $cs=sub $c; Write-Host ('GUI subsystem: '+$gs); Write-Host ('CLI subsystem: '+$cs); if($gs-ne 2){exit 1}; if($cs-ne 3){exit 2}" || exit /b 33

echo === CLI VALIDATION ===
set "EXPECTED_OUTPUT=ytsubs-cli 2.12"& set "CLI_VERSION="
for /f "delims=" %%V in ('"%CD%\build\publish-cli\ytsubs-cli.exe" --version') do set "CLI_VERSION=%%V"
if /i not "!CLI_VERSION!"=="!EXPECTED_OUTPUT!" (echo ERROR: CLI candidate version mismatch.& echo Expected: !EXPECTED_OUTPUT!& echo Actual: !CLI_VERSION!& exit /b 27)

echo === INSTALL CANDIDATES ===
copy /y "build\publish-gui\ytsubs.exe" "%CD%\ytsubs.exe" >nul || exit /b 28
copy /y "build\publish-cli\ytsubs-cli.exe" "%CD%\ytsubs-cli.exe" >nul || exit /b 28
set "INSTALLED_CLI_VERSION="
for /f "delims=" %%V in ('"%CD%\ytsubs-cli.exe" --version') do set "INSTALLED_CLI_VERSION=%%V"
if /i not "!INSTALLED_CLI_VERSION!"=="!EXPECTED_OUTPUT!" exit /b 35

echo Version validation: !CLI_VERSION!
echo Installed validation: !INSTALLED_CLI_VERSION!
echo Media tools: latest stable yt-dlp + FFmpeg
echo Network repository support: enabled
echo Build system: .NET 10 self-contained single-file win-x64
echo.
echo YouTubeSubs update completed successfully on branch %BRANCH%.
exit /b 0
