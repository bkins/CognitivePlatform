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
$apiProject = "C:\Users\benho\source\repos\Coco.API\Coco.API.csproj"

Write-Host "========================================"
Write-Host "Building Coco.API"
Write-Host "========================================"
Write-Host "Build Label:   $Environment"
Write-Host "Version:       $Version"
Write-Host "Output Path:   $OutputPath"
Write-Host "Project Path:  $apiProject"
Write-Host ""

if (-not (Test-Path $apiProject)) {
    Write-Host "========================================" -ForegroundColor Red
    Write-Host "ERROR: Project not found" -ForegroundColor Red
    Write-Host "========================================" -ForegroundColor Red
    Write-Host "Expected: $apiProject" -ForegroundColor Yellow
    exit 1
}

# ── Restore ────────────────────────────────────────────────────────────────────
# Restore the project file directly (not the solution) so NuGet sees only the
# main project and its transitive dependencies — not Coco.API.Tests, which also
# references Coco.API and would cause "Ambiguous project name" during a combined
# restore+publish pass.
Write-Host "Restoring Coco.API..."

dotnet restore $apiProject -r win-x64

if ($LASTEXITCODE -ne 0) {
    Write-Host "========================================" -ForegroundColor Red
    Write-Host "ERROR: dotnet restore failed" -ForegroundColor Red
    Write-Host "========================================" -ForegroundColor Red
    Write-Host "Exit code: $LASTEXITCODE" -ForegroundColor Yellow
    exit $LASTEXITCODE
}

Write-Host ""

# ── Publish ────────────────────────────────────────────────────────────────────
# --no-restore skips the combined restore step that would re-discover the .sln.
Write-Host "Publishing Coco.API (universal artifact)..."

$assemblyName = "Coco.API"

dotnet publish $apiProject `
    --no-restore `
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
Write-Host "Built: Coco.API v$Version (universal)"
Write-Host "Output: $OutputPath"
Write-Host "Note: This artifact can be deployed to any environment"
Write-Host ""

exit 0
