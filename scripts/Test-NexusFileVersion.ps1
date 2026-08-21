#requires -Version 5
<#
.SYNOPSIS
Checks whether a Nexus Mods file already contains a release version.

.DESCRIPTION
Queries the Nexus Mods v3 file-version endpoint before a release upload. The
script fails closed when the API cannot be queried or returns an unexpected
payload. It writes GitHub Actions outputs that allow an existing version to be
treated as a successful no-op instead of uploading and archiving it again.

The API key is read only from the NEXUSMODS_API_KEY environment variable so it
is never exposed in a process command line.
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)]
  [ValidatePattern('^[1-9][0-9]*$')]
  [string]$FileId,

  [Parameter(Mandatory = $true)]
  [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+$')]
  [string]$Version,

  [Parameter(Mandatory = $true)]
  [string]$GitHubOutputPath,

  [Parameter(Mandatory = $false)]
  [ValidatePattern('^https?://')]
  [string]$ApiBaseUrl = 'https://api.nexusmods.com/v3'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$apiKey = [Environment]::GetEnvironmentVariable('NEXUSMODS_API_KEY')
if ([string]::IsNullOrWhiteSpace($apiKey)) {
  throw 'Environment secret NEXUSMODS_API_KEY is not configured.'
}

$outputDirectory = Split-Path -Parent $GitHubOutputPath
if (![string]::IsNullOrWhiteSpace($outputDirectory) -and
    !(Test-Path -LiteralPath $outputDirectory -PathType Container)) {
  throw "GitHub output directory '$outputDirectory' does not exist."
}

$requestUri = '{0}/mod-files/{1}/versions' -f $ApiBaseUrl.TrimEnd('/'), $FileId
$headers = @{
  Accept = 'application/json'
  apikey = $apiKey
  'User-Agent' = 'F-U-S-E-E/FuseDevelopmentGroup-release'
}

try {
  $response = Invoke-RestMethod `
    -Uri $requestUri `
    -Method Get `
    -Headers $headers `
    -ErrorAction Stop
}
catch {
  $statusSuffix = ''
  if ($null -ne $_.Exception.Response -and
      $null -ne $_.Exception.Response.StatusCode) {
    $statusSuffix = " (HTTP $([int]$_.Exception.Response.StatusCode))"
  }
  throw "Nexus version lookup failed for file ID '$FileId'$statusSuffix."
}

if ($null -eq $response -or
    $null -eq $response.PSObject.Properties['data'] -or
    $null -eq $response.data -or
    $null -eq $response.data.PSObject.Properties['versions'] -or
    $null -eq $response.data.versions -or
    !($response.data.versions -is [System.Array])) {
  throw "Nexus version lookup for file ID '$FileId' returned an unexpected response."
}

$versions = @($response.data.versions)
foreach ($item in $versions) {
  if ($null -eq $item -or
      $null -eq $item.PSObject.Properties['version'] -or
      !($item.version -is [string]) -or
      [string]::IsNullOrWhiteSpace($item.version)) {
    throw "Nexus version lookup for file ID '$FileId' returned an invalid version entry."
  }
}

$matchingVersions = @(
  $versions | Where-Object { $_.version -ceq $Version }
)
if ($matchingVersions.Count -gt 1) {
  throw "Nexus file ID '$FileId' contains duplicate entries for version '$Version'."
}

$alreadyPublished = $matchingVersions.Count -eq 1
$publishRequired = if ($alreadyPublished) { 'false' } else { 'true' }

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::AppendAllText(
  $GitHubOutputPath,
  "file_id=$FileId$([Environment]::NewLine)" +
    "publish_required=$publishRequired$([Environment]::NewLine)",
  $utf8NoBom
)

if ($alreadyPublished) {
  Write-Host "Nexus file ID $FileId already contains version $Version; upload is a no-op."
}
else {
  Write-Host "Nexus file ID $FileId does not contain version $Version; upload is required."
}
