# ============================================================
# ChatGPT Sync Script – FINAL v5
# Completely ignores entire obj/ and bin/ folders at traversal level
# No auto-generated files will EVER be scanned again
# ============================================================

function Get-HandwrittenCsFiles($root) {

    $result = @()

    foreach ($item in Get-ChildItem -Path $root) {

        if ($item.PSIsContainer) {

            # ------------------------------------------------------
            # Skip entire folders, never descend into them
            # ------------------------------------------------------
            if ($item.Name -in @("obj", "bin", "ChatGPT.Sync")) {
                continue
            }

            # Recurse into allowed directories
            $result += Get-HandwrittenCsFiles $item.FullName
        }
        else {

            # Only include .cs files
            if ($item.Extension -eq ".cs") {

                # Skip auto-generated files
                if ($item.Name -match "\.g\.cs$") { continue }
                if ($item.Name -match "\.generated\.cs$") { continue }
                if ($item.Name -match "AssemblyAttributes") { continue }
                if ($item.Name -match "AssemblyInfo") { continue }
                if ($item.Name -match "GlobalUsings") { continue }

                $result += $item
            }
        }
    }

    return $result
}

function Normalize-FilesForCompare($files) {
    if ($files -and $files.Count -gt 0) { return $files }

    return @([PSCustomObject]@{
        FullName      = ""
        Length        = 0
        LastWriteTime = [DateTime]::MinValue
    })
}

# -------------------------------------------------------------------
# Copy documentation files (flattened) into Upload/Docs
# -------------------------------------------------------------------
function Copy-DocsToUpload($docsSource, $uploadRoot) {

    $docsFolder = Join-Path $uploadRoot "Docs"

    if (Test-Path $docsFolder) { Remove-Item $docsFolder -Recurse -Force }
    New-Item -ItemType Directory -Path $docsFolder | Out-Null

    # Get ALL files, any extension
    Get-ChildItem -Path $docsSource -Recurse -File | ForEach-Object {
        Copy-Item $_.FullName -Destination (Join-Path $docsFolder $_.Name) -Force
    }

    Write-Host "Documentation copied to: $docsFolder" -ForegroundColor Green
}


# -------------------------------------------------------------------
# ROOTS
# -------------------------------------------------------------------
$repoRoot  = "C:\Users\benho\source\repos\CognitivePlatform"
$syncRoot  = "$repoRoot\ChatGPT.Sync\Active"
$uploadDir = "$repoRoot\ChatGPT.Sync\Upload"

$include = @("CognitivePlatform")

Write-Host ""
Write-Host "=== ChatGPT Sync + Upload Prep (Final v5) ===" -ForegroundColor Cyan

# Reset upload folder
if (Test-Path $uploadDir) { Remove-Item $uploadDir -Recurse -Force }
New-Item -ItemType Directory -Path $uploadDir | Out-Null

# -------------------------------------------------------------------
# STEP 1 — Sync handwritten .cs files
# -------------------------------------------------------------------
foreach ($folder in $include) {

    $srcPath  = Join-Path $repoRoot $folder
    $syncPath = Join-Path $syncRoot $folder
    if (-not (Test-Path $syncPath)) {
        New-Item -ItemType Directory -Path $syncPath | Out-Null
    }

    $sourceFiles = Normalize-FilesForCompare (Get-HandwrittenCsFiles $srcPath)
    $syncFiles   = Normalize-FilesForCompare (Get-HandwrittenCsFiles $syncPath)

    $diff = Compare-Object `
        -ReferenceObject $sourceFiles `
        -DifferenceObject $syncFiles `
        -Property FullName, Length, LastWriteTime `
        -PassThru

    if ($diff) {
        Write-Host "`n-- Changes in $folder --" -ForegroundColor Yellow

        foreach ($d in $diff) {

            if ($d.SideIndicator -eq "<=") {
                $relative = $d.FullName.Replace("$repoRoot\", "")
                Write-Host "Added:    $relative" -ForegroundColor Green
            }
            elseif ($d.SideIndicator -eq "=>") {
                $relative = $d.FullName.Replace("$syncRoot\", "")
                Write-Host "Deleted:  $relative" -ForegroundColor DarkYellow
            }
            else {
                $relative = $d.FullName.Replace("$repoRoot\", "")
                Write-Host "Modified: $relative" -ForegroundColor Magenta
            }
        }

        # Sync handwritten .cs files (structure preserved)
        foreach ($file in Get-HandwrittenCsFiles $srcPath) {

            $relative = $file.FullName.Replace("$repoRoot\", "")
            $destPath = Join-Path $syncRoot $relative
            $destDir  = Split-Path $destPath

            if (-not (Test-Path $destDir)) {
                New-Item -ItemType Directory -Path $destDir -Force | Out-Null
            }

            Copy-Item $file.FullName -Destination $destPath -Force
        }
    }
    else {
        Write-Host "No changes in $folder"
    }
}


# -------------------------------------------------------------------
# STEP 2 — Flatten into Upload folder
# -------------------------------------------------------------------
Write-Host "`nFlattening Active files into Upload..." -ForegroundColor Cyan

foreach ($file in Get-HandwrittenCsFiles $syncRoot) {
    Copy-Item $file.FullName -Destination (Join-Path $uploadDir $file.Name) -Force
}

# -------------------------------------------------------------------
# STEP 3 — Copy development documentation into Upload/Docs
# -------------------------------------------------------------------
$docsSource = "C:\Users\benho\source\Application Documentation\Natural Language Command System"
Copy-DocsToUpload -docsSource $docsSource -uploadRoot $uploadDir


Write-Host "Upload folder ready: $uploadDir" -ForegroundColor Green

Start-Process explorer.exe $uploadDir

Write-Host "`n=== DONE — Drag files from Upload/ into ChatGPT ===" -ForegroundColor Cyan

# ============================================================
# END — Final v5
# ============================================================
