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
    '--windowed',
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
$smokeProcess = Start-Process `
    -FilePath $exePath `
    -ArgumentList @('--help') `
    -WindowStyle Hidden `
    -Wait `
    -PassThru
$smokeExit = $smokeProcess.ExitCode
if ($smokeExit -ne 0) {
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
    $ProbeUmmDir = Join-Path $ProbeDir 'Railroader_Data\Managed\UnityModManager'
    New-Item -ItemType Directory -Force -Path $ProbeUmmDir | Out-Null
    New-Item -ItemType File -Force -Path (Join-Path $ProbeDir 'Railroader.exe') | Out-Null
    New-Item -ItemType File -Force -Path (Join-Path $ProbeUmmDir 'UnityModManager.dll') | Out-Null
    # PyInstaller's windowed one-file launcher can return control to a PowerShell
    # invocation before its extracted child process has finished. Start-Process
    # -Wait follows the GUI process tree, so the assertions below cannot race the
    # actual install (the old direct '& $exePath' check intermittently reported
    # a missing Info.json while the child was still writing it).
    $quotedProbeDir = '"' + $ProbeDir + '"'
    $selfProcess = Start-Process `
        -FilePath $exePath `
        -ArgumentList @('--cli', '--no-pause', '--game-dir', $quotedProbeDir) `
        -WindowStyle Hidden `
        -Wait `
        -PassThru
    $selfExit = $selfProcess.ExitCode
    if ($selfExit -ne 0) {
        throw "Self-check failed (exit=$selfExit): bundled FUSE install did not succeed."
    }
    # Confirm extraction actually wrote the mod to disk.
    $installedInfo = Join-Path $ProbeDir 'Mods\FUSE\Info.json'
    if (-not (Test-Path -LiteralPath $installedInfo -PathType Leaf)) {
        throw "Self-check failed: expected $installedInfo after installing bundled FUSE."
    }
    $installedManifest = Get-Content -LiteralPath $installedInfo -Raw | ConvertFrom-Json
    $installedId = if ($null -ne $installedManifest.Id) { [string]$installedManifest.Id } else { [string]$installedManifest.id }
    if ($installedId -ne 'FUSE') {
        throw "Self-check failed: bundled package id is '$installedId', expected 'FUSE'."
    }
    $assetLoaderInfo = Join-Path $ProbeDir 'Mods\AssetLoader\Info.json'
    if (-not (Test-Path -LiteralPath $assetLoaderInfo -PathType Leaf)) {
        throw "Self-check failed: expected FUSE's data-only AssetLoader alias at $assetLoaderInfo."
    }
    $assetLoaderManifest = Get-Content -LiteralPath $assetLoaderInfo -Raw | ConvertFrom-Json
    if ([string]$assetLoaderManifest.FuseProvidedCompatibility -ne 'FUSE.AssetLoaderCompatibility') {
        throw "Self-check failed: the AssetLoader alias is missing FUSE's compatibility marker."
    }
    $assetLoaderDll = Join-Path $ProbeDir 'Mods\AssetLoader\AssetLoader.dll'
    if (Test-Path -LiteralPath $assetLoaderDll) {
        throw "Self-check failed: old AssetLoader runtime DLL remains installed at $assetLoaderDll."
    }
    Write-Host "Self-check passed: FUSE and its DLL-free AssetLoader dependency alias were installed."
}

Write-Host "Built: $exePath"
