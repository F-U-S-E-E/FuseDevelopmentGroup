# Releasing

FUSE ships in two independent lanes, each driven by a git tag. Pushing the tag
is the only trigger — the matching workflow does the build, packaging, and
GitHub Release.

## Mod release — `mod-v<semver>`

Tag: `mod-v0.13.0`, `mod-v1.0.1-rc2`, ... (must match
`mod-v<major>.<minor>.<patch>` with an optional `-prerelease` suffix).

Runs [`.github/workflows/release.yml`](../.github/workflows/release.yml) on the
**self-hosted** Railroader runner (it needs the game's Unity/UMM reference
assemblies). Produces and attaches:

- `FUSE-v<ver>.zip` — the UMM runtime mod (FUSE.dll, stamped `Info.json`,
  schemas, icon, `LICENSE`). **This is the only file that goes to Nexus.**
- `FUSE-Complete-v<ver>.zip` — everything in one download, GitHub only:
  `Mods/FUSE`, `Mods/FUSE.LiveBridge`, `Tools/` (converter, installer,
  folder-converter), plus `LICENSE` and `INSTALL.md`. `Mods/` sits at the
  archive root so `FUSE-Installer.exe` can consume it as a multi-package zip.
- `FUSE.LiveBridge-v<ver>.zip` — optional in-game hot-reload bridge
- `FUSE-Converter.exe`, `FUSE-Installer.exe`, `FUSEConvertFolder.pyz` — tools

Both mod zips carry `LICENSE`; FUSE ships under the AGPL-3.0, which requires
conveying the license with the binary, and `scripts/Validate-ModPackage.cs`
fails the release if it is missing.

Nexus receives the core zip alone, on purpose. A player installing from Nexus
needs one file; the converter, installer and dev bridge only belong in front of
package authors, who get them from GitHub.

The mod version flows in via `-p:ModVersion=<ver>`, which stamps the assemblies
and `Info.json` (see `Directory.Build.targets`). The release flow is the only
place a real version is set.

The source `FUSE/Info.json` stays pinned at `0.0.0`. A build without
`-p:ModVersion=...` skips stamping and mirrors source, so `0.0.0` showing in
Unity Mod Manager is the deliberate signal that someone is running a local or
debug build rather than a release. Do not bump the source manifest to track the
latest release, and do not add a fallback that derives a version from git tags —
either would make a dev build indistinguishable from a shipped one in UMM.
`scripts/Validate-ModPackage.cs` fails the release if a packaged `Info.json`
still reads `0.0.0`, so an unstamped build cannot ship.

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

- **The mod lane publishes release candidates as full releases, not GitHub
  prereleases.** GitHub defines the "Latest" badge and `/releases/latest` as the
  newest non-draft, non-prerelease release, so a tag flagged prerelease can
  never hold it. Publishing RCs normally is what puts the current RC in front of
  testers on the repo front page. Both `-rc2` and `-rc.1` spellings count; any
  other suffix (`-beta.2`, `-alpha.1`) still publishes as a GitHub prerelease.
  Consequence to keep in mind: after pushing an RC, `/releases/latest` points at
  it rather than at the last GA, so cut the GA tag when the RCs settle.
- The external-editor lane still treats **any** suffix as a prerelease. The two
  lanes deliberately differ here; align them if that ever becomes confusing.
- Nothing writes a version back into the repo. There used to be a
  `sync-info-json.yml` workflow that committed the released version into
  `FUSE/Info.json` on `main`; it was removed because it fought the pinned-`0.0.0`
  rule above — with it working, a local build would report the last released
  version and look exactly like a release in UMM. The version lives in the tag
  and reaches artifacts through `-p:ModVersion`; the repo does not track it.
- The tag prefixes don't collide: `mod-v*`, `externaleditor-v*`, and the
  existing `tools-v*` are disjoint globs.
- To dry-run a lane, push a throwaway tag (e.g. `externaleditor-v0.2.0-rc.1`),
  confirm the release and its assets, then delete the tag and release with
  `gh release delete <tag> --yes --cleanup-tag`.
