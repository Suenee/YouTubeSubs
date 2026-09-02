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
    Copy-Item -Force (Join-Path $temp 'yt-dlp.exe') (Join-Path $tools 'yt-dlp.exe')
    Copy-Item -Force $ffmpeg.FullName (Join-Path $tools 'ffmpeg.exe')
    $ffprobe = Get-ChildItem (Join-Path $temp 'ffmpeg') -Filter ffprobe.exe -Recurse | Select-Object -First 1
    if ($ffprobe) { Copy-Item -Force $ffprobe.FullName (Join-Path $tools 'ffprobe.exe') }
    & (Join-Path $tools 'yt-dlp.exe') --version
    & (Join-Path $tools 'ffmpeg.exe') -version | Select-Object -First 1
    if ($LASTEXITCODE -ne 0) { throw 'Media tool validation failed.' }
}
finally {
    Remove-Item -Recurse -Force $temp -ErrorAction SilentlyContinue
}
