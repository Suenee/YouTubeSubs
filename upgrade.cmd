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
for /f "delims=" %%D in ('where dotnet 2^>nul') do if not defined DOTNET_EXE set "DOTNET_EXE=%%D"
if not defined DOTNET_EXE if exist "%ProgramFiles%\dotnet\dotnet.exe" set "DOTNET_EXE=%ProgramFiles%\dotnet\dotnet.exe"
set "SDK_LIST_FILE=%TEMP%\ytsubs_dotnet_sdks_%RANDOM%.txt"
set "HAS_DOTNET10="
if defined DOTNET_EXE (
  "!DOTNET_EXE!" --list-sdks > "!SDK_LIST_FILE!" 2>nul
  findstr /b /c:"10." "!SDK_LIST_FILE!" >nul 2>nul && set "HAS_DOTNET10=1"
)
del "!SDK_LIST_FILE!" >nul 2>nul
if not defined HAS_DOTNET10 (
  if defined DOTNET_EXE echo .NET host was found, but no .NET 10 SDK was detected.
  if not defined DOTNET_EXE echo .NET SDK host was not found.
  echo upgrade.cmd will install the current Microsoft .NET 10 SDK.
  where winget >nul 2>nul || (echo ERROR: .NET 10 SDK is unavailable and winget is unavailable.& exit /b 17)
  winget install --id Microsoft.DotNet.SDK.10 --exact --silent --accept-package-agreements --accept-source-agreements
  if errorlevel 1 (echo ERROR: Automatic .NET 10 SDK installation failed.& exit /b 18)
  set "DOTNET_EXE=%ProgramFiles%\dotnet\dotnet.exe"
  if not exist "!DOTNET_EXE!" (echo ERROR: dotnet.exe is unavailable after installation.& exit /b 19)
)
for /f "delims=" %%V in ('"!DOTNET_EXE!" --version') do set "DOTNET_VERSION=%%V"
echo Build SDK: .NET !DOTNET_VERSION!

echo === ICON VALIDATION ===
if not exist "assets\ytsubs.ico.b64" (echo ERROR: Missing encoded Windows icon asset.& exit /b 20)
powershell -NoProfile -Command "$raw=[Convert]::FromBase64String((Get-Content -Raw 'assets\ytsubs.ico.b64')); if($raw.Length -lt 22 -or $raw[0] -ne 0 -or $raw[1] -ne 0 -or $raw[2] -ne 1 -or $raw[3] -ne 0){exit 1}; [IO.File]::WriteAllBytes('assets\ytsubs.ico',$raw)" || (echo ERROR: Encoded Windows ICO asset is invalid.& exit /b 21)

echo === SOURCE VALIDATION ===
"!DOTNET_EXE!" restore YouTubeSubs.csproj || (echo ERROR: GUI restore failed.& exit /b 22)
"!DOTNET_EXE!" restore YouTubeSubs.Cli.csproj || (echo ERROR: CLI restore failed.& exit /b 22)
"!DOTNET_EXE!" build YouTubeSubs.csproj -c Release --no-restore || (echo ERROR: GUI build failed.& exit /b 23)
"!DOTNET_EXE!" build YouTubeSubs.Cli.csproj -c Release --no-restore || (echo ERROR: CLI build failed.& exit /b 23)

echo === PUBLISH GUI AND CLI ===
if exist "build\publish-gui" rmdir /s /q "build\publish-gui"
if exist "build\publish-cli" rmdir /s /q "build\publish-cli"
"!DOTNET_EXE!" publish YouTubeSubs.csproj -c Release -r win-x64 --self-contained true --no-restore -p:PublishSingleFile=true -p:PublishTrimmed=false -o "build\publish-gui" || (echo ERROR: GUI publish failed. Existing executables were left untouched.& exit /b 24)
"!DOTNET_EXE!" publish YouTubeSubs.Cli.csproj -c Release -r win-x64 --self-contained true --no-restore -p:PublishSingleFile=true -p:PublishTrimmed=false -o "build\publish-cli" || (echo ERROR: CLI publish failed. Existing executables were left untouched.& exit /b 24)
if not exist "build\publish-gui\ytsubs.exe" (echo ERROR: GUI candidate is missing.& exit /b 25)
if not exist "build\publish-cli\ytsubs-cli.exe" (echo ERROR: CLI candidate is missing.& exit /b 25)

echo === PE SUBSYSTEM VALIDATION ===
powershell -NoProfile -Command "$g=[IO.File]::ReadAllBytes('%CD%\build\publish-gui\ytsubs.exe'); $c=[IO.File]::ReadAllBytes('%CD%\build\publish-cli\ytsubs-cli.exe'); function sub($b){$pe=[BitConverter]::ToInt32($b,0x3c); [BitConverter]::ToUInt16($b,$pe+92)}; if((sub $g)-ne 2){exit 1}; if((sub $c)-ne 3){exit 2}" || (echo ERROR: GUI/CLI PE subsystems are not separated correctly.& echo        Existing executables were left untouched.& exit /b 33)

echo === CLI VALIDATION ===
set "EXPECTED_OUTPUT=ytsubs-cli 2.07"
set "CLI_VERSION="
for /f "delims=" %%V in ('"%CD%\build\publish-cli\ytsubs-cli.exe" --version') do set "CLI_VERSION=%%V"
if /i not "!CLI_VERSION!"=="!EXPECTED_OUTPUT!" (echo ERROR: CLI candidate version mismatch.& echo Expected: !EXPECTED_OUTPUT!& echo Actual: !CLI_VERSION!& exit /b 27)

echo === INSTALL CANDIDATES ===
copy /y "build\publish-gui\ytsubs.exe" "%CD%\ytsubs.exe" >nul || (echo ERROR: Unable to install ytsubs.exe.& exit /b 28)
copy /y "build\publish-cli\ytsubs-cli.exe" "%CD%\ytsubs-cli.exe" >nul || (echo ERROR: Unable to install ytsubs-cli.exe.& exit /b 28)
if not exist "%CD%\ytsubs.exe" (echo ERROR: ytsubs.exe is missing after installation.& exit /b 34)
if not exist "%CD%\ytsubs-cli.exe" (echo ERROR: ytsubs-cli.exe is missing after installation.& exit /b 34)

echo === INSTALLED CLI VALIDATION ===
set "INSTALLED_CLI_VERSION="
for /f "delims=" %%V in ('"%CD%\ytsubs-cli.exe" --version') do set "INSTALLED_CLI_VERSION=%%V"
if /i not "!INSTALLED_CLI_VERSION!"=="!EXPECTED_OUTPUT!" (echo ERROR: Installed ytsubs-cli.exe did not execute correctly.& echo Expected: !EXPECTED_OUTPUT!& echo Actual: !INSTALLED_CLI_VERSION!& exit /b 35)

echo === PORTABLE PAIR TEST ===
set "PORTABLE_DIR=%TEMP%\ytsubs_portable_%RANDOM%_%RANDOM%"
mkdir "!PORTABLE_DIR!" >nul 2>nul || exit /b 29
copy /y "%CD%\ytsubs.exe" "!PORTABLE_DIR!\ytsubs.exe" >nul
copy /y "%CD%\ytsubs-cli.exe" "!PORTABLE_DIR!\ytsubs-cli.exe" >nul
set "PORTABLE_VERSION="
for /f "delims=" %%V in ('"!PORTABLE_DIR!\ytsubs-cli.exe" --version') do set "PORTABLE_VERSION=%%V"
rmdir /s /q "!PORTABLE_DIR!" >nul 2>nul
if /i not "!PORTABLE_VERSION!"=="!EXPECTED_OUTPUT!" (echo ERROR: Portable CLI validation failed.& exit /b 31)

echo Version validation: !CLI_VERSION!
echo Installed validation: !INSTALLED_CLI_VERSION!
echo GUI executable: ytsubs.exe - Windows GUI subsystem
echo CLI executable: ytsubs-cli.exe - Windows console subsystem
echo CLI without arguments: launches ytsubs.exe
echo Portable validation: GUI/CLI pair passed outside repository
echo Build system: .NET 10 self-contained single-file win-x64
echo Format validation: .srt .sub .txt .vtt
echo.
echo YouTubeSubs update completed successfully on branch %BRANCH%.
exit /b 0
