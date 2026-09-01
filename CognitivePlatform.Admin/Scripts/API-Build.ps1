[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Environment,  # Still passed, but only for logging/metadata
    
    [Parameter(Mandatory = $false)]
    [string]$Version = "1.0.0.1",
    
    [Parameter(Mandatory)]
    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($Version)) { $Version = "1.0.0.1" }
$apiProject = "C:\Users\benho\source\repos\CognitivePlatform\CognitivePlatform\CognitivePlatform.Api.csproj"
# ... your existing path detection logic ...

Write-Host "========================================"
Write-Host "Building CognitivePlatform API"
Write-Host "========================================"
Write-Host "Build Label:   $Environment"  # Changed from "Environment"
Write-Host "Version:       $Version"
Write-Host "Output Path:   $OutputPath"
Write-Host "Project Path:  $apiProject"
Write-Host ""

# ... validation ...

Write-Host "Publishing API (universal artifact)..."

# Single assembly name for all environments
$assemblyName = "CognitivePlatform.Api"
$assemblyTitle = "CognitivePlatform.Api"
$product = "CognitivePlatform.Api"

dotnet publish $apiProject `
    -c Release `
    -r win-x64 `
    --self-contained false `
    /p:UseAppHost=true `
    /p:Version=$Version `
    /p:AssemblyVersion=$Version `
    /p:FileVersion=$Version `
    -o $OutputPath

if ($LASTEXITCODE -ne 0) {
    Write-Host "========================================" -ForegroundColor Red
    Write-Host "ERROR: dotnet publish failed" -ForegroundColor Red  
    Write-Host "========================================" -ForegroundColor Red
    Write-Host "Exit code: $LASTEXITCODE" -ForegroundColor Yellow
    exit $LASTEXITCODE
}

# Verification
$expectedExe = Join-Path $OutputPath "$assemblyName.exe"
if (-not (Test-Path $expectedExe)) {
    Write-Host "========================================" -ForegroundColor Red
    Write-Host "ERROR: Expected executable not found" -ForegroundColor Red
    Write-Host "========================================" -ForegroundColor Red
    Write-Host "Expected: $expectedExe" -ForegroundColor Yellow
    exit 1
}

Write-Host ""
Write-Host "========================================"
Write-Host "Build Successful"
Write-Host "========================================"
Write-Host "Built: CognitivePlatform API v$Version (universal)"
Write-Host "Output: $OutputPath"
Write-Host "Note: This artifact can be deployed to any environment"
Write-Host ""

exit 0