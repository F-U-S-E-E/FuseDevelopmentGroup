# Populate FUSE.UnityTests/Assets/Plugins/ with FUSE.dll and the full
# Railroader-shipped Managed/ DLL set so Unity can load FUSE.dll for
# EditMode tests.
#
# Why every DLL: FUSE.dll references Serilog, 0Harmony,
# UnityModManager, UnityEngine.UI; Assembly-CSharp.dll references
# 30+ more (MessagePack, RuntimeLevelDesign, LeanTween, Steamworks,
# MoonSharp, Unity.Burst, KinematicCharacterController, Obi,
# Enviro3.Runtime, Heathen.*, GPUInstancer, ...). Unity refuses to
# load a DLL until ALL its referenced assemblies resolve, so without
# the full graph we get cascade failures and zero tests discovered.
# Copying the whole Managed/ folder is the brute-force approach but
# it's the cleanest one — Railroader's Managed/ is authoritative for
# what FUSE compiles against.
#
# Each copied DLL also gets a custom .meta file with:
#   - validateReferences: 0  (Unity stops bouncing on the dep graph)
#   - Editor platform enabled: 1
#   - Any platform enabled: 0  (these DLLs only matter for the test
#     domain; we never want them included in player builds)
#
# Run before launching the Unity Test Runner. Idempotent — re-running
# just overwrites with the latest DLLs. FUSE.UnityTests/.gitignore excludes
# Assets/Plugins/*.dll and *.meta so nothing here ends up in version
# control.

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSCommandPath)
$plugins  = Join-Path $repoRoot 'FUSE.UnityTests\Assets\Plugins'
$fuseDll  = Join-Path $repoRoot 'FUSE\bin\Release\net48\FUSE.dll'

# GameDir mirrors the same property the production build uses (see
# FUSE.csproj). Honour an explicit override env var first so CI / users
# with a non-Steam install can point this at their copy.
$gameDir = $env:GameDir
if (-not $gameDir) {
    $candidates = @(
        'F:\SteamLibrary\steamapps\common\Railroader',
        'D:\SteamLibrary\steamapps\common\Railroader',
        'C:\Steam\steamapps\common\Railroader',
        'C:\Program Files (x86)\Steam\steamapps\common\Railroader'
    )
    foreach ($c in $candidates) {
        if (Test-Path (Join-Path $c 'Railroader.exe')) { $gameDir = $c; break }
    }
}
if (-not $gameDir -or -not (Test-Path $gameDir)) {
    throw "Could not locate Railroader install. Set `$env:GameDir or edit prepare_assets.ps1."
}

$managed = Join-Path $gameDir 'Railroader_Data\Managed'
if (-not (Test-Path $managed)) {
    throw "Railroader_Data\Managed not found under '$gameDir'."
}

if (-not (Test-Path $fuseDll)) {
    throw "FUSE.dll not found at '$fuseDll'. Run 'dotnet build FUSE/FUSE.csproj -c Release' first."
}

# Some Managed/ DLLs are Unity Editor-only stubs (UnityEditor.*) or
# things we definitely should not redistribute. The Editor stubs would
# clash with the Unity Editor's own copies; skip them.
$skip = @(
    'UnityEditor.dll',
    'UnityEditor.CoreModule.dll',
    'UnityEditor.UI.dll'
)

# Stable-ish guid generator: hash the DLL filename so the same name
# resolves to the same guid across runs. Unity needs guids unique per
# asset within a project, and "stable across reruns of prepare_assets"
# is enough to keep the editor's import cache happy.
function New-StableGuid([string]$key) {
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($key)
    $hash  = [System.Security.Cryptography.MD5]::Create().ComputeHash($bytes)
    # Stringify as Unity's 32-char-no-dashes lowercase hex form.
    -join ($hash | ForEach-Object { '{0:x2}' -f $_ })
}

function Write-PluginMeta([string]$dllPath) {
    # Match Unity's own auto-generated PluginImporter YAML shape, with
    # two deliberate changes from the default:
    #   * validateReferences: 0  — Unity stops bouncing the import on
    #     missing transitive references (Serilog, Steamworks, etc.)
    #     so we can load FUSE.dll without dragging in every nuget
    #     dep Railroader has under the sun.
    #   * Any/Editor flipped — default is Any=1 / Editor=0 (i.e. for
    #     player builds, not Editor), we want the opposite so the
    #     DLLs are visible to the Editor-mode test runner.
    #
    # The bare `Any:`-with-trailing-space (no value) form is what
    # Unity emits; replacing it with `: Any` (colon-before-key) was a
    # YAML parser error we shipped once and won't again — Unity 2022
    # logs "Expect ':' between key and value within mapping" line 40.
    $name = [System.IO.Path]::GetFileName($dllPath)
    $guid = New-StableGuid -key "fuse-unitytests::$name"
    $meta = @"
fileFormatVersion: 2
guid: $guid
PluginImporter:
  externalObjects: {}
  serializedVersion: 2
  iconMap: {}
  executionOrder: {}
  defineConstraints: []
  isPreloaded: 0
  isOverridable: 0
  isExplicitlyReferenced: 0
  validateReferences: 0
  platformData:
  - first:
      Any:
    second:
      enabled: 0
      settings: {}
  - first:
      Editor: Editor
    second:
      enabled: 1
      settings:
        CPU: AnyCPU
        DefaultValueInitialized: true
        OS: AnyOS
  - first:
      Windows Store Apps: WindowsStoreApps
    second:
      enabled: 0
      settings:
        CPU: AnyCPU
  userData:
  assetBundleName:
  assetBundleVariant:
"@
    Set-Content -Path ($dllPath + '.meta') -Value $meta -Encoding UTF8 -NoNewline
}

Write-Host "Repo root: $repoRoot"
Write-Host "GameDir:   $gameDir"
Write-Host "Plugins:   $plugins"
Write-Host ''

# Wipe any previous DLLs first so a removed dependency doesn't linger.
Get-ChildItem -Path $plugins -Filter '*.dll' -ErrorAction SilentlyContinue | Remove-Item -Force
Get-ChildItem -Path $plugins -Filter '*.dll.meta' -ErrorAction SilentlyContinue | Remove-Item -Force

# 1. FUSE.dll first — the artifact under test.
$dst = Join-Path $plugins 'FUSE.dll'
Copy-Item $fuseDll $dst -Force
Write-PluginMeta -dllPath $dst
Write-Host "Copied FUSE.dll"

# 2. Every DLL from Railroader's Managed/ AND its subdirectories.
#    Critically, this picks up Managed/UnityModManager/*.dll (UMM
#    itself + 0Harmony + dnlib) which FuseLog references — without
#    them, every test that exercises FuseLog hits a TypeLoadException
#    during JIT and the whole executor stack is unreachable.
#
#    All copies land flat in Plugins/ because Unity doesn't honour
#    nested directory structure for plugin asmrefs the way .NET does;
#    asmname resolution looks at every Plugins/ DLL by simple name.
#
#    Duplicate handling: some DLLs are shipped twice — e.g.
#    Managed/0Harmony.dll and Managed/UnityModManager/0Harmony.dll.
#    For BIT-IDENTICAL duplicates (same SHA256) silently dedupe; the
#    first one encountered wins. For genuinely different files
#    sharing a name, fail loud — Unity would silently pick one and
#    leave tests subtly broken otherwise.
$copied = 0
$skipped = 0
$dedup = 0
$seen = @{}
foreach ($src in Get-ChildItem -Path $managed -Filter '*.dll' -Recurse) {
    if ($skip -contains $src.Name) { $skipped++; continue }
    if ($seen.ContainsKey($src.Name)) {
        $previousHash = $seen[$src.Name].Hash
        $currentHash  = (Get-FileHash $src.FullName -Algorithm SHA256).Hash
        if ($previousHash -eq $currentHash) {
            $dedup++
            continue
        }
        throw "Two DIFFERENT DLLs named '$($src.Name)' under '$managed' (first at '$($seen[$src.Name].Path)' hash $previousHash, second at '$($src.FullName)' hash $currentHash). Pick one explicitly in this script before continuing."
    }
    $hash = (Get-FileHash $src.FullName -Algorithm SHA256).Hash
    $seen[$src.Name] = @{ Path = $src.FullName; Hash = $hash }
    $target = Join-Path $plugins $src.Name
    Copy-Item $src.FullName $target -Force
    Write-PluginMeta -dllPath $target
    $copied++
}
Write-Host "Copied $copied Railroader Managed/ DLLs (skipped $skipped Editor stubs; deduped $dedup identical copies)"
Write-Host ''
Write-Host 'prepare_assets.ps1 complete.'
