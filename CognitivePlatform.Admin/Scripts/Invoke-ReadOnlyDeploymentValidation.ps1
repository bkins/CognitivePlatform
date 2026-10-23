[CmdletBinding()]
param([Parameter(Mandatory)][string]$TargetJson)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'PayloadManifest.ps1')
. (Join-Path $PSScriptRoot 'WindowsArtifactEnvironment.ps1')
$target = $TargetJson | ConvertFrom-Json
$checks = [System.Collections.Generic.List[object]]::new()
function Add-Check([string]$Name, [string]$Outcome, [string]$Detail) {
    $checks.Add([pscustomobject]@{name=$Name;outcome=$Outcome;detail=$Detail})
}
function Assert-NoLinks([string]$Root) {
    $current = [System.IO.DirectoryInfo]::new([System.IO.Path]::GetFullPath($Root))
    while ($null -ne $current) {
        if ($current.Exists -and ($current.Attributes -band [System.IO.FileAttributes]::ReparsePoint)) { throw 'Linked root.' }
        $current = $current.Parent
    }
    foreach($item in Get-ChildItem -LiteralPath $Root -Force -Recurse) {
        if ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) { throw 'Linked payload entry.' }
    }
}
function Get-ApiInventory([string]$Root, [switch]$Deployment) {
    Assert-NoLinks $Root
    $inventory = [System.Collections.Generic.SortedDictionary[string,string]]::new([StringComparer]::Ordinal)
    foreach($file in Get-ChildItem -LiteralPath $Root -File -Recurse -Force) {
        $relative = [System.IO.Path]::GetRelativePath($Root,$file.FullName).Replace('\','/')
        # Preserved runtime configuration and explicitly known evidence/log files are not payload bytes.
        if ($relative -like 'PromptLogs/*' -or $relative -in @('metadata.json','deployment.json','payload-manifest.json') -or $relative -like 'appsettings*.json') { continue }
        if ($Deployment -and $relative -in @('environment.json','start-api.ps1')) { continue } # Independently pinned below.
        $inventory.Add($relative,(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash)
    }
    return ,$inventory
}
function Get-InventoryDigest($Inventory) {
    $text = -join @($Inventory.GetEnumerator() | ForEach-Object { $_.Key + "`t" + $_.Value + "`n" })
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($text)))
}
try {
    if (!(Test-Path -LiteralPath $target.ApiArtifact -PathType Container) -or !(Test-Path -LiteralPath $target.ApiDeployment -PathType Container)) {
        Add-Check 'API payload' 'Unavailable' 'Pinned API artifact or deployment directory is missing.'
    } else {
        $artifact = Get-ApiInventory $target.ApiArtifact
        $deployed = Get-ApiInventory $target.ApiDeployment -Deployment
        $artifactDigest = Get-InventoryDigest $artifact
        $deploymentDigest = Get-InventoryDigest $deployed
        if ($artifactDigest -cne $target.ApiInventorySha256 -or $deploymentDigest -cne $artifactDigest) { throw 'API inventory mismatch.' }
        # Re-read both inventories to reject a target or approved artifact changed during collection.
        if ((Get-InventoryDigest (Get-ApiInventory $target.ApiArtifact)) -cne $artifactDigest -or (Get-InventoryDigest (Get-ApiInventory $target.ApiDeployment -Deployment)) -cne $deploymentDigest) { throw 'API changed during collection.' }
        Add-Check 'API payload' 'Passed' "$($artifact.Count) payload files match the pinned inventory; preserved settings/logs excluded explicitly."
    }
} catch { Add-Check 'API payload' 'Failed' 'API inventory is unsafe, unreadable, changed, or differs from the pinned artifact.' }
try {
    $settingsFile = Join-Path $target.ApiDeployment 'appsettings.json'
    if (!(Test-Path -LiteralPath $settingsFile -PathType Leaf)) { Add-Check 'API preserved configuration' 'Unavailable' 'Runtime settings are missing.' }
    else {
        Assert-NoLinks $target.ApiDeployment
        if ((Get-FileHash -LiteralPath (Join-Path $target.ApiDeployment 'environment.json')).Hash -cne $target.ApiEnvironmentSha256 -or (Get-FileHash -LiteralPath (Join-Path $target.ApiDeployment 'start-api.ps1')).Hash -cne $target.ApiLauncherSha256) { throw 'Generated launcher/environment do not match pinned deployment evidence.' }
        $beforeSettings = (Get-FileHash -LiteralPath $settingsFile).Hash
        $settings = Get-Content -LiteralPath $settingsFile -Raw -Encoding UTF8 | ConvertFrom-Json -AsHashtable
        $environmentFile = Join-Path $target.ApiDeployment $(if($target.Environment -eq 'Dev'){'appsettings.Development.json'}else{'appsettings.Production.json'})
        if (!(Test-Path -LiteralPath $environmentFile -PathType Leaf)) { throw 'Environment configuration missing.' }
        $beforeEnvironment = (Get-FileHash -LiteralPath $environmentFile).Hash
        $environmentSettings = Get-Content -LiteralPath $environmentFile -Raw -Encoding UTF8 | ConvertFrom-Json -AsHashtable
        $database = if($environmentSettings.ContainsKey('SystemPaths') -and $environmentSettings.SystemPaths.ContainsKey('DatabasePath')){$environmentSettings.SystemPaths.DatabasePath}elseif($settings.ContainsKey('SystemPaths') -and $settings.SystemPaths.ContainsKey('DatabasePath')){$settings.SystemPaths.DatabasePath}else{$null}
        $dataRoot = if($environmentSettings.ContainsKey('SystemPaths') -and $environmentSettings.SystemPaths.ContainsKey('DataRoot')){$environmentSettings.SystemPaths.DataRoot}elseif($settings.ContainsKey('SystemPaths') -and $settings.SystemPaths.ContainsKey('DataRoot')){$settings.SystemPaths.DataRoot}else{$null}
        if (!$database -or !$dataRoot) { Add-Check 'API preserved configuration' 'Unavailable' 'Explicit database path/data-root evidence is absent; runtime identity is checked separately.' }
        else {
            $effectiveDatabase = if([IO.Path]::IsPathRooted($database)){$database}else{Join-Path $dataRoot $database}
            if ([IO.Path]::GetFullPath($effectiveDatabase) -ine [IO.Path]::GetFullPath($target.Database)) { throw 'Database configuration mismatch.' }
            if ((Get-FileHash -LiteralPath $settingsFile).Hash -cne $beforeSettings -or (Get-FileHash -LiteralPath $environmentFile).Hash -cne $beforeEnvironment) { throw 'Configuration changed.' }
            if ((Get-FileHash -LiteralPath (Join-Path $target.ApiDeployment 'environment.json')).Hash -cne $target.ApiEnvironmentSha256 -or (Get-FileHash -LiteralPath (Join-Path $target.ApiDeployment 'start-api.ps1')).Hash -cne $target.ApiLauncherSha256) { throw 'Generated deployment files changed during inspection.' }
            Add-Check 'API preserved configuration' 'Passed' 'Allowlisted database path matches target; configuration values and credentials are not exported. Runtime GET separately verifies effective environment/content-root/version/source.'
        }
    }
} catch { Add-Check 'API preserved configuration' 'Failed' 'Allowlisted configuration is unsafe, unreadable, changed or mismatched; values withheld.' }
try {
    $artifactManifest = Join-Path $target.WindowsArtifact 'payload-manifest.json'
    $deployedManifest = Join-Path $target.WindowsDeployment 'payload-manifest.json'
    if (!(Test-Path -LiteralPath $artifactManifest -PathType Leaf) -or !(Test-Path -LiteralPath $deployedManifest -PathType Leaf)) { Add-Check 'Windows payload and provenance' 'Unavailable' 'Pinned Windows artifact/deployment manifest is missing.' }
    else {
        Assert-NoLinks $target.WindowsArtifact
        Assert-NoLinks $target.WindowsDeployment
        foreach($root in @($target.WindowsArtifact,$target.WindowsDeployment)) {
            if ((Get-FileHash -LiteralPath (Join-Path $root 'payload-manifest.json')).Hash -cne $target.WindowsManifestSha256) { throw 'Manifest does not match pinned identity.' }
            $null = Test-PayloadManifest -RootPath $root
            $null = Test-WindowsArtifactEnvironment -RootPath $root -Environment $target.Environment -Version $target.WindowsVersion
            # A second canonical pass detects payload changes during the first pass.
            $null = Test-PayloadManifest -RootPath $root
            if ((Get-FileHash -LiteralPath (Join-Path $root 'payload-manifest.json')).Hash -cne $target.WindowsManifestSha256) { throw 'Manifest changed.' }
        }
        Add-Check 'Windows payload and provenance' 'Passed' "Windows $($target.WindowsVersion) matches pinned manifest and manifest-covered build provenance for $($target.Environment). Native runtime routing is not asserted."
    }
} catch { Add-Check 'Windows payload and provenance' 'Failed' 'Windows manifest/payload/environment provenance is unsafe, unreadable, changed or mismatched.' }
ConvertTo-Json -InputObject @($checks) -Depth 4 -Compress
