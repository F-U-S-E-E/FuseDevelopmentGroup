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
- Inferred missing no-type legacy industry component patches from formula input/output terms so load-specific track patches materialize as real loaders or unloaders.
- Added clearly marked temporary legacy support for `container:<id>` mixinto fragments and old `zsc://...` asset-pack references, allowing legacy car/load-model patches to bind to FUSE direct asset stores.
- Applied legacy `$find`, `$replace`, `$add`, and `$remove` directives inside temporary `container:<id>` compatibility patches before passing cloned car definitions to the base deserializer.
- Added a robust aggregate material lookup path so exact installed material definitions such as `aggregateModelLoadId=gondola-woodchips` remain visible when custom asset packs are mounted.

### Converter

- Added drag/drop style folder conversion support through the FUSE converter tools.
- Preserved legacy source-file concerns instead of merging unrelated files into large generated outputs.
- Preserved no-type legacy industry component list patches as partial component patches so existing drop-off spans are not replaced.
- Named materialized legacy interchange aliases from overlapping interchange components so raw sub-ids such as `t1` do not surface as destination names.
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
