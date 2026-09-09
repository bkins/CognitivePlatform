<#
.SYNOPSIS
Restores the most recent dev database backup created by Swap-ProdDbToDev.ps1.

.DESCRIPTION
Finds the newest timestamped backup file in C:\CP\Data\Development\
(pattern: platform.dev.backup-yyyyMMdd-HHmmss.db) and copies it back over
the current development platform.db.

Run this when you are done testing with prod data and want your dev data back.

.PARAMETER DryRun
Show what would happen without making any changes.

.PARAMETER BackupPath
Path to a specific backup file to restore. If omitted, the most recent backup
(by filename sort, which matches chronological order) is used.

.EXAMPLE
  .\Restore-DevDb.ps1
  .\Restore-DevDb.ps1 -DryRun
  .\Restore-DevDb.ps1 -BackupPath "C:\CP\Data\Development\platform.dev.backup-20260512-143022.db"
#>

[CmdletBinding()]
param(
    [switch]$DryRun
  , [string]$BackupPath = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ---------------------------------------------------------------------------
# Paths
# ---------------------------------------------------------------------------
$devDataDir = "C:\CP\Data\Development"
$devDbPath  = "$devDataDir\platform.db"

# ---------------------------------------------------------------------------
# Find the backup to restore
# ---------------------------------------------------------------------------
if ($BackupPath -ne "") {
    if (-not (Test-Path $BackupPath)) {
        Write-Error "Specified backup not found: $BackupPath"
        exit 1
    }
    $resolvedBackup = $BackupPath
} else {
    $backups = Get-ChildItem -Path $devDataDir -Filter "platform.dev.backup-*.db" `
                             -ErrorAction SilentlyContinue `
               | Sort-Object Name -Descending

    if ($null -eq $backups -or @($backups).Count -eq 0) {
        Write-Error @"
No backup files found in $devDataDir
Pattern expected: platform.dev.backup-yyyyMMdd-HHmmss.db

Run Swap-ProdDbToDev.ps1 first -- it creates a backup before overwriting the dev DB.
"@
        exit 1
    }

    $backupsArray   = @($backups)
    $resolvedBackup = $backupsArray[0].FullName

    if ($backupsArray.Count -gt 1) {
        Write-Host "Found $($backupsArray.Count) backup(s). Using most recent:"
        $backupsArray | Select-Object -First 5 | ForEach-Object {
            $marker = if ($_.FullName -eq $resolvedBackup) { " <- restoring this one" } else { "" }
            Write-Host "  $($_.Name)$marker"
        }
        if ($backupsArray.Count -gt 5) {
            Write-Host "  ... ($($backupsArray.Count - 5) older backups not shown)"
        }
    }
}

# ---------------------------------------------------------------------------
# Banner
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "========================================"
if ($DryRun) { Write-Host "DRY RUN -- no changes will be made" }
else         { Write-Host "Restore-DevDb" }
Write-Host "========================================"
Write-Host "Restore from: $resolvedBackup"
Write-Host "Restore to:   $devDbPath"
Write-Host ""

# ---------------------------------------------------------------------------
# Guard: stop if the dev API is running
# ---------------------------------------------------------------------------
$apiProcess = Get-Process -Name "CognitivePlatform.Api" -ErrorAction SilentlyContinue
if ($null -ne $apiProcess) {
    Write-Error @"
CognitivePlatform.Api is running (PID $($apiProcess.Id)).
Stop the API before restoring the database.
  In the API console window: Ctrl+C
  Or: Stop-Process -Id $($apiProcess.Id)
"@
    exit 1
}

# ---------------------------------------------------------------------------
# DRY RUN: print plan and exit
# ---------------------------------------------------------------------------
if ($DryRun) {
    Write-Host "[DRY RUN] Would restore:"
    Write-Host "  '$resolvedBackup' -> '$devDbPath'"
    Write-Host "(No changes made.)"
    exit 0
}

# ---------------------------------------------------------------------------
# Restore
# ---------------------------------------------------------------------------
Write-Host "Restoring database..."

try {
    Copy-Item -Path $resolvedBackup -Destination $devDbPath -Force
} catch {
    Write-Error "Restore failed: $_"
    exit 1
}

# Restore WAL/SHM sidecar files if the backup has them
foreach ($ext in @("-wal", "-shm")) {
    $sidecar = "$resolvedBackup$ext"
    if (Test-Path $sidecar) {
        Copy-Item -Path $sidecar -Destination "$devDbPath$ext" -Force
        Write-Host "  Sidecar restored: $devDbPath$ext"
    } else {
        # Remove any stale sidecar left from the prod copy
        $stale = "$devDbPath$ext"
        if (Test-Path $stale) {
            Remove-Item -Path $stale -Force
            Write-Host "  Stale sidecar removed: $stale"
        }
    }
}

$restoredSize = (Get-Item $devDbPath).Length
Write-Host ("  Restored: $resolvedBackup -> $devDbPath (" + $restoredSize + " bytes)")

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "========================================"
Write-Host "Restore complete"
Write-Host "========================================"
Write-Host ""
Write-Host "NEXT STEP: Restart the dev API"
Write-Host "  cd C:\CP\Deploy\API\Dev"
Write-Host "  .\start-api.ps1"
Write-Host ""

exit 0
