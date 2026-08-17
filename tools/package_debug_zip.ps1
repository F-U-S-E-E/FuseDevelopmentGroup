[CmdletBinding()]
param(
    [string]$OutputDir = "",
    [string]$BaseDir = "",
    [switch]$NoBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ToolsRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Split-Path -Parent $ToolsRoot
$ProjectPath = Join-Path $RepoRoot 'FUSE.csproj'
$BuildOutput = Join-Path $RepoRoot 'bin\Debug\net48'
$StageRoot = Join-Path $RepoRoot '_work\package-fuse-debug'
$PackageRoot = Join-Path $StageRoot 'FUSE'

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $RepoRoot 'dist'
}

if (-not $NoBuild) {
    $buildArgs = @(
        'build',
        $ProjectPath,
        '-c',
        'Debug',
        '-v:minimal'
    )

    if (-not [string]::IsNullOrWhiteSpace($BaseDir)) {
        $buildArgs += "-p:GameDir=$BaseDir"
    }

    & dotnet @buildArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Debug build failed."
    }
}

$dllPath = Join-Path $BuildOutput 'FUSE.dll'
$pdbPath = Join-Path $BuildOutput 'FUSE.pdb'
$infoPath = Join-Path $RepoRoot 'Info.json'
$schemasPath = Join-Path $RepoRoot 'schemas'
$iconPath = Join-Path $ToolsRoot 'assets\fuse_converter_icon.png'

foreach ($required in @($dllPath, $pdbPath, $infoPath, $schemasPath, $iconPath)) {
    if (-not (Test-Path -LiteralPath $required)) {
        throw "Required package input missing: $required"
    }
}

if (Test-Path -LiteralPath $StageRoot) {
    Remove-Item -LiteralPath $StageRoot -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $PackageRoot | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $PackageRoot 'assets') | Out-Null

Copy-Item -LiteralPath $dllPath -Destination $PackageRoot
Copy-Item -LiteralPath $pdbPath -Destination $PackageRoot
Copy-Item -LiteralPath $infoPath -Destination $PackageRoot
Copy-Item -LiteralPath $schemasPath -Destination $PackageRoot -Recurse
Copy-Item -LiteralPath $iconPath -Destination (Join-Path $PackageRoot 'assets\fuse_icon.png')

$info = Get-Content -LiteralPath $infoPath -Raw | ConvertFrom-Json
$version = if ([string]::IsNullOrWhiteSpace($info.Version)) { 'debug' } else { $info.Version }
$safeVersion = $version -replace '[^A-Za-z0-9._-]', '-'

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$zipPath = Join-Path $OutputDir "FUSE-Debug-$safeVersion.zip"
if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

# Not Compress-Archive: under Windows PowerShell 5.1 it writes backslash entry
# names, and a debug zip dropped on UMM would then fail to update an existing
# install exactly the way the shipped ones did. See issue #209.
& (Join-Path $RepoRoot 'scripts\New-ModArchive.ps1') -Path $PackageRoot -DestinationPath $zipPath

$resolvedZip = [System.IO.Path]::GetFullPath($zipPath)
Write-Host "Packaged: $resolvedZip"
Write-Host "Stage:    $PackageRoot"
