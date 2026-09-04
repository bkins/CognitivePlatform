<#
.SYNOPSIS
Deploys the CognitivePlatform API to a specific environment.

.DESCRIPTION
Copies the universal API artifact to the target environment directory
and configures environment-specific settings.

.PARAMETER Environment
Target environment: DEV, QA, or PROD

.PARAMETER SourcePath
Path to the extracted artifact (provided by CP Admin release tooling)

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
Write-Host "Deploying CognitivePlatform API"
Write-Host "========================================"
Write-Host "Environment: $Environment"
Write-Host "Version:     $Version"
Write-Host "Source:      $SourcePath"
Write-Host ""

# Environment-specific configuration
switch ($Environment) {
    "DEV" {
        $deployPath = "C:\CP\Deploy\API\Dev"
        $port = "5273"
        $aspnetEnv = "Development"
        $urls = "https://localhost:7083;http://localhost:5273;http://192.168.0.33:5273"
    }
    "QA" {
        $deployPath = "C:\CP\Deploy\API\QA"
        $port = "5274"
        $aspnetEnv = "QA"
        $urls = "https://localhost:7084;http://localhost:5274;http://192.168.0.33:5274"
    }
    "PROD" {
        $deployPath = "C:\CP\Deploy\API\Prod"
        $port = "5275"
        $aspnetEnv = "Prod"
        $urls = "https://localhost:7085;http://localhost:5275;http://192.168.0.33:5275"
    }
}

Write-Host "Target: $deployPath"
Write-Host ""

# Stop any running instance (if running as service/process)
# TODO: Add your service stop logic here if needed

# Backup existing deployment
if (Test-Path $deployPath) {
    $backupPath = "$deployPath.backup.$(Get-Date -Format 'yyyyMMdd-HHmmss')"
    Write-Host "Creating backup: $backupPath"
    Copy-Item -Path $deployPath -Destination $backupPath -Recurse -Force
}

# Create deployment directory.
# ADM-05: The live database now lives OUTSIDE the deploy tree at:
#   C:\CP\Data\{AspNetCoreEnvironment}\platform.db
# e.g. C:\CP\Data\Development\platform.db  (DEV)
#      C:\CP\Data\QA\platform.db            (QA)
#      C:\CP\Data\Prod\platform.db          (PROD)
#
# The path is set in Program.cs BuildDataPersistenceLayer and is
# independent of $deployPath. This means a clean-wipe of $deployPath
# (Remove-Item $deployPath -Recurse -Force) is now safe to perform
# before deploying — the database will never be touched.
New-Item -ItemType Directory -Path $deployPath -Force | Out-Null

# Copy artifact.
# Copy-Item only touches files present in $SourcePath. Because platform.db
# is not produced by dotnet publish, it will never be in the artifact and
# will never be overwritten by this step.
Write-Host "Copying artifact files..."
Copy-Item -Path "$SourcePath\*" -Destination $deployPath -Recurse -Force

# Create environment configuration file
Write-Host "Creating environment configuration..."
$envConfig = @{
    ASPNETCORE_ENVIRONMENT = $aspnetEnv
    ASPNETCORE_URLS = $urls
} | ConvertTo-Json

Set-Content -Path "$deployPath\environment.json" -Value $envConfig -Encoding UTF8

# Create startup batch file
# Create startup script
Write-Host "Creating startup script..."
@"
`$host.UI.RawUI.WindowTitle = "CognitivePlatform API ($Environment)"

Write-Host "Starting CognitivePlatform API ($Environment)"
Write-Host "Version: $Version"
Write-Host ""

`$env:ASPNETCORE_ENVIRONMENT = "$aspnetEnv"
`$env:ASPNETCORE_URLS        = "$urls"

.\CognitivePlatform.Api.exe
"@ | Out-File "$deployPath\start-api.ps1" -Encoding UTF8

# Start the service/process
# TODO: Add your service start logic here if needed

Write-Host ""
Write-Host "========================================"
Write-Host "Deployment Successful"
Write-Host "========================================"
Write-Host "Deployed: CognitivePlatform API v$Version"
Write-Host "Environment: $Environment"
Write-Host "Location: $deployPath"
Write-Host ""
Write-Host "To start manually: $deployPath\start-api.ps1"
Write-Host ""

exit 0
