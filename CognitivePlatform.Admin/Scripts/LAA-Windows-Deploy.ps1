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

$deployPath = [System.IO.Path]::GetFullPath($deployPathMap[$Environment])
$allowedDeployPaths = @($deployPathMap.Values | ForEach-Object { [System.IO.Path]::GetFullPath($_) })
if ($allowedDeployPaths -notcontains $deployPath) {
    throw "Refusing deployment to an unexpected target path: $deployPath"
}

if (-not (Test-Path -LiteralPath $ArtifactPath -PathType Container)) {
    throw "Artifact path not found: $ArtifactPath"
}

$artifactPathResolved = [System.IO.Path]::GetFullPath((Resolve-Path -LiteralPath $ArtifactPath).Path)
$exeInArtifacts = Join-Path $artifactPathResolved $exeName
if (-not (Test-Path -LiteralPath $exeInArtifacts -PathType Leaf)) {
    throw "Executable not found in artifact path: $exeInArtifacts"
}

$artifactSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $exeInArtifacts).Hash
$timestamp = Get-Date -Format "yyyyMMdd-HHmmssfff"
$backupPath = "$deployPath.backup.$timestamp"
$stagingPath = "$deployPath.staging.$([Guid]::NewGuid().ToString('N'))"
$failedPath = "$deployPath.failed.$timestamp"
$backupCreated = $false
$deployPathWithSeparator = $deployPath.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar

Write-Host "Deploy path: $deployPath"
Write-Host "SHA-256:    $artifactSha256"
Write-Host ""

try {
    Write-Host "Staging artifact in: $stagingPath"
    New-Item -Path $stagingPath -ItemType Directory | Out-Null
    Get-ChildItem -LiteralPath $artifactPathResolved -Force |
        Copy-Item -Destination $stagingPath -Recurse -Force

    $stagedExe = Join-Path $stagingPath $exeName
    $stagedSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $stagedExe).Hash
    if ($stagedSha256 -ne $artifactSha256) {
        throw "Staged executable hash mismatch. Expected $artifactSha256 but found $stagedSha256."
    }

    $stagedSettings = Join-Path $stagingPath 'appsettings.json'
    if (Test-Path -LiteralPath $stagedSettings -PathType Leaf) {
        Get-Content -LiteralPath $stagedSettings -Raw -Encoding UTF8 | ConvertFrom-Json | Out-Null
    }

    $metadataPath = Join-Path $stagingPath "deployment.json"
    [PSCustomObject]@{
        ComponentName = "LaaWindows"
        Environment   = $Environment
        Version       = $Version
        DeployedAt    = (Get-Date -Format "o")
        DeployedBy    = $env:USERNAME
        ArtifactSha256 = $artifactSha256
    } | ConvertTo-Json | Set-Content -LiteralPath $metadataPath -Encoding utf8

    $processName = [System.IO.Path]::GetFileNameWithoutExtension($exeName)
    $running = Get-Process -Name $processName -ErrorAction SilentlyContinue | Where-Object {
        try {
            $processPath = [System.IO.Path]::GetFullPath($_.Path)
            $processPath.StartsWith($deployPathWithSeparator, [System.StringComparison]::OrdinalIgnoreCase)
        }
        catch { $false }
    }

    foreach ($process in @($running)) {
        Write-Host "Stopping target LAA process (PID $($process.Id))..."
        if ($process.CloseMainWindow()) {
            $null = $process.WaitForExit(5000)
        }
        if (-not $process.HasExited) {
            $process.Kill()
            $null = $process.WaitForExit(5000)
        }
    }

    if (Test-Path -LiteralPath $deployPath -PathType Container) {
        Write-Host "Moving previous deployment to: $backupPath"
        Move-Item -LiteralPath $deployPath -Destination $backupPath
        $backupCreated = $true
    }

    Write-Host "Activating staged deployment..."
    Move-Item -LiteralPath $stagingPath -Destination $deployPath

    $deployedExe = Join-Path $deployPath $exeName
    $deployedSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $deployedExe).Hash
    if ($deployedSha256 -ne $artifactSha256) {
        throw "Deployed executable hash mismatch. Expected $artifactSha256 but found $deployedSha256."
    }
    if (-not (Test-Path -LiteralPath (Join-Path $deployPath 'deployment.json') -PathType Leaf)) {
        throw 'Deployment metadata is missing after activation.'
    }
}
catch {
    $deploymentError = $_
    if ($backupCreated -and (Test-Path -LiteralPath $backupPath -PathType Container)) {
        if (Test-Path -LiteralPath $deployPath -PathType Container) {
            Write-Warning "Moving failed deployment to: $failedPath"
            Move-Item -LiteralPath $deployPath -Destination $failedPath
        }
        Write-Warning "Restoring previous deployment from: $backupPath"
        Move-Item -LiteralPath $backupPath -Destination $deployPath
    }
    throw $deploymentError
}

Write-Host "========================================"
Write-Host "Deployment Successful"
Write-Host "========================================"
Write-Host "Deployed: v$Version to $Environment"
Write-Host "Location: $deployPath"
Write-Host "SHA-256: $artifactSha256"
if ($backupCreated) { Write-Host "Rollback: $backupPath" }
Write-Host ""
Write-Host "To launch: & `"$deployPath\$exeName`""
Write-Host ""

exit 0
