<#
.SYNOPSIS
Builds the Local AI Assistant MAUI application for ONE specific environment.

.DESCRIPTION
This script builds the MAUI Android application for the specified environment
and version. It produces a single signed APK artifact for that environment only.

This script is invoked by the CP Admin Release page.
It does not generate versions, enforce promotion rules, or manage artifacts.

IMPORTANT: This script builds ONLY the specified environment, not all environments.

.PARAMETER Environment
Target environment. Valid values: Dev, QA
NOTE: Prod is NOT allowed - Prod artifacts come from promotion only.

.PARAMETER Version
Version string provided by CP Admin release tooling (e.g. 1.0.20260201.123045).

.EXAMPLE
.\Laa-Build.ps1 -Environment Dev -Version 1.0.20260201.123045

.EXAMPLE
.\Laa-Build.ps1 -Environment QA -Version 1.0.20260201.140530
#>

[CmdletBinding()]
param (
    [Parameter(Mandatory)]
    [ValidateSet("Dev", "QA", "Prod")]
    [string]$Environment,

    [Parameter(Mandatory = $false)]
    [string]$Version = "1.0.0.1",
    
    [Parameter(Mandatory)]
    [string]$ArtifactsPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($Version)) { $Version = "1.0.0.1" }

function Log {
    param ([string]$Message)
    Write-Host "[BUILD][$Environment] $Message"
}

$manifestPath          = "C:\Users\benho\source\repos\LocalAIAssistant\Platforms\Android\AndroidManifest.xml"
$originalManifestBytes = $null

try {
    Write-Host "Building LocalAIAssistant for [$Environment], version [$Version]" -ForegroundColor Cyan

    # Project paths
    $mauiProject = "C:\Users\benho\source\repos\LocalAIAssistant\LocalAIAssistant.Ui.Maui.csproj"

    # $artifactsPath = "C:\CP\Artifacts\Laa\$Version\$Environment"
    
    if (-not (Test-Path $mauiProject)) {
        throw "MAUI project not found: $mauiProject"
    }

    if (-not (Test-Path $manifestPath)) {
        throw "Android manifest not found: $manifestPath"
    }

    # Configure based on SINGLE environment
    switch ($Environment) {
        "Dev" {
            $apiBaseUrl = "http://192.168.0.33:5273"
            $appId = "com.snikpoh.localaiassistant.dev"
            $appLabel = "Laa (Dev)"
            $outputDir = "$ArtifactsPath"
        }
        "QA" {
            $apiBaseUrl = "http://192.168.0.33:5274"
            $appId = "com.snikpoh.localaiassistant.qa"
            $appLabel = "Laa (QA)"
            $outputDir = "$ArtifactsPath"
        }
        "Prod" {
            $apiBaseUrl = "http://192.168.0.33:5275"
            $appId = "com.snikpoh.localaiassistant.prod"
            $appLabel = "Laa (Prod)"
            $outputDir = "$ArtifactsPath"
        }
    }

    Log "Configuration:"
    Log "  App ID: $appId"
    Log "  API URL: $apiBaseUrl"
    Log "  Output: $outputDir"

    # Clean previous builds
    Log "Cleaning project..."
    dotnet clean $mauiProject | Out-Null

    # Temporarily stamp the source manifest for MAUI packaging, then restore it
    # in finally so environment builds do not leave the repo dirty.
    Log "Temporarily stamping Android manifest label..."
    $originalManifestBytes = [System.IO.File]::ReadAllBytes($manifestPath)
    [xml]$manifest = Get-Content $manifestPath
    $manifest.manifest.application.SetAttribute("android:label", $appLabel)
    $manifest.Save($manifestPath)

    # Ensure output directory exists
    if (-not (Test-Path $outputDir)) {
        New-Item -Path $outputDir -ItemType Directory -Force | Out-Null
    }

    # MAUI Resizetizer requires a 3-part ApplicationDisplayVersion. LAA maps
    # AndroidFullVersionName into the Android manifest versionName at build time.
    $versionParts = $Version.Split('.')
    if ($versionParts.Count -ge 4) {
        $displayVersion = ($versionParts[0..2]) -join '.'
        $appVersion     = [int]$versionParts[3]
    } elseif ($versionParts.Count -eq 3) {
        $displayVersion = $Version
        $appVersion     = [int]$versionParts[2]
    } else {
        $displayVersion = "$Version.0.0"
        $appVersion     = 1
    }

    # Build the APK for THIS environment ONLY
    Log "Publishing MAUI app..."
    
    dotnet publish $mauiProject `
        -c Release `
        -f net9.0-android `
        /p:ApplicationLabel="$appLabel" `
        /p:ApplicationDisplayVersion="$displayVersion" `
        /p:AndroidFullVersionName="$Version" `
        /p:ApplicationVersion=$appVersion `
        /p:ApiEnvironmentName="$Environment" `
        /p:ApplicationId="$appId" `
        /p:AppEnvironment="$Environment" `
        /p:ApiBaseUrl="$apiBaseUrl" `
        /p:AndroidPackageFormat=apk `
        /p:AndroidKeyStore=true `
        /p:AndroidSigningKeyStore="$env:USERPROFILE\.android\debug.keystore" `
        /p:AndroidSigningKeyAlias=androiddebugkey `
        /p:AndroidSigningKeyPass=android `
        /p:AndroidSigningStorePass=android `
        -o $outputDir

    if ($LASTEXITCODE -ne 0) {
        throw "Dotnet publish failed with exit code $LASTEXITCODE"
    }

    # Find the signed APK
    $signedApk = Get-ChildItem "$outputDir\*-Signed.apk" -ErrorAction SilentlyContinue | Select-Object -First 1
    
    if (-not $signedApk) {
        throw "Build appeared to succeed, but no signed APK was found in $outputDir"
    }

    Log "Signed APK located: $($signedApk.FullName)"
    
    # Rename APK to standard format
    $targetName = "Laa-$Version-$($Environment.ToLower()).apk"
    $targetPath = Join-Path $outputDir $targetName
    
    Log "Renaming APK to: $targetName"
    Log "Moving APK to: $targetPath"
    
    if (Test-Path $targetPath) {
        Remove-Item $targetPath -Force
    }
    
    Move-Item $signedApk.FullName $targetPath -Force
    
    Log "Build complete!"
    Log "  APK: $targetPath"
    Log "  Version: $Version"
    Log "  Environment: $Environment"
    
    exit 0
}
catch {
    Write-Host "Build failed: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
finally {
    if ($null -ne $originalManifestBytes) {
        try {
            [System.IO.File]::WriteAllBytes($manifestPath, $originalManifestBytes)
            Log "Restored Android manifest after build."
        }
        catch {
            Write-Warning "Failed to restore Android manifest: $($_.Exception.Message)"
        }
    }
}
