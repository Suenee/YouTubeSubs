$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $MyInvocation.MyCommand.Path
$tools = Join-Path $repo 'tools'
$temp = Join-Path $env:TEMP ('ytsubs-tools-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $tools, $temp | Out-Null
try {
    Write-Host 'Downloading latest stable yt-dlp...'
    Invoke-WebRequest -UseBasicParsing 'https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe' -OutFile (Join-Path $temp 'yt-dlp.exe')

    Write-Host 'Downloading latest stable FFmpeg essentials build...'
    $zip = Join-Path $temp 'ffmpeg.zip'
    Invoke-WebRequest -UseBasicParsing 'https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip' -OutFile $zip
    Expand-Archive -Force $zip (Join-Path $temp 'ffmpeg')

    $ffmpeg = Get-ChildItem (Join-Path $temp 'ffmpeg') -Filter ffmpeg.exe -Recurse | Select-Object -First 1
    if (-not $ffmpeg) { throw 'ffmpeg.exe was not found in the downloaded archive.' }

    $ffprobe = Get-ChildItem (Join-Path $temp 'ffmpeg') -Filter ffprobe.exe -Recurse | Select-Object -First 1

    Copy-Item -Force (Join-Path $temp 'yt-dlp.exe') (Join-Path $tools 'yt-dlp.exe')
    Copy-Item -Force $ffmpeg.FullName (Join-Path $tools 'ffmpeg.exe')
    if ($ffprobe) { Copy-Item -Force $ffprobe.FullName (Join-Path $tools 'ffprobe.exe') }

    $ytDlpPath = Join-Path $tools 'yt-dlp.exe'
    $ffmpegPath = Join-Path $tools 'ffmpeg.exe'

    $ytDlpVersion = & $ytDlpPath --version
    $ytDlpExitCode = $LASTEXITCODE
    if ($ytDlpExitCode -ne 0) { throw "yt-dlp validation failed with exit code $ytDlpExitCode." }
    Write-Host ($ytDlpVersion | Select-Object -First 1)

    $ffmpegVersion = & $ffmpegPath -version 2>&1
    $ffmpegExitCode = $LASTEXITCODE
    if ($ffmpegExitCode -ne 0) { throw "FFmpeg validation failed with exit code $ffmpegExitCode." }
    Write-Host ($ffmpegVersion | Select-Object -First 1)

    if ($ffprobe) {
        $ffprobePath = Join-Path $tools 'ffprobe.exe'
        $null = & $ffprobePath -version 2>&1
        $ffprobeExitCode = $LASTEXITCODE
        if ($ffprobeExitCode -ne 0) { throw "ffprobe validation failed with exit code $ffprobeExitCode." }
    }
}
finally {
    Remove-Item -Recurse -Force $temp -ErrorAction SilentlyContinue
}
