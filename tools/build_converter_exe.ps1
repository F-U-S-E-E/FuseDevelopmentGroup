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
    $EntryScript
)

Write-Host "Building FUSE-Converter.exe..."
$buildExit = Invoke-ConverterPython @args
if ($buildExit -ne 0) {
    throw "PyInstaller build failed."
}

$exePath = Join-Path $OutputDir 'FUSE-Converter.exe'
if (-not (Test-Path -LiteralPath $exePath)) {
    throw "Build completed but exe was not found: $exePath"
}

Write-Host "Built: $exePath"
