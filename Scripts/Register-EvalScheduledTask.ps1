<#
.SYNOPSIS
    Registers a Windows Scheduled Task that runs the interpreter evaluator
    automatically on the 1st of each month.

.DESCRIPTION
    Creates a task named "CognitivePlatform - Monthly Evaluator Run" under
    the \CognitivePlatform\ task folder. The task runs Run-InterpreterEval.ps1
    using PowerShell 7 (pwsh.exe). If PS7 is not installed the script errors
    out with guidance.

    The task uses the S4U logon type so it can run whether or not the user
    is interactively logged on, without requiring a stored password. It does
    not have access to network shares but can reach localhost (Ollama) and
    the internet (Groq).

    Run this script once from an elevated prompt. It is idempotent — running
    it again simply updates the existing task.

.PARAMETER ScriptPath
    Full path to Run-InterpreterEval.ps1. Defaults to the standard repo location.

.PARAMETER DayOfMonth
    Day of month the task fires (1–28). Default: 1.

.PARAMETER StartTime
    Local time the task fires on the trigger day, e.g. "02:00". Default: 02:00.

.PARAMETER TaskFolder
    Scheduled Task folder path. Default: \CognitivePlatform\.

.EXAMPLE
    # Register with defaults (1st of month at 02:00)
    .\Register-EvalScheduledTask.ps1

.EXAMPLE
    # Register to run on the 5th at 03:30
    .\Register-EvalScheduledTask.ps1 -DayOfMonth 5 -StartTime "03:30"
#>
#Requires -RunAsAdministrator
param(
    [string] $ScriptPath  = "C:\Users\benho\source\repos\CognitivePlatform\Scripts\Run-InterpreterEval.ps1"
  , [int]    $DayOfMonth  = 1
  , [string] $StartTime   = "02:00"
  , [string] $TaskFolder  = "\CognitivePlatform\"
  , [string] $TaskName    = "Monthly Evaluator Run"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ── Locate pwsh.exe ──────────────────────────────────────────────────────────

$pwshCandidates = @(
    "C:\Program Files\PowerShell\7\pwsh.exe"
    "C:\Program Files\PowerShell\pwsh.exe"
)
$pwshExe = $null
foreach ($candidate in $pwshCandidates) {
    if (Test-Path $candidate) { $pwshExe = $candidate; break }
}
if (-not $pwshExe) {
    $fromPath = (Get-Command pwsh -ErrorAction SilentlyContinue)?.Source
    if ($fromPath) { $pwshExe = $fromPath }
}

if (-not $pwshExe) {
    Write-Error @"
PowerShell 7 (pwsh.exe) was not found. The evaluator script uses PS7 syntax.
Install PowerShell 7 from: https://aka.ms/install-powershell
Then re-run this script.
"@
    exit 1
}

# ── Validate script path ─────────────────────────────────────────────────────

if (-not (Test-Path $ScriptPath)) {
    Write-Error "Evaluator script not found: $ScriptPath"
    exit 1
}

# ── Build task components ─────────────────────────────────────────────────────

$fullTaskPath = "$TaskFolder$TaskName"

$action = New-ScheduledTaskAction `
    -Execute $pwshExe `
    -Argument "-NonInteractive -ExecutionPolicy Bypass -File `"$ScriptPath`""

$trigger = New-ScheduledTaskTrigger -Monthly -DaysOfMonth $DayOfMonth -At $StartTime

$settings = New-ScheduledTaskSettingsSet `
    -ExecutionTimeLimit   (New-TimeSpan -Hours 3) `
    -StartWhenAvailable                             `
    -RunOnlyIfNetworkAvailable:$false               `
    -WakeToRun:$false                               `
    -MultipleInstances    IgnoreNew

# S4U: run as the current user without storing a password.
# Has local network access (Ollama) but not UNC shares.
$principal = New-ScheduledTaskPrincipal `
    -UserId   "$env:USERDOMAIN\$env:USERNAME" `
    -LogonType S4U `
    -RunLevel  Highest

# ── Ensure task folder exists ─────────────────────────────────────────────────

$scheduler = New-Object -ComObject Schedule.Service
$scheduler.Connect()
$root = $scheduler.GetFolder("\")

try {
    $null = $root.GetFolder("CognitivePlatform")
}
catch {
    $null = $root.CreateFolder("CognitivePlatform")
    Write-Host "Created task folder: \CognitivePlatform\"
}

# ── Register (or update) the task ─────────────────────────────────────────────

$task = New-ScheduledTask `
    -Action    $action   `
    -Trigger   $trigger  `
    -Settings  $settings `
    -Principal $principal `
    -Description "Runs the CP interpreter evaluator on the $DayOfMonth`st of each month and saves results to C:\CP\Data\EvalResults\."

Register-ScheduledTask `
    -TaskPath $TaskFolder `
    -TaskName $TaskName   `
    -InputObject $task    `
    -Force | Out-Null

# ── Confirm ───────────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "=== Scheduled task registered ===" -ForegroundColor Green
Write-Host "  Path   : $fullTaskPath"
Write-Host "  Shell  : $pwshExe"
Write-Host "  Script : $ScriptPath"
Write-Host "  Fires  : Day $DayOfMonth of each month at $StartTime"
Write-Host "  Output : C:\CP\Data\EvalResults\eval-{date}.txt"
Write-Host ""
Write-Host "To verify: Get-ScheduledTask -TaskPath '$TaskFolder' -TaskName '$TaskName'"
Write-Host "To run now: Start-ScheduledTask -TaskPath '$TaskFolder' -TaskName '$TaskName'"
