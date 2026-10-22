Set-StrictMode -Version Latest

function Test-WindowsArtifactEnvironment {
    param (
        [Parameter(Mandatory)][string]$RootPath
      , [Parameter(Mandatory)][ValidateSet('Dev', 'QA', 'Prod')][string]$Environment
      , [Parameter(Mandatory)][string]$Version
    )

    $provenancePath = Join-Path $RootPath 'build-environment.json'
    if (-not (Test-Path -LiteralPath $provenancePath -PathType Leaf)) {
        throw 'build-environment.json is missing. Rebuild Windows LAA for the selected environment; legacy artifacts cannot be promoted.'
    }
    try {
        $provenance = Get-Content -LiteralPath $provenancePath -Raw -Encoding UTF8 | ConvertFrom-Json -AsHashtable
        if ($provenance -isnot [System.Collections.IDictionary] -or $provenance.SchemaVersion -ne 1) {
            throw 'Unsupported provenance schema.'
        }
        foreach ($field in @('BuiltFor', 'Version', 'ApiBaseUrl')) {
            if ($provenance[$field] -isnot [string] -or [string]::IsNullOrWhiteSpace($provenance[$field])) {
                throw "Missing or invalid $field."
            }
        }
    }
    catch {
        throw "build-environment.json is invalid. Rebuild Windows LAA. $($_.Exception.Message)"
    }

    $endpoints = @{ 'DEV' = 'http://localhost:5273'; 'QA' = 'http://localhost:5274'; 'PROD' = 'http://localhost:5275' }
    $builtFor = $provenance.BuiltFor.ToUpperInvariant()
    if (-not $endpoints.ContainsKey($builtFor)) {
        throw "Windows artifact environment is invalid: $($provenance.BuiltFor). Rebuild Windows LAA."
    }
    if ($builtFor -ne $Environment.ToUpperInvariant()) {
        throw "Windows artifact was built for $builtFor, not $Environment. Build a new Windows LAA artifact for $Environment; cross-environment promotion is not supported."
    }
    if ($provenance.Version -cne $Version) {
        throw "Windows artifact version $($provenance.Version) does not match requested version $Version. Select the matching artifact."
    }
    if ($provenance.ApiBaseUrl -cne $endpoints[$builtFor]) {
        throw "Windows artifact endpoint does not match $builtFor. Rebuild Windows LAA for the selected environment."
    }
    return $provenance
}
