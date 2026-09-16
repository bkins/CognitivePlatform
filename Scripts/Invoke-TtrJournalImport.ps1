param(
    [Parameter(Mandatory)] [ValidateSet('Plan', 'Execute')] [string] $Mode,
    [Parameter(Mandatory)] [string] $ApiBaseUrl,
    [Parameter(Mandatory)] [string] $AdminKey,
    [Parameter(Mandatory)] [string] $DatabasePath,
    [Parameter(Mandatory)] [string] $RecoveryBundlePath,
    [Parameter(Mandatory)] [string] $ExpectedDatabaseSha256,
    [Parameter(Mandatory)] [string] $TargetPartition,
    [string] $LogicalSourceInstance = 'ttr-xamarin-bens-s10plus',
    [string] $SourceTimeZoneId = 'America/Los_Angeles',
    [string] $ReviewedPlanSha256,
    [string] $BackupManifestPath,
    [string] $BackupManifestSha256,
    [string] $TargetEnvironment,
    [string] $TargetDatabasePath
)

$headers = @{ 'X-Admin-Secret' = $AdminKey }
$source = @{
    databasePath = $DatabasePath
    recoveryBundlePath = $RecoveryBundlePath
    expectedDatabaseSha256 = $ExpectedDatabaseSha256
    logicalSourceInstance = $LogicalSourceInstance
    sourceTimeZoneId = $SourceTimeZoneId
    targetPartition = $TargetPartition
}

if ($Mode -eq 'Plan') {
    $body = $source
    $route = 'api/admin/journal/import/ttr/plan'
}
else {
    foreach ($required in @('ReviewedPlanSha256', 'BackupManifestPath', 'BackupManifestSha256', 'TargetEnvironment', 'TargetDatabasePath')) {
        if ([string]::IsNullOrWhiteSpace((Get-Variable -Name $required -ValueOnly))) {
            throw "$required is required for Execute mode."
        }
    }
    $body = @{
        source = $source
        execution = @{
            reviewedPlanSha256 = $ReviewedPlanSha256
            backupManifestPath = $BackupManifestPath
            backupManifestSha256 = $BackupManifestSha256
            targetEnvironment = $TargetEnvironment
            targetDatabasePath = $TargetDatabasePath
        }
    }
    $route = 'api/admin/journal/import/ttr/execute'
}

$uri = "$($ApiBaseUrl.TrimEnd('/'))/$route"
Invoke-RestMethod -Method Post -Uri $uri -Headers $headers -ContentType 'application/json' -Body ($body | ConvertTo-Json -Depth 8)
