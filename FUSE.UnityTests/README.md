# FUSE EditMode tests

A minimal Unity project that drives FUSE's scene-clone executor and
prefab resolver against real Unity `Transform`/`GameObject` instances —
the parts of FUSE that the fast xUnit suite in `../FUSE.Tests/` cannot
reach because `GameObject` can't be instantiated outside the Unity
Editor.

The xUnit suite covers the decision logic (`FuseSceneCloneApplyPlanner`,
`FuseFindChildResolver`); this project covers the execution layer
(`SceneCloneAPI.ApplyDefinition`, `FusePrefabResolver.ResolveScenePath`)
against an actual Unity scene graph.

## What's tested

| Test file | Coverage |
|---|---|
| `Assets/Tests/SceneCloneApplyExecutorTests.cs` | An `{ enabled: true }` mandela on a vanilla wrapper with non-zero `localPosition` must leave the position untouched (the Bryson Freight House regression). Plus enabled/disabled active-state, partial overrides (scale-only, etc.), and the touchless null-enabled case. |
| `Assets/Tests/SceneCloneApiSurfaceTests.cs` | The rest of the `SceneCloneAPI` public surface: Add/Update/Remove round-trips, validation throws (blank id, null definition, missing target, duplicate id), `GetSceneClone` / `GetAllSceneClones` / `TryGetSceneCloneInfo` lookups, `GetDefinition` reading live transform state, `ReapplyEnabledFromCache` reconciling cache-vs-live divergence (and its no-op path for `Enabled = null`), and the marker's `OnEnable` guard auto-re-disabling a force-reactivated clone. |
| `Assets/Tests/FindChildIntegrationTests.cs` | Duplicate-named-sibling disambiguation: content-bearing wins, tie-break by sibling order, exact match beats case-insensitive, null when no match. Drives the real `Transform`-walking wrapper, not just the pure resolver. |
| `Assets/Tests/PrefabResolverUriTests.cs` | `FusePrefabResolver.Resolve(uri)` scheme dispatch (`empty://`, `path://` with/without `scene/` prefix, case-insensitive scheme) and validation throws (unknown scheme, missing `://` separator, null/blank). Plus the `ResolveScenePath` edge cases the FindChild suite doesn't hit (blank input, root-only path, exact-vs-case-insensitive root match). |
| `Assets/Tests/HarmonyPatchInstallationTests.cs` | Harmony's full installation pipeline against `FuseAssetPackPatchHelpers.Assembly` — every prefix/postfix signature must match its target's parameters, no patch class can crash during apply. Plus an end-to-end drive of the `FuseAggregateLoadModelMaterialFieldPatch` prefix against a malformed `MaterialDefinition` to prove the patch actually intercepts the live game type. |
| `Assets/Tests/RailroaderReflectionSurfaceTests.cs` | Regression canary for every Railroader private/internal member FUSE accesses by **name string** via reflection. One `[Test]` per `(type, member, BindingFlags)` tuple across ~25 game types (`PassengerStop`, `Turntable`, `Graph`, `StationAgent`, `MapLabel`/`MapStore`/`MapManager`, `AssetPackRuntimeStore`, `PrefabStore`, `CarInspector`, `MapFeature`, `TrackSpan`, `Industry`/`IndustryComponent`/`FormulaicIndustryComponent`/`RepairTrack`/`IndustryContentHoverable`, `MapFeatureManager`/`ProgressionManager`/`Progression`/`Section`/`InterchangeTransfer`, `RiverBuilder`, `TelegraphPoleManager`, `CTCAutoSignal`, `Model.Car`, `ContainerSerialization`). When a Railroader patch renames or moves a field FUSE depends on, the test goes red immediately and pinpoints exactly which member is gone — instead of the rename surfacing as a silent NRE in someone's playthrough months later. **When you add a new reflection-by-name accessor anywhere in FUSE, add a matching `[Test]` here.** |

## How to run locally

Prerequisites:

- **Unity Editor 2022.3.62f2** — matches Railroader's shipped Unity
  version. Install via Unity Hub. Other versions of 2022.3.x will
  *probably* work but may surface UnityEngine API drift; pin if you can.
- A Railroader install at one of the paths `prepare_assets.ps1`
  probes, or `$env:GameDir` pointing at it.
- A Release build of FUSE (`dotnet build FUSE/FUSE.csproj -c Release`
  from the repo root).

Run:

```powershell
# 1. Copy FUSE.dll + Railroader-shipped DLLs into Assets/Plugins/
.\FUSE.UnityTests\prepare_assets.ps1

# 2. Open the project in Unity Hub, then Window -> General -> Test Runner
#    -> EditMode tab -> Run All.
```

For a headless run (no GUI, suitable for CI):

```powershell
$unity   = 'C:\Program Files\Unity\Hub\Editor\2022.3.62f2\Editor\Unity.exe'
$project = "$PWD\FUSE.UnityTests"
$results = "$PWD\FUSE.UnityTests\TestResults\editmode-results.xml"
New-Item -ItemType Directory -Force -Path (Split-Path $results) | Out-Null

& $unity `
  -batchmode -nographics `
  -projectPath $project `
  -runTests `
  -testPlatform EditMode `
  -testResults $results `
  -logFile -
```

Unity returns exit code 0 (all tests passed) or 2 (passed, but some
warning). Anything else means a test failed or the run itself bombed —
read `editmode-results.xml` (NUnit format) for the details.

## CI

`.github/workflows/ci.yml` includes optional EditMode steps. They are
**off by default** — every PR / push to `main` skips them and the rest
of CI runs as normal. Two ways to enable:

- **For every run**: set the repo variable
  `RUN_UNITY_EDITMODE_TESTS=true` under *Settings → Secrets and
  variables → Actions → Variables*.
- **For one-off runs**: trigger the workflow manually
  (*Actions → CI → Run workflow*) with the `run_unity_editmode`
  checkbox ticked.

When enabled, the steps additionally require Unity 2022.3.62f2 on the
runner — either at the default Hub path
(`C:\Program Files\Unity\Hub\Editor\2022.3.62f2\...`) or pointed at via
`UNITY_EDITOR_PATH`. If Unity isn't found the steps skip cleanly;
they're also `continue-on-error: true` so a Unity install / probe
hiccup never blocks a PR.

## What this project is NOT

- Not a place to add MonoBehaviour-style PlayMode tests. Use the
  EditMode tab and `[Test]` (NUnit), not `[UnityTest]` and coroutines.
- Not a place to depend on Railroader's actual scene assets. The tests
  build their own `Transform` trees in memory so they're hermetic and
  the Bryson Freight House case isn't entangled with whatever the
  shipped scene file currently looks like.
- Not a substitute for the xUnit suite. The xUnit suite is the fast
  feedback loop (under 3 seconds for 634 tests); EditMode tests are
  the slow, thorough backstop.
- Not a substitute for true end-to-end testing against a live
  Railroader session. The reflection canary catches *member-renamed*
  drift; behavioural drift (e.g. a game patch that keeps `_spans` the
  same name and type but changes WHEN it gets re-read) still needs
  the in-game harness tracked in
  [FuseDevelopmentGroup#16](https://github.com/F-U-S-E-E/FuseDevelopmentGroup/issues/16).
