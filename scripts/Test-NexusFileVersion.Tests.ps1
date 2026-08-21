#requires -Version 5
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$probePath = Join-Path $PSScriptRoot 'Test-NexusFileVersion.ps1'
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) (
  'fuse-nexus-probe-tests-{0}' -f [Guid]::NewGuid().ToString('N')
)
$originalApiKey = [Environment]::GetEnvironmentVariable('NEXUSMODS_API_KEY')

function Assert-Condition {
  param(
    [Parameter(Mandatory = $true)]
    [bool]$Condition,

    [Parameter(Mandatory = $true)]
    [string]$Message
  )

  if (!$Condition) {
    throw $Message
  }
}

function Invoke-SuccessCase {
  param(
    [Parameter(Mandatory = $true)]
    [string]$Name,

    [Parameter(Mandatory = $true)]
    [object]$Response,

    [Parameter(Mandatory = $true)]
    [ValidateSet('true', 'false')]
    [string]$ExpectedPublishRequired
  )

  $global:NexusProbeMockMode = 'return'
  $global:NexusProbeMockResponse = $Response
  $outputPath = Join-Path $tempRoot "$Name-output.txt"

  & $probePath `
    -FileId '7822458' `
    -Version '1.2.3' `
    -GitHubOutputPath $outputPath `
    -ApiBaseUrl 'https://mock.nexus.invalid/v3'

  $outputs = Get-Content -LiteralPath $outputPath
  Assert-Condition `
    -Condition ($outputs -contains 'file_id=7822458') `
    -Message "$Name did not emit the file_id output."
  Assert-Condition `
    -Condition ($outputs -contains "publish_required=$ExpectedPublishRequired") `
    -Message "$Name emitted the wrong publish_required output."
  Assert-Condition `
    -Condition ($global:NexusProbeLastUri -eq 'https://mock.nexus.invalid/v3/mod-files/7822458/versions') `
    -Message "$Name called the wrong Nexus endpoint."
  Assert-Condition `
    -Condition ($global:NexusProbeLastHeaders.apikey -eq 'test-api-key') `
    -Message "$Name did not send the API key header."
  Assert-Condition `
    -Condition ($global:NexusProbeLastTimeoutSec -eq 30) `
    -Message "$Name did not use the expected Nexus request timeout."
}

function Invoke-FailureCase {
  param(
    [Parameter(Mandatory = $true)]
    [string]$Name,

    [Parameter(Mandatory = $true)]
    [object]$Response,

    [Parameter(Mandatory = $false)]
    [switch]$RequestFailure
  )

  $global:NexusProbeMockMode = if ($RequestFailure) { 'throw' } else { 'return' }
  $global:NexusProbeMockResponse = $Response
  $outputPath = Join-Path $tempRoot "$Name-output.txt"
  $failed = $false

  try {
    & $probePath `
      -FileId '7822458' `
      -Version '1.2.3' `
      -GitHubOutputPath $outputPath `
      -ApiBaseUrl 'https://mock.nexus.invalid/v3'
  }
  catch {
    $failed = $true
  }

  Assert-Condition -Condition $failed -Message "$Name should have failed closed."
  Assert-Condition `
    -Condition (!(Test-Path -LiteralPath $outputPath)) `
    -Message "$Name wrote outputs despite a failed lookup."
}

try {
  New-Item -ItemType Directory -Path $tempRoot | Out-Null
  [Environment]::SetEnvironmentVariable('NEXUSMODS_API_KEY', 'test-api-key')

  function global:Invoke-RestMethod {
    [CmdletBinding()]
    param(
      [Parameter(Mandatory = $true)]
      [string]$Uri,

      [Parameter(Mandatory = $true)]
      [string]$Method,

      [Parameter(Mandatory = $true)]
      [hashtable]$Headers,

      [Parameter(Mandatory = $true)]
      [int]$TimeoutSec
    )

    $global:NexusProbeLastUri = $Uri
    $global:NexusProbeLastHeaders = $Headers
    $global:NexusProbeLastTimeoutSec = $TimeoutSec
    if ($global:NexusProbeMockMode -eq 'throw') {
      throw 'Simulated Nexus API failure.'
    }
    return $global:NexusProbeMockResponse
  }

  Invoke-SuccessCase `
    -Name 'existing-version' `
    -Response ([pscustomobject]@{
      data = [pscustomobject]@{
        versions = @([pscustomobject]@{ version = '1.2.3' })
      }
    }) `
    -ExpectedPublishRequired 'false'

  Invoke-SuccessCase `
    -Name 'missing-version' `
    -Response ([pscustomobject]@{
      data = [pscustomobject]@{
        versions = @([pscustomobject]@{ version = '1.2.2' })
      }
    }) `
    -ExpectedPublishRequired 'true'

  Invoke-SuccessCase `
    -Name 'empty-version-list' `
    -Response ([pscustomobject]@{
      data = [pscustomobject]@{
        versions = @()
      }
    }) `
    -ExpectedPublishRequired 'true'

  Invoke-SuccessCase `
    -Name 'case-sensitive-version' `
    -Response ([pscustomobject]@{
      data = [pscustomobject]@{
        versions = @([pscustomobject]@{ version = '1.2.3-RC1' })
      }
    }) `
    -ExpectedPublishRequired 'true'

  Invoke-FailureCase `
    -Name 'missing-versions-property' `
    -Response ([pscustomobject]@{ data = [pscustomobject]@{} })

  Invoke-FailureCase `
    -Name 'invalid-version-entry' `
    -Response ([pscustomobject]@{
      data = [pscustomobject]@{
        versions = @([pscustomobject]@{ id = 'missing-version' })
      }
    })

  Invoke-FailureCase `
    -Name 'scalar-versions-property' `
    -Response ([pscustomobject]@{
      data = [pscustomobject]@{
        versions = [pscustomobject]@{ version = '1.2.3' }
      }
    })

  Invoke-FailureCase `
    -Name 'duplicate-version' `
    -Response ([pscustomobject]@{
      data = [pscustomobject]@{
        versions = @(
          [pscustomobject]@{ version = '1.2.3' },
          [pscustomobject]@{ version = '1.2.3' }
        )
      }
    })

  Invoke-FailureCase `
    -Name 'request-failure' `
    -Response ([pscustomobject]@{}) `
    -RequestFailure

  [Environment]::SetEnvironmentVariable('NEXUSMODS_API_KEY', $null)
  Invoke-FailureCase `
    -Name 'missing-api-key' `
    -Response ([pscustomobject]@{})

  $repoRoot = Split-Path -Parent $PSScriptRoot
  $workflowPaths = @(
    (Join-Path $repoRoot '.github\workflows\release.yml'),
    (Join-Path $repoRoot '.github\workflows\release-optional-mod.yml')
  )
  foreach ($workflowPath in $workflowPaths) {
    $workflow = Get-Content -LiteralPath $workflowPath -Raw
    Assert-Condition `
      -Condition ($workflow -match '(?m)^\s+environment: nexus-release\s*$') `
      -Message "$workflowPath does not use the protected Nexus environment."
    Assert-Condition `
      -Condition ($workflow -match 'git fetch --no-tags origin main:refs/remotes/origin/main') `
      -Message "$workflowPath does not verify its tag against protected main."
    Assert-Condition `
      -Condition ($workflow -match 'Nexus-Mods/upload-action@c96019556046053aa26044b44396cd38929daf23') `
      -Message "$workflowPath does not pin Nexus upload-action beta.10."
    Assert-Condition `
      -Condition ($workflow -match 'steps\.nexus\.outputs\.publish_required') `
      -Message "$workflowPath does not gate uploads on the preflight result."
    Assert-Condition `
      -Condition ($workflow -notmatch '(?m)^\s+(file_group_id|file_category|archive_existing_file):') `
      -Message "$workflowPath still uses a removed Nexus upload-action input."

    if ([System.IO.Path]::GetFileName($workflowPath) -eq 'release.yml') {
      $packageIndex = $workflow.IndexOf('- name: Package Nexus variant')
      $preflightIndex = $workflow.IndexOf('- name: Preflight Nexus upload')
      $releaseIndex = $workflow.IndexOf('- name: Create GitHub release')
      Assert-Condition `
        -Condition (
          $packageIndex -ge 0 -and
          $packageIndex -lt $preflightIndex -and
          $preflightIndex -lt $releaseIndex
        ) `
        -Message "$workflowPath must package and preflight Nexus before creating the GitHub release."
    }
  }

  Write-Host 'Nexus file-version preflight tests passed.'
}
finally {
  [Environment]::SetEnvironmentVariable('NEXUSMODS_API_KEY', $originalApiKey)
  Remove-Item -Path Function:\Invoke-RestMethod -ErrorAction SilentlyContinue
  Remove-Variable -Name NexusProbeMockMode -Scope Global -ErrorAction SilentlyContinue
  Remove-Variable -Name NexusProbeMockResponse -Scope Global -ErrorAction SilentlyContinue
  Remove-Variable -Name NexusProbeLastUri -Scope Global -ErrorAction SilentlyContinue
  Remove-Variable -Name NexusProbeLastHeaders -Scope Global -ErrorAction SilentlyContinue
  Remove-Variable -Name NexusProbeLastTimeoutSec -Scope Global -ErrorAction SilentlyContinue
  if (Test-Path -LiteralPath $tempRoot -PathType Container) {
    Remove-Item -LiteralPath $tempRoot -Recurse -Force
  }
}
