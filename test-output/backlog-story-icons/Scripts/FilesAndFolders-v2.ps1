
#Getting:
# You cannot run this script on the current system. For 
# more information about running scripts and setting execution policy, see 
# about_Execution_Policies at https:/go.microsoft.com/fwlink/?LinkID=135170.

#Run this 1st: 
# Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser

#Requires -Version 5.1

<#
.SYNOPSIS
    Generates a directory tree tailored for .NET solutions.

.DESCRIPTION
    Recursively prints a directory tree, excluding common development folders.
    Supports smart emoji icons for .NET file types, optional file export, 
    local timestamp, and a summary. Automatically formats Markdown (.md) exports.

.PARAMETER Path
    Root path to scan. Defaults to the script directory.

.PARAMETER Emoji
    Use emoji/icons alongside clean tree graphics.

.PARAMETER OutputFile
    Optional UTF-8 output file (.txt or .md). Console output is still displayed.

.PARAMETER ShowSummary
    Display file/folder counts at the end.

.EXAMPLE
    .\Get-SolutionTree.ps1 -Emoji -OutputFile .\tree.md
#>


<#
@tool
Name=Files and Folders
Category=Utilities
Description=Generates a directory tree tailored for .NET solutions.
Order=100
Icon=Folder
RequiresConfirmation=false
Hidden=false
#>

param
(
      # @Label Root Path 
      [string]$Path = $PSScriptRoot

      # @Label Use Emoji Icons 
    , [switch]$Emoji

      # @Label Output File
    , [string]$OutputFile

      # @Label Show Summary
    , [switch]$ShowSummary = $true
)

$script:Lines       = [System.Collections.Generic.List[string]]::new()
$script:FolderCount = 0
$script:FileCount   = 0

# Exclusions tailored for modern .NET, Python, and IDE noise
$Exclude = @(
                 "bin"
                ,"obj"
                ,".git"
                ,".vs"
                ,".idea"
                ,".vscode"
                ,".gitignore"
                ,".gitattributes"
                ,"node_modules"
                ,"AppPackages"
                ,"_ReSharper.Caches"
                ,"publish"
            )

# Standard Extension Mapping Table
$ExtensionIcons = 
@{
    ".sln"      = "🚀 "  # Solution
    ".slnx"     = "🚀 "  # Modern Solution XML
    ".csproj"   = "🛠️ "  # C# Project
    ".cs"       = "📝 "  # C# Code File
    ".razor"    = "🧩 "  # Blazor Component
    ".xaml"     = "🎨 "  # XAML Layout
    ".py"       = "🐍 "  # Python Code
    ".pyc"      = "🐍 "  # Compiled Python
    ".ps1"      = "📜 "  # PowerShell Script
    ".db"       = "🛢️ "  # Database Instance
    ".json"     = "⚙️ "  # Configuration
    ".xml"      = "⚙️ "  # Configuration
    ".config"   = "⚙️ "  # Configuration
    ".yaml"     = "🔌 "  # General YAML
    ".yml"      = "🔌 "  # General YAML
    ".md"       = "📘 "  # Markdown Docs
    ".txt"      = "💬 "  # Plain Text / Prompts
    ".png"      = "🖼️ "  # Image Assets
    ".ico"      = "🖼️ "  # Icon Assets
    ".zip"      = "🗜️ "  # Archives
    ".trx"      = "📊 "  # Test Results Log
}

function Write-Line([string]$Text) 
{
    Write-Host $Text
    $script:Lines.Add($Text) | Out-Null
}

function Show-Tree 
{
    param
    (
          [string]$Current
        , [string]$Prefix = ""
        , [int]$Level = 0
    )

    if ($Exclude -contains (Split-Path $Current -Leaf)) { return }

    $items = Get-ChildItem -LiteralPath $Current -Force |
        Where-Object { $_.Name -notin $Exclude } |
        Sort-Object @{Expression='PSIsContainer';Descending=$true}, Name

    if ($Level -eq 0) 
    {
        if ($Emoji) 
        { 
            Write-Line "📦 $Current" 
        } 
        else 
        { 
            Write-Line $Current 
        }
    }

    for ($i=0; $i -lt $items.Count; $i++) 
    {
        $item = $items[$i]
        $last = ($i -eq ($items.Count-1))
        
        $branch = $(if($last)
                    {
                        "└── "
                    }
                    else
                    {
                        "├── "
                    })
        $nextPrefix = $(if($last)
                        {
                            $Prefix+"    "
                        }
                        else
                        {
                            $Prefix+"│   "
                        })
        
        $emojiStr = ""

        if ($Emoji) 
        {
            if ($item.PSIsContainer) 
            {
                $emojiStr = "📁 "
            } 
            else 
            {
                $nameLower = $item.Name.ToLower()
                
                # 1. Match complex/compound file definitions first
                if ($nameLower.EndsWith(".request.yaml") -or $nameLower.EndsWith(".request.yml")) 
                {
                    $emojiStr = "📡 "
                } 
                elseif ($nameLower.EndsWith(".run.xml")) 
                {
                    $emojiStr = "🏃 "
                } 
                else 
                {
                    # 2. Fallback to standard extension mapping
                    $ext = $item.Extension.ToLower()
                    if ($ExtensionIcons.ContainsKey($ext)) 
                    {
                        $emojiStr = $ExtensionIcons[$ext]
                    } 
                    else 
                    {
                        $emojiStr = "📄 " # Default document fallback
                    }
                }
            }
        }

        if ($item.PSIsContainer) 
        {
            $script:FolderCount++
            Write-Line ($Prefix + $branch + $emojiStr + $item.Name)
            Show-Tree -Current $item.FullName -Prefix $nextPrefix -Level ($Level+1)
        } 
        else 
        {
            $script:FileCount++
            Write-Line ($Prefix + $branch + $emojiStr + $item.Name)
        }
    }
}

$ts = Get-Date -Format "yyyy-MM-dd hh:mm:ss tt"
Write-Line ("Generated: " + $ts)
Write-Line ("─" * 80)
Show-Tree -Current $Path

if ($ShowSummary) 
{
    Write-Line ("─" * 80)
    Write-Line ("Folders : {0}" -f $script:FolderCount)
    Write-Line ("Files   : {0}" -f $script:FileCount)
}

if ($OutputFile) 
{
    $dir = Split-Path -Parent $OutputFile
    if ($dir -and -not (Test-Path $dir)) 
    {
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
    }
    
    if ($OutputFile -like "*.md") 
    {
        $mdLines = [System.Collections.Generic.List[string]]::new()
        $mdLines.Add("<pre style=""line-height: 1.05; font-family: 'Cascadia Code', 'JetBrains Mono', Consolas, monospace; font-size: 14px;"">")
        foreach ($line in $script:Lines) 
        {
            $safeLine = $line.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
            $mdLines.Add($safeLine)
        }
        $mdLines.Add("</pre>")
        $mdLines | Set-Content -Path $OutputFile -Encoding utf8
    } 
    else 
    {
        $script:Lines | Set-Content -Path $OutputFile -Encoding utf8
    }
}