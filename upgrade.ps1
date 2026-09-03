$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$UpgradeRevision = '2.18-runner-01'
$ExpectedVersion = '2.18'
$Branch = if ($env:YTSUBS_BRANCH) { $env:YTSUBS_BRANCH } else { 'devel' }
$Repo = if ($env:YTSUBS_REPO_DIR) { $env:YTSUBS_REPO_DIR } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
$Repo = [IO.Path]::GetFullPath($Repo).TrimEnd('\')
$LogDirectory = Join-Path $Repo 'logs'
$LogPath = Join-Path $LogDirectory 'upgrade.log'
$LegacyLogPath = Join-Path $Repo 'upgrade.log'
$Phase = 'SELF-UPDATE'
$HadWarning = $false
$WasRunning = $false

New-Item -ItemType Directory -Path $LogDirectory -Force | Out-Null
if (Test-Path -LiteralPath $LegacyLogPath) { Remove-Item -LiteralPath $LegacyLogPath -Force -ErrorAction SilentlyContinue }
Set-Content -LiteralPath $LogPath -Value '' -Encoding UTF8

function Write-UpgradeLine {
    param([string]$Text, [ConsoleColor]$Color = [ConsoleColor]::Gray)
    try { Write-Host $Text -ForegroundColor $Color } catch { Write-Host $Text }
    Add-Content -LiteralPath $LogPath -Value $Text -Encoding UTF8
}

function Set-Phase {
    param([string]$Name)
    $script:Phase = $Name
    Write-UpgradeLine ''
    Write-UpgradeLine ("=== {0} ===" -f $Name)
}

function Invoke-Native {
    param([Parameter(Mandatory=$true)][string]$File, [Parameter(Mandatory=$true)][string[]]$Arguments, [switch]$AllowFailure)
    $old = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & $File @Arguments 2>&1 | ForEach-Object { Write-UpgradeLine ([string]$_) }; $code = $LASTEXITCODE }
    finally { $ErrorActionPreference = $old }
    if ($code -ne 0 -and -not $AllowFailure) { throw ("Command failed with exit code {0}: {1} {2}" -f $code, $File, ($Arguments -join ' ')) }
    return $code
}

function Invoke-NativeCapture {
    param([Parameter(Mandatory=$true)][string]$File, [Parameter(Mandatory=$true)][string[]]$Arguments)
    $old = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { $output = @(& $File @Arguments 2>&1 | ForEach-Object { [string]$_ }); $code = $LASTEXITCODE }
    finally { $ErrorActionPreference = $old }
    return [pscustomobject]@{ ExitCode = $code; Output = $output }
}

function Resolve-DotNet {
    $command = Get-Command dotnet.exe -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    $candidate = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
    if (Test-Path -LiteralPath $candidate) { return $candidate }
    return $null
}

function Test-DotNet10 {
    param([string]$DotNet)
    if (-not $DotNet) { return $false }
    $result = Invoke-NativeCapture $DotNet @('--list-sdks')
    if ($result.ExitCode -ne 0) { return $false }
    return [bool]($result.Output | Where-Object { $_ -match '^10\.' } | Select-Object -First 1)
}

function Stop-RunningApplication {
    $target = Join-Path $Repo 'ytsubs.exe'
    if (-not (Test-Path -LiteralPath $target)) { return }
    $targetFull = [IO.Path]::GetFullPath($target)
    $matches = @()
    Get-Process -Name 'ytsubs' -ErrorAction SilentlyContinue | ForEach-Object {
        try { if ([string]::Equals([IO.Path]::GetFullPath($_.Path), $targetFull, [StringComparison]::OrdinalIgnoreCase)) { $matches += $_ } } catch { }
    }
    if ($matches.Count -eq 0) { return }
    $script:WasRunning = $true
    Write-UpgradeLine 'YouTubeSubs is running; requesting shutdown before deployment.'
    foreach ($process in $matches) { try { [void]$process.CloseMainWindow() } catch { } }
    $deadline = [DateTime]::UtcNow.AddSeconds(5)
    do {
        Start-Sleep -Milliseconds 200
        $alive = @($matches | Where-Object { try { -not $_.HasExited } catch { $false } })
    } while ($alive.Count -gt 0 -and [DateTime]::UtcNow -lt $deadline)
    if ($alive.Count -gt 0) {
        $script:HadWarning = $true
        Write-UpgradeLine 'WARNING: Graceful shutdown timed out; forcing YouTubeSubs to stop.' Yellow
        foreach ($process in $alive) { try { $process.Kill() } catch { } }
        Start-Sleep -Milliseconds 300
    }
}

function Get-PeSubsystem {
    param([string]$Path)
    $bytes = [IO.File]::ReadAllBytes($Path)
    $pe = [BitConverter]::ToInt32($bytes, 0x3c)
    return [BitConverter]::ToUInt16($bytes, $pe + 92)
}

function Repair-BootstrapChanges {
    param([string]$GitPath)
    $status = Invoke-NativeCapture $GitPath @('-C', $Repo, 'status', '--porcelain', '--untracked-files=no')
    if ($status.ExitCode -ne 0) { throw 'Unable to inspect tracked local changes.' }
    if ($status.Output.Count -eq 0) { return }
    $bootstrapChanged = @()
    foreach ($line in $status.Output) {
        if ($line.Length -lt 4) { continue }
        $path = $line.Substring(3).Trim().Trim('"')
        if (@('upgrade.cmd', 'upgrade.ps1') -contains $path) { $bootstrapChanged += $path }
    }
    if ($bootstrapChanged.Count -eq 0) { return }
    $bootstrapChanged = @($bootstrapChanged | Sort-Object -Unique)
    Write-UpgradeLine ("Bootstrap file change detected: {0}" -f ($bootstrapChanged -join ', ')) Yellow
    Write-UpgradeLine 'Restoring updater bootstrap files from the current repository commit before synchronization.'
    Invoke-Native $GitPath (@('-C', $Repo, 'checkout', 'HEAD', '--') + $bootstrapChanged)
}

try {
    Write-UpgradeLine '============================================================'
    Write-UpgradeLine 'YouTubeSubs upgrade diagnostic log'
    Write-UpgradeLine ("Upgrade revision: {0}" -f $UpgradeRevision)
    Write-UpgradeLine ("Started:          {0}" -f (Get-Date -Format 'dd.MM.yyyy HH:mm:ss.fff'))
    Write-UpgradeLine ("Repository:       {0}" -f $Repo)
    Write-UpgradeLine ("Branch:           {0}" -f $Branch)
    Write-UpgradeLine 'Runner:           temporary PowerShell runner; upgrade.cmd is launcher only'
    Write-UpgradeLine '============================================================'

    Set-Location -LiteralPath $Repo
    $gitCommand = Get-Command git.exe -ErrorAction SilentlyContinue
    if (-not $gitCommand) { throw 'Git was not found in PATH.' }
    $git = $gitCommand.Source

    Set-Phase 'REPOSITORY'
    $inside = Invoke-NativeCapture $git @('-C', $Repo, 'rev-parse', '--is-inside-work-tree')
    if ($inside.ExitCode -ne 0 -or -not ($inside.Output -contains 'true')) { throw 'The selected directory is not a Git working tree.' }
    $origin = Invoke-NativeCapture $git @('-C', $Repo, 'remote', 'get-url', 'origin')
    if ($origin.ExitCode -ne 0) { throw 'Git remote origin is missing.' }
    Write-UpgradeLine ("Origin: {0}" -f ($origin.Output | Select-Object -First 1))
    Repair-BootstrapChanges $git
    $status = Invoke-NativeCapture $git @('-C', $Repo, 'status', '--porcelain', '--untracked-files=no')
    if ($status.ExitCode -ne 0) { throw 'Unable to inspect tracked local changes.' }
    if ($status.Output.Count -gt 0) {
        Write-UpgradeLine 'Tracked local changes:' Yellow
        $status.Output | ForEach-Object { Write-UpgradeLine $_ Yellow }
        throw 'Tracked local changes detected outside updater bootstrap files. Commit, stash, or revert them before upgrading.'
    }

    Invoke-Native $git @('-C', $Repo, 'fetch', 'origin', $Branch)
    $local = Invoke-NativeCapture $git @('-C', $Repo, 'rev-parse', 'HEAD')
    $remote = Invoke-NativeCapture $git @('-C', $Repo, 'rev-parse', ("origin/{0}" -f $Branch))
    $base = Invoke-NativeCapture $git @('-C', $Repo, 'merge-base', 'HEAD', ("origin/{0}" -f $Branch))
    if ($local.ExitCode -ne 0 -or $remote.ExitCode -ne 0 -or $base.ExitCode -ne 0) { throw 'Unable to compare local and remote Git commits.' }
    if ($local.Output[0] -ne $remote.Output[0]) {
        if ($local.Output[0] -ne $base.Output[0]) { throw ("Local branch diverged from origin/{0}; automatic destructive reset is intentionally refused." -f $Branch) }
        Invoke-Native $git @('-C', $Repo, 'merge', '--ff-only', ("origin/{0}" -f $Branch))
    }
    $head = Invoke-NativeCapture $git @('-C', $Repo, 'rev-parse', 'HEAD')
    $remoteNow = Invoke-NativeCapture $git @('-C', $Repo, 'rev-parse', ("origin/{0}" -f $Branch))
    if ($head.ExitCode -ne 0 -or $remoteNow.ExitCode -ne 0 -or $head.Output[0] -ne $remoteNow.Output[0]) { throw 'Repository synchronization verification failed.' }
    Write-UpgradeLine ("Build commit: {0}" -f $head.Output[0])

    Set-Phase 'DEPENDENCIES'
    $dotnet = Resolve-DotNet
    if (-not (Test-DotNet10 $dotnet)) {
        Write-UpgradeLine 'Microsoft .NET 10 SDK is missing; installing the current stable SDK.' Yellow
        $wingetCommand = Get-Command winget.exe -ErrorAction SilentlyContinue
        if (-not $wingetCommand) { throw 'winget is unavailable, so .NET 10 SDK cannot be installed automatically.' }
        Invoke-Native $wingetCommand.Source @('install', '--id', 'Microsoft.DotNet.SDK.10', '--exact', '--silent', '--accept-package-agreements', '--accept-source-agreements')
        $dotnet = Resolve-DotNet
        if (-not (Test-DotNet10 $dotnet)) { throw '.NET 10 SDK installation completed but SDK 10.x is still unavailable.' }
    }
    $dotnetVersion = Invoke-NativeCapture $dotnet @('--version')
    Write-UpgradeLine ("Build SDK: .NET {0}" -f $dotnetVersion.Output[0])
    $toolsUpdate = Join-Path $Repo 'tools-update.ps1'
    if (-not (Test-Path -LiteralPath $toolsUpdate)) { throw 'tools-update.ps1 is missing.' }
    Invoke-Native (Join-Path $PSHOME 'powershell.exe') @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $toolsUpdate)
    foreach ($tool in @('tools\yt-dlp.exe', 'tools\ffmpeg.exe', 'tools\ffprobe.exe')) {
        if (-not (Test-Path -LiteralPath (Join-Path $Repo $tool))) { throw ("Required media tool is missing after update: {0}" -f $tool) }
    }

    Set-Phase 'MIGRATION'
    foreach ($legacy in @('__pycache__', '.venv', '.pytest_cache')) {
        $path = Join-Path $Repo $legacy
        if (Test-Path -LiteralPath $path) { Write-UpgradeLine ("Removing obsolete Python artifact: {0}" -f $legacy); Remove-Item -LiteralPath $path -Recurse -Force }
    }

    Set-Phase 'CONFIGURATION'
    $encodedIcon = Join-Path $Repo 'assets\ytsubs.ico.b64'
    $icon = Join-Path $Repo 'assets\ytsubs.ico'
    if (-not (Test-Path -LiteralPath $encodedIcon)) { throw 'Missing encoded Windows icon asset.' }
    $raw = [Convert]::FromBase64String((Get-Content -LiteralPath $encodedIcon -Raw))
    if ($raw.Length -lt 22 -or $raw[0] -ne 0 -or $raw[1] -ne 0 -or $raw[2] -ne 1 -or $raw[3] -ne 0) { throw 'Encoded icon validation failed.' }
    [IO.File]::WriteAllBytes($icon, $raw)

    Set-Phase 'BUILD'
    foreach ($relative in @('obj', 'bin', 'cli\obj', 'cli\bin', 'build\publish-gui', 'build\publish-cli')) {
        $path = Join-Path $Repo $relative
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
    }
    Invoke-Native $dotnet @('restore', (Join-Path $Repo 'YouTubeSubs.csproj'))
    Invoke-Native $dotnet @('restore', (Join-Path $Repo 'cli\YouTubeSubs.Cli.csproj'))
    Invoke-Native $dotnet @('build', (Join-Path $Repo 'YouTubeSubs.csproj'), '-c', 'Release', '--no-restore')
    Invoke-Native $dotnet @('build', (Join-Path $Repo 'cli\YouTubeSubs.Cli.csproj'), '-c', 'Release', '--no-restore')

    Set-Phase 'DIST'
    $guiDir = Join-Path $Repo 'build\publish-gui'
    $cliDir = Join-Path $Repo 'build\publish-cli'
    Invoke-Native $dotnet @('publish', (Join-Path $Repo 'YouTubeSubs.csproj'), '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '--no-restore', '-p:PublishSingleFile=true', '-p:PublishTrimmed=false', '-o', $guiDir)
    Invoke-Native $dotnet @('publish', (Join-Path $Repo 'cli\YouTubeSubs.Cli.csproj'), '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '--no-restore', '-p:PublishSingleFile=true', '-p:PublishTrimmed=false', '-o', $cliDir)
    $guiCandidate = Join-Path $guiDir 'ytsubs.exe'
    $cliCandidate = Join-Path $cliDir 'ytsubs-cli.exe'
    if (-not (Test-Path -LiteralPath $guiCandidate) -or -not (Test-Path -LiteralPath $cliCandidate)) { throw 'Published GUI or CLI candidate is missing.' }
    $guiSubsystem = Get-PeSubsystem $guiCandidate
    $cliSubsystem = Get-PeSubsystem $cliCandidate
    Write-UpgradeLine ("GUI subsystem: {0}" -f $guiSubsystem)
    Write-UpgradeLine ("CLI subsystem: {0}" -f $cliSubsystem)
    if ($guiSubsystem -ne 2) { throw 'GUI candidate does not use Windows GUI subsystem 2.' }
    if ($cliSubsystem -ne 3) { throw 'CLI candidate does not use Windows console subsystem 3.' }
    $expectedOutput = 'ytsubs-cli ' + $ExpectedVersion
    $versionCheck = Invoke-NativeCapture $cliCandidate @('--version')
    if ($versionCheck.ExitCode -ne 0 -or $versionCheck.Output.Count -eq 0 -or $versionCheck.Output[0] -ne $expectedOutput) { throw 'CLI candidate version mismatch.' }
    Write-UpgradeLine ("Version validation: {0}" -f $versionCheck.Output[0])

    Set-Phase 'STOP-RUNTIME'
    Stop-RunningApplication

    Set-Phase 'DEPLOY'
    Copy-Item -LiteralPath $guiCandidate -Destination (Join-Path $Repo 'ytsubs.exe') -Force
    Copy-Item -LiteralPath $cliCandidate -Destination (Join-Path $Repo 'ytsubs-cli.exe') -Force
    $installed = Invoke-NativeCapture (Join-Path $Repo 'ytsubs-cli.exe') @('--version')
    if ($installed.ExitCode -ne 0 -or $installed.Output.Count -eq 0 -or $installed.Output[0] -ne $expectedOutput) { throw 'Installed CLI validation failed.' }
    Write-UpgradeLine ("Installed validation: {0}" -f $installed.Output[0])

    Set-Phase 'RESTART'
    if ($WasRunning) { Start-Process -FilePath (Join-Path $Repo 'ytsubs.exe') -WorkingDirectory $Repo | Out-Null }

    Set-Phase 'COMPLETE'
    if ($HadWarning) { Write-UpgradeLine ("STATUS: WARNING - YouTubeSubs {0}" -f $ExpectedVersion) Yellow }
    else { Write-UpgradeLine ("STATUS: SUCCESS - YouTubeSubs {0}" -f $ExpectedVersion) Green }
    exit 0
}
catch {
    Write-UpgradeLine ("ERROR: {0}" -f $_.Exception.Message) Red
    Write-UpgradeLine ("STATUS: FAILED - YouTubeSubs {0} - phase={1}" -f $ExpectedVersion, $Phase) Red
    exit 1
}
