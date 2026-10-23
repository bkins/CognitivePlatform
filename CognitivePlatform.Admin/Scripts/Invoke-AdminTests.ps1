[CmdletBinding()]
param (
    [ValidateSet('Profiles','Start','Status','Logs','Results','Summary','Evidence','Cancel')]
    [string]$Action = 'Start'
  , [ValidateSet('unit','client-contracts','routing-contracts','runtime-dev','runtime-prod','deployment-dev','deployment-prod')]
    [string]$Profile = 'unit'
  , [Guid]$RunId = [Guid]::Empty
  , [Guid]$RequestId = [Guid]::NewGuid()
  , [Uri]$BaseUri = 'https://localhost:7290'
  , [switch]$Wait
  , [ValidateRange(0,2147483647)][int]$After = 0
  , [ValidateRange(1,500)][int]$Limit = 200
  , [ValidateRange(30,7200)][int]$TimeoutSeconds = 1800
)

$ErrorActionPreference = 'Stop'
if (-not $BaseUri.IsLoopback -or $BaseUri.Scheme -ne 'https') {
    throw 'Use the trusted HTTPS loopback address of CP Admin.'
}

# Load local configuration without accepting or printing credentials on the command line.
$secret = $null
$adminRoot = Split-Path -Parent $PSScriptRoot
foreach ($path in @((Join-Path $adminRoot 'appsettings.json'), (Join-Path $adminRoot 'appsettings.Development.json'))) {
    if (Test-Path -LiteralPath $path) {
        $settings = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -AsHashtable
        if ($settings.AdminSettings.AdminSecret) { $secret = $settings.AdminSettings.AdminSecret }
    }
}
$secretsPath = Join-Path $env:APPDATA 'Microsoft\UserSecrets\cp-admin-local-secrets\secrets.json'
if (Test-Path -LiteralPath $secretsPath) {
    $settings = Get-Content -LiteralPath $secretsPath -Raw | ConvertFrom-Json -AsHashtable
    if ($settings['AdminSettings:AdminSecret']) { $secret = $settings['AdminSettings:AdminSecret'] }
    elseif ($settings.AdminSettings.AdminSecret) { $secret = $settings.AdminSettings.AdminSecret }
}
if ($env:AdminSettings__AdminSecret) { $secret = $env:AdminSettings__AdminSecret }
if ($env:CP_ADMIN_SECRET) { $secret = $env:CP_ADMIN_SECRET }
if ([string]::IsNullOrWhiteSpace($secret)) { throw 'No local Admin credential is configured.' }
$headers = @{ 'X-Admin-Secret' = $secret }
$base = $BaseUri.AbsoluteUri.TrimEnd('/') + '/api/operations'

function Invoke-Operation {
    param ([string]$Path, [string]$Method = 'Get', [object]$Body = $null, [switch]$Raw)
    $parameters = @{ Uri = $base + $Path; Method = $Method; Headers = $headers; MaximumRedirection = 0; TimeoutSec = 30 }
    if ($null -ne $Body) {
        $parameters.Body = $Body | ConvertTo-Json -Depth 4
        $parameters.ContentType = 'application/json'
    }
    if ($Raw) { (Invoke-WebRequest @parameters).Content } else { Invoke-RestMethod @parameters }
}

if ($Action -notin @('Profiles','Start') -and $RunId -eq [Guid]::Empty) { throw 'RunId is required for this action.' }
$result = switch ($Action) {
    'Profiles' { Invoke-Operation '/test-profiles' }
    'Start' { Invoke-Operation '/test-runs' 'Post' @{ profile = $Profile; requestId = $RequestId.ToString() } }
    'Status' { Invoke-Operation "/test-runs/$RunId" }
    'Logs' { Invoke-Operation "/test-runs/$RunId/logs?after=$After&limit=$Limit" }
    'Results' { Invoke-Operation "/test-runs/$RunId/results" }
    'Summary' { Invoke-Operation "/test-runs/$RunId/summary" }
    'Evidence' { Invoke-Operation "/test-runs/$RunId/evidence" -Raw }
    'Cancel' { Invoke-Operation "/test-runs/$RunId/cancel" 'Post'; @{ id = $RunId; cancellationRequested = $true } }
}
if ($Action -eq 'Start' -and $Wait) {
    $RunId = [Guid]$result.id
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $result = Invoke-Operation "/test-runs/$RunId"
        if ($null -ne $result.finishedAt) { break }
        if ([DateTime]::UtcNow -ge $deadline) { throw "Polling timed out. Run $RunId remains server-owned; inspect or cancel it explicitly." }
        Start-Sleep -Seconds 2
    } while ($true)
}
if ($Action -in @('Summary','Evidence')) { $result } else { $result | ConvertTo-Json -Depth 12 }
if ($Action -eq 'Start' -and $Wait -and $result.status -ne 'Passed') { exit 1 }
