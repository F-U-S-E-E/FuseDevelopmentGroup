# Live-harness fixtures

Each fixture is a folder describing a repeatable golden-master check against a **live**
Railroader session, driven by `FUSE.TestCli run-fixture <id>` (which uses
`FUSE.LiveHarness`). The live run is a **local dev gate** — it needs a licensed game
install, the fixture's save, and the `FUSE.TestBridge` mod enabled. It is *not* a CI
gate. (The harness's own normalize/diff logic **is** covered by `FUSE.LiveHarness.Tests`
in CI.)

## Layout

```
tests/live/fixtures/<id>/
  fixture.json            # the manifest (see example/)
  packages/               # optional: the *.FUSE package stack this fixture expects (provision into Mods/)
  save/<name>.shortsave   # optional: the known save (or document where to get it)
  baselines/              # generated golden-master JSON (committed, reviewed like code)
    report.json
    runtimegraph.json
    mandelas.json
```

`baselines/` is written by the first `--update` run and then committed. A normal run
diffs the live capture against it and fails on any delta, pointing at the exact JSON path.

## fixture.json

| Field | Meaning |
|---|---|
| `id` | Fixture identifier (matches the folder name). |
| `gameVersion` | Expected `Application.version`; a mismatch **skips** (baselines are version-pinned). |
| `saveName` | Save to load into the running session before capturing (empty = use the loaded session). |
| `reason` | Reason passed to reload (kept fixed so it does not perturb the report). |
| `captureReport` | Capture + diff `/fuse.report json`. |
| `dumps` | Dump captures to diff: any of `graph`, `runtimegraph`, `mandelas`, `progression`. |

## Running

```powershell
$env:FUSE_GAME_MODS = "F:\SteamLibrary\steamapps\common\Railroader\Mods"
# 1. Launch the game with FUSE.TestBridge enabled and get into any session (host).
# 2. First time — capture baselines, then review/commit them:
dotnet run --project FUSE.TestCli -- run-fixture <id> --update
# 3. Thereafter — diff against the committed baselines (non-zero exit on drift):
dotnet run --project FUSE.TestCli -- run-fixture <id>
```

Captured JSON is normalized before comparison (volatile timestamps/ids stripped, floats
rounded, object keys and arrays sorted) so only meaningful drift surfaces. See
`FUSE.LiveHarness/Json/JsonNormalizer.cs`.
