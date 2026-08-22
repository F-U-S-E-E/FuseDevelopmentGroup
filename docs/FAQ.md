# Frequently Asked Questions

## General

### What is FUSE?

A Unity Mod Manager modding layer for Railroader. It loads FUSE data packages —
route extensions, asset packs, audio packs, track graph changes, world scenery,
operations, and progression data — and provides drop-in compatibility for legacy
Railloader, Strange Customs, ConfusingSupplements, For Your Convenience, and
Alina's Map Mod packages.

### Which Railroader version does it support?

The `2025.1.x` line. FUSE logs its version report at startup in `FUSE.log`.

### Do I need Railloader as well?

No. FUSE replaces it. Running both means two loaders are live at once, and FUSE
warns when it finds a leftover `Railloader.dll`, `Railloader.Injector.dll`, or
`Railloader.Interchange.dll`.

### Is FUSE free and open source?

Yes, under the GNU Affero General Public License v3.0. See
[LICENSE](../LICENSE).

## Installing And Packages

### Where do packages go?

`Railroader/Mods`, alongside FUSE itself. Package folders end in `.FUSE`.

### Can I disable a package without deleting it?

Yes — uncheck it in Unity Mod Manager. FUSE respects UMM's enabled checkbox for
any package folder UMM can see, and skips its track and data files.

### Why isn't my package loading?

Run `/fuse.loaded`. If the package is not listed at all, FUSE never discovered it —
check that it is in `Railroader/Mods` and enabled in UMM. If it is listed as
faulted, it was found but failed to apply; `FUSE.log` names the package and the
phase that failed.

### A building or piece of scenery is missing.

Usually a missing asset pack. Run `/fuse.assets` to see which packs FUSE actually
found, and install the one the package expects.

Do not work around it by pointing the package at a different asset name. If the
correct pack exists, install it.

### Does a broken package break everything else?

No. A package that faults is reported and skipped; unrelated packages still load.

## Legacy Mods

### Can I run my old mods?

RailLoader JSON data mods can convert. Run them through the converter to produce
a `*.FUSE` package — see
[MIGRATION_FROM_LEGACY.md](MIGRATION_FROM_LEGACY.md). Asset packs and Alina
map-tile packages install directly because FUSE loads their supported legacy
formats; converting them would only create a misleading empty wrapper.

Script mods that ship `.dll` logic do not convert. Neither do rolling stock,
locomotive, and car mods, except audio definitions FUSE can import.

### Can I run the legacy and converted versions of a route together?

No — not for normal play. Both claim the same object ids and you get duplicated
track, industries, or buildings. `/fuse.conflicts` reports it. Loading both is
supported only as a deliberate conflict test.

### Do I need to reconvert after updating?

Reconvert when the converter version changes, the schema version changes, a
converter repair became an actual runtime fix, or the legacy package itself was
updated.

## Multiplayer

### Does FUSE work in multiplayer?

In compatibility mode only. FUSE does not sync package contents over the network.
Every host and client applies its own local package stack — the same expectation
legacy Railloader had, where everyone installs the same mods.

### What do all players need?

The same FUSE build, the same enabled package list, and the same load order,
installed locally on each machine.

### What happens if a client's mods don't match?

FUSE does not negotiate the mismatch. A mismatched client can desync visually or
operationally. Non-host clients log a warning the first time they apply runtime
world changes.

A server that would rather refuse a mismatched client than let it desync can set
`BlockNonHostMultiplayerClientWorldApply` to `true`. It is off by default so
private tests behave like RailLoader. See [SETTINGS.md](SETTINGS.md#multiplayer).

## Saves And Safety

### Will FUSE break my saves?

Back them up before installing, updating, or changing your package list. A save
made with packages loaded depends on the world those packages created — removing a
package that a save relies on changes what that save can load.

### Can I remove FUSE later?

Yes. Remove your `*.FUSE` packages first, then FUSE itself, then restore your
previous mods and save backup. Order matters — see
[GETTING_STARTED.md](GETTING_STARTED.md#uninstalling).

### Are the experimental commands safe?

`/fuse.reapply` and `/fuse.restore` are for testing and recovery, not normal play.
Both refuse to run while a map is loaded unless you pass `--force`, because
re-applying mid-session can destabilize a running save. Back up first. Restarting
the game is the safer option in every case where you are not specifically testing
reload behavior.

## Performance

### FUSE is slow to load / the game stutters.

Turn on `EnableFrameSpikeDiagnostics` to log frames over the threshold, then check
`FUSE.log` for what is spiking. It is a measurement tool — it identifies the cause
rather than fixing it. See [SETTINGS.md](SETTINGS.md#performance-diagnostics).

Note that load time scales with how much your packages actually change: track
segments, scenery, and industries all cost time to apply.

Current builds also name the slowest measured FUSE runtime-pump phase on a
logged spike. `fusePumpWorstPhase='none measured'` means the detector did not
find expensive work in FUSE's own per-frame pump; correlate that timestamp with
the surrounding game and third-party logs instead. Equipment warm-up completion
lines include the slowest asset store and how many stores crossed the slow-store
threshold, which is the useful evidence for a buy-menu delay report.

For a release-quality before/after run, follow
[Performance Acceptance Testing](PERFORMANCE_TESTING.md).

### Should I leave the debug overlays on?

No. The track, scenery, and world-label overlays draw every frame and exist for
authoring and debugging. Leave them off during normal play.

## Authoring

### How do I make a package?

Start with [PACKAGE_AUTHOR_GUIDE.md](PACKAGE_AUTHOR_GUIDE.md), then the schema at
[`schemas/FUSE_JSON_SCHEMA.md`](../schemas/FUSE_JSON_SCHEMA.md).

### Is there an editor?

The standalone desktop editor ships as its own download — see
[EXTERNAL_EDITOR.md](EXTERNAL_EDITOR.md). The full in-game editor workflow is not
part of this release.

### How do I check my package is valid?

`/fuse.validate <modId>` re-runs the validator against a loaded package and prints
errors and warnings with the offending field. The `fuse-convert` CLI also validates
as part of conversion and can fail a build on errors, which is the better fit for
CI.

## Reporting Problems

### What should I include in a bug report?

- `FUSE.log` and `Player.log`
- The output of `/fuse.report json`
- `/fuse.loaded` when package state matters
- `/fuse.conflicts` when conflicts are reported
- `conversion-report.json` and `conversion-report.md` for the affected package
- Screenshots and your exact mod and package list

Turning on `VerboseApplyReportDetails` before reproducing usually identifies the
offending object without a second attempt.

### Where do I report it?

[GitHub Issues](https://github.com/F-U-S-E-E/FuseDevelopmentGroup/issues). Issues
reproducible with a minimal converted package, or against a documented legacy
route the converter covers, get priority.
