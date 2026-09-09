<#
.SYNOPSIS
Copies the production SQLite database into the development environment
and fixes workspace scoping so all prod data is immediately visible.

.DESCRIPTION
The CognitivePlatform API isolates data by workspace using a PartitionKey column
in the Objects table. After a raw DB copy, the UserSettings record (which stores
the active workspace name) may reference a workspace that does not match what the
dev server expects, causing queries to return zero rows.

The fix: reset the UserSettings record to workspace "personal". Because "personal"
maps to a NULL partition key, the IObjectStore.List<T>() SQL filter:
  ($partitionKey IS NULL OR PartitionKey = $partitionKey)
evaluates to TRUE for every row when partitionKey is NULL, making ALL prod data
visible in dev regardless of which workspace it was written to.

Paths are derived from BuildDataPersistenceLayer in Program.cs:
  C:\CP\Data\{ASPNETCORE_ENVIRONMENT}\platform.db
where ASPNETCORE_ENVIRONMENT = "Prod" (production) and "Development" (dev).

FUTURE ALTERNATIVE: A dev-only admin endpoint or a config flag in
appsettings.Development.json (e.g. "Workspace.ForceOnStartup": "personal")
could reset the active workspace automatically at API startup, removing the
need for this script.

RESTORE: Run Restore-DevDb.ps1 to swap the timestamped backup back in.

.PARAMETER DryRun
Show what would happen without making any changes.

.EXAMPLE
  .\Swap-ProdDbToDev.ps1
  .\Swap-ProdDbToDev.ps1 -DryRun
#>

[CmdletBinding()]
param(
    [switch]$DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ---------------------------------------------------------------------------
# Paths -- derived from BuildDataPersistenceLayer in Program.cs
# ---------------------------------------------------------------------------
$prodDbPath = "C:\CP\Data\Prod\platform.db"
$devDataDir = "C:\CP\Data\Development"
$devDbPath  = "$devDataDir\platform.db"
$timestamp  = Get-Date -Format 'yyyyMMdd-HHmmss'
$backupPath = "$devDataDir\platform.dev.backup-$timestamp.db"

# ---------------------------------------------------------------------------
# Target workspace -- "personal" maps to NULL partition key in IObjectStore,
# which makes ALL rows visible regardless of their stored PartitionKey value.
# If a future "Workspace.ForceOnStartup" key is added to appsettings.Development.json,
# read it here instead of hard-coding.
# ---------------------------------------------------------------------------
$devWorkspace = "personal"

# JSON that UserSettings.cs serialises to (camelCase, JsonNamingPolicy.CamelCase).
# Double each backtick pair so the embedded string survives PowerShell expansion.
$userSettingsJson = '{"id":"user-settings","activeWorkspace":"personal","knownWorkspaces":["personal"]}'

# ---------------------------------------------------------------------------
# Locate sqlite3.exe
# ---------------------------------------------------------------------------
$sqlite3Exe = $null

$sqlite3Candidate = Get-Command sqlite3 -ErrorAction SilentlyContinue
if ($null -ne $sqlite3Candidate) {
    $sqlite3Exe = $sqlite3Candidate.Source
}

if ($null -eq $sqlite3Exe) {
    $knownLocations = @(
        "$env:LOCALAPPDATA\Android\Sdk\platform-tools\sqlite3.exe"
        "C:\sqlite\sqlite3.exe"
        "C:\tools\sqlite3.exe"
    )
    foreach ($loc in $knownLocations) {
        if (Test-Path $loc) { $sqlite3Exe = $loc; break }
    }
}

if ($null -eq $sqlite3Exe) {
    Write-Error @"
sqlite3.exe not found.

Options:
  1. Install Android SDK platform-tools (already present on this machine via Android Studio)
  2. Download sqlite3.exe from https://sqlite.org/download.html and place at C:\sqlite\
  3. Add sqlite3.exe to your PATH
"@
    exit 1
}

Write-Host "sqlite3:  $sqlite3Exe"

# ---------------------------------------------------------------------------
# Banner
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "========================================"
if ($DryRun) { Write-Host "DRY RUN -- no changes will be made" }
else         { Write-Host "Swap-ProdDbToDev" }
Write-Host "========================================"
Write-Host "Prod DB:  $prodDbPath"
Write-Host "Dev DB:   $devDbPath"
Write-Host "Backup:   $backupPath"
Write-Host "Workspace fix: reset UserSettings to '$devWorkspace' (null partition key = all rows visible)"
Write-Host ""

# ---------------------------------------------------------------------------
# Guard: prod DB must exist before touching anything
# ---------------------------------------------------------------------------
if (-not (Test-Path $prodDbPath)) {
    Write-Error "Production database not found at: $prodDbPath`nAborting -- dev database was not touched."
    exit 1
}

# ---------------------------------------------------------------------------
# DRY RUN: print plan and exit (process guard skipped -- no changes made)
# ---------------------------------------------------------------------------
if ($DryRun) {
    Write-Host "[DRY RUN] Would perform these steps:"
    Write-Host "  1. Backup  : Copy '$devDbPath' -> '$backupPath'"
    Write-Host "  2. Copy    : Copy '$prodDbPath' -> '$devDbPath' (overwrite)"
    Write-Host "  3. Fix     : UPDATE Objects SET Json = '$userSettingsJson'"
    Write-Host "               WHERE Id = 'user-settings'"
    Write-Host "(No changes made.)"
    exit 0
}

# ---------------------------------------------------------------------------
# Guard: stop if the dev API is running (risk of inconsistent copy)
# ---------------------------------------------------------------------------
$apiProcess = Get-Process -Name "CognitivePlatform.Api" -ErrorAction SilentlyContinue
if ($null -ne $apiProcess) {
    Write-Error @"
CognitivePlatform.Api is running (PID $($apiProcess.Id)).
Stop the API before swapping the database to avoid an inconsistent copy.
  In the API console window: Ctrl+C
  Or: Stop-Process -Id $($apiProcess.Id)
"@
    exit 1
}

# ---------------------------------------------------------------------------
# Step 1: Backup the current dev database
# ---------------------------------------------------------------------------
Write-Host "Step 1: Backing up dev database..."

if (-not (Test-Path $devDbPath)) {
    Write-Host "  Dev database does not exist yet -- no backup needed."
} else {
    try {
        Copy-Item -Path $devDbPath -Destination $backupPath -Force
    } catch {
        Write-Error "Backup failed: $_`nAborting -- dev database was not touched."
        exit 1
    }

    if (-not (Test-Path $backupPath)) {
        Write-Error "Backup file was not created at '$backupPath'.`nAborting -- dev database was not touched."
        exit 1
    }

    $backupSize = (Get-Item $backupPath).Length
    Write-Host "  Backup created: $backupPath ($backupSize bytes)"

    # Also copy WAL/SHM sidecar files if they exist
    foreach ($ext in @("-wal", "-shm")) {
        $sidecar = "$devDbPath$ext"
        if (Test-Path $sidecar) {
            Copy-Item -Path $sidecar -Destination "$backupPath$ext" -Force
            Write-Host "  Sidecar backed up: $backupPath$ext"
        }
    }
}

# ---------------------------------------------------------------------------
# Step 2: Copy prod DB to dev (overwrite)
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "Step 2: Copying production database to dev..."

try {
    Copy-Item -Path $prodDbPath -Destination $devDbPath -Force
} catch {
    Write-Error "Copy failed: $_"
    exit 1
}

# Copy WAL/SHM sidecars if they exist alongside prod DB
foreach ($ext in @("-wal", "-shm")) {
    $sidecar = "$prodDbPath$ext"
    if (Test-Path $sidecar) {
        Copy-Item -Path $sidecar -Destination "$devDbPath$ext" -Force
        Write-Host "  Sidecar copied: $devDbPath$ext"
    }
}

$copiedSize = (Get-Item $devDbPath).Length
Write-Host ("  Copied: $prodDbPath -> $devDbPath (" + $copiedSize + " bytes)")

# ---------------------------------------------------------------------------
# Step 3: Fix workspace -- reset UserSettings to 'personal' workspace
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "Step 3: Fixing workspace in dev database..."

# Count non-personal rows so we can report that they are now accessible
$countSql = "SELECT COUNT(*) FROM Objects WHERE PartitionKey IS NOT NULL AND DeletedUtc IS NULL;"
$nonPersonalRows = ($countSql | & $sqlite3Exe $devDbPath 2>&1).Trim()

# sqlite3 uses single quotes for string literals; the JSON already uses double quotes so no escaping needed.
$fixSql = @"
UPDATE Objects
SET    Json       = '$userSettingsJson'
     , UpdatedUtc = datetime('now')
WHERE  Id         = 'user-settings';
SELECT changes();
"@

$fixResult = ($fixSql | & $sqlite3Exe $devDbPath 2>&1)

if ($LASTEXITCODE -ne 0) {
    Write-Warning "Workspace fix reported an error: $fixResult"
    Write-Warning "The DB was copied successfully but UserSettings may not have been reset."
    Write-Warning "You may need to switch workspace manually in the API after startup."
} else {
    $rowsUpdated = ($fixResult | Select-Object -Last 1).Trim()
    if ($rowsUpdated -eq "1") {
        Write-Host "  UserSettings reset: activeWorkspace = '$devWorkspace'"
    } else {
        Write-Host "  UserSettings record not found -- a fresh default will be created on API startup."
        Write-Host "  (Default workspace is '$devWorkspace', which is correct.)"
    }
}

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "========================================"
Write-Host "Swap complete"
Write-Host "========================================"
Write-Host ""
Write-Host "Backup:    $backupPath"

if ($null -ne $nonPersonalRows -and $nonPersonalRows -match '^\d+$' -and [int]$nonPersonalRows -gt 0) {
    Write-Host ""
    Write-Host "Note: $nonPersonalRows row(s) in prod had a non-null PartitionKey."
    Write-Host "      With workspace reset to '$devWorkspace' (null partition key),"
    Write-Host "      all rows are now visible via the IObjectStore null-key filter."
}

Write-Host ""
Write-Host "NEXT STEP: Restart the dev API"
Write-Host "  cd C:\CP\Deploy\API\Dev"
Write-Host "  .\start-api.ps1"
Write-Host ""
Write-Host "To restore your original dev database:"
Write-Host "  .\Restore-DevDb.ps1"
Write-Host ""

exit 0
