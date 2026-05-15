# Changelog

## Unreleased Beta

### Runtime

- Added FUSE-specific logging in `FUSE.log` beside `Player.log`.
- Added a compact public load report toast with full-word counters for faults, conflicts, assets, graph issues, transfers, and suppressions.
- Added startup version reporting for FUSE, schema, converter, Railroader, Unity, and build configuration.
- Split discovery, disk loading, and runtime apply so map reload and reapply can use resident definitions without needless disk reloads.
- Added package fault isolation and final package summaries.
- Added runtime graph and original graph dump commands.
- Added scene clone sanitizer behavior so collider-only meshes are not rendered over cloned buildings.
- Added direct asset pack discovery without mirroring asset packs into LocalLow by default.

### Converter

- Added drag/drop style folder conversion support through the FUSE converter tools.
- Preserved legacy source-file concerns instead of merging unrelated files into large generated outputs.
- Added structured conversion reports with repaired, preserved, unresolved, unsupported, and dependency-required entries.
- Added source-file reporting for passenger stop and span warnings.
- Added route, map tile, asset pack, and audio pack conversion coverage for the current beta corpus.

### Schema

- Added `schemaVersion` handling and migration notes.
- Added mixinto metadata support.
- Added audio definitions for whistles, horns, and bells.
- Added telegraph pole movements.
- Added spawn points.
- Added span-anchored scenery.
- Added progression sections, delivery phases, unlock feature lists, area unlocks, game object unlocks, and track group unlocks.
- Added custom industry component support through fully-qualified component types and reflection-bound `fields`.

### Breaking / Compatibility Notes

- `RAIL` naming has been superseded by `FUSE`. Reconvert packages for clean public beta testing.
- Signals are not part of the beta support promise yet.
- Converter output should be regenerated with the matching converter version when schema/runtime behavior changes.
