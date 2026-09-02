# YouTubeSubs Upgrade Notes

YouTubeSubs follows the shared Windows upgrade protocol maintained in `Suenee/FolderHeatMap` (`UPGRADE.md`).

## Architecture

- `upgrade.cmd` is a small bootstrap launcher only.
- The launcher always fetches the current `upgrade.ps1` from `origin/devel` into a temporary file and executes that copy.
- `upgrade.ps1` is authoritative for repository synchronization, dependencies, local migration/cleanup, build, validation, deployment, restart, diagnostics, and final status.
- `upgrade.log` is generated in the repository root in single-run mode and is ignored by Git.
- Local modifications to the known updater bootstrap files `upgrade.cmd` and `upgrade.ps1` are automatically restored from the current local `HEAD` before repository synchronization, so updater self-changes cannot trap a machine in a permanent dirty-worktree failure. Other tracked local changes are never discarded automatically and still stop the upgrade.

## Network repositories

Mapped drives and UNC repositories are supported. If Git reports `dubious ownership`, the bootstrap registers only the exact current repository path as `safe.directory` and retries. Wildcard trust (`safe.directory=*`) is intentionally not used.

## Runtime data

Application configuration and application logs live under `%LOCALAPPDATA%\YouTubeSubs` and are not part of the repository deployment. Upgrade cleanup removes only known obsolete generated Python artifacts and current build intermediates; it does not run broad `git clean` commands.

## Build and deployment

The required build environment is the current stable .NET 10 SDK. The upgrader installs it through `winget` when necessary. Current stable yt-dlp and FFmpeg builds are refreshed through `tools-update.ps1`.

GUI and CLI candidates are built and published into isolated staging directories. Deployment occurs only after both candidates exist, the GUI PE subsystem is 2, the CLI PE subsystem is 3, and `ytsubs-cli --version` reports the expected application version.

If the installed GUI was running before deployment, the upgrader requests shutdown, waits for a bounded grace period, force-stops only when required (with a warning), deploys the validated artifacts, and restarts the GUI after success.

## Status contract

Every completed runner execution writes one of these final status lines to `upgrade.log`:

```text
STATUS: SUCCESS - phase=COMPLETE
STATUS: WARNING - phase=COMPLETE
STATUS: FAILED - phase=<PHASE>
```

A failed phase returns a non-zero process exit code and does not intentionally deploy unvalidated candidates.
