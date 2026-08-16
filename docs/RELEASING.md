# Releasing

FUSE ships in two independent lanes, each driven by a git tag. Pushing the tag
is the only trigger — the matching workflow does the build, packaging, and
GitHub Release.

## Mod release — `mod-v<semver>`

Tag: `mod-v0.13.0`, `mod-v1.0.0-rc.1`, ... (must match
`mod-v<major>.<minor>.<patch>` with an optional `-prerelease` suffix).

Runs [`.github/workflows/release.yml`](../.github/workflows/release.yml) on the
**self-hosted** Railroader runner (it needs the game's Unity/UMM reference
assemblies). Produces and attaches:

- `FUSE-v<ver>.zip` — the UMM runtime mod (FUSE.dll, stamped `Info.json`,
  schemas, icon)
- `FUSE.LiveBridge-v<ver>.zip` — optional in-game hot-reload bridge
- `FUSE-Converter.exe`, `FUSE-Installer.exe`, `FUSEConvertFolder.pyz` — tools

The mod version flows in via `-p:ModVersion=<ver>`, which stamps the assemblies
and `Info.json` (see `Directory.Build.targets`). On a non-prerelease (GA) tag,
[`sync-info-json.yml`](../.github/workflows/sync-info-json.yml) commits the
matching `FUSE/Info.json` version back to `main`.

This lane does **not** ship the standalone editor — that has its own lane.

## External editor release — `externaleditor-v<semver>`

Tag: `externaleditor-v0.2.0`, `externaleditor-v1.0.0-rc.1`, ...

The prefix is `externaleditor-` (not `editor-`) to distinguish the standalone
desktop editor from the retired in-game editor, which is no longer shipped.

Runs [`.github/workflows/release-externaleditor.yml`](../.github/workflows/release-externaleditor.yml)
on a **GitHub-hosted** `ubuntu-latest` runner — the standalone editor is
game-free (`FuseIsGameFree=true`), so it needs no Railroader DLLs and builds in
the same environment CI's `build-net10` job already proves. Produces and
attaches the self-contained, per-OS bundles:

- `FUSE.ExternalEditor-v<ver>-win-x64.zip`
- `FUSE.ExternalEditor-v<ver>-linux-x64.zip`
- `FUSE.ExternalEditor-v<ver>-osx-x64.zip`

The version flows in via `-p:ExternalEditorVersion=<ver>` (see
`FUSE.ExternalEditor/FUSE.ExternalEditor.csproj`); local/CI builds without it
fall back to the dev floor in that csproj. The mod and external-editor version
numbers are independent — bump and tag them separately.

## Notes

- Both lanes mark a GitHub prerelease only for an explicit prerelease suffix
  (e.g. `-rc.1`). A plain `<major>.<minor>.<patch>` tag publishes as a full
  release, so the repo front page shows a "Latest" card with the version number.
- A plain GA mod tag also triggers `sync-info-json.yml`, which commits the
  matching `FUSE/Info.json` version back to `main`.
- The tag prefixes don't collide: `mod-v*`, `externaleditor-v*`, and the
  existing `tools-v*` are disjoint globs, and `sync-info-json.yml`'s
  `^mod-v...` parse only ever stamps `Info.json` from a mod release.
- To dry-run a lane, push a throwaway prerelease tag (e.g.
  `externaleditor-v0.2.0-rc.1`), confirm the release and its assets, then
  delete the tag and release.
