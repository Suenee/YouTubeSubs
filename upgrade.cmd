@echo off
cls
setlocal EnableExtensions EnableDelayedExpansion

if defined YTSUBS_REPO_DIR (
    for %%I in ("%YTSUBS_REPO_DIR%\.") do set "YTSUBS_REPO_DIR=%%~fI"
) else (
    for %%I in ("%~dp0.") do set "YTSUBS_REPO_DIR=%%~fI"
)
set "YTSUBS_BRANCH=devel"

where git >nul 2>nul || (echo ERROR: Git was not found.& exit /b 10)
where powershell >nul 2>nul || (echo ERROR: Windows PowerShell was not found.& exit /b 11)

set "YTSUBS_GIT_ERROR=%TEMP%\ytsubs_git_%RANDOM%_%RANDOM%.log"
git -C "%YTSUBS_REPO_DIR%" rev-parse --is-inside-work-tree >nul 2>"%YTSUBS_GIT_ERROR%"
if errorlevel 1 (
    findstr /i /c:"dubious ownership" "%YTSUBS_GIT_ERROR%" >nul 2>nul
    if not errorlevel 1 (
        echo Git requires this network repository to be trusted. Registering this exact path only...
        git config --global --add safe.directory "%YTSUBS_REPO_DIR%" || (del "%YTSUBS_GIT_ERROR%" >nul 2>nul& echo ERROR: Unable to register Git safe.directory.& exit /b 12)
        git -C "%YTSUBS_REPO_DIR%" rev-parse --is-inside-work-tree >nul 2>"%YTSUBS_GIT_ERROR%"
    )
)
if errorlevel 1 (
    type "%YTSUBS_GIT_ERROR%"
    del "%YTSUBS_GIT_ERROR%" >nul 2>nul
    echo ERROR: This directory is not a usable Git working tree.
    exit /b 13
)
del "%YTSUBS_GIT_ERROR%" >nul 2>nul

echo === SELF-UPDATE ===
git -C "%YTSUBS_REPO_DIR%" fetch origin "%YTSUBS_BRANCH%" || (echo ERROR: git fetch failed.& exit /b 14)
set "YTSUBS_REMOTE_RUNNER=%TEMP%\ytsubs_upgrade_%RANDOM%_%RANDOM%.ps1"
git -C "%YTSUBS_REPO_DIR%" show "origin/%YTSUBS_BRANCH%:upgrade.ps1" > "%YTSUBS_REMOTE_RUNNER%" 2>nul
if errorlevel 1 (
    del "%YTSUBS_REMOTE_RUNNER%" >nul 2>nul
    echo ERROR: Current upgrade.ps1 could not be fetched from origin/%YTSUBS_BRANCH%.
    exit /b 15
)

rem Keep the runner call, cleanup, and exit on one parsed line so replacing upgrade.cmd during Git sync cannot mix launcher generations.
powershell -NoProfile -ExecutionPolicy Bypass -File "%YTSUBS_REMOTE_RUNNER%" & set "YTSUBS_RC=!ERRORLEVEL!" & del "%YTSUBS_REMOTE_RUNNER%" >nul 2>nul & exit /b !YTSUBS_RC!
