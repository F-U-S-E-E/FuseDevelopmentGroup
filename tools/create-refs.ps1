[CmdletBinding()]
param(
    [string]$GameDir,
    [string]$OutputDir
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $repoRoot "lib\refs"
}

if ([string]::IsNullOrWhiteSpace($GameDir)) {
    $pathsUserPath = Join-Path $repoRoot "Paths.user"
    if (Test-Path $pathsUserPath) {
        [xml]$pathsUser = Get-Content -Path $pathsUserPath -Raw
        $GameDir = $pathsUser.Project.PropertyGroup.GameDir
    }
}

if ([string]::IsNullOrWhiteSpace($GameDir)) {
    throw "GameDir not provided and Paths.user does not contain a GameDir value."
}

if (-not (Test-Path $GameDir)) {
    throw "GameDir does not exist: $GameDir"
}

$dataDir = Get-ChildItem -Path $GameDir -Directory -Filter "*_Data" | Select-Object -First 1
if ($null -eq $dataDir) {
    throw "Could not find a *_Data directory under $GameDir"
}

$managedDir = Join-Path $dataDir.FullName "Managed"
if (-not (Test-Path $managedDir)) {
    throw "Could not find Managed directory at $managedDir"
}

# Game/runtime assemblies FUSE compiles against. Keep in sync with the
# References in FUSE.csproj.
$managedAssemblies = @(
    "0Harmony.dll",
    "Assembly-CSharp.dll",
    "Core.dll",
    "Definition.dll",
    "KeyValue.Runtime.dll",
    "Map.Runtime.dll",
    "Newtonsoft.Json.dll",
    "SimpleGraph.Runtime.dll",
    "Unity.Mathematics.dll",
    "Unity.TextMeshPro.dll",
    "UnityEngine.AudioModule.dll",
    "UnityEngine.CoreModule.dll",
    "UnityEngine.IMGUIModule.dll",
    "UnityEngine.ImageConversionModule.dll",
    "UnityEngine.TextRenderingModule.dll",
    "UnityEngine.UI.dll",
    "UnityEngine.UIModule.dll",
    "UnityEngine.UnityWebRequestAudioModule.dll",
    "UnityEngine.UnityWebRequestModule.dll"
) | ForEach-Object { Join-Path $managedDir $_ }

# UnityModManager ships either as UnityModManagerNet.dll (newer UMM) or as the
# legacy UnityModManager.dll. Whichever exists is what FUSE.csproj will look
# for, so refasm whichever is present.
$ummDir = Join-Path $managedDir "UnityModManager"
$ummCandidatePaths = @(
    Join-Path $ummDir "UnityModManagerNet.dll"
    Join-Path $ummDir "UnityModManager.dll"
)
$ummCandidates = @($ummCandidatePaths | Where-Object { Test-Path $_ })

if ($ummCandidates.Count -eq 0) {
    throw "Could not find UnityModManager(Net).dll under $ummDir"
}

$assemblies = @($managedAssemblies) + $ummCandidates

$missingAssemblies = @($assemblies | Where-Object { -not (Test-Path $_) })
if ($missingAssemblies.Count -gt 0) {
    $missingList = $missingAssemblies -join [Environment]::NewLine
    throw "Missing required assemblies:$([Environment]::NewLine)$missingList"
}

$refasmer = Get-Command refasmer -ErrorAction SilentlyContinue
if ($null -eq $refasmer) {
    Write-Host "Refasmer not found. Installing JetBrains.Refasmer.CliTool..."
    dotnet tool install -g JetBrains.Refasmer.CliTool
    $env:PATH += ";$HOME\.dotnet\tools"
    $refasmer = Get-Command refasmer -ErrorAction SilentlyContinue
}

if ($null -eq $refasmer) {
    throw "Refasmer CLI is not available. Install it with: dotnet tool install -g JetBrains.Refasmer.CliTool"
}

New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

Get-ChildItem -Path $OutputDir -Filter "*.dll" -File -ErrorAction SilentlyContinue | Remove-Item -Force

Write-Host "Generating reference assemblies to $OutputDir"
& $refasmer.Source --all --outputdir $OutputDir @assemblies

if ($LASTEXITCODE -ne 0) {
    throw "Refasmer failed with exit code $LASTEXITCODE"
}

Write-Host "Reference assemblies generated:"
Get-ChildItem -Path $OutputDir -Filter "*.dll" -File | Sort-Object Name | ForEach-Object {
    Write-Host " - $($_.Name)"
}
