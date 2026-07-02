#Requires -Version 5.1
<#
.SYNOPSIS
Runs the InterpreterEvaluationRunner and saves results to a timestamped output file.

.DESCRIPTION
Builds and executes the InterpreterEvaluationRunner project using
the specified configuration (Debug or Release).

The console output from the evaluation run is captured and written
to a timestamped text file in the output directory while also being
displayed on screen.

If the output directory does not exist, it will be created automatically.

.PARAMETER OutputDir
Directory where evaluation result files will be stored.

Defaults to:
    C:\CP\Data\EvalResults

.PARAMETER ProjectPath
Path to the InterpreterEvaluationRunner project directory.

Defaults to:
    C:\Users\benho\source\repos\InterpreterEvaluationRunner

.PARAMETER Configuration
Build configuration used when running the project.

Valid values:
    Debug
    Release

Default:
    Release

.EXAMPLE
.\Run-InterpreterEval.ps1

Runs the InterpreterEvaluationRunner using the default settings:

    Configuration = Release
    OutputDir     = C:\CP\Data\EvalResults

Results are written to a timestamped file such as:

    C:\CP\Data\EvalResults\eval-2026-06-06 07-15-22.txt

.EXAMPLE
.\Run-InterpreterEval.ps1 -Configuration Debug

Runs the evaluation using the Debug build configuration.

Useful when testing local code changes before creating a Release build.

.EXAMPLE
.\Run-InterpreterEval.ps1 -OutputDir "D:\EvalResults"

Stores the generated evaluation report in:

    D:\EvalResults

instead of the default directory.

.EXAMPLE
.\Run-InterpreterEval.ps1 `
    -ProjectPath "D:\Source\InterpreterEvaluationRunner" `
    -Configuration Release

Runs the InterpreterEvaluationRunner from a custom repository location.

.EXAMPLE
powershell.exe -ExecutionPolicy Bypass `
    -File .\Run-InterpreterEval.ps1

Runs the script from a command prompt or CI/CD process while bypassing
the current PowerShell execution policy for this invocation only.

.EXAMPLE
.\Run-InterpreterEval.ps1 `
    -OutputDir "\\Server\Share\Evals" `
    -Configuration Release

Runs the evaluation and saves results to a network share.

.NOTES
Author: Ben
Requires:
    - PowerShell 5.1 or later
    - .NET SDK installed and available on PATH

The script executes:

    dotnet run --project <ProjectPath> -c <Configuration>

The project directory becomes the current working directory during
execution so that relative paths used by the application resolve
correctly.

.LINK
Get-Help .\Run-InterpreterEval.ps1 -Full

.LINK
Get-Help .\Run-InterpreterEval.ps1 -Examples
#>
param(
    [string]$OutputDir = "C:\CP\Data\EvalResults",
    [string]$ProjectPath = "C:\Users\benho\source\repos\InterpreterEvaluationRunner",
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

# Set UTF-8 throughout so Unicode box-drawing characters survive the pipeline.
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

$fileDate = Get-Date -Format "yyyy-MM-dd_HH-mm-ss"
$runDate  = Get-Date -Format "yyyy-MM-dd H:mm:ss tt"

$outputFile = Join-Path $OutputDir "eval-$fileDate.txt"

# $date = (Get-Date -Format "yyyy-MM-dd_HH-mm-ss")
# $outputFile = Join-Path $OutputDir "eval-$date.txt"

if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
    Write-Host "Created output directory: $OutputDir"
}

if (-not (Test-Path $ProjectPath)) {
    Write-Error "InterpreterEvaluationRunner project not found at: $ProjectPath"
    exit 1
}

Write-Host "=== Interpreter Evaluation Run ($runDate) ==="
Write-Host "Project : $ProjectPath"
Write-Host "Config  : $Configuration"
Write-Host "Output  : $outputFile"
Write-Host ""

$header = @"
=== Interpreter Evaluation Run ===
Date    : $runDate
Config  : $Configuration
Project : $ProjectPath
"@
$header | Out-File -FilePath $outputFile -Encoding utf8

# dotnet run inherits the current working directory, so change to the project
# folder before running so the app can locate its relative Data\benchmark path.
Push-Location $ProjectPath
try {
    # Added the -c flag to specify the build configuration
    dotnet run --project "$ProjectPath" -c $Configuration 2>&1 | Tee-Object -FilePath $outputFile -Append
}
finally {
    Pop-Location
}

$exitCode = $LASTEXITCODE
Write-Host ""
Write-Host "=== Run complete (exit $exitCode). Results saved to $outputFile ==="
exit $exitCode
