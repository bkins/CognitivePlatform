#Requires -Version 5.1
<#
.SYNOPSIS
    Runs the InterpreterEvaluationRunner and saves output to a dated results file.
.PARAMETER OutputDir
    Directory where eval result files are saved. Defaults to C:\CP\Data\EvalResults.
.PARAMETER ProjectPath
    Path to the InterpreterEvaluationRunner .NET project.
#>
param(
    [string]$OutputDir   = "C:\CP\Data\EvalResults"
  , [string]$ProjectPath = "C:\Users\benho\source\repos\InterpreterEvaluationRunner"
)

# Set UTF-8 throughout so Unicode box-drawing characters survive the pipeline.
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding            = [System.Text.Encoding]::UTF8

$date       = (Get-Date -Format "yyyy-MM-dd")
$outputFile = Join-Path $OutputDir "eval-$date.txt"

if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
    Write-Host "Created output directory: $OutputDir"
}

if (-not (Test-Path $ProjectPath)) {
    Write-Error "InterpreterEvaluationRunner project not found at: $ProjectPath"
    exit 1
}

Write-Host "=== Interpreter Evaluation Run ($date) ==="
Write-Host "Project : $ProjectPath"
Write-Host "Output  : $outputFile"
Write-Host ""

$header = @"
=== Interpreter Evaluation Run ===
Date    : $date
Project : $ProjectPath
"@

$header | Out-File -FilePath $outputFile -Encoding utf8

# dotnet run inherits the current working directory, so change to the project
# folder before running so the app can locate its relative Data\benchmark path.
Push-Location $ProjectPath
try {
    dotnet run --project "$ProjectPath" 2>&1 | Tee-Object -FilePath $outputFile -Append
}
finally {
    Pop-Location
}

$exitCode = $LASTEXITCODE

Write-Host ""
Write-Host "=== Run complete (exit $exitCode). Results saved to $outputFile ==="

exit $exitCode
