[CmdletBinding()]
param(
    [string]$OutputDir = "",
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

$args += $EntryScript

Write-Host "Building FUSE-Installer.exe..."
Write-Host "  entry:   $EntryScript"
Write-Host "  out:     $OutputDir"
Write-Host "  hidden:  $($HiddenImports -join ', ')"

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

Write-Host "Built: $exePath"
