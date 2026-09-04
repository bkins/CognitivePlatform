<#
.SYNOPSIS
Builds the Local AI Assistant MAUI Windows application for ONE specific environment.

.DESCRIPTION
This script builds the MAUI Windows application (unpackaged, self-contained folder)
for the specified environment and version. The output is a publishable folder of
binaries that LAA-Windows-Deploy.ps1 can copy to the final deploy location.

This script is invoked by the CP Admin Release Management page.
It does not generate versions, enforce promotion rules, or manage artifacts.

IMPORTANT: This script builds ONLY the specified environment, not all environments.

.PARAMETER Environment
Target environment. Valid values: Dev, QA, Prod.

.PARAMETER Version
Version string provided by CP Admin release tooling (e.g. 1.0.20260201.123045).

.PARAMETER OutputPath
Directory where the published binaries are written.

.EXAMPLE
.\LAA-Windows-Build.ps1 -Environment Dev -Version 1.0.20260201.123045 -OutputPath "C:\CP\Deploy\LaaWindows"

.EXAMPLE
.\LAA-Windows-Build.ps1 -Environment QA -Version 1.0.20260201.140530 -OutputPath "C:\CP\Deploy\LaaWindows"
#>

[CmdletBinding()]
param (
    [Parameter(Mandatory)]
    [ValidateSet("Dev", "QA", "Prod")]
    [string]$Environment

  , [Parameter(Mandatory = $false)]
    [string]$Version = "1.0.0.1"

  , [Parameter(Mandatory)]
    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($Version)) { $Version = "1.0.0.1" }

function Log {
    param ([string]$Message)
    Write-Host "[BUILD][$Environment] $Message"
}

try {
    Write-Host "Building LocalAIAssistant Windows for [$Environment], version [$Version]" -ForegroundColor Cyan

    $mauiProject = "C:\Users\benho\source\repos\LocalAIAssistant\LocalAIAssistant.Ui.Maui.csproj"

    if (-not (Test-Path $mauiProject)) {
        throw "MAUI project not found: $mauiProject"
    }

    switch ($Environment) {
        "Dev" {
            $apiBaseUrl  = "http://localhost:5273"
            $appTitle    = "Laa (Dev)"
        }
        "QA" {
            $apiBaseUrl  = "http://localhost:5274"
            $appTitle    = "Laa (QA)"
        }
        "Prod" {
            $apiBaseUrl  = "http://localhost:5275"
            $appTitle    = "Laa (Prod)"
        }
    }

    Log "Configuration:"
    Log "  App Title: $appTitle"
    Log "  API URL:   $apiBaseUrl"
    Log "  Output:    $OutputPath"

    # Clean previous output so stale files don't linger
    if (Test-Path $OutputPath) {
        Log "Removing previous output at $OutputPath..."
        Remove-Item $OutputPath -Recurse -Force
    }
    New-Item -Path $OutputPath -ItemType Directory -Force | Out-Null

    # Parse version into 3-part SemVer ApplicationDisplayVersion and integer ApplicationVersion for MAUI
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

    Log "Publishing MAUI Windows app..."

    dotnet publish $mauiProject `
        -c Release `
        -f net9.0-windows10.0.19041.0 `
        /p:ApplicationTitle="$appTitle" `
        /p:ApplicationDisplayVersion="$displayVersion" `
        /p:ApplicationVersion=$appVersion `
        /p:ApiEnvironmentName="$Environment" `
        /p:AppEnvironment="$Environment" `
        /p:ApiBaseUrl="$apiBaseUrl" `
        /p:WindowsPackageType=None `
        /p:SelfContained=false `
        -o $OutputPath

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE"
    }

    # Locate the main executable (assembly name matches the project file stem)
    $exeName = "LocalAIAssistant.Ui.Maui.exe"
    $exePath = Join-Path $OutputPath $exeName

    if (-not (Test-Path $exePath)) {
        # Fall back to any .exe that isn't a runtime host sidecar
        $exePath = Get-ChildItem "$OutputPath\*.exe" -ErrorAction SilentlyContinue |
                   Where-Object { $_.Name -notmatch "createdump|vstest" } |
                   Select-Object -First 1 |
                   ForEach-Object { $_.FullName }

        if (-not $exePath) {
            throw "Build appeared to succeed but no executable was found in $OutputPath"
        }
    }

    Log "Build complete!"
    Log "  Executable: $exePath"
    Log "  Version:    $Version"
    Log "  Environment: $Environment"

    exit 0
}
catch {
    Write-Host "Build failed: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
