$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$UpgradeRevision = '2.30-runner-01'
$ExpectedVersion = '2.30'
$Branch = if ($env:YTSUBS_BRANCH) { $env:YTSUBS_BRANCH } else { 'devel' }
$Repo = if ($env:YTSUBS_REPO_DIR) { $env:YTSUBS_REPO_DIR } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
$Repo = [IO.Path]::GetFullPath($Repo).TrimEnd('\')

$script:Phase = 'BOOTSTRAP'; $script:Log = $null; $script:AppVersion = $ExpectedVersion; $script:AppWasRunning = $false; $script:AppWasRunningAtStart = $false; $script:RestartedApp = $false
function Write-UpgradeLine { param([string]$Text, [ConsoleColor]$Color = [ConsoleColor]::Gray) try { Write-Host $Text -ForegroundColor $Color } catch { Write-Output $Text }; if ($script:Log) { try { Add-Content -LiteralPath $script:Log -Value $Text -Encoding UTF8 } catch {} } }
function Invoke-NativeCapture { param([string]$File,[string[]]$Arguments) $psi = [Diagnostics.ProcessStartInfo]::new(); $psi.FileName=$File; $psi.UseShellExecute=$false; $psi.RedirectStandardOutput=$true; $psi.RedirectStandardError=$true; foreach($a in $Arguments){[void]$psi.ArgumentList.Add($a)}; $p=[Diagnostics.Process]::Start($psi); $stdout=$p.StandardOutput.ReadToEnd(); $stderr=$p.StandardError.ReadToEnd(); $p.WaitForExit(); [pscustomobject]@{ExitCode=$p.ExitCode;Output=@($stdout -split "`r?`n" | Where-Object {$_ -ne ''});Error=@($stderr -split "`r?`n" | Where-Object {$_ -ne ''})} }
function Invoke-Native { param([string]$File,[string[]]$Arguments) $r=Invoke-NativeCapture $File $Arguments; $r.Output|ForEach-Object{Write-UpgradeLine $_}; $r.Error|ForEach-Object{Write-UpgradeLine $_ Yellow}; if($r.ExitCode-ne 0){throw "Command failed with exit code $($r.ExitCode): $File $($Arguments -join ' ')"}; $r }
function Resolve-CommandPath { param([string]$Name) $c=Get-Command $Name -ErrorAction SilentlyContinue; if($c){return $c.Source}; return $null }
function Ensure-SafeDirectory { param([string]$Git) $probe=Invoke-NativeCapture $Git @('-C',$Repo,'rev-parse','--show-toplevel'); if($probe.ExitCode-ne 0 -and (($probe.Error -join "`n") -match 'dubious ownership')){[void](Invoke-Native $Git @('config','--global','--add','safe.directory',$Repo))} }
function Stop-App { $ps=@(Get-Process ytsubs -ErrorAction SilentlyContinue); if($ps.Count-eq 0){return}; $script:AppWasRunning=$true; foreach($p in $ps){try{$null=$p.CloseMainWindow()}catch{}}; Start-Sleep -Milliseconds 1200; @(Get-Process ytsubs -ErrorAction SilentlyContinue)|ForEach-Object{try{$_.Kill($true);$_.WaitForExit(3000)}catch{}} }
function Restart-App { if(-not $script:AppWasRunningAtStart -or $script:RestartedApp){return}; $exe=Join-Path $Repo 'ytsubs.exe'; if(Test-Path $exe){Start-Process -FilePath $exe -WorkingDirectory $Repo; $script:RestartedApp=$true; Write-UpgradeLine 'Application restarted.' Green} }

try {
    [Console]::OutputEncoding=[Text.UTF8Encoding]::new($false); $OutputEncoding=[Console]::OutputEncoding
    $logs=Join-Path $Repo 'logs'; New-Item -ItemType Directory -Force -Path $logs|Out-Null; $script:Log=Join-Path $logs 'upgrade.log'; Set-Content -LiteralPath $script:Log -Value '' -Encoding UTF8
    Write-UpgradeLine '============================================================' DarkGray; Write-UpgradeLine 'YouTubeSubs upgrade diagnostic log' Cyan; Write-UpgradeLine ("Upgrade revision: {0}" -f $UpgradeRevision); Write-UpgradeLine ("Started:          {0:dd.MM.yyyy HH:mm:ss.fff}" -f (Get-Date)); Write-UpgradeLine ("Repository:       {0}" -f $Repo); Write-UpgradeLine ("Branch:           {0}" -f $Branch); Write-UpgradeLine '============================================================' DarkGray
    $git=Resolve-CommandPath 'git.exe'; if(-not $git){throw 'Git not found.'}; Ensure-SafeDirectory $git
    $script:Phase='REPOSITORY'; Write-UpgradeLine ''; Write-UpgradeLine '=== REPOSITORY ===' Cyan
    $status=Invoke-NativeCapture $git @('-C',$Repo,'status','--porcelain','--untracked-files=no'); if($status.ExitCode-ne 0){throw 'Unable to inspect tracked local changes.'}; if($status.Output.Count-gt 0){Write-UpgradeLine 'Tracked local changes:' Yellow; $status.Output|ForEach-Object{Write-UpgradeLine $_ Yellow}; throw 'Tracked local changes detected. Commit, stash, or revert them before upgrading.'}
    Invoke-Native $git @('-C',$Repo,'fetch','origin',$Branch)|Out-Null; $local=(Invoke-NativeCapture $git @('-C',$Repo,'rev-parse','HEAD')).Output[0]; $remote=(Invoke-NativeCapture $git @('-C',$Repo,'rev-parse',("origin/{0}" -f $Branch))).Output[0]; $base=(Invoke-NativeCapture $git @('-C',$Repo,'merge-base','HEAD',("origin/{0}" -f $Branch))).Output[0]; if($local-ne$remote){if($local-ne$base){throw 'Local branch diverged; automatic destructive reset refused.'}; Invoke-Native $git @('-C',$Repo,'merge','--ff-only',("origin/{0}" -f $Branch))|Out-Null}
    $script:AppWasRunningAtStart=@(Get-Process ytsubs -ErrorAction SilentlyContinue).Count-gt 0; Stop-App
    $dotnet=Resolve-CommandPath 'dotnet.exe'; if(-not $dotnet){throw '.NET SDK not found.'}
    $script:Phase='BUILD'; Write-UpgradeLine ''; Write-UpgradeLine '=== BUILD ===' Cyan
    Invoke-Native $dotnet @('restore',(Join-Path $Repo 'YouTubeSubs.csproj'))|Out-Null; Invoke-Native $dotnet @('build',(Join-Path $Repo 'YouTubeSubs.csproj'),'-c','Release','--no-restore')|Out-Null; Invoke-Native $dotnet @('restore',(Join-Path $Repo 'cli\YouTubeSubs.Cli.csproj'))|Out-Null; Invoke-Native $dotnet @('build',(Join-Path $Repo 'cli\YouTubeSubs.Cli.csproj'),'-c','Release','--no-restore')|Out-Null
    $script:Phase='PUBLISH'; Write-UpgradeLine ''; Write-UpgradeLine '=== PUBLISH ===' Cyan; $pub=Join-Path $env:TEMP ('ytsubs-publish-'+[guid]::NewGuid().ToString('N')); New-Item -ItemType Directory -Force -Path $pub|Out-Null
    $guiPub=Join-Path $pub 'gui'; $cliPub=Join-Path $pub 'cli'; Invoke-Native $dotnet @('publish',(Join-Path $Repo 'YouTubeSubs.csproj'),'-c','Release','-r','win-x64','--self-contained','true','-p:PublishSingleFile=true','-o',$guiPub)|Out-Null; Invoke-Native $dotnet @('publish',(Join-Path $Repo 'cli\YouTubeSubs.Cli.csproj'),'-c','Release','-r','win-x64','--self-contained','true','-p:PublishSingleFile=true','-o',$cliPub)|Out-Null
    Copy-Item -LiteralPath (Join-Path $guiPub 'ytsubs.exe') -Destination (Join-Path $Repo 'ytsubs.exe') -Force; Copy-Item -LiteralPath (Join-Path $cliPub 'ytsubs-cli.exe') -Destination (Join-Path $Repo 'ytsubs-cli.exe') -Force
    $script:Phase='VALIDATE'; Write-UpgradeLine ''; Write-UpgradeLine '=== VALIDATE ===' Cyan; $v=Invoke-NativeCapture (Join-Path $Repo 'ytsubs-cli.exe') @('--version'); $actual=($v.Output|Select-Object -First 1); Write-UpgradeLine ("CLI: {0}" -f $actual); if($actual-ne("ytsubs-cli {0}" -f $ExpectedVersion)){throw "Installed CLI version mismatch."}
    Remove-Item -LiteralPath $pub -Recurse -Force -ErrorAction SilentlyContinue; Restart-App; Write-UpgradeLine ("STATUS: SUCCESS - YouTubeSubs {0}" -f $ExpectedVersion) Green; exit 0
}
catch { Write-UpgradeLine ("ERROR: {0}" -f $_.Exception.Message) Red; Write-UpgradeLine ("STATUS: FAILED - YouTubeSubs {0} - phase={1}" -f $ExpectedVersion,$script:Phase) Red; Restart-App; exit 1 }
