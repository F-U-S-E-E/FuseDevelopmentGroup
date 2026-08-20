#requires -Version 5
<#
.SYNOPSIS
Validates optional-mod release metadata and resolves a FUSE release tag.

.DESCRIPTION
The trusted metadata in .github/optional-mod-releases.json is the allow-list
between a tag pushed to this repository and an asset downloaded from another
F-U-S-E-E repository. The resolver fails closed on mismatched versions,
unexpected repositories, unpinned assets, or a baseline that is not ready for
promotion.
#>

[CmdletBinding()]
param(
  [Parameter(Mandatory)][string]$ConfigPath,
  [string]$Tag = "",
  [switch]$ValidateOnly,
  [string]$GitHubOutputPath = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$semverPattern = '^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?$'
$allowedProfiles = @{
  'tile-editor-suite' = 'NEXUS_TILE_EDITOR_FILE_GROUP_ID'
  'toolshed' = 'NEXUS_TOOLSHED_FILE_GROUP_ID'
  'narrow-gauge' = 'NEXUS_NARROW_GAUGE_FILE_GROUP_ID'
}

function Require-String {
  param(
    [Parameter(Mandatory)]$Object,
    [Parameter(Mandatory)][string]$Property,
    [Parameter(Mandatory)][string]$Context,
    [switch]$AllowEmpty
  )

  $member = $Object.PSObject.Properties[$Property]
  if ($null -eq $member -or $null -eq $member.Value) {
    throw "$Context is missing '$Property'."
  }

  $value = [string]$member.Value
  if (!$AllowEmpty -and [string]::IsNullOrWhiteSpace($value)) {
    throw "$Context has an empty '$Property'."
  }
  if ($value.Contains("`r") -or $value.Contains("`n")) {
    throw "$Context '$Property' must be a single line."
  }

  return $value
}

function Add-UniqueValue {
  param(
    [Parameter(Mandatory)][hashtable]$Seen,
    [Parameter(Mandatory)][string]$Value,
    [Parameter(Mandatory)][string]$Label
  )

  $normalized = $Value.ToLowerInvariant()
  if ($Seen.ContainsKey($normalized)) {
    throw "Duplicate optional-mod $Label '$Value'."
  }
  $Seen[$normalized] = $true
}

$resolvedConfigPath = (Resolve-Path -LiteralPath $ConfigPath).Path
try {
  $config = Get-Content -LiteralPath $resolvedConfigPath -Raw |
    ConvertFrom-Json
}
catch {
  throw "Optional-mod release metadata is not valid JSON: $($_.Exception.Message)"
}

if ($null -eq $config.PSObject.Properties['schemaVersion'] -or
    [int]$config.schemaVersion -ne 1) {
  throw "Optional-mod release metadata must use schemaVersion 1."
}
if ($null -eq $config.PSObject.Properties['mods']) {
  throw "Optional-mod release metadata is missing 'mods'."
}

$mods = @($config.mods)
if ($mods.Count -eq 0) {
  throw "Optional-mod release metadata contains no mods."
}

$seenKeys = @{}
$seenPrefixes = @{}
$seenNexusVariables = @{}
$validatedMods = New-Object System.Collections.Generic.List[object]

foreach ($mod in $mods) {
  $context = "Optional-mod entry"
  $key = Require-String $mod 'key' $context
  $context = "Optional-mod '$key'"
  $tagPrefix = Require-String $mod 'tagPrefix' $context
  $version = Require-String $mod 'version' $context
  $lastNexusVersion = Require-String $mod 'lastNexusVersion' $context
  $blockedReason = Require-String $mod 'blockedReason' $context -AllowEmpty
  $sourceRepository = Require-String $mod 'sourceRepository' $context
  $sourceTag = Require-String $mod 'sourceTag' $context
  $assetName = Require-String $mod 'assetName' $context
  $assetSha256 = Require-String $mod 'assetSha256' $context -AllowEmpty
  $archiveProfile = Require-String $mod 'archiveProfile' $context
  $releaseDisplayName = Require-String $mod 'releaseDisplayName' $context
  $nexusDisplayName = Require-String $mod 'nexusDisplayName' $context
  $nexusVariable = Require-String $mod 'nexusVariable' $context

  if ($null -eq $mod.PSObject.Properties['promotionReady'] -or
      $mod.promotionReady -isnot [bool]) {
    throw "$context must set boolean 'promotionReady'."
  }
  $promotionReady = [bool]$mod.promotionReady

  if ($key -notmatch '^[a-z0-9]+(?:-[a-z0-9]+)*$') {
    throw "$context key '$key' must be a lowercase kebab-case identifier."
  }
  if ($tagPrefix -notmatch '^[a-z][a-z0-9-]*-v$') {
    throw "$context tagPrefix '$tagPrefix' must end in '-v'."
  }
  if ($version -notmatch $semverPattern) {
    throw "$context version '$version' is not supported semantic version syntax."
  }
  if ($lastNexusVersion -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$') {
    throw "$context lastNexusVersion '$lastNexusVersion' must be a stable major.minor.patch version."
  }
  try {
    $candidateCoreVersion = [System.Version]::Parse(($version -split '-', 2)[0])
    $nexusVersion = [System.Version]::Parse($lastNexusVersion)
  }
  catch {
    throw "$context has a version component outside the supported System.Version range."
  }
  if ($candidateCoreVersion -lt $nexusVersion) {
    throw "$context version '$version' is older than lastNexusVersion '$lastNexusVersion'."
  }
  if ($sourceRepository -notmatch '^F-U-S-E-E/[A-Za-z0-9_.-]+$') {
    throw "$context sourceRepository '$sourceRepository' is outside the F-U-S-E-E organization."
  }
  if ($sourceTag -ne "v$version") {
    throw "$context sourceTag '$sourceTag' must be exactly 'v$version'."
  }
  if ($assetName -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]*\.zip$' -or
      !$assetName.Contains($version)) {
    throw "$context assetName '$assetName' must be a simple versioned ZIP filename."
  }
  if (!$allowedProfiles.ContainsKey($archiveProfile)) {
    throw "$context archiveProfile '$archiveProfile' is not supported."
  }
  $expectedNexusVariable = $allowedProfiles[$archiveProfile]
  if ($nexusVariable -ne $expectedNexusVariable) {
    throw "$context must use Nexus variable '$expectedNexusVariable', not '$nexusVariable'."
  }

  if (![string]::IsNullOrWhiteSpace($assetSha256) -and
      $assetSha256 -notmatch '^[0-9A-Fa-f]{64}$') {
    throw "$context assetSha256 is not a full SHA-256 digest."
  }
  if ($promotionReady) {
    if ([string]::IsNullOrWhiteSpace($assetSha256)) {
      throw "$context is promotion-ready but assetSha256 is empty."
    }
    if ($candidateCoreVersion -le $nexusVersion) {
      throw "$context version '$version' must be newer than lastNexusVersion '$lastNexusVersion' before promotion."
    }
  }
  elseif ([string]::IsNullOrWhiteSpace($blockedReason)) {
    throw "$context is not promotion-ready and must explain why in blockedReason."
  }

  Add-UniqueValue $seenKeys $key 'key'
  Add-UniqueValue $seenPrefixes $tagPrefix 'tagPrefix'
  Add-UniqueValue $seenNexusVariables $nexusVariable 'nexusVariable'

  $validatedMods.Add([pscustomobject]@{
      Key = $key
      TagPrefix = $tagPrefix
      Version = $version
      LastNexusVersion = $lastNexusVersion
      PromotionReady = $promotionReady
      BlockedReason = $blockedReason
      SourceRepository = $sourceRepository
      SourceTag = $sourceTag
      AssetName = $assetName
      AssetSha256 = $assetSha256.ToLowerInvariant()
      ArchiveProfile = $archiveProfile
      ReleaseDisplayName = $releaseDisplayName
      NexusDisplayName = $nexusDisplayName
      NexusVariable = $nexusVariable
    })
}

if ($ValidateOnly) {
  $readyCount = @($validatedMods | Where-Object { $_.PromotionReady }).Count
  Write-Host "Validated $($validatedMods.Count) optional-mod release entries ($readyCount promotion-ready)."
  return
}

if ([string]::IsNullOrWhiteSpace($Tag)) {
  throw "Specify -Tag unless -ValidateOnly is used."
}
if ($Tag.Contains("`r") -or $Tag.Contains("`n")) {
  throw "Release tag must be a single line."
}

$matchedMods = New-Object System.Collections.Generic.List[object]
foreach ($mod in $validatedMods) {
  $pattern = '^' + [regex]::Escape($mod.TagPrefix) +
    '(?<version>[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?)$'
  if ($Tag -match $pattern) {
    $matchedMods.Add([pscustomobject]@{
        Mod = $mod
        Version = $Matches.version
      })
  }
}

if ($matchedMods.Count -ne 1) {
  $prefixes = @($validatedMods | ForEach-Object { $_.TagPrefix }) -join ', '
  throw "Tag '$Tag' must use exactly one configured prefix: $prefixes."
}

$selected = $matchedMods[0].Mod
$tagVersion = [string]$matchedMods[0].Version
if ($tagVersion -ne $selected.Version) {
  throw "Tag '$Tag' requests $tagVersion, but '$($selected.Key)' is locked to $($selected.Version) in $resolvedConfigPath."
}
if (!$selected.PromotionReady) {
  throw "'$($selected.Key)' $tagVersion is recorded as a Nexus baseline, not a promotable release: $($selected.BlockedReason)"
}

$isGa = $tagVersion -match '^[0-9]+\.[0-9]+\.[0-9]+$'
$result = [pscustomobject]@{
  ModKey = $selected.Key
  Version = $tagVersion
  LastNexusVersion = $selected.LastNexusVersion
  IsGa = $isGa.ToString().ToLowerInvariant()
  SourceRepository = $selected.SourceRepository
  SourceTag = $selected.SourceTag
  SourceReleaseUrl = "https://github.com/$($selected.SourceRepository)/releases/tag/$($selected.SourceTag)"
  AssetName = $selected.AssetName
  AssetSha256 = $selected.AssetSha256
  ArchiveProfile = $selected.ArchiveProfile
  ReleaseDisplayName = $selected.ReleaseDisplayName
  NexusDisplayName = $selected.NexusDisplayName
  NexusVariable = $selected.NexusVariable
}

if (![string]::IsNullOrWhiteSpace($GitHubOutputPath)) {
  $outputLines = @(
    "mod_key=$($result.ModKey)",
    "version=$($result.Version)",
    "last_nexus_version=$($result.LastNexusVersion)",
    "is_ga=$($result.IsGa)",
    "source_repository=$($result.SourceRepository)",
    "source_tag=$($result.SourceTag)",
    "source_release_url=$($result.SourceReleaseUrl)",
    "asset_name=$($result.AssetName)",
    "asset_sha256=$($result.AssetSha256)",
    "archive_profile=$($result.ArchiveProfile)",
    "release_display_name=$($result.ReleaseDisplayName)",
    "nexus_display_name=$($result.NexusDisplayName)",
    "nexus_variable=$($result.NexusVariable)"
  )
  $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
  $outputWriter = [System.IO.StreamWriter]::new(
    $GitHubOutputPath,
    $true,
    $utf8NoBom
  )
  try {
    foreach ($line in $outputLines) {
      $outputWriter.WriteLine($line)
    }
  }
  finally {
    $outputWriter.Dispose()
  }
}

return $result
