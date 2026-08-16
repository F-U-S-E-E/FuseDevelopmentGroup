# Package Author Guide

This guide is the short public authoring contract for beta packages. The full schema is documented in `schemas/FUSE_JSON_SCHEMA.md` and `schemas/fuse-mod.schema.json`.

## File Shape

Use one or more `*.fuse.json` files inside a FUSE package folder. Each file should keep one clear concern where practical: graph, scenery, industries, progressions, audio, map tiles, or asset metadata.

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

## Dependencies

Use package metadata for required package ordering and dependencies:

- `RailLoadPriority`
- `RailLoadAfter`
- `RailLoadBefore`
- normal UMM `Requirements` when the mod itself requires FUSE or another mod

Use `mixinto` when converting legacy conditional mixin files. Missing mixinto requirements should skip only that fragment, not the whole stack.

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
