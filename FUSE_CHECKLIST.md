# FUSE API Progress Checklist

Use this as the living status tracker. Each unchecked item should keep one of these labels: PARTIAL, IN PROGRESS, BROKEN, MISSING, NEEDS TESTING, NEEDS DOCUMENTATION, UNKNOWN.

## Core API

- [x] DONE - Unity Mod Manager plugin initializes FUSE.
- [x] DONE - Top-level FUSE schema exists.
- [x] DONE - JSON and BSON serializer paths exist.
- [x] DONE - Schema migration and normalization path exists.
- [ ] PARTIAL - Public API surface is stable enough for alpha.
- [ ] NEEDS DOCUMENTATION - Public lifecycle hooks documented.
- [ ] NEEDS DOCUMENTATION - API stability policy written.
- [ ] MISSING - Minimal checked-in sample packages.

## Loader And Runtime Apply

- [x] DONE - Package discovery is separated from disk load and runtime apply.
- [x] DONE - Runtime reapply uses resident definitions.
- [x] DONE - Package fault registry exists.
- [x] DONE - Apply transaction phases exist.
- [x] DONE - Aggregate apply report logs per package.
- [x] DONE - Load report is visible in game.
- [ ] NEEDS TESTING - Dirty-load test with one faulted package.
- [ ] NEEDS TESTING - Map reload and save reload package reapply.
- [ ] NEEDS TESTING - Package unload and replace cycle.

## Dependencies

- [x] DONE - `FuseLoadPriority` supported.
- [x] DONE - `FuseLoadAfter` supported.
- [x] DONE - `FuseLoadBefore` supported.
- [x] DONE - Dependency cycles log warnings.
- [x] DONE - Disabled packages are skipped and reported.
- [ ] NEEDS DOCUMENTATION - Dependency examples for `Info.json`.

## Track And Graph

- [x] DONE - Track nodes can be created and updated.
- [x] DONE - Track segments can be created and updated.
- [x] DONE - Track spans can be created and updated.
- [x] DONE - Track areas can be created and ordered.
- [x] DONE - Track removals exist.
- [x] DONE - Reversible removal snapshots exist.
- [x] DONE - Batched graph rebuild exists.
- [ ] NEEDS TESTING - Span direction rules across real converted routes.
- [ ] NEEDS TESTING - Turnout-heavy areas after conversion.
- [ ] NEEDS DOCUMENTATION - Base-game graph IDs versus UI names.
- [ ] NEEDS DOCUMENTATION - Span authoring diagrams.

## Operations

- [x] DONE - Loads can be added.
- [x] DONE - Industries can be created under areas.
- [x] DONE - Industry order values affect company-window sorting.
- [x] DONE - Loader, unloader, formulaic, team track, repair, interchange, progression, teleport loading, and passenger stop component types are represented.
- [x] DONE - Legacy industry component aliases normalize to FUSE names.
- [ ] NEEDS TESTING - Every industry component type in game.
- [ ] NEEDS TESTING - Bad component cannot abort whole package.
- [ ] NEEDS DOCUMENTATION - Industry component field reference.

## Turntables And Roundhouses

- [x] DONE - Turntable API exists.
- [x] DONE - Deterministic pit node IDs exist.
- [x] DONE - Legacy identifier path exists.
- [x] DONE - Roundhouse prefab support exists.
- [ ] NEEDS TESTING - Multiple turntables in one package.
- [ ] NEEDS TESTING - Save/load with turntable bridge and roundhouse.
- [ ] NEEDS DOCUMENTATION - Turntable naming and generated node IDs.

## World Objects

- [x] DONE - Scenery runtime API exists.
- [x] DONE - Scene clone runtime API exists.
- [x] DONE - Spliney runtime API exists.
- [x] DONE - Telegraph pole sets exist.
- [x] DONE - Telegraph pole movements exist.
- [x] DONE - Map labels exist.
- [x] DONE - Speed-limit map signs exist.
- [x] DONE - Map masks exist.
- [x] DONE - Map tile overlays exist.
- [x] DONE - Spawn points exist.
- [x] DONE - Span-anchored scenery exists.
- [ ] NEEDS TESTING - Scene clone paths across maps.
- [ ] NEEDS TESTING - Anchored scenery on curved spans.
- [ ] NEEDS DOCUMENTATION - Spliney type/style/profile rules.
- [ ] NEEDS DOCUMENTATION - Map tile package layout.

## Stations And Map Icons

- [x] DONE - Passenger station API exists.
- [x] DONE - Passenger stop industry component exists.
- [x] DONE - FUSE-created station map icons are generated.
- [ ] NEEDS TESTING - Station icon size and rotation across all converted stops.
- [ ] NEEDS TESTING - Station icon click behavior.
- [ ] NEEDS DOCUMENTATION - Station and passenger stop conversion guide.

## Audio And Rolling Stock

- [x] DONE - Whistle definitions exist.
- [x] DONE - Horn definitions exist.
- [x] DONE - Bell definitions exist.
- [x] DONE - Loose horn/whistle/bell converter exists.
- [x] DONE - Audio UI patches exist.
- [ ] NEEDS TESTING - Converted whistle packs in customization UI.
- [ ] NEEDS TESTING - Converted horn packs in customization UI.
- [ ] NEEDS TESTING - Converted bell packs if source packages are found.
- [ ] MISSING - Prime mover audio replacement support.
- [ ] MISSING - Full rolling stock definition schema.
- [ ] NEEDS DOCUMENTATION - Audio package guide.

## Progression And Suppression

- [x] DONE - Progression map features exist.
- [x] DONE - Progression root sections exist.
- [x] DONE - Delivery phases exist in schema.
- [x] DONE - Group and area suppression runtime exists.
- [x] DONE - Scene-path suppression exists behind experimental setting.
- [ ] NEEDS TESTING - Narrative unlock chains from converted mods.
- [ ] NEEDS TESTING - Shared suppression ownership and release.
- [ ] NEEDS DOCUMENTATION - Suppression tradeoffs and synthetic MapFeature prefix.

## Save / Load / Editor

- [x] DONE - Snapshot reapply patches exist.
- [x] DONE - Turntable snapshot timing patch exists.
- [x] DONE - Authoring entity base exists.
- [x] DONE - Reflection edit attributes exist.
- [x] DONE - Authoring registry exists.
- [x] DONE - Authoring persistence service exists.
- [ ] IN PROGRESS - In-game editor integration.
- [ ] NEEDS TESTING - Manual save for edited objects.
- [ ] NEEDS TESTING - Autosave for edited objects.
- [ ] NEEDS DOCUMENTATION - Authoring/editor API.

## Docs And Migration

- [x] DONE - Schema reference exists.
- [x] DONE - Large schema example exists.
- [x] DONE - Legacy route converter exists.
- [x] DONE - Audio converter exists.
- [ ] NEEDS DOCUMENTATION - Quickstart guide.
- [ ] NEEDS DOCUMENTATION - Legacy migration guide.
- [ ] NEEDS DOCUMENTATION - Troubleshooting guide.
- [ ] NEEDS DOCUMENTATION - Console command reference.
- [ ] NEEDS TESTING - Conversion test matrix executed.

