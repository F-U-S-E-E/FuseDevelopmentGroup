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
# The mod project (csproj, Info.json, bin) lives in FUSE/ under the
# .NET-conventional layout. schemas/ and tools/assets/ stay at the repo root
# as the canonical copies shared with the JSON lint and the release workflow,
# so those two keep resolving from $RepoRoot / $ToolsRoot below.
$ProjectRoot = Join-Path $RepoRoot 'FUSE'
$ProjectPath = Join-Path $ProjectRoot 'FUSE.csproj'
$BuildOutput = Join-Path $ProjectRoot 'bin\Debug\net48'
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
$infoPath = Join-Path $ProjectRoot 'Info.json'
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

Compress-Archive -Path $PackageRoot -DestinationPath $zipPath -CompressionLevel Optimal

$resolvedZip = [System.IO.Path]::GetFullPath($zipPath)
Write-Host "Packaged: $resolvedZip"
Write-Host "Stage:    $PackageRoot"
