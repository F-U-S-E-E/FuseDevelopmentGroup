#requires -Version 5
<#
.SYNOPSIS
Validates an optional-mod release archive before GitHub or Nexus publication.

.DESCRIPTION
The validator treats the upstream ZIP as untrusted even though its SHA-256 is
pinned in FUSE. It rejects unsafe or non-portable entry names, checks the UMM
manifest identity and version, and enforces the established package layout for
each optional mod. Tile Editor's internal checksum manifest is also verified.
#>

[CmdletBinding()]
param(
  [Parameter(Mandatory)][string]$ArchivePath,
  [Parameter(Mandatory)][string]$Version,
  [Parameter(Mandatory)][ValidateSet(
    'tile-editor-suite',
    'toolshed',
    'narrow-gauge'
  )][string]$ArchiveProfile,
  [Parameter(Mandatory)][string]$ExpectedSha256
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ($Version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?$') {
  throw "Version '$Version' is not supported semantic version syntax."
}
if ($ExpectedSha256 -notmatch '^[0-9A-Fa-f]{64}$') {
  throw "ExpectedSha256 must be a full SHA-256 digest."
}

$resolvedArchive = (Resolve-Path -LiteralPath $ArchivePath).Path
$actualSha256 = (Get-FileHash -LiteralPath $resolvedArchive -Algorithm SHA256).Hash.ToLowerInvariant()
$expectedSha256Normalized = $ExpectedSha256.ToLowerInvariant()
if ($actualSha256 -ne $expectedSha256Normalized) {
  throw "Archive SHA-256 is $actualSha256; expected $expectedSha256Normalized."
}

Add-Type -AssemblyName System.IO.Compression.FileSystem

function Get-Entry {
  param([Parameter(Mandatory)][string]$Name)

  if (!$script:entryMap.ContainsKey($Name)) {
    throw "Archive is missing required entry '$Name'."
  }
  $entry = $script:entryMap[$Name]
  if ($entry.FullName -cne $Name) {
    throw "Archive entry '$($entry.FullName)' must use exact casing '$Name'."
  }
  return $entry
}

function Read-EntryText {
  param([Parameter(Mandatory)][string]$Name)

  $entry = Get-Entry $Name
  $stream = $entry.Open()
  try {
    $reader = [System.IO.StreamReader]::new(
      $stream,
      [System.Text.Encoding]::UTF8,
      $true
    )
    try {
      return $reader.ReadToEnd()
    }
    finally {
      $reader.Dispose()
    }
  }
  finally {
    $stream.Dispose()
  }
}

function Get-EntrySha256 {
  param([Parameter(Mandatory)]$Entry)

  $stream = $Entry.Open()
  $algorithm = [System.Security.Cryptography.SHA256]::Create()
  try {
    $bytes = $algorithm.ComputeHash($stream)
    return -join ($bytes | ForEach-Object { $_.ToString('x2') })
  }
  finally {
    $algorithm.Dispose()
    $stream.Dispose()
  }
}

$profiles = @{
  'tile-editor-suite' = @{
    Root = 'Hrogers.TileEditorBridge'
    Manifest = 'Hrogers.TileEditorBridge/Info.json'
    Id = 'Hrogers.TileEditorBridge'
    Assembly = 'Hrogers.TileEditorBridge.dll'
    EntryMethod = 'Hrogers.TileEditorBridge.Main.Load'
    Required = @(
      'Hrogers.TileEditorBridge/Hrogers.TileEditorBridge.dll',
      'Hrogers.TileEditorBridge/PackageManifest.json',
      'Hrogers.TileEditorBridge/VERSION.txt',
      'Hrogers.TileEditorBridge/checksums.sha256',
      'Hrogers.TileEditorBridge/TileEditor/edit_tiles/version.py',
      'Hrogers.TileEditorBridge/TileEditor/requirements.txt'
    )
  }
  'toolshed' = @{
    Root = 'Toolshed'
    Manifest = 'Toolshed/Info.json'
    Id = 'Toolshed'
    Assembly = 'Toolshed.dll'
    EntryMethod = 'Toolshed.Main.Load'
    Required = @(
      'Toolshed/Toolshed.dll',
      'Toolshed/LICENSE',
      'Toolshed/README.md',
      'Toolshed/SCAssetPacks/WoodShed/Bundle',
      'Toolshed/SCAssetPacks/link-pin-coupler/Bundle',
      'Toolshed/Examples/service-facility-setup-guide.md'
    )
  }
  'narrow-gauge' = @{
    Root = ''
    Manifest = 'Info.json'
    Id = 'FUSE.NarrowGauge'
    Assembly = 'NarrowGaugeMod.dll'
    EntryMethod = 'NarrowGaugeMod.Main.Load'
    Required = @(
      'NarrowGaugeMod.dll',
      'README.md'
    )
  }
}

$profileSpec = $profiles[$ArchiveProfile]
$archive = [System.IO.Compression.ZipFile]::OpenRead($resolvedArchive)
try {
  $script:entryMap = @{}
  $normalizedPathMap = [System.Collections.Generic.Dictionary[string,string]]::new(
    [System.StringComparer]::OrdinalIgnoreCase
  )
  $normalizedFilePaths = New-Object System.Collections.Generic.List[string]
  $fileEntries = New-Object System.Collections.Generic.List[object]

  foreach ($entry in $archive.Entries) {
    $name = $entry.FullName
    if ([string]::IsNullOrWhiteSpace($name)) {
      throw "Archive contains an entry with an empty name."
    }
    if ($name.Contains('\')) {
      throw "Archive entry '$name' contains a backslash; ZIP paths must use '/'."
    }
    if ($name.StartsWith('/') -or $name -match '^[A-Za-z]:') {
      throw "Archive entry '$name' uses an absolute path."
    }

    $segments = @($name.Split('/'))
    $lastSegmentIndex = $segments.Count - 1
    $normalizedSegments = New-Object System.Collections.Generic.List[string]
    $isDirectoryEntry = $name.EndsWith('/')
    for ($index = 0; $index -lt $segments.Count; $index++) {
      $segment = $segments[$index]
      $isDirectoryTerminator = $index -eq $lastSegmentIndex -and
        [string]::IsNullOrEmpty($segment) -and $isDirectoryEntry
      if (!$isDirectoryTerminator -and
          ([string]::IsNullOrEmpty($segment) -or $segment -eq '.' -or $segment -eq '..')) {
        throw "Archive entry '$name' contains an unsafe path segment."
      }
      if ($isDirectoryTerminator) {
        continue
      }
      if ($segment -match '[\x00-\x1F<>:"\\|?*]') {
        throw "Archive entry '$name' contains a character that is invalid on Windows."
      }
      if ($segment.EndsWith('.') -or $segment.EndsWith(' ')) {
        throw "Archive entry '$name' contains a segment with a trailing dot or space."
      }

      $normalizedSegment = $segment.Normalize(
        [System.Text.NormalizationForm]::FormC
      )
      if ($normalizedSegment.Length -gt 255) {
        throw "Archive entry '$name' contains a segment longer than 255 characters."
      }
      $deviceBaseName = @($normalizedSegment -split '\.', 2)[0]
      if ($deviceBaseName -match '^(?i:CON|PRN|AUX|NUL|CONIN\$|CONOUT\$|COM[1-9\u00B9\u00B2\u00B3]|LPT[1-9\u00B9\u00B2\u00B3])$') {
        throw "Archive entry '$name' uses reserved Windows device name '$deviceBaseName'."
      }
      $normalizedSegments.Add($normalizedSegment)
    }

    $normalizedPath = $normalizedSegments -join '/'
    if ($normalizedPathMap.ContainsKey($normalizedPath)) {
      throw "Archive entries '$($normalizedPathMap[$normalizedPath])' and '$name' normalize to the same Windows path."
    }
    $normalizedPathMap[$normalizedPath] = $name
    if (!$isDirectoryEntry) {
      $normalizedFilePaths.Add($normalizedPath)
    }

    if ($script:entryMap.ContainsKey($name)) {
      throw "Archive contains duplicate or case-colliding entry '$name'."
    }
    $script:entryMap[$name] = $entry

    if (!$name.EndsWith('/')) {
      $fileEntries.Add($entry)
    }
  }

  foreach ($filePath in $normalizedFilePaths) {
    $childPrefix = "$filePath/"
    foreach ($candidatePath in $normalizedPathMap.Keys) {
      if ($candidatePath.StartsWith(
          $childPrefix,
          [System.StringComparison]::OrdinalIgnoreCase
        )) {
        throw "Archive file '$filePath' conflicts with child path '$candidatePath'."
      }
    }
  }

  if ($fileEntries.Count -eq 0) {
    throw "Archive contains no files."
  }

  $root = [string]$profileSpec.Root
  foreach ($entry in $archive.Entries) {
    if ([string]::IsNullOrEmpty($root)) {
      if ($entry.FullName.Contains('/')) {
        throw "Profile '$ArchiveProfile' requires entries at the archive root; found '$($entry.FullName)'."
      }
    }
    elseif ($entry.FullName -cne "$root/" -and
        !$entry.FullName.StartsWith(
          "$root/",
          [System.StringComparison]::Ordinal
        )) {
      throw "Profile '$ArchiveProfile' requires the single archive root '$root/'; found '$($entry.FullName)'."
    }
  }

  foreach ($required in @($profileSpec.Required)) {
    $null = Get-Entry $required
  }

  $manifestPath = [string]$profileSpec.Manifest
  try {
    $manifest = Read-EntryText $manifestPath | ConvertFrom-Json
  }
  catch {
    throw "Manifest '$manifestPath' is not valid JSON: $($_.Exception.Message)"
  }

  $expectedManifestValues = @{
    Id = [string]$profileSpec.Id
    Version = $Version
    AssemblyName = [string]$profileSpec.Assembly
    EntryMethod = [string]$profileSpec.EntryMethod
  }
  foreach ($property in $expectedManifestValues.Keys) {
    if ($null -eq $manifest.PSObject.Properties[$property]) {
      throw "Manifest '$manifestPath' is missing '$property'."
    }
    $actual = [string]$manifest.$property
    $expected = [string]$expectedManifestValues[$property]
    if ($actual -cne $expected) {
      throw "Manifest '$manifestPath' has $property='$actual'; expected '$expected'."
    }
  }

  $assemblyPath = if ([string]::IsNullOrEmpty($root)) {
    [string]$profileSpec.Assembly
  }
  else {
    "$root/$($profileSpec.Assembly)"
  }
  $assemblyEntry = Get-Entry $assemblyPath
  if ($assemblyEntry.Length -le 0) {
    throw "Assembly '$assemblyPath' is empty."
  }

  if ($ArchiveProfile -eq 'tile-editor-suite') {
    $packageManifestPath = "$root/PackageManifest.json"
    try {
      $packageManifest = Read-EntryText $packageManifestPath | ConvertFrom-Json
    }
    catch {
      throw "Package manifest '$packageManifestPath' is not valid JSON: $($_.Exception.Message)"
    }
    if ($null -eq $packageManifest.PSObject.Properties['version'] -or
        [string]$packageManifest.version -cne $Version) {
      throw "Package manifest version must be '$Version'."
    }

    $versionText = (Read-EntryText "$root/VERSION.txt").Trim()
    if ($versionText -cne $Version) {
      throw "VERSION.txt contains '$versionText'; expected '$Version'."
    }

    $pythonVersionSource = Read-EntryText "$root/TileEditor/edit_tiles/version.py"
    if ($pythonVersionSource -notmatch '__version__\s*=\s*"(?<version>[^"]+)"' -or
        $Matches.version -cne $Version) {
      throw "Tile Editor Python version does not match '$Version'."
    }

    $checksumPath = "$root/checksums.sha256"
    $checksumText = Read-EntryText $checksumPath
    $checksums = @{}
    foreach ($line in @($checksumText -split "`r?`n")) {
      if ([string]::IsNullOrWhiteSpace($line)) {
        continue
      }
      if ($line -notmatch '^(?<hash>[0-9A-Fa-f]{64})  (?<path>.+)$') {
        throw "Invalid Tile Editor checksum line '$line'."
      }
      $relativePath = $Matches.path
      if ($relativePath.Contains('\') -or
          $relativePath.StartsWith('/') -or
          $relativePath.Contains('../')) {
        throw "Unsafe Tile Editor checksum path '$relativePath'."
      }
      if ($checksums.ContainsKey($relativePath)) {
        throw "Duplicate Tile Editor checksum path '$relativePath'."
      }
      $checksums[$relativePath] = $Matches.hash.ToLowerInvariant()
    }

    $checkedFileCount = 0
    foreach ($entry in $fileEntries) {
      if ($entry.FullName -ceq $checksumPath) {
        continue
      }
      $relativePath = $entry.FullName.Substring($root.Length + 1)
      if (!$checksums.ContainsKey($relativePath)) {
        throw "Tile Editor checksum manifest is missing '$relativePath'."
      }
      $entryHash = Get-EntrySha256 $entry
      if ($entryHash -ne $checksums[$relativePath]) {
        throw "Tile Editor checksum mismatch for '$relativePath'."
      }
      $checkedFileCount++
    }
    if ($checkedFileCount -ne $checksums.Count) {
      throw "Tile Editor checksum manifest contains entries that are not in the archive."
    }
  }
}
finally {
  $archive.Dispose()
  Remove-Variable -Name entryMap -Scope Script -ErrorAction SilentlyContinue
}

Write-Host "Validated $ArchiveProfile package '$resolvedArchive' as version $Version (sha256:$actualSha256)."
