#requires -Version 5
# Rewrites the "Version" field in a UMM Info.json file.
#
# Used by:
#   - Directory.Build.targets (InjectModVersionIntoInfoJson) to stamp the
#     bin-deployed copy with the MSBuild ModVersion property.
#   - .github/workflows/sync-info-json.yml to sync the source manifest to
#     the latest published GitHub release tag.
#
# The replacement is byte-faithful: only the version value changes;
# whitespace, line endings, key order, and encoding are preserved.

param(
  [Parameter(Mandatory)][string]$Path,
  [Parameter(Mandatory)][string]$Version
)

if (-not (Test-Path -LiteralPath $Path)) {
  Write-Error "File not found: $Path"
  exit 1
}

$resolved = (Resolve-Path -LiteralPath $Path).ProviderPath
$content  = Get-Content -LiteralPath $resolved -Raw

if ($content -notmatch '"Version"\s*:\s*"[^"]+"') {
  Write-Error "No 'Version' field found in $resolved."
  exit 1
}

# Lookbehind/lookahead so the surrounding quotes are preserved. The
# literal '"Version"' anchor avoids accidental matches on neighboring
# fields like ManagerVersion or GameVersion (their leading char is not
# a quote).
$updated = $content -replace '(?<="Version":\s*")[^"]+(?=")', $Version

if ($content -eq $updated) {
  Write-Host "$resolved already at Version=$Version; no change."
  exit 0
}

$utf8NoBom = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText($resolved, $updated, $utf8NoBom)
Write-Host "Stamped $resolved Version=$Version"
