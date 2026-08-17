[CmdletBinding()]
param(
    [string]$OutputDir = "",
    [string]$FusePayload = "",
    [switch]$InstallPyInstaller
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ToolsRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Split-Path -Parent $ToolsRoot
$EntryScript = Join-Path $ToolsRoot 'fuse_installer.py'
$IconPath = Join-Path $ToolsRoot 'assets\fuse_converter.ico'

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $RepoRoot 'dist'
}

function Invoke-InstallerPython {
    param(
        [Parameter(ValueFromRemainingArguments = $true)]
        [string[]]$Arguments
    )

    $python = Get-Command python -ErrorAction SilentlyContinue
    if ($null -ne $python) {
        & python @Arguments | ForEach-Object { Write-Host $_ }
        $exitCode = $LASTEXITCODE
        return $exitCode
    }

    $py = Get-Command py -ErrorAction SilentlyContinue
    if ($null -ne $py) {
        & py -3 @Arguments | ForEach-Object { Write-Host $_ }
    }
    else {
        Write-Host "Neither py -3 nor python could be started."
        return 1
    }

    $exitCode = $LASTEXITCODE
    return $exitCode
}

if (-not (Test-Path -LiteralPath $EntryScript)) {
    throw "Installer entry script not found: $EntryScript"
}

if (-not (Test-Path -LiteralPath $IconPath)) {
    throw "FUSE installer icon not found: $IconPath"
}

$versionExit = Invoke-InstallerPython -m PyInstaller --version
if ($versionExit -ne 0) {
    if (-not $InstallPyInstaller) {
        Write-Host "PyInstaller is not installed for this Python environment."
        Write-Host "Run this again with -InstallPyInstaller, or install manually with:"
        Write-Host "  py -3 -m pip install pyinstaller"
        exit 1
    }

    Write-Host "Installing PyInstaller..."
    $installExit = Invoke-InstallerPython -m pip install pyinstaller
    if ($installExit -ne 0) {
        throw "Failed to install PyInstaller."
    }
}

$OutputDir = [System.IO.Path]::GetFullPath($OutputDir)
$WorkDir = Join-Path $RepoRoot '_work\pyinstaller-fuse-installer'
$SpecDir = Join-Path $WorkDir 'spec'

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null
New-Item -ItemType Directory -Force -Path $SpecDir | Out-Null

$HiddenImports = @(
    'legacy_json'
)

$args = @(
    '-m', 'PyInstaller',
    '--clean',
    '--noconfirm',
    '--onefile',
    '--name', 'FUSE-Installer',
    '--icon', $IconPath,
    '--distpath', $OutputDir,
    '--workpath', $WorkDir,
    '--specpath', $SpecDir,
    '--paths', $ToolsRoot
)

foreach ($mod in $HiddenImports) {
    $args += '--hidden-import'
    $args += $mod
}

# Bundle the FUSE mod zip into the exe so a manual (no-argument) run installs
# FUSE. PyInstaller's onefile bundle unpacks this to sys._MEIPASS at runtime,
# where fuse_installer.resolve_bundled_fuse() picks it up as bundled_fuse.zip.
$BundledFuse = ''
if (-not [string]::IsNullOrWhiteSpace($FusePayload)) {
    # -PathType Leaf so a directory can't slip through (Copy-Item would then make
    # bundled_fuse.zip a folder instead of the zip file).
    if (-not (Test-Path -LiteralPath $FusePayload -PathType Leaf)) {
        throw "FUSE payload not found or not a file: $FusePayload"
    }
    $BundledFuse = Join-Path $WorkDir 'bundled_fuse.zip'
    Copy-Item -LiteralPath $FusePayload -Destination $BundledFuse -Force
    # On Windows, --add-data separates the source and destination with ';'.
    $args += '--add-data'
    $args += "$BundledFuse;."
}
else {
    Write-Host "WARNING: no -FusePayload given; the exe will NOT self-install FUSE on a manual run."
    Write-Host "         Pass -FusePayload <path to FUSE-v*.zip> to enable that."
}

$args += $EntryScript

Write-Host "Building FUSE-Installer.exe..."
Write-Host "  entry:   $EntryScript"
Write-Host "  out:     $OutputDir"
Write-Host "  hidden:  $($HiddenImports -join ', ')"
Write-Host "  bundled: $(if ($BundledFuse) { $FusePayload } else { '(none)' })"

$buildExit = Invoke-InstallerPython @args
if ($buildExit -ne 0) {
    throw "PyInstaller build failed."
}

$exePath = Join-Path $OutputDir 'FUSE-Installer.exe'
if (-not (Test-Path -LiteralPath $exePath)) {
    throw "Build completed but exe was not found: $exePath"
}

Write-Host "Smoke check: running '$exePath --help'..."
$smokeOutput = & $exePath --help 2>&1
$smokeExit = $LASTEXITCODE
if ($smokeExit -ne 0) {
    Write-Host $smokeOutput
    throw "Smoke check failed (exit=$smokeExit)."
}

# Self-check: prove that a manual (no-argument) run will actually install FUSE.
# Runs a REAL install (not --dry-run) into a throwaway game dir, so the check
# exercises zip extraction and file writes — catching unreadable members or
# write-path failures — and then asserts the mod actually landed on disk.
if (-not [string]::IsNullOrWhiteSpace($BundledFuse)) {
    Write-Host "Self-check: installing bundled FUSE into a throwaway dir..."
    $ProbeDir = Join-Path $WorkDir 'selfcheck'
    if (Test-Path $ProbeDir) { Remove-Item $ProbeDir -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $ProbeDir | Out-Null
    $selfOutput = & $exePath --no-pause --game-dir $ProbeDir 2>&1
    $selfExit = $LASTEXITCODE
    Write-Host $selfOutput
    if ($selfExit -ne 0) {
        throw "Self-check failed (exit=$selfExit): bundled FUSE install did not succeed."
    }
    # Match the inspected package id, not just the word FUSE (which also appears
    # in the static "bundled FUSE:" / "FUSE:" output labels), so a non-FUSE
    # payload can't silently pass the self-check.
    if ("$selfOutput" -notmatch 'id=FUSE(\s|$)') {
        throw "Self-check failed: bundled package is not FUSE (id=FUSE missing from install output)."
    }
    # Confirm extraction actually wrote the mod to disk.
    $installedInfo = Join-Path $ProbeDir 'Mods\FUSE\Info.json'
    if (-not (Test-Path -LiteralPath $installedInfo -PathType Leaf)) {
        throw "Self-check failed: expected $installedInfo after installing bundled FUSE."
    }
    Write-Host "Self-check passed: a manual run installs FUSE to Mods\FUSE."
}

Write-Host "Built: $exePath"
