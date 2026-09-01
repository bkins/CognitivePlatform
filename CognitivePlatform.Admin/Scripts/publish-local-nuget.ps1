<#
.SYNOPSIS
Builds and publishes local NuGet packages for CP shared libraries,
automatically bumping the patch version.

.DESCRIPTION
This script builds and publishes one or more CP NuGet packages
to a local NuGet feed.

Before publishing, the script automatically increments the PATCH
version number in the project file. Major and minor versions are
left untouched and must be updated manually.

Supported targets:
- CP.Shared.Primitives
- CP.Client.Core
- Both packages (default)

Packages are built in Release configuration and pushed to a local
NuGet source using `dotnet nuget push`.

Duplicate packages are skipped automatically.

.PARAMETER Target
Specifies which package(s) to publish.

Valid values:
- Primitives : Publish CP.Shared.Primitives only
- Core       : Publish CP.Client.Core only
- All        : Publish both packages (default)

.EXAMPLE
.\publish-local-nuget.ps1

Bumps patch versions and publishes both packages.

.EXAMPLE
.\publish-local-nuget.ps1 -Target Core

Bumps the patch version and publishes only CP.Client.Core.

.NOTES
Local NuGet feed path:
C:\Users\benho\source\NuGet\Local

This script intentionally:
- Auto-increments PATCH version only
- Does not auto-increment MAJOR or MINOR
- Does not publish Debug builds
- Does not push to remote feeds
#>

param (
    [ValidateSet("Primitives", "Core", "All")]
    [string]$Target = "All"
)

[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$OutputEncoding = [System.Text.UTF8Encoding]::new($false)

$ErrorActionPreference = "Stop"

$LocalNuGetSource = "C:\Users\benho\source\NuGet\Local"

$Projects = @{
    "Primitives" = "C:\Users\benho\source\repos\CP.Shared.Primitives\CP.Shared.Primitives\CP.Shared.Primitives.csproj"
    "Core"       = "C:\Users\benho\source\repos\CP.Client.Core\CP.Client.Core\CP.Client.Core.csproj"
}

function Update-ProjectVersion {
    param (
            [string]$ProjectPath
        )
    
        $versionFile = "C:\Users\benho\source\repos\CP\CP.Workbench\version.json"
        if (-not (Test-Path $versionFile)) {
            throw "Central version file not found at $versionFile"
        }
    
        $versionJson = Get-Content $versionFile | ConvertFrom-Json
        $newVersion = "$($versionJson.Major).$($versionJson.Minor).$($versionJson.Patch).$($versionJson.Build)"
    
        Write-Host "Updating version in $ProjectPath to $newVersion"
    
        [xml]$csproj = Get-Content $ProjectPath
        $versionNode = $csproj.SelectSingleNode("//Version")
    
        if (-not $versionNode) {
            throw "No <Version> element found in $ProjectPath."
        }
    
        $currentVersion = $versionNode.InnerText.Trim()
        $versionNode.InnerText = $newVersion
        $csproj.Save($ProjectPath)
        
        return $currentVersion
    }
    
    function Publish-Package {
        param (
            [string]$ProjectPath
        )
    
        $oldVersion = Update-ProjectVersion $ProjectPath
        
        # Redirect verbose output to null
        dotnet build $ProjectPath -c Release > $null 2>&1
    
        $PackageDir = Join-Path (Split-Path $ProjectPath) "bin\Release"
        $Packages = Get-ChildItem $PackageDir -Filter "*.nupkg" |
                    Where-Object { $_.Name -notmatch "\.symbols\.nupkg$" }
    
        if ($Packages.Count -eq 0) {
            throw "No NuGet packages found in $PackageDir"
        }
    
        foreach ($pkg in $Packages) {
            dotnet nuget push $pkg.FullName `
                --source $LocalNuGetSource `
                --skip-duplicate > $null 2>&1
        }
        
        # Output only what matters
        $newVersion = ([xml](Get-Content $ProjectPath)).SelectSingleNode("//Version").InnerText
        
        # Extract package name from path
        $packageName = (Split-Path $ProjectPath -Leaf).Replace(".csproj", "")
        
        Write-Output "✅  $packageName $oldVersion → $newVersion"
    }

switch ($Target) {
          "Primitives" {
              Publish-Package $Projects["Primitives"]
          }
          "Core" {
              Publish-Package $Projects["Core"]
          }
          "All" {
              Write-Output "Publishing NuGet packages:"
              Publish-Package $Projects["Primitives"]
              Publish-Package $Projects["Core"]
              Write-Output "All packages published successfully."
          }
      }