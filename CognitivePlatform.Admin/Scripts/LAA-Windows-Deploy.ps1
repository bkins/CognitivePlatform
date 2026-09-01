<#
.SYNOPSIS
Deploys a built Local AI Assistant Windows artifact to the local machine.

.DESCRIPTION
Copies the published Windows binaries from the staging path to the environment-specific
deploy directory. Stops any running instance of the app before copying, then backs up
the previous deployment. Mirrors the structure used by API-Deploy.ps1.

Deploy locations:
  Dev:  C:\CP\Deploy\LaaWindows\Dev
  QA:   C:\CP\Deploy\LaaWindows\QA
  Prod: C:\CP\Deploy\LaaWindows\Prod

.PARAMETER Environment
Target environment. Valid values: Dev, QA, Prod.

.PARAMETER Version
Version string matching the built artifact (used for logging and deployment state).

.PARAMETER ArtifactPath
Path to the directory containing the published binaries (output of LAA-Windows-Build.ps1).

.EXAMPLE
.\LAA-Windows-Deploy.ps1 -Environment Dev -Version 1.0.20260201.123045 -ArtifactPath "C:\CP\Deploy\LaaWindows"

.EXAMPLE
.\LAA-Windows-Deploy.ps1 -Environment QA -Version 1.0.20260201.140530 -ArtifactPath "C:\CP\Deploy\LaaWindows"
#>

[CmdletBinding()]
param (
    [Parameter(Mandatory)]
    [ValidateSet("Dev", "QA", "Prod")]
    [string]$Environment

  , [Parameter(Mandatory = $false)]
    [string]$Version = "1.0.0.1"

  , [Parameter(Mandatory)]
    [string]$ArtifactPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($Version)) { $Version = "1.0.0.1" }

$exeName = "LocalAIAssistant.Ui.Maui.exe"

$deployPathMap = @{
    "Dev"  = "C:\CP\Deploy\LaaWindows\Dev"
    "QA"   = "C:\CP\Deploy\LaaWindows\QA"
    "Prod" = "C:\CP\Deploy\LaaWindows\Prod"
}

Write-Host "========================================"
Write-Host "Deploying Local AI Assistant (Windows)"
Write-Host "========================================"
Write-Host "Environment: $Environment"
Write-Host "Version:     $Version"
Write-Host ""

if (-not (Test-Path $ArtifactPath)) {
    Write-Error "Artifact path not found: $ArtifactPath"
    exit 1
}

$exeInArtifacts = Join-Path $ArtifactPath $exeName
if (-not (Test-Path $exeInArtifacts)) {
    Write-Error "Executable not found in artifact path: $exeInArtifacts"
    exit 1
}

$deployPath = $deployPathMap[$Environment]
Write-Host "Deploy path: $deployPath"
Write-Host ""

# Stop any running instance to avoid file-lock errors during copy
$processName = [System.IO.Path]::GetFileNameWithoutExtension($exeName)
$running = Get-Process -Name $processName -ErrorAction SilentlyContinue |
           Where-Object { $_.Path -like "$deployPath*" }

if ($running) {
    Write-Host "Stopping running instance (PID $($running.Id))..."
    $running | Stop-Process -Force
    Start-Sleep -Milliseconds 800
    Write-Host "Process stopped."
    Write-Host ""
}

# Back up existing deployment
if (Test-Path $deployPath) {
    $timestamp  = Get-Date -Format "yyyyMMdd-HHmmss"
    $backupPath = "$deployPath.backup.$timestamp"
    Write-Host "Backing up existing deployment to: $backupPath"
    Rename-Item -Path $deployPath -NewName $backupPath
    Write-Host "Backup complete."
    Write-Host ""
}

# Create fresh deploy directory
New-Item -Path $deployPath -ItemType Directory -Force | Out-Null

# Copy artifact binaries
Write-Host "Copying binaries..."
Copy-Item "$ArtifactPath\*" $deployPath -Recurse -Force
Write-Host "Copy complete."
Write-Host ""

# Write deployment metadata alongside the binaries
$metadataPath = Join-Path $deployPath "deployment.json"
$metadata = [PSCustomObject]@{
    ComponentName = "LaaWindows"
    Environment   = $Environment
    Version       = $Version
    DeployedAt    = (Get-Date -Format "o")
    DeployedBy    = $env:USERNAME
}
$metadata | ConvertTo-Json | Set-Content $metadataPath -Encoding utf8

Write-Host "========================================"
Write-Host "Deployment Successful"
Write-Host "========================================"
Write-Host "Deployed: v$Version to $Environment"
Write-Host "Location: $deployPath"
Write-Host ""
Write-Host "To launch: & `"$deployPath\$exeName`""
Write-Host ""

exit 0
