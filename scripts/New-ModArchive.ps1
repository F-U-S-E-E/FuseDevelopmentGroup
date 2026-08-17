#requires -Version 5
# Creates a .zip whose entry names always use forward slashes.
#
# This exists because `Compress-Archive` under Windows PowerShell 5.1 — the
# `shell: powershell` default on the self-hosted runner — writes entry names with
# BACKSLASHES, which violates ZIP APPNOTE 4.4.17.1. Nothing noticed for three
# releases, because backslashes happen to work everywhere the archive is merely
# extracted: .NET's ZipFile.ExtractToDirectory and Windows Explorer both accept
# '\' as a separator.
#
# Unity Mod Manager does not merely extract. When the mod is ALREADY INSTALLED,
# its installer rewrites every entry name (UnityModManagerApp/Mods.cs, InstallMod):
#
#     var pos = filename.IndexOf(Path.AltDirectorySeparatorChar);   // '/'
#     filename = replaceModDir + filename.Substring(pos, filename.Length - pos);
#
# With backslash entries there is no '/', so pos is -1, Substring throws
# ArgumentOutOfRangeException, and UMM aborts the whole unpack with
# "Error when unpacking '<zip>'" having extracted nothing. A FRESH install skips
# that rewrite entirely, so it succeeds — which is why FUSE 1.0.0-1.0.2 installed
# fine for new users and could not be UPDATED by anyone. See issue #209.
#
# scripts/Validate-ModPackage.cs fails the build if a packaged archive ever
# regains a backslash entry, so this cannot silently come back.
#
# Ordering is sorted by entry name so repeated packaging runs are reproducible.

[CmdletBinding()]
param(
  # Directory to archive.
  [Parameter(Mandatory)][string]$Path,
  # Destination .zip. Overwritten if it already exists.
  [Parameter(Mandatory)][string]$DestinationPath,
  # Archive the CONTENTS of -Path rather than -Path itself. Mirrors the
  # difference between `Compress-Archive -Path dir` and `-Path dir\*`.
  [switch]$ContentsOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Present by default on PowerShell 7; needs loading on Windows PowerShell 5.1.
foreach ($assembly in @('System.IO.Compression', 'System.IO.Compression.FileSystem')) {
  try { Add-Type -AssemblyName $assembly -ErrorAction Stop } catch { }
}

if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
  throw "Source directory not found: $Path"
}

$root = (Resolve-Path -LiteralPath $Path).ProviderPath.TrimEnd('\', '/')

# Entry names are relative to this. Including the source folder itself (the
# default) yields 'FUSE/FUSE.dll'; -ContentsOnly yields 'Mods/FUSE/FUSE.dll'
# from a staging root that holds Mods/ and Tools/.
$baseDir = if ($ContentsOnly) { $root } else { Split-Path -Parent $root }
if ([string]::IsNullOrEmpty($baseDir)) {
  throw "Cannot determine a parent directory for '$root'; pass -ContentsOnly or a nested path."
}

function Resolve-OutputPath([string]$value) {
  if ([System.IO.Path]::IsPathRooted($value)) {
    return [System.IO.Path]::GetFullPath($value)
  }
  # .NET resolves relative paths against the process working directory, which is
  # not necessarily PowerShell's current location. Anchor explicitly.
  return [System.IO.Path]::GetFullPath((Join-Path (Get-Location).ProviderPath $value))
}

function ConvertTo-EntryName([string]$fullName, [string]$base) {
  $relative = $fullName.Substring($base.Length).TrimStart('\', '/')
  return $relative -replace '\\', '/'
}

$destination = Resolve-OutputPath $DestinationPath
$destinationDir = Split-Path -Parent $destination
if ($destinationDir -and -not (Test-Path -LiteralPath $destinationDir)) {
  New-Item -ItemType Directory -Force -Path $destinationDir | Out-Null
}
if (Test-Path -LiteralPath $destination) {
  Remove-Item -LiteralPath $destination -Force
}

$files = @(Get-ChildItem -LiteralPath $root -Recurse -File -Force |
  Sort-Object -Property FullName)

# Compress-Archive records empty directories; preserve that so this stays a
# drop-in replacement rather than a subtly lossy one.
$emptyDirs = @(Get-ChildItem -LiteralPath $root -Recurse -Directory -Force |
  Where-Object { -not (Get-ChildItem -LiteralPath $_.FullName -Force | Select-Object -First 1) } |
  Sort-Object -Property FullName)

if ($files.Count -eq 0 -and $emptyDirs.Count -eq 0) {
  throw "Refusing to write an empty archive: '$root' contains no files."
}

$archive = [System.IO.Compression.ZipFile]::Open(
  $destination, [System.IO.Compression.ZipArchiveMode]::Create)
try {
  foreach ($dir in $emptyDirs) {
    $archive.CreateEntry((ConvertTo-EntryName $dir.FullName $baseDir) + '/') | Out-Null
  }
  foreach ($file in $files) {
    [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
      $archive,
      $file.FullName,
      (ConvertTo-EntryName $file.FullName $baseDir),
      [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
  }
}
catch {
  # Dispose before deleting, to release the file handle. In Create mode Dispose()
  # writes a valid central directory for whatever was added before the fault, so
  # an undeleted partial is a well-formed but TRUNCATED archive that looks
  # complete — the worst possible artifact to leave in dist/. Compress-Archive
  # removed its partial output on failure; match that.
  $archive.Dispose()
  $archive = $null
  if (Test-Path -LiteralPath $destination) {
    Remove-Item -LiteralPath $destination -Force
  }
  throw
}
finally {
  if ($archive) { $archive.Dispose() }
}

# Self-check. Cheap, and it turns "we shipped a broken zip" into "packaging failed".
$verify = [System.IO.Compression.ZipFile]::OpenRead($destination)
try {
  $bad = @($verify.Entries | Where-Object { $_.FullName.Contains('\') })
  $count = $verify.Entries.Count
}
finally {
  $verify.Dispose()
}
if ($bad.Count -gt 0) {
  Remove-Item -LiteralPath $destination -Force
  throw "Archive contained backslash entry names: $(($bad | ForEach-Object { $_.FullName }) -join ', ')"
}

Write-Host "Packaged $destination ($count entries, forward-slash separators)."
