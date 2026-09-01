<#
.SYNOPSIS
Deploys Coco.API to a specific environment.

.DESCRIPTION
Copies the universal Coco.API artifact to the target environment directory
and configures environment-specific settings.

.PARAMETER Environment
Target environment: DEV, QA, or PROD

.PARAMETER SourcePath
Path to the extracted artifact (provided by ReleaseConsole)

.PARAMETER Version
Version being deployed (for logging)
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

Write-Host "========================================"
Write-Host "Deploying Coco.API"
Write-Host "========================================"
Write-Host "Environment: $Environment"
Write-Host "Version:     $Version"
Write-Host "Source:      $SourcePath"
Write-Host ""

switch ($Environment) {
    "DEV" {
        $deployPath = "C:\CP\Deploy\Coco\Dev"
        $port       = "5292"
        $aspnetEnv  = "Development"
        $urls       = "https://localhost:7243;http://localhost:5292;http://192.168.0.33:5292"
    }
    "QA" {
        $deployPath = "C:\CP\Deploy\Coco\QA"
        $port       = "5293"
        $aspnetEnv  = "QA"
        $urls       = "https://localhost:7244;http://localhost:5293;http://192.168.0.33:5293"
    }
    "PROD" {
        $deployPath = "C:\CP\Deploy\Coco\Prod"
        $port       = "5294"
        $aspnetEnv  = "Prod"
        $urls       = "https://localhost:7245;http://localhost:5294;http://192.168.0.33:5294"
    }
}

Write-Host "Target: $deployPath"
Write-Host ""

function Stop-CocoProcessForDeploy {
    param([string] $DeployPath)

    $resolvedDeployPath = [System.IO.Path]::GetFullPath($DeployPath).TrimEnd('\')
    $processes = @(Get-Process -Name "Coco.API" -ErrorAction SilentlyContinue | Where-Object {
        try {
            $processPath = [System.IO.Path]::GetFullPath($_.Path)
            return $processPath.StartsWith($resolvedDeployPath, [System.StringComparison]::OrdinalIgnoreCase)
        }
        catch {
            return $false
        }
    })

    if ($processes.Count -eq 0) {
        Write-Host "No running Coco.API process found for this deployment."
        return
    }

    foreach ($process in $processes) {
        Write-Host "Stopping running Coco.API process $($process.Id)..."
        try {
            if ($process.MainWindowHandle -ne 0) {
                $process.CloseMainWindow() | Out-Null
                if ($process.WaitForExit(5000)) {
                    Write-Host "Stopped Coco.API process $($process.Id)."
                    continue
                }
            }

            Stop-Process -Id $process.Id -Force -ErrorAction Stop
            $process.WaitForExit(5000)
            Write-Host "Stopped Coco.API process $($process.Id)."
        }
        catch {
            throw "Could not stop Coco.API process $($process.Id): $($_.Exception.Message)"
        }
    }
}

Stop-CocoProcessForDeploy -DeployPath $deployPath

# Backup existing deployment
if (Test-Path $deployPath) {
    $backupPath = "$deployPath.backup.$(Get-Date -Format 'yyyyMMdd-HHmmss')"
    Write-Host "Creating backup: $backupPath"
    Copy-Item -Path $deployPath -Destination $backupPath -Recurse -Force
}

New-Item -ItemType Directory -Path $deployPath -Force | Out-Null

Write-Host "Copying artifact files..."
Copy-Item -Path "$SourcePath\*" -Destination $deployPath -Recurse -Force

Write-Host "Creating environment configuration..."
$envConfig = @{
    ASPNETCORE_ENVIRONMENT = $aspnetEnv
    ASPNETCORE_URLS        = $urls
} | ConvertTo-Json

Set-Content -Path "$deployPath\environment.json" -Value $envConfig -Encoding UTF8

Write-Host "Creating startup script..."
@"
`$host.UI.RawUI.WindowTitle = "Coco.API ($Environment)"

Write-Host "Starting Coco.API ($Environment)"
Write-Host "Version: $Version"
Write-Host ""

`$env:ASPNETCORE_ENVIRONMENT = "$aspnetEnv"
`$env:ASPNETCORE_URLS        = "$urls"

.\Coco.API.exe
"@ | Out-File "$deployPath\start-coco.ps1" -Encoding UTF8

Write-Host ""
Write-Host "========================================"
Write-Host "Deployment Successful"
Write-Host "========================================"
Write-Host "Deployed: Coco.API v$Version"
Write-Host "Environment: $Environment"
Write-Host "Location: $deployPath"
Write-Host ""
Write-Host "To start manually: $deployPath\start-coco.ps1"
Write-Host ""

exit 0
