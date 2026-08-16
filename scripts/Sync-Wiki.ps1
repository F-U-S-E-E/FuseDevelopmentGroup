<#
.SYNOPSIS
    Mirrors the user-facing docs/ pages into the GitHub Wiki.

.DESCRIPTION
    The repo is the source of truth. This script clones (or updates) the wiki
    repo, copies the user-facing subset of docs/ into it under wiki page names,
    rewrites relative Markdown links to wiki links, regenerates Home and the
    sidebar, then commits and pushes.

    Contributor docs (ARCHITECTURE, RELEASING, EDITOR_*, CHANGELOG) are
    deliberately NOT mirrored — they are for people already working in the repo.

    Run this after merging a docs change to main.

.PARAMETER WikiUrl
    The wiki remote. Defaults to this repo's wiki.

.PARAMETER WorkDir
    Where to check the wiki out. Defaults to a temp folder.

.PARAMETER DryRun
    Build the wiki content and report the diff without committing or pushing.

.EXAMPLE
    .\scripts\Sync-Wiki.ps1 -DryRun
    .\scripts\Sync-Wiki.ps1
#>
[CmdletBinding()]
param(
    [string] $WikiUrl = 'https://github.com/F-U-S-E-E/FuseDevelopmentGroup.wiki.git',
    [string] $WorkDir = (Join-Path $env:TEMP 'fuse-wiki-sync'),
    [switch] $DryRun
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$docsDir = Join-Path $repoRoot 'docs'
$blobBase = 'https://github.com/F-U-S-E-E/FuseDevelopmentGroup/blob/main'

# docs/ file -> wiki page name. Order drives the sidebar.
$pageMap = [ordered]@{
    'GETTING_STARTED.md'      = 'Getting-Started'
    'FAQ.md'                  = 'FAQ'
    'MIGRATION_FROM_LEGACY.md' = 'Migrating-From-Legacy-Mods'
    'SETTINGS.md'             = 'Settings'
    'CONSOLE_COMMANDS.md'     = 'Console-Commands'
    'TROUBLESHOOTING.md'      = 'Troubleshooting'
    'KNOWN_ISSUES.md'         = 'Known-Issues'
    'FUSE_INSTALLER.md'       = 'Installer'
    'FUSE_CONVERTER.md'       = 'Converter'
    'PACKAGE_AUTHOR_GUIDE.md' = 'Package-Author-Guide'
    'EXTERNAL_EDITOR.md'      = 'External-Editor'
}

# Sidebar grouping.
$sections = [ordered]@{
    'Players'         = @('Getting-Started', 'FAQ', 'Migrating-From-Legacy-Mods', 'Settings', 'Console-Commands', 'Troubleshooting', 'Known-Issues', 'Installer')
    'Package Authors' = @('Converter', 'Package-Author-Guide', 'External-Editor')
}

function Convert-Links {
    <#
        Rewrites Markdown links for the wiki:
          docs-relative .md links   -> wiki page links
          ../schemas/... , ../*.md  -> absolute blob URLs (not mirrored)
          anchors are preserved on both
    #>
    param([string] $Text)

    # Links that escape docs/ (../LICENSE, ../schemas/..., ../CONTRIBUTING.md)
    $Text = [regex]::Replace($Text, '\]\(\.\./([^)#]+)(#[^)]*)?\)', {
        param($m)
        $target = $m.Groups[1].Value
        $anchor = $m.Groups[2].Value
        "]($blobBase/$target$anchor)"
    })

    # Sibling docs/ links -> wiki pages
    $Text = [regex]::Replace($Text, '\]\(([A-Za-z0-9_]+\.md)(#[^)]*)?\)', {
        param($m)
        $file = $m.Groups[1].Value
        $anchor = $m.Groups[2].Value
        if ($pageMap.Contains($file)) {
            "]($($pageMap[$file])$anchor)"
        }
        else {
            # Not mirrored (e.g. ARCHITECTURE.md) — point at the repo copy.
            "]($blobBase/docs/$file$anchor)"
        }
    })

    return $Text
}

Write-Host 'FUSE wiki sync' -ForegroundColor Cyan

# --- Check out the wiki -----------------------------------------------------
if (Test-Path $WorkDir) {
    Write-Host "Updating existing checkout at $WorkDir"
    git -C $WorkDir fetch --quiet origin
    git -C $WorkDir reset --quiet --hard origin/master
}
else {
    Write-Host "Cloning wiki into $WorkDir"
    git clone --quiet $WikiUrl $WorkDir
    if ($LASTEXITCODE -ne 0) {
        throw "Could not clone $WikiUrl. Create the wiki first by adding one page through the GitHub UI — GitHub does not create the wiki repo until it has a page."
    }
}

# Clear previously synced pages so deletions propagate.
Get-ChildItem -Path $WorkDir -Filter '*.md' -File | Remove-Item -Force

# --- Copy pages -------------------------------------------------------------
$copied = 0
foreach ($entry in $pageMap.GetEnumerator()) {
    $source = Join-Path $docsDir $entry.Key
    if (-not (Test-Path $source)) {
        Write-Warning "Missing source doc: $($entry.Key) — skipped."
        continue
    }

    $body = Convert-Links (Get-Content -Raw -Path $source)
    $footer = @"

---

*Mirrored from [``docs/$($entry.Key)``]($blobBase/docs/$($entry.Key)) — edit there, not here.*
"@

    Set-Content -Path (Join-Path $WorkDir "$($entry.Value).md") -Value ($body.TrimEnd() + "`n" + $footer) -Encoding utf8
    $copied++
}

# --- Home -------------------------------------------------------------------
# NOTE: not $home — that is a read-only PowerShell automatic variable.
$homePage = @"
# FUSE

FUSE is a Unity Mod Manager modding layer for Railroader. It loads FUSE data
packages — route extensions, asset packs, audio packs, track graph changes, world
scenery, operations, and progression data — and provides drop-in compatibility for
legacy Railloader, Strange Customs, ConfusingSupplements, For Your Convenience,
and Alina's Map Mod packages.

**New here?** Start with [Getting Started](Getting-Started).

**Coming from Railloader or another legacy stack?** Read
[Migrating From Legacy Mods](Migrating-From-Legacy-Mods) before installing
anything.

## Players

- [Getting Started](Getting-Started) — install, verify, update, uninstall
- [FAQ](FAQ) — legacy mods, multiplayer, saves, performance
- [Settings](Settings) — every setting and its default
- [Console Commands](Console-Commands) — every ``/fuse.*`` command
- [Troubleshooting](Troubleshooting) — symptom to diagnostic
- [Known Issues](Known-Issues) — current limitations
- [Installer](Installer) — installing packages from zips

## Package Authors

- [Converter](Converter) — converting legacy mods
- [Package Author Guide](Package-Author-Guide) — the authoring contract
- [External Editor](External-Editor) — the standalone desktop editor
- [JSON Schema Reference]($blobBase/schemas/FUSE_JSON_SCHEMA.md) — the data contract

## Offline manuals

- [FUSE User Manual (PDF)]($blobBase/docs/pdf/FUSE-User-Manual.pdf)
- [FUSE Package Author Guide (PDF)]($blobBase/docs/pdf/FUSE-Package-Author-Guide.pdf)

## Contributors

Contributor documentation lives in the repository:

- [Contributing]($blobBase/CONTRIBUTING.md)
- [Architecture]($blobBase/docs/ARCHITECTURE.md)
- [Changelog]($blobBase/docs/CHANGELOG.md)
- [Security Policy]($blobBase/SECURITY.md)

## Project

- [Repository](https://github.com/F-U-S-E-E/FuseDevelopmentGroup)
- [Releases](https://github.com/F-U-S-E-E/FuseDevelopmentGroup/releases)
- [Issues](https://github.com/F-U-S-E-E/FuseDevelopmentGroup/issues)
- Licensed under [AGPL-3.0]($blobBase/LICENSE)

---

*This wiki is generated from ``docs/`` in the repository. Edits made here are
overwritten on the next sync — change the repo instead.*
"@
Set-Content -Path (Join-Path $WorkDir 'Home.md') -Value $homePage -Encoding utf8

# --- Sidebar ----------------------------------------------------------------
$sidebar = New-Object System.Text.StringBuilder
[void]$sidebar.AppendLine('**[FUSE](Home)**')
foreach ($section in $sections.GetEnumerator()) {
    [void]$sidebar.AppendLine()
    [void]$sidebar.AppendLine("**$($section.Key)**")
    [void]$sidebar.AppendLine()
    foreach ($page in $section.Value) {
        [void]$sidebar.AppendLine("- [$($page -replace '-', ' ')]($page)")
    }
}
Set-Content -Path (Join-Path $WorkDir '_Sidebar.md') -Value $sidebar.ToString() -Encoding utf8

Write-Host "Wrote $copied page(s) plus Home and _Sidebar." -ForegroundColor Green

# --- Commit -----------------------------------------------------------------
Push-Location $WorkDir
try {
    $status = git status --porcelain
    if (-not $status) {
        Write-Host 'Wiki already up to date — nothing to push.' -ForegroundColor Green
        return
    }

    Write-Host 'Changes:'
    git -c color.status=always status --short

    if ($DryRun) {
        Write-Host "`nDry run — nothing committed or pushed." -ForegroundColor Yellow
        return
    }

    $sha = (git -C $repoRoot rev-parse --short HEAD)
    git add -A
    git commit --quiet -m "docs: sync wiki from repo @ $sha"
    git push --quiet origin HEAD
    Write-Host "Pushed wiki update (source $sha)." -ForegroundColor Green
}
finally {
    Pop-Location
}
