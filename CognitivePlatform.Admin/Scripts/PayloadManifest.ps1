Set-StrictMode -Version Latest

$script:PayloadManifestName = "payload-manifest.json"
$script:PayloadEvidenceNames = [System.Collections.Generic.HashSet[string]]::new(
    [string[]]@($script:PayloadManifestName, "metadata.json", "deployment.json")
  , [System.StringComparer]::OrdinalIgnoreCase)

function Get-PayloadRelativePaths {
    param (
        [Parameter(Mandatory)]
        [string]$RootPath
    )

    $resolvedRoot = [System.IO.Path]::GetFullPath((Resolve-Path -LiteralPath $RootPath).Path)
    $relativePaths = [System.Collections.Generic.List[string]]::new()

    foreach ($file in Get-ChildItem -LiteralPath $resolvedRoot -File -Recurse -Force) {
        $relativePath = [System.IO.Path]::GetRelativePath($resolvedRoot, $file.FullName).Replace('\', '/')
        if (-not $script:PayloadEvidenceNames.Contains($relativePath)) {
            $relativePaths.Add($relativePath)
        }
    }

    $relativePaths.Sort([System.StringComparer]::Ordinal)
    return $relativePaths.ToArray()
}

function Resolve-PayloadFilePath {
    param (
        [Parameter(Mandatory)]
        [string]$RootPath

      , [Parameter(Mandatory)]
        [string]$RelativePath
    )

    if ([System.IO.Path]::IsPathRooted($RelativePath) -or $RelativePath.Split('/') -contains '..') {
        throw "Payload manifest contains an unsafe relative path: $RelativePath"
    }

    $resolvedRoot = [System.IO.Path]::GetFullPath((Resolve-Path -LiteralPath $RootPath).Path)
    $rootPrefix = $resolvedRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    $nativeRelativePath = $RelativePath.Replace('/', [System.IO.Path]::DirectorySeparatorChar)
    $filePath = [System.IO.Path]::GetFullPath((Join-Path $resolvedRoot $nativeRelativePath))

    if (-not $filePath.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Payload manifest path escapes the payload root: $RelativePath"
    }

    return $filePath
}

function New-PayloadManifest {
    param (
        [Parameter(Mandatory)]
        [string]$RootPath
    )

    $resolvedRoot = [System.IO.Path]::GetFullPath((Resolve-Path -LiteralPath $RootPath).Path)
    $entries = foreach ($relativePath in Get-PayloadRelativePaths -RootPath $resolvedRoot) {
        $filePath = Resolve-PayloadFilePath -RootPath $resolvedRoot -RelativePath $relativePath
        $file = Get-Item -LiteralPath $filePath
        [PSCustomObject][ordered]@{
            RelativePath = $relativePath
            Length       = [long]$file.Length
            Sha256       = (Get-FileHash -Algorithm SHA256 -LiteralPath $filePath).Hash
        }
    }

    $manifest = [PSCustomObject][ordered]@{
        SchemaVersion = 1
        Algorithm     = "SHA256"
        Files         = @($entries)
    }
    $manifestPath = Join-Path $resolvedRoot $script:PayloadManifestName
    $manifestJson = $manifest | ConvertTo-Json -Depth 5
    [System.IO.File]::WriteAllText($manifestPath, $manifestJson, [System.Text.UTF8Encoding]::new($false))

    return [PSCustomObject]@{
        ManifestPath   = $manifestPath
        ManifestSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $manifestPath).Hash
        FileCount      = @($entries).Count
    }
}

function Test-PayloadManifest {
    param (
        [Parameter(Mandatory)]
        [string]$RootPath
    )

    $resolvedRoot = [System.IO.Path]::GetFullPath((Resolve-Path -LiteralPath $RootPath).Path)
    $manifestPath = Join-Path $resolvedRoot $script:PayloadManifestName
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "Payload manifest mismatch at [$resolvedRoot]: $script:PayloadManifestName is missing."
    }

    try {
        $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    }
    catch {
        throw "Payload manifest mismatch at [$resolvedRoot]: manifest JSON is invalid. $($_.Exception.Message)"
    }

    if ($manifest.SchemaVersion -ne 1 -or $manifest.Algorithm -ne "SHA256") {
        throw "Payload manifest mismatch at [$resolvedRoot]: unsupported schema or algorithm."
    }

    $entriesByPath = [System.Collections.Generic.Dictionary[string, object]]::new([System.StringComparer]::Ordinal)
    foreach ($entry in @($manifest.Files)) {
        $relativePath = [string]$entry.RelativePath
        if ([string]::IsNullOrWhiteSpace($relativePath) -or $entriesByPath.ContainsKey($relativePath)) {
            throw "Payload manifest mismatch at [$resolvedRoot]: missing or duplicate relative path."
        }
        $entriesByPath.Add($relativePath, $entry)
    }

    $actualPaths = @(Get-PayloadRelativePaths -RootPath $resolvedRoot)
    if ($actualPaths.Count -ne $entriesByPath.Count) {
        throw "Payload manifest mismatch at [$resolvedRoot]: expected $($entriesByPath.Count) payload files but found $($actualPaths.Count)."
    }

    foreach ($relativePath in $actualPaths) {
        if (-not $entriesByPath.ContainsKey($relativePath)) {
            throw "Payload manifest mismatch at [$resolvedRoot]: unexpected payload file [$relativePath]."
        }

        $filePath = Resolve-PayloadFilePath -RootPath $resolvedRoot -RelativePath $relativePath
        $file = Get-Item -LiteralPath $filePath
        $entry = $entriesByPath[$relativePath]
        if ([long]$entry.Length -ne [long]$file.Length) {
            throw "Payload manifest mismatch at [$resolvedRoot]: length differs for [$relativePath]."
        }

        $actualSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $filePath).Hash
        if (-not $actualSha256.Equals([string]$entry.Sha256, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Payload manifest mismatch at [$resolvedRoot]: SHA-256 differs for [$relativePath]."
        }
    }

    return [PSCustomObject]@{
        ManifestPath   = $manifestPath
        ManifestSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $manifestPath).Hash
        FileCount      = $entriesByPath.Count
    }
}
