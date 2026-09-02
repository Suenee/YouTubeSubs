$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $MyInvocation.MyCommand.Path
$tools = Join-Path $repo 'tools'
$temp = Join-Path $env:TEMP ('ytsubs-tools-' + [guid]::NewGuid().ToString('N'))
$ytDlpPath = Join-Path $tools 'yt-dlp.exe'
$ffmpegPath = Join-Path $tools 'ffmpeg.exe'
$ffprobePath = Join-Path $tools 'ffprobe.exe'
$ffmpegVersionUrl = 'https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip.ver'
$ffmpegZipUrl = 'https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip'
$ytDlpDownloadUrl = 'https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe'

New-Item -ItemType Directory -Force -Path $tools, $temp | Out-Null

function Get-YtDlpVersion {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    try {
        $output = @(& $Path --version 2>&1)
        $exitCode = $LASTEXITCODE
        if ($exitCode -ne 0 -or $output.Count -eq 0) { return $null }
        return ([string]$output[0]).Trim()
    }
    catch { return $null }
}

function Get-FfmpegVersion {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    try {
        $output = @(& $Path -version 2>&1)
        $exitCode = $LASTEXITCODE
        if ($exitCode -ne 0 -or $output.Count -eq 0) { return $null }
        $first = ([string]$output[0]).Trim()
        if ($first -match '^ffmpeg version\s+([^\s-]+)') { return $Matches[1] }
        return $null
    }
    catch { return $null }
}

function Validate-Tool {
    param([string]$Path, [string[]]$Arguments, [string]$Name)
    if (-not (Test-Path -LiteralPath $Path)) { throw "$Name is missing after update." }
    $output = @(& $Path @Arguments 2>&1)
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) { throw "$Name validation failed with exit code $exitCode." }
    return $output
}

try {
    # yt-dlp release binaries have their own stable-channel updater. It performs a
    # lightweight release check and downloads a new executable only when needed.
    $localYtDlpVersion = Get-YtDlpVersion $ytDlpPath
    if (-not $localYtDlpVersion) {
        Write-Host 'yt-dlp missing or invalid; downloading latest stable release...'
        Invoke-WebRequest -UseBasicParsing $ytDlpDownloadUrl -OutFile (Join-Path $temp 'yt-dlp.exe')
        Copy-Item -Force (Join-Path $temp 'yt-dlp.exe') $ytDlpPath
    }
    else {
        Write-Host "yt-dlp local version: $localYtDlpVersion"
        Write-Host 'Checking yt-dlp stable channel...'
        $updateOutput = @(& $ytDlpPath --update-to stable 2>&1)
        $updateExitCode = $LASTEXITCODE
        $updateOutput | ForEach-Object { Write-Host $_ }
        if ($updateExitCode -ne 0) { throw "yt-dlp update check failed with exit code $updateExitCode." }
    }

    $ytDlpOutput = Validate-Tool $ytDlpPath @('--version') 'yt-dlp'
    $validatedYtDlpVersion = ([string]($ytDlpOutput | Select-Object -First 1)).Trim()
    Write-Host "yt-dlp ready: $validatedYtDlpVersion"

    # Gyan publishes a tiny .ver endpoint next to the release package. Compare it
    # with the installed FFmpeg version first; download the ~100 MB archive only
    # when the version changed or the local executable is missing/invalid.
    Write-Host 'Checking latest stable FFmpeg essentials version...'
    $remoteFfmpegVersion = ([string](Invoke-RestMethod -UseBasicParsing $ffmpegVersionUrl)).Trim()
    if ([string]::IsNullOrWhiteSpace($remoteFfmpegVersion)) { throw 'Unable to determine latest FFmpeg essentials version.' }
    $localFfmpegVersion = Get-FfmpegVersion $ffmpegPath

    if ($localFfmpegVersion -and $localFfmpegVersion -eq $remoteFfmpegVersion -and (Test-Path -LiteralPath $ffprobePath)) {
        Write-Host "FFmpeg already current: $localFfmpegVersion"
    }
    else {
        if ($localFfmpegVersion) {
            Write-Host "FFmpeg update required: $localFfmpegVersion -> $remoteFfmpegVersion"
        }
        else {
            Write-Host "FFmpeg missing or invalid; installing stable $remoteFfmpegVersion"
        }

        $zip = Join-Path $temp 'ffmpeg.zip'
        Write-Host 'Downloading FFmpeg essentials build...'
        Invoke-WebRequest -UseBasicParsing $ffmpegZipUrl -OutFile $zip
        Expand-Archive -Force $zip (Join-Path $temp 'ffmpeg')

        $ffmpeg = Get-ChildItem (Join-Path $temp 'ffmpeg') -Filter ffmpeg.exe -Recurse | Select-Object -First 1
        if (-not $ffmpeg) { throw 'ffmpeg.exe was not found in the downloaded archive.' }
        $ffprobe = Get-ChildItem (Join-Path $temp 'ffmpeg') -Filter ffprobe.exe -Recurse | Select-Object -First 1
        if (-not $ffprobe) { throw 'ffprobe.exe was not found in the downloaded archive.' }

        Copy-Item -Force $ffmpeg.FullName $ffmpegPath
        Copy-Item -Force $ffprobe.FullName $ffprobePath
    }

    $ffmpegOutput = Validate-Tool $ffmpegPath @('-version') 'FFmpeg'
    $validatedFfmpegVersion = Get-FfmpegVersion $ffmpegPath
    if (-not $validatedFfmpegVersion) { throw 'Unable to parse validated FFmpeg version.' }
    if ($validatedFfmpegVersion -ne $remoteFfmpegVersion) {
        throw "FFmpeg version mismatch after update. Expected $remoteFfmpegVersion, found $validatedFfmpegVersion."
    }
    Write-Host ([string]($ffmpegOutput | Select-Object -First 1))

    $null = Validate-Tool $ffprobePath @('-version') 'ffprobe'
    Write-Host 'Media tools are current.'
}
finally {
    Remove-Item -Recurse -Force $temp -ErrorAction SilentlyContinue
}
