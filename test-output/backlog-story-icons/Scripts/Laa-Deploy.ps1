<#
.SYNOPSIS
Installs a specific version of the Local AI Assistant APK to a connected Android device.

.DESCRIPTION
Installs the specified version of the MAUI APK for the given environment
onto a connected physical Android device. Optionally uninstalls the previous
version if CurrentApkName is provided.

.PARAMETER Environment
Target environment. Valid values: DEV, QA, PROD.

.PARAMETER SourcePath
Path to the extracted artifact directory containing the APK.

.PARAMETER Version
Version string matching the built artifact.

.PARAMETER CurrentApkName
Optional. Name of the currently deployed APK to uninstall before installing the new version.

.PARAMETER CurrentVersion
Optional. Version string of the currently deployed APK (for logging purposes).

.EXAMPLE
.\Laa-Deploy.ps1 -Environment DEV -SourcePath "C:\temp\deploy" -Version 1.0.20260125.153045

.EXAMPLE
.\Laa-Deploy.ps1 -Environment QA -SourcePath "C:\temp\deploy" -Version 1.0.20260125.160000 -CurrentApkName "Laa-1.0.20260125.153045-qa.apk" -CurrentVersion "1.0.20260125.153045"
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Environment,
    
    [Parameter(Mandatory)]
    [string]$SourcePath,
    
    [Parameter(Mandatory = $false)]
    [string]$Version = "1.0.0.1",
    
    [Parameter()]
    [string]$CurrentApkName,
    
    [Parameter()]
    [string]$CurrentVersion
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($Version)) { $Version = "1.0.0.1" }

$appIdMap = @{
    "Dev"  = "com.snikpoh.localaiassistant.dev"
    "Qa"   = "com.snikpoh.localaiassistant.qa"
    "Prod" = "com.snikpoh.localaiassistant"
}

Write-Host "========================================"
Write-Host "Deploying Local AI Assistant"
Write-Host "========================================"
Write-Host "Environment: $Environment"
Write-Host "Version:     $Version"

if ($CurrentVersion) {
    Write-Host "Upgrading from: v$CurrentVersion"
}

Write-Host ""

$appId = $appIdMap[$Environment]
Write-Host "App ID: $appId"

$apkName = "Laa-$Version-$($Environment.ToLower()).apk"
Write-Host "APK Name: $apkName"

$apkPath = Join-Path $SourcePath $apkName
Write-Host "APK Path: $apkPath"
Write-Host ""

if (-not (Test-Path $apkPath)) {
    Write-Error "APK not found: $apkPath"
    exit 1
}

# Get target device
$devices = adb devices | Select-String "device$" | ForEach-Object { ($_ -split "\s+")[0] }
$targetDevice = $devices | Where-Object { $_ -notmatch "^emulator-" } | Select-Object -First 1

if (-not $targetDevice) {
    Write-Error "No physical Android device found. Please connect a device and enable USB debugging."
    exit 1
}

Write-Host "Target Device: $targetDevice"
Write-Host ""

# Always attempt to uninstall existing version first (handles signature mismatch scenarios)
Write-Host "Checking for existing installation..."
$uninstallResult = adb -s $targetDevice uninstall $appId 2>&1

if ($LASTEXITCODE -eq 0) {
    Write-Host "✅  Uninstalled existing version"
}
else {
    Write-Host "ℹ️  No existing installation found (first-time deploy)"
}

Write-Host ""

# Install new version
Write-Host "Installing new version ($Version)..."
Write-Host "  APK: $apkName"

$installResult = adb -s $targetDevice install -r $apkPath 2>&1

if ($LASTEXITCODE -ne 0) {
    Write-Error "Installation failed: $installResult"
    exit 1
}

Write-Host ""
Write-Host "========================================"
Write-Host "✅ Deployment Successful"
Write-Host "========================================"
Write-Host "Deployed: v$Version to $Environment"
Write-Host "Device:   $targetDevice"
Write-Host ""

exit 0
