# FUSE.TestBridge (dev-only)

A point-to-point RPC channel for driving the **live** Railroader game to test FUSE —
reload packages, run `/fuse.*` console commands, and read the load report from outside
the game. It is the in-game half; [`FUSE.TestCli`](../FUSE.TestCli) is the driver Claude
(or you) invokes from a shell.

This mod is **dev-only** and triple-gated so it can never affect a player:

1. A normal build does not deploy it (`EnableTestBridgeDeploy` defaults `false`).
2. Its `Info.json` ships `"Enabled": false`.
3. The host refuses to spawn unless `FUSE_TEST_BRIDGE=1` **or** `Info.json "Enabled": true`.

## How it works

The `FUSE.TestBridge` MonoBehaviour (DontDestroyOnLoad) watches its own
`Mods/FUSE.TestBridge/` folder for `test_request_<id>.json` files. On the Unity **main
thread** (in `Update()`) it executes each via the public `FUSE.Testing.FuseTestApi` facade
and writes `test_result_<id>.json` (correlated by `RequestId`), then deletes the request.
Per-request files mean a slow or aborted request never clobbers the next. It also writes a
~1s heartbeat to `test_state.json` (`pid`, `gameVersion`, `mapLoaded`, `canApply`, log path).

All IO reuses the atomic, camelCase `Fuse.Core.Bridge` protocol shared with the editor's
live-reload bridge, so the net48 game and the net10 CLI exchange the exact same DTOs.

Verbs: `reload`, `reloadTerrain`, `report` (`json`/`detail`), `console` (any `/fuse.*`),
`dump` (graph/runtimegraph/mandelas/progression), `screenshot`, `loadSave`, `save`, `saves`,
`umm` (open/close overlay), `newGame` (fresh sandbox), `cleanup` (delete test saves).

## Enable it

Deploy the mod into your Railroader install (worktrees pass `GameDir` explicitly — see
[the build note](../README.md#local-development)):

```powershell
dotnet build FUSE.TestBridge/FUSE.TestBridge.csproj -c Debug `
  -p:GameDir=F:\SteamLibrary\steamapps\common\Railroader `
  -p:EnableTestBridgeDeploy=true
```

Then enable it (pick one) and launch the game:

```powershell
$env:FUSE_TEST_BRIDGE = "1"     # session-scoped, or set "Enabled": true in the deployed Info.json
```

Check `UnityModManager`'s log for `FUSE.TestBridge enabled; watching its folder for test requests.`

## Drive it

```powershell
$env:FUSE_GAME_MODS = "F:\SteamLibrary\steamapps\common\Railroader\Mods"

dotnet run --project FUSE.TestCli -- status                  # connection + mapLoaded + canApply (no round trip)
dotnet run --project FUSE.TestCli -- newgame clean           # fresh sandbox from the menu; deletes old fuse-test-* saves
dotnet run --project FUSE.TestCli -- ready                   # block until connected, mapLoaded, and FUSE has settled
dotnet run --project FUSE.TestCli -- save fuse-test-clean    # persist the clean baseline
dotnet run --project FUSE.TestCli -- reload "my change"      # re-read + re-apply; prints applied count
dotnet run --project FUSE.TestCli -- report json
dotnet run --project FUSE.TestCli -- dump runtimegraph       # writes the dump JSON; prints its path
dotnet run --project FUSE.TestCli -- umm close               # hide the mod-manager overlay
dotnet run --project FUSE.TestCli -- screenshot before-fix   # prints the saved PNG path
dotnet run --project FUSE.TestCli -- tail-log 200            # last 200 lines of FUSE.log (no round trip)
dotnet run --project FUSE.TestCli -- load fuse-test-clean    # reset to the baseline (cold-boot or in-session swap)
dotnet run --project FUSE.TestCli -- run-fixture example     # golden-master a fixture (see tests/live/fixtures)
```

`status`, `tail-log`, and `ready` read local files directly (no round trip). `ready` waits
until the heartbeat is steadily beating, so a heavy post-load apply has finished before the
next command runs. `load` cold-boots from the main menu, or swaps saves in-session when a
map is already loaded.

## Test saves

Harness-created saves use the reserved `fuse-test-` prefix. `cleanup` (and the cleanup step
of `newgame`) **only ever delete saves with that prefix** — your real saves are never
touched. Typical loop: `newgame clean` → `ready` → `save fuse-test-clean` once; then reset to
that clean baseline any time with `load fuse-test-clean`, or `newgame` again for a fresh one
(which removes the previous `fuse-test-*`).

## Status / roadmap

Implemented: the interactive driver (reload, console, report, status), screenshots, log
tailing, dump capture, save + load (cold-boot from the menu and in-session swap), fresh-sandbox
`newGame` with prefixed-save cleanup, the UMM overlay toggle, a settle-aware `ready`,
per-request files (no clobbering), and the golden-master regression harness (`FUSE.LiveHarness`
+ `run-fixture`, with normalize/diff covered by `FUSE.LiveHarness.Tests`).

Deferred (optional): a localhost HTTP transport for lower-latency interactive loops — the
file channel covers every verb, so this is only worth adding if the loop feels sluggish.
See the project plan.
