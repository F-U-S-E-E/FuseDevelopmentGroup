# Migrating From Legacy Mods

FUSE replaces the legacy Railroader modding stack. This guide covers moving an
existing install — Railloader, Strange Customs, ConfusingSupplements, For Your
Convenience, or Alina's Map Mod — over to FUSE packages.

If you are setting up a fresh install with no legacy mods, you do not need this
page. See [GETTING_STARTED.md](GETTING_STARTED.md).

## What Changes

Legacy data mods are **converted**, not loaded as-is. The converter reads a legacy
mod folder and writes a `*.FUSE` package next to it, containing the same content
expressed in the FUSE schema plus a conversion report.

What carries over:

| Legacy content | Status under FUSE |
| --- | --- |
| Track / route JSON | Converted, one FUSE data file per source JSON |
| Horn / whistle / bell packs | Converted, audio files copied |
| Strange Customs asset packs | Converted to a FUSE asset wrapper |
| World scenery, scene clones, map masks, splines | Converted |
| Industries, loaders, stations, team/repair tracks | Converted |
| Progression sections and delivery phases | Converted |

What does not carry over:

- **Arbitrary script mods.** A legacy mod that ships `.dll` logic is not a data
  package, and the converter cannot translate its behavior. It warns and leaves
  the binaries alone.
- **Rolling stock, locomotive, and car mods**, except audio definitions.
- **Signals.**
- **Three-way switches** — Railroader does not support them as standard graph
  switches, so legacy content authoring one has an authoring problem that predates
  FUSE.

## The Critical Rule

**Do not run a legacy route and its converted FUSE equivalent at the same time.**

Both stacks will try to own the same object ids, and you get duplicate track,
duplicate industries, or duplicate buildings. `/fuse.conflicts` reports exactly
this situation, and it is the single most common self-inflicted problem when
migrating.

The same applies to the loaders themselves. FUSE detects a leftover
`Railloader.dll` or `Railloader.Interchange.dll` in the install and warns about it,
both on disk and among loaded assemblies. Take the warning seriously — it means
two loaders are live at once.

Loading both is supported only as a deliberate conflict test.

## Migration Steps

### 1. Back up first

Back up your saves and your entire `Mods` folder before touching anything. This is
the step people skip and regret.

### 2. Inventory what you have

List your legacy mods and sort them into three groups:

- **Data mods** (routes, scenery, industries, audio, asset packs) — these convert.
- **Script mods** (`.dll` logic) — these do not convert; check whether a FUSE-native
  equivalent exists.
- **Rolling stock** — out of scope except audio.

### 3. Convert the data mods

Point the converter at each legacy mod folder. The drag-and-drop route:

```powershell
.\FUSE-Converter.exe
```

Or the .NET CLI, which converts and validates in one pass:

```powershell
fuse-convert "C:\Path\To\LegacyMod" --out "C:\Steam\steamapps\common\Railroader\Mods" --format all
```

`--batch` converts an entire folder of legacy mods at once. Full options are in
[FUSE_CONVERTER.md](FUSE_CONVERTER.md).

### 4. Read the conversion report

Every converted package gets `conversion-report.json` and `conversion-report.md`.
**Read them.** The converter classifies each source concept rather than silently
dropping it:

| Outcome | What it means for you |
| --- | --- |
| `converted` | Maps directly to FUSE. Nothing to do. |
| `repaired` | Invalid legacy data was fixed. Worth a look, but generally fine. |
| `preserved` | Kept in the package but without full runtime behavior yet. |
| `dependency-required` | Works once you install the named dependency. |
| `unresolved` | A reference could not be resolved — needs manual repair. |
| `unsupported` | FUSE intentionally does not support this. Named in the report. |
| `error` | Conversion failed. Do not ship or rely on this package. |

Warnings do not automatically mean a package is broken. They mean the conversion
needs a look before you call it verified.

The lossy areas that most often need manual verification: legacy formulaic
`formula` values, progression `interchangeTransfers`, unknown spline or object
handlers, unknown industry component types, and script binaries.

### 5. Remove the legacy versions

Once the converted package is in place, remove or disable the legacy original.
Both cannot be active.

Remove the legacy loader itself only after every data mod you care about has been
converted and verified — you may want to go back.

### 6. Install dependencies

Converted route packages frequently depend on asset packs. Install them as real
asset packs. Do not work around a missing asset by aliasing it to a different
name; if the correct pack exists, install it. `/fuse.assets` lists what FUSE
actually found.

### 7. Verify in game

Load a map and check:

```
/fuse.report
/fuse.loaded
/fuse.conflicts
```

A clean migration shows zero faults and zero conflicts. Then look at the world —
location list, progression visibility, and terrain masks are the things that look
wrong in ways a report will not catch.

## Per-Mod Notes

### Railloader

The legacy loader. FUSE reads Railloader's package metadata directly, including
`RailLoadPriority`, `RailLoadAfter`, and `RailLoadBefore` for load ordering, so
ordering relationships you already established carry across.

Remove `Railloader.dll` and `Railloader.Interchange.dll` once you have migrated —
FUSE warns while they remain.

### Strange Customs

Asset packs convert to a FUSE asset wrapper with `FuseAssetPacks` declared in
`Info.json`. Mixin files convert through the `mixinto` mechanism; a missing mixinto
requirement skips only that fragment rather than faulting the whole stack.

### ConfusingSupplements / For Your Convenience

Data content converts. Any script-driven behavior does not — check the conversion
report for what was classified `unsupported`.

### Alina's Map Mod

Map content converts. As with any route, do not run the legacy version alongside
the converted one.

## When To Reconvert

Reconvert a package when:

- The FUSE converter version changes.
- The schema version changes.
- A converter repair changed from warning-only to an actual runtime fix.
- The legacy package was updated or redownloaded.

## If Something Goes Wrong

Restore your backup — that is what it is for. Then narrow the problem down to a
single package by disabling packages until the symptom disappears, and file a
report with the conversion report for that one package attached.

[TROUBLESHOOTING.md](TROUBLESHOOTING.md) maps symptoms to the diagnostic command
that explains them.

## Related

- [FUSE_CONVERTER.md](FUSE_CONVERTER.md) — full converter reference
- [GETTING_STARTED.md](GETTING_STARTED.md) — installing FUSE itself
- [KNOWN_ISSUES.md](KNOWN_ISSUES.md) — known legacy interaction problems
