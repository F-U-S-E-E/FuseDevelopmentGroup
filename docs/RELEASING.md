# Releasing

FUSE ships through two build-and-release lanes plus one optional-mod promotion
lane, each driven by a disjoint git tag. Pushing the tag is the only trigger —
the matching workflow either builds and packages FUSE-owned code or promotes a
checksum-pinned optional-mod release. Nexus-capable tags are promotion requests:
the core and optional-mod jobs wait at the protected `nexus-release`
environment before GitHub assigns the organization's self-hosted runner or
provides the Nexus credential. One organization member other than the person
who started the deployment must approve it.

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

The Nexus upload also carries a **provenance stamp** so the in-game update check
can send an out-of-date player back to where they installed. Every GitHub artifact
ships the committed `Info.json` default `"Source": "github"`; the release builds a
second copy of the core zip with `Source` re-stamped to `"nexus"` (via
`scripts/stamp-info-source.ps1`) and uploads *that* one to Nexus. The two zips
share a name and their entire contents — only that one field differs. GitHub stays
canonical for versioning; the stamp only decides which download link the notice
shows. The stamp step is GA-only and gated on `NEXUS_FILE_GROUP_ID`, so only stable
builds are ever stamped `nexus`. The runtime reads it in
`FUSE.Infrastructure.FuseInstallSource`.

Before a stable upload, the workflow queries the Nexus v3 file-version API for
the exact release version. A matching live version makes a rerun a successful
no-op; an absent version proceeds to upload. HTTP errors, authentication errors,
and malformed responses fail closed before the upload action runs. The legacy
repository variable name `NEXUS_FILE_GROUP_ID` is retained for compatibility,
but its API Info value is the Nexus v3 `file_id`. This is version-level replay
protection; Nexus does not expose the remote archive's SHA-256 in this response,
so it complements rather than replaces the workflow's checksum validation of
the source artifact.

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

## Optional mod Nexus promotion — product tags

The optional mods keep their source and build releases in their own repositories,
but FUSE is the authority that promotes an exact release asset to Nexus:

| Product | Source repository | FUSE tag |
| --- | --- | --- |
| Tile Editor Suite | `F-U-S-E-E/Tile_Editor` | `tileeditorsuite-v<semver>` |
| Toolshed FUSE | `F-U-S-E-E/TheToolShed` | `toolshed-v<semver>` |
| FUSE Narrow Gauge | `F-U-S-E-E/Narrow_Gauge` | `narrowgauge-v<semver>` |

These tags run
[`.github/workflows/release-optional-mod.yml`](../.github/workflows/release-optional-mod.yml)
on the organization's **self-hosted** Railroader runner, keeping deployment and
the Nexus credential on the same runner as the core FUSE lane. The workflow
does not rebuild or edit the optional repository. It downloads one versioned
ZIP from that repository's GitHub release, verifies the SHA-256 and
product-specific UMM layout locked in
[`.github/optional-mod-releases.json`](../.github/optional-mod-releases.json),
attaches the unchanged ZIP to a FUSE repository release record, and uploads the
same bytes to the product's Nexus file chain. This keeps source ownership in the
product repository while centralizing Nexus credentials and deployment in FUSE.

The protected environment gate is reached before the job is assigned to the
self-hosted runner. Matching release tags are also protected by repository tag
rules: only release administrators can create them, they cannot be deleted or
rewritten, and the workflow verifies that each tag resolves to a commit on
protected `main`. The tag supplies the requested product and version; the lock
file and release code on that reviewed commit remain the authority.

Optional GitHub release records always set `make_latest: false`. They must not
replace the `mod-v*` release behind FUSE's `/releases/latest` update and tool
download links. Prerelease versions can create a GitHub prerelease record, but
only a plain `major.minor.patch` version is sent to Nexus.

The versions already uploaded manually to Nexus are recorded as the initial
baselines:

- Hrogers.TileEditorSuite `0.26.6`
- Toolshed FUSE `0.3.0`
- NarrowGaugeMod `0.4.0`

All three baseline entries have `promotionReady: false`, so pushing a baseline
tag fails before it can archive or replace an existing Nexus file. Toolshed and
Narrow Gauge have matching, checksum-pinned GitHub assets in the lock file.
Tile Editor has an additional reconciliation block: Nexus is at `0.26.6`, but
its public source/release metadata and ZIP are still `0.26.4`, and that ZIP uses
Windows backslash entry names. Reconcile its six version sources, create a ZIP
with portable `/` entry names, and cut a matching upstream release before
enabling its next promotion. Advance the relevant lock entry to a successor and
enable it only when that exact upstream asset is ready. The release resolver
fails closed while a baseline is not promotion-ready.

To promote a successor:

1. Build and publish the optional repository's `v<semver>` GitHub release with
   its final, versioned ZIP.
2. Download that ZIP and compute its digest with
   `Get-FileHash -Algorithm SHA256 <archive>`.
3. Update that product's `version`, `sourceTag`, `assetName`, `assetSha256`, and
   `promotionReady` fields in `.github/optional-mod-releases.json`. Keep
   `lastNexusVersion` at the version currently live on Nexus; the resolver
   rejects candidates that are not newer. CI validates the lock file. Merge
   this change before tagging.
4. Tag the locked FUSE commit with the product prefix above and push the tag.
5. Verify both the non-latest FUSE GitHub release record and the Nexus upload.
6. After a successful stable upload, advance `lastNexusVersion` to the published
   version and set `promotionReady: false` in the next FUSE commit. This retains
   the deployed baseline and prevents a later lock-file rollback from being
   promoted as a successor.

The `nexus-release` environment holds `NEXUSMODS_API_KEY`, and the repository
holds one Nexus API Info variable per product. The variable names predate the
v3 API and retain `FILE_GROUP_ID` for compatibility, but their values are v3
`file_id` values:

- `NEXUS_TILE_EDITOR_FILE_GROUP_ID=7822458`
- `NEXUS_TOOLSHED_FILE_GROUP_ID=7822380`
- `NEXUS_NARROW_GAUGE_FILE_GROUP_ID=7822374`

The values are the API Info IDs from the files already uploaded to Nexus, not
the Nexus mod page IDs. Keep the existing `NEXUS_FILE_GROUP_ID` reserved for
the core FUSE release lane. Stable optional-mod promotions use the same
fail-closed v3 lookup as core FUSE: an exact version already present is logged
as a no-op, while only a missing version reaches the pinned upload action.

Repository setup for Nexus publishing therefore requires all of the following:

- A protected `nexus-release` environment with `NEXUSMODS_API_KEY`, required
  organization-member reviewers, self-review prevention, and deployment tag
  policies for `mod-v*`, `tileeditorsuite-v*`, `toolshed-v*`, and
  `narrowgauge-v*`.
- Active tag rules for those four patterns that restrict creation to release
  administrators and prevent deletion or rewriting.
- The four API Info variables described above (the three optional products plus
  the existing core `NEXUS_FILE_GROUP_ID`).

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
- For the core mod, nothing writes a version back into the repo. There used to
  be a `sync-info-json.yml` workflow that committed the released version into
  `FUSE/Info.json` on `main`; it was removed because it fought the pinned-`0.0.0`
  rule above — with it working, a local build would report the last released
  version and look exactly like a release in UMM. The core version lives in the
  tag and reaches artifacts through `-p:ModVersion`; only the optional-mod lock
  file tracks deployed optional-product versions.
- The tag prefixes don't collide: `mod-v*`, `externaleditor-v*`,
  `tileeditorsuite-v*`, `toolshed-v*`, `narrowgauge-v*`, and the existing
  `tools-v*` are disjoint globs. In particular, do not shorten `toolshed-v*`
  to `tools-v*`; that older prefix already belongs to the converter tools.
- To dry-run either build lane, push a throwaway tag (e.g.
  `externaleditor-v0.2.0-rc.1`), confirm the release and its assets, then delete
  the tag and release with `gh release delete <tag> --yes --cleanup-tag`.
  Optional-mod tags are allow-listed by the lock file and are not throwaway
  dry runs; validate their resolver and package locally before pushing.
