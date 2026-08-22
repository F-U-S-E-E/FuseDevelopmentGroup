# Package Author Guide

This guide is the short public authoring contract for FUSE packages. The full schema is documented in [`../schemas/FUSE_JSON_SCHEMA.md`](../schemas/FUSE_JSON_SCHEMA.md) and `schemas/fuse-mod.schema.json`.

## File Shape

Use one or more `*.fuse.json` files inside a FUSE package folder. Each file should keep one clear concern where practical: graph, scenery, industries, progressions, audio, map tiles, or asset metadata.

Every top-level file ending in `*.fuse.json` is an active definition. Do not
leave backups such as `progressions-old.fuse.json` or
`progressions-copy.fuse.json` in the installed package: FUSE cannot distinguish
them from intentional fragments and will apply them too. Rename archival files
so they no longer end in `.fuse.json`, or keep them outside the release folder.

Every file should include:

- `schemaVersion`
- `id`
- `name`
- `modVersion`

`author` may be blank if the source package does not provide one.

## Id Rules

- Package ids should be stable across releases.
- Object ids should be stable and human-readable.
- Do not reuse the same object id for different objects in one package.
- Do not rename an id just to change display text.
- Keep display names in `name`; keep identity in `id`.

## Player-Selectable Features

Use top-level `settings` plus `featureRules` when one package should offer
optional track, scenery, industries, loaders, or other authored sections. The
Tile Editor's **Options** workspace creates on/off, choice, and slider settings,
lets you select the exact objects controlled by each option, and writes both
dictionaries together.

Feature settings must be marked `reloadRequired`; the Editor does this
automatically. A false rule omits only the targets listed by that rule from the
runtime definition. It does not delete those objects from the package file.
Every target must be authored in the same definition, and an industry-component
target uses `industryId/componentId`. See the schema guide for the full target
list and JSON example.

RailLoader output has no equivalent contract. The Editor therefore disables
this workspace in legacy mode instead of creating a lossy or misleading export.

## Dependencies

Use package metadata for required package ordering and dependencies:

- `FuseLoadPriority`
- `FuseRequires` for hard FUSE data-package dependencies
- `FuseLoadAfter`
- `FuseLoadBefore`
- `FuseConflictsWith` for explicit package incompatibilities, with optional
  `NotBefore`/`NotAfter` bounds
- normal UMM `Requirements`/`LoadAfter` when a UMM code mod requires FUSE or another UMM mod

Use `mixinto` when converting legacy conditional mixin files. Missing mixinto
requirements or matching `mixinto.conflictsWith` references skip only that
fragment, not the whole stack.

## Optional References

References that may not exist on every route should be treated as optional by schema/runtime design. Missing optional references should log an info-level skip, not fault an unrelated package.

Hard references should identify:

- package id
- source object id
- source field
- target kind
- target id

## Graph Data

Track graph data is based on node, segment, and span ids. Spans must point at valid segment ids and valid segment ends. FUSE can reference base-game graph objects at runtime, but converted packages should not invent ids that cannot exist in the runtime graph.

Use `/fuse.dumpgraph` and `/fuse.dumpruntimegraph` to inspect graph state.

## Operations Data

Supported industry component types include:

- `loader`
- `unloader`
- `formulaic`
- `repairTrack`
- `teamTrack`
- `interchange`
- `interchangedLoader`
- `interchangedUnloader`
- `teleportLoading`
- `progression`
- `passengerStop`
- fully-qualified custom `IndustryComponent` types from loaded assemblies

Custom components can use `fields` for reflection-bound values. The custom component assembly must be installed separately.

## World Data

Use `world.scenery` for asset-pack objects. Use `world.sceneClones` for base-game scene objects. Use `world.mapMasks` for terrain flattening, tree cutting, height masks, and mask modifiers. Use `world.splineys` for roads, rivers, trestles, and related spline builders.

Packages with a `map` declaration are treated as complete replacement worlds:
`map.suppressBaseWorld` defaults to `true`. FUSE keeps Railroader's required
scene managers but removes the stock track graph and suppresses the stock
operations, scenery, map labels, signs, setups, progression, and CTC content
before applying the selected package. Set `suppressBaseWorld` to `false` only
for a map that intentionally overlays custom terrain on Bushnell/Whittier.

Asset pack objects should keep their real asset identifiers. Do not alias to unrelated assets if the correct pack exists.

### Asset stores and definition overrides

Declare normal asset-pack roots with `FuseAssetPacks`. A runtime store is
identified by `Catalog.json`; `Bundle` is optional for a definitions-only
catalog whose assets are supplied by another store. FUSE reports an actual
missing bundle if an asset from that store is requested.

For the old AssetLoader pattern where a `Definitions.json` file replaces the
definitions of an existing store (rolling-stock and tender swaps), prefer an
explicit native manifest entry:

```json
{
  "Requirements": ["FUSE"],
  "FuseDefinitionOverrides": [
    {
      "StoreIdentifier": "fm-flatcar03",
      "Path": "DefinitionOverrides/fm-flatcar03/Definitions.json"
    }
  ]
}
```

The path must stay inside the package. An object entry names the exact existing
store id; a string path infers the id from its parent folder. FUSE also detects
AssetLoader's legacy immediate-child convention automatically, but native
packages should be explicit. If two packages target the same exact store, FUSE
chooses deterministically and reports both source files.

New packages should require `FUSE`, not `AssetLoader`. The installer's data-only
`AssetLoader` alias exists only for old manifests.

## Diagnostics

Use these commands while authoring:

- `/fuse.report`
- `/fuse.loaded`
- `/fuse.conflicts`
- `/fuse.graph`
- `/fuse.operations`
- `/fuse.progressions`
- `/fuse.assets`
- `/fuse.dumpgraph`
- `/fuse.dumpruntimegraph`
- `/fuse.dumpmandelas`

Warnings should name package id, operation, object id, and field whenever possible.

Malformed JSON and validation faults are isolated to the affected definition.
`/fuse.report` and `/fuse.report json` include the absolute folder/file, JSON
path, line/column when available, and a suggested action. Treat a report with a
faulted package as an authoring failure even if unrelated packages still work.

For task-based examples, see [Authoring Recipes](AUTHORING_RECIPES.md).
