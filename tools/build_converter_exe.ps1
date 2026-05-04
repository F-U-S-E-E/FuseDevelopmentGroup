[CmdletBinding()]
param(
    [string]$OutputDir = "",
    [switch]$InstallPyInstaller
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ToolsRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Split-Path -Parent $ToolsRoot
$EntryScript = Join-Path $ToolsRoot 'fuse_converter.py'
$IconPath = Join-Path $ToolsRoot 'assets\fuse_converter.ico'

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $RepoRoot 'dist'
}

function Invoke-ConverterPython {
    param(
        [Parameter(ValueFromRemainingArguments = $true)]
        [string[]]$Arguments
    )

    $py = Get-Command py -ErrorAction SilentlyContinue
    if ($null -ne $py) {
        & py -3 @Arguments | ForEach-Object { Write-Host $_ }
    }
    else {
        & python @Arguments | ForEach-Object { Write-Host $_ }
    }

    $exitCode = $LASTEXITCODE
    return $exitCode
}

if (-not (Test-Path -LiteralPath $EntryScript)) {
    throw "Converter entry script not found: $EntryScript"
}

if (-not (Test-Path -LiteralPath $IconPath)) {
    throw "FUSE converter icon not found: $IconPath"
}

$versionExit = Invoke-ConverterPython -m PyInstaller --version
if ($versionExit -ne 0) {
    if (-not $InstallPyInstaller) {
        Write-Host "PyInstaller is not installed for this Python environment."
        Write-Host "Run this again with -InstallPyInstaller, or install manually with:"
        Write-Host "  py -3 -m pip install pyinstaller"
        exit 1
    }

    Write-Host "Installing PyInstaller..."
    $installExit = Invoke-ConverterPython -m pip install pyinstaller
    if ($installExit -ne 0) {
        throw "Failed to install PyInstaller."
    }
}

$OutputDir = [System.IO.Path]::GetFullPath($OutputDir)
$WorkDir = Join-Path $RepoRoot '_work\pyinstaller-fuse-converter'
$SpecDir = Join-Path $WorkDir 'spec'

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null
New-Item -ItemType Directory -Force -Path $SpecDir | Out-Null

# PyInstaller's static analysis sees the `import fuse_convert` line in
# fuse_converter.py but the module lives in the same folder and is only on
# sys.path because of a runtime sys.path.insert call - that mutation happens
# after analysis, so the bundle would miss the dependency without explicit
# --hidden-import hints. Same goes for the two sibling helper modules and
# the convert_fuse_audio submodules they pull in transitively.
$HiddenImports = @(
    'fuse_convert',
    'fuse_converter',
    'convert_fuse_audio',
    'legacy_json'
)

$args = @(
    '-m', 'PyInstaller',
    '--clean',
    '--noconfirm',
    '--onefile',
    '--name', 'FUSE-Converter',
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

$args += $EntryScript

Write-Host "Building FUSE-Converter.exe..."
Write-Host "  entry:   $EntryScript"
Write-Host "  out:     $OutputDir"
Write-Host "  hidden:  $($HiddenImports -join ', ')"

$buildExit = Invoke-ConverterPython @args
if ($buildExit -ne 0) {
    throw "PyInstaller build failed."
}

$exePath = Join-Path $OutputDir 'FUSE-Converter.exe'
if (-not (Test-Path -LiteralPath $exePath)) {
    throw "Build completed but exe was not found: $exePath"
}

# Quick smoke check that the bundle actually loaded our patched modules.
# Running --help short-circuits before any conversion work, so it's a cheap
# way to catch a missing-import regression before the user does.
Write-Host "Smoke check: running '$exePath --help'..."
$smokeOutput = & $exePath --help 2>&1
$smokeExit = $LASTEXITCODE
if ($smokeExit -ne 0) {
    Write-Host $smokeOutput
    throw "Smoke check failed (exit=$smokeExit)."
}

Write-Host "Built: $exePath"
