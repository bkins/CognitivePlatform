<#
.SYNOPSIS
Deploys the CognitivePlatform API to a specific environment using a validated clean swap.

.DESCRIPTION
Stages and validates the universal API artifact before stopping only the API process that
runs from the selected environment directory. The previous directory is moved to a dated
backup, the staged directory is activated, and the backup is restored automatically if the
swap or post-swap validation fails. Data under C:\CP\Data is never modified by this script.

.PARAMETER Environment
Target environment: DEV, QA, or PROD.

.PARAMETER SourcePath
Path to the universal API artifact produced by API-Build.ps1.

.PARAMETER Version
Version being deployed and written to deployment.json.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet("DEV", "QA", "PROD")]
    [string]$Environment,

    [Parameter(Mandatory)]
    [string]$SourcePath,

    [Parameter(Mandatory = $false)]
    [string]$Version = "1.0.0.1"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($Version)) { $Version = "1.0.0.1" }

$targets = @{
    "DEV" = @{
        DeployPath = "C:\CP\Deploy\API\Dev"
        AspNetEnv   = "Development"
        Urls        = "https://localhost:7083;http://localhost:5273;http://192.168.0.33:5273"
    }
    "QA" = @{
        DeployPath = "C:\CP\Deploy\API\QA"
        AspNetEnv   = "QA"
        Urls        = "https://localhost:7084;http://localhost:5274;http://192.168.0.33:5274"
    }
    "PROD" = @{
        DeployPath = "C:\CP\Deploy\API\Prod"
        AspNetEnv   = "Prod"
        Urls        = "https://localhost:7085;http://localhost:5275;http://192.168.0.33:5275"
    }
}

$target = $targets[$Environment]
$deployPath = [System.IO.Path]::GetFullPath([string]$target.DeployPath)
$aspnetEnv = [string]$target.AspNetEnv
$urls = [string]$target.Urls
$allowedDeployPaths = @($targets.Values | ForEach-Object { [System.IO.Path]::GetFullPath([string]$_.DeployPath) })

if ($allowedDeployPaths -notcontains $deployPath) {
    throw "Refusing deployment to an unexpected target path: $deployPath"
}

if (-not (Test-Path -LiteralPath $SourcePath -PathType Container)) {
    throw "Artifact path not found: $SourcePath"
}

$sourcePathResolved = [System.IO.Path]::GetFullPath((Resolve-Path -LiteralPath $SourcePath).Path)
$exeName = "CognitivePlatform.Api.exe"
$sourceExe = Join-Path $sourcePathResolved $exeName
if (-not (Test-Path -LiteralPath $sourceExe -PathType Leaf)) {
    throw "Expected API executable not found in artifact: $sourceExe"
}

$artifactSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $sourceExe).Hash
$timestamp = Get-Date -Format "yyyyMMdd-HHmmssfff"
$backupPath = "$deployPath.backup.$timestamp"
$stagingPath = "$deployPath.staging.$([Guid]::NewGuid().ToString('N'))"
$failedPath = "$deployPath.failed.$timestamp"
$backupCreated = $false

Write-Host "========================================"
Write-Host "Deploying CognitivePlatform API"
Write-Host "========================================"
Write-Host "Environment: $Environment"
Write-Host "Version:     $Version"
Write-Host "Source:      $sourcePathResolved"
Write-Host "Target:      $deployPath"
Write-Host "SHA-256:     $artifactSha256"
Write-Host ""

try {
    Write-Host "Staging artifact in: $stagingPath"
    New-Item -ItemType Directory -Path $stagingPath -Force | Out-Null
    Get-ChildItem -LiteralPath $sourcePathResolved -Force |
        Copy-Item -Destination $stagingPath -Recurse -Force

    $stagedExe = Join-Path $stagingPath $exeName
    if (-not (Test-Path -LiteralPath $stagedExe -PathType Leaf)) {
        throw "Staged deployment is missing $exeName."
    }

    $stagedSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $stagedExe).Hash
    if (-not $stagedSha256.Equals($artifactSha256, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Staged executable hash does not match the source artifact."
    }

    $envConfig = @{
        ASPNETCORE_ENVIRONMENT = $aspnetEnv
        ASPNETCORE_URLS        = $urls
    } | ConvertTo-Json
    Set-Content -LiteralPath (Join-Path $stagingPath "environment.json") -Value $envConfig -Encoding UTF8

    $startScript = @"
`$host.UI.RawUI.WindowTitle = "CognitivePlatform API ($Environment)"

Write-Host "Starting CognitivePlatform API ($Environment)"
Write-Host "Version: $Version"
Write-Host ""

`$env:ASPNETCORE_ENVIRONMENT = "$aspnetEnv"
`$env:ASPNETCORE_URLS        = "$urls"

.\CognitivePlatform.Api.exe
"@
    Set-Content -LiteralPath (Join-Path $stagingPath "start-api.ps1") -Value $startScript -Encoding UTF8

    $productVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($stagedExe).ProductVersion
    $gitCommitHash = if ($productVersion -and $productVersion.Contains('+')) {
        $productVersion.Split('+', 2)[1]
    } else {
        ""
    }

    $metadata = [PSCustomObject]@{
        ComponentName  = "API"
        Environment    = $Environment
        Version        = $Version
        GitCommitHash  = $gitCommitHash
        ArtifactSha256 = $artifactSha256
        DeployedAt     = (Get-Date -Format "o")
        DeployedBy     = $env:USERNAME
    }
    $metadata | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $stagingPath "deployment.json") -Encoding UTF8

    $processName = [System.IO.Path]::GetFileNameWithoutExtension($exeName)
    $deployPathWithSeparator = $deployPath.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    $running = @(Get-Process -Name $processName -ErrorAction SilentlyContinue | Where-Object {
        $processPath = $_.Path
        $processPath -and [System.IO.Path]::GetFullPath($processPath).StartsWith($deployPathWithSeparator, [StringComparison]::OrdinalIgnoreCase)
    })

    foreach ($process in $running) {
        Write-Host "Stopping target API process PID $($process.Id)..."
        Stop-Process -Id $process.Id -Force -ErrorAction Stop
        if (-not $process.WaitForExit(10000)) {
            throw "Target API process $($process.Id) did not exit within 10 seconds."
        }
    }

    if (Test-Path -LiteralPath $deployPath) {
        Write-Host "Moving previous deployment to: $backupPath"
        Move-Item -LiteralPath $deployPath -Destination $backupPath
        $backupCreated = $true
    }

    Write-Host "Activating staged deployment..."
    Move-Item -LiteralPath $stagingPath -Destination $deployPath

    $deployedExe = Join-Path $deployPath $exeName
    $deployedSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $deployedExe).Hash
    if (-not $deployedSha256.Equals($artifactSha256, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Deployed executable hash does not match the validated artifact."
    }
}
catch {
    $deploymentError = $_

    if (Test-Path -LiteralPath $deployPath) {
        Write-Warning "Moving failed deployment aside to: $failedPath"
        Move-Item -LiteralPath $deployPath -Destination $failedPath -ErrorAction SilentlyContinue
    }

    if ($backupCreated -and (Test-Path -LiteralPath $backupPath) -and -not (Test-Path -LiteralPath $deployPath)) {
        Write-Warning "Restoring previous deployment from: $backupPath"
        Move-Item -LiteralPath $backupPath -Destination $deployPath
    }

    if (Test-Path -LiteralPath $stagingPath) {
        $expectedStagingPrefix = "$deployPath.staging."
        if ($stagingPath.StartsWith($expectedStagingPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            Remove-Item -LiteralPath $stagingPath -Recurse -Force
        }
    }

    throw $deploymentError
}

Write-Host ""
Write-Host "========================================"
Write-Host "Deployment Successful"
Write-Host "========================================"
Write-Host "Deployed:    CognitivePlatform API v$Version"
Write-Host "Environment: $Environment"
Write-Host "Location:    $deployPath"
Write-Host "SHA-256:     $artifactSha256"
if ($backupCreated) { Write-Host "Rollback:    $backupPath" }
Write-Host ""
Write-Host "To start manually: $deployPath\start-api.ps1"
Write-Host ""

exit 0
