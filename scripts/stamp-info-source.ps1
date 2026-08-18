#requires -Version 5
# Rewrites the "Source" field in a UMM Info.json file.
#
# The running mod reads this provenance stamp (FUSE.Infrastructure.FuseInstallSource)
# to point an out-of-date player at the place they most likely downloaded FUSE.
# Every GitHub-published artifact ships the source default ("github"); the release
# flow runs this script on the Nexus-bound copy ONLY, flipping it to "nexus", so a
# Nexus install links back to Nexus while GitHub stays canonical. See
# .github/workflows/release.yml and docs/RELEASING.md.
#
# The replacement is byte-faithful: only the source value changes; whitespace,
# line endings, key order, and encoding are preserved. The "Source" field must
# already exist (it is committed in FUSE/Info.json) — this script re-stamps it,
# it does not add it.

param(
  [Parameter(Mandatory)][string]$Path,
  [Parameter(Mandatory)][ValidateSet('github', 'nexus', 'local')][string]$Source
)

if (-not (Test-Path -LiteralPath $Path)) {
  Write-Error "File not found: $Path"
  exit 1
}

$resolved = (Resolve-Path -LiteralPath $Path).ProviderPath
$content  = Get-Content -LiteralPath $resolved -Raw

if ($content -notmatch '"Source"\s*:\s*"[^"]*"') {
  Write-Error "No 'Source' field found in $resolved. It must be present in the source Info.json."
  exit 1
}

# Lookbehind/lookahead so the surrounding quotes are preserved. The literal
# '"Source"' anchor avoids matching any neighboring field. The lookbehind
# tolerates whitespace around the colon (.NET allows variable-length lookbehind)
# so it stays symmetric with the existence guard above — otherwise a manifest
# formatted as '"Source" : "..."' would pass the guard yet silently not stamp.
$updated = $content -replace '(?<="Source"\s*:\s*")[^"]*(?=")', $Source

if ($content -eq $updated) {
  Write-Host "$resolved already at Source=$Source; no change."
  exit 0
}

$utf8NoBom = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText($resolved, $updated, $utf8NoBom)
Write-Host "Stamped $resolved Source=$Source"
