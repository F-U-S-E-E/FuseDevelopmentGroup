# RAIL Audit

Date: 2026-05-01

Scope: Rail runtime API, package schema, loader lifecycle, converter tooling, and known legacy mod conversion needs for the Railroader Unity Mod Manager ecosystem.

Status legend: DONE, PARTIAL, IN PROGRESS, BROKEN, MISSING, NEEDS TESTING, NEEDS DOCUMENTATION, UNKNOWN.

## Executive Summary

Rail is no longer just a schema experiment. The current codebase has a working Unity Mod Manager plugin, separated package discovery/load/apply stages, dependency ordering, runtime apply transactions, package fault reporting, map-load user reports, route/world conversion tooling, map tiles, asset-pack mounting, operations support, progression extensions, world suppression, and loose horn/whistle/bell audio support.

The main release risk is confidence, not breadth. Many systems exist but need repeatable in-game test coverage, conversion checklists, and modder-facing docs. Legacy route conversion is far along. Full rolling stock support, prime mover audio replacement, complete public authoring/editor documentation, and automated validation tooling are still the largest gaps.

## Status Overview

| Area | Status | Priority | Evidence | Next action |
| --- | --- | --- | --- | --- |
| Core Rail API architecture | PARTIAL | High | `API/*.cs`, `Data/RailModDefinition.cs`, `Serialization/RailSerializer.cs`, `Migrations/RailMigration.cs` | Freeze public naming, document supported APIs, add example packages. |
| Mod loading and initialization | DONE | High | `RailPlugin.cs`, `Lifecycle/RailLifecycle.cs`, `Loading/RailDataPackageDiscovery.cs` | Keep testing duplicate loads, unloads, and map reloads. |
| Unity Mod Manager integration | DONE | High | `RailPlugin.cs`, `RAIL.csproj`, `Info.json` | Document build and deploy setup for contributors. |
| Route mod loading | PARTIAL | Critical | `tools/rail_convert.py`, `Loading/RailModLoader.cs`, converted Appalachian packages | Build a route regression checklist using Appalachian, Oconaluftee, and East Whittier. |
| Rail asset loading | PARTIAL | High | `Loading/RailAssetPackRegistry.cs`, `API/RailPrefabResolver.cs`, `tools/convert_rail_audio.py` | Document asset-pack layouts and unknown asset diagnostics. |
| Track / rail systems | PARTIAL | Critical | `API/TrackAPI.cs`, `Loading/RailTrackRemovalSnapshotStore.cs`, `Validation/RailDefinitionValidator.cs` | Add span and removal test cases for exact base-game graph IDs. |
| Rolling stock support | PARTIAL | High | `Patches/RailAudioPatches.cs`, `API/RailAudioAPI.cs`; no car definition API found | Define whether Rail will support cars/locos directly or only audio/customization hooks for alpha. |
| Scenery / placeable objects | PARTIAL | High | `API/SceneryAPI.cs`, `API/SceneCloneAPI.cs`, authoring entities | Test scene clones, anchored scenery, and asset packs across multiple maps. |
| Map / terrain support | PARTIAL | High | `API/MapAPI.cs`, `Loading/RailMapTileRegistry.cs`, `Patches/MapStorePatches.cs` | Document map tiles, map labels, speed signs, station icons, and map masks. |
| Save/load behavior | PARTIAL | Critical | `Patches/StateManagerPatches.cs`, `Patches/TrainControllerPatches.cs`, `RailApiPersistence.cs` | Run save/load/reload tests for route packages and runtime edits. |
| Harmony patching | DONE | High | `Patches/RailPatchResilience.cs`, patch classes | Add a patch report section to troubleshooting docs. |
| Event hooks / lifecycle hooks | PARTIAL | Medium | `Events/RailEvents.cs`, `Lifecycle/RailLifecycle.cs` | Document which events are stable and when they fire. |
| Public API surface for modders | NEEDS DOCUMENTATION | High | Public methods across `API/*.cs` | Create API reference pages with examples and safe call timing. |
| Compatibility with existing mods | PARTIAL | Critical | `tools/rail_convert.py`, audio converter, legacy alias normalization | Maintain a conversion test matrix and package-by-package acceptance notes. |
| Error handling and logging | PARTIAL | High | `Infrastructure/RailLog.cs`, `RailPackageFaultRegistry.cs`, `RailLoadReport.cs`, `RailApplyTransaction.cs` | Standardize remaining warnings with package, operation, and object id. |
| Config/settings support | PARTIAL | Medium | `RailSettings.cs` | Add documented settings model before exposing more feature flags. |
| Dependency handling between mods | DONE | High | `RailDataPackageDiscovery.cs` supports `RailLoadPriority`, `RailLoadAfter`, `RailLoadBefore` | Document dependency cycles and missing dependency behavior. |
| Documentation quality | PARTIAL | High | `README.md`, `schemas/RAIL_JSON_SCHEMA.md`, generated docs in this audit | Add task-based guides and troubleshooting pages. |
| Examples/sample mods | PARTIAL | Medium | `schemas/rail-mod.example.json` | Add runnable sample packages under an examples folder. |
| Migration path from Legacy mods to Rail | PARTIAL | Critical | `tools/rail_convert.py`, `tools/convert_rail_audio.py`, converted packages in Mods | Document conversion workflow and known manual-fix items. |

## Detailed Findings

### Core Rail API Architecture

Current status: PARTIAL

Evidence:
- `Data/RailModDefinition.cs` defines the top-level package model: tracks, operations, world, audio, progression, editor, and extensions.
- `Serialization/RailSerializer.cs` supports JSON and BSON with migration on load.
- `Migrations/RailMigration.cs` normalizes null collections, aliases, component types, progression sections, and schema version.
- `API/*.cs` exposes runtime APIs for track, operations, world, map, audio, spawn points, and definitions.

Already working:
- Clean grouped schema rather than direct legacy serialized object clones.
- Runtime definition cache and `GetDefinition`-style APIs exist for many runtime objects.
- JSON/BSON are handled through one serializer path.
- Schema version defaults to `1.0` and future versions warn instead of hard failing.

Incomplete or risky:
- API stability is not documented.
- Some public APIs are proven only by converted packages, not standalone sample mods.
- Authoring/editor layer exists but is early and not yet documented as stable.

Needs to be added:
- Public API reference.
- Compatibility/stability policy.
- Minimal sample package for each API family.

Priority: High

Suggested next action: Publish a "Rail API alpha contract" doc that names supported public APIs, unstable APIs, and expected call timing.

### Mod Loading and Initialization

Current status: DONE

Evidence:
- `RailPlugin.cs` initializes settings, asset packs, Harmony patches, lifecycle, and console commands.
- `Lifecycle/RailLifecycle.cs` handles map load, graph rebuild, and map unload.
- `Loading/RailDataPackageDiscovery.cs` separates discovery, disk load, and runtime apply.

Already working:
- Discovery only scans once unless refreshed.
- Runtime apply can run from resident definitions without disk reload.
- Package failures are recorded instead of stopping unrelated packages.
- Map-load report is surfaced through `RailLoadReport`.

Incomplete or risky:
- Needs repeated in-game testing with dirty package sets.
- Map-load sequencing still depends on Railroader lifecycle events and Harmony patches.

Needs to be added:
- Regression test checklist for fresh launch, map reload, save reload, package unload, and reapply.

Priority: High

Suggested next action: Build one deliberately dirty load test with a faulted package, unknown scenery asset, disabled package, and dependency cycle.

### Unity Mod Manager Integration

Current status: DONE

Evidence:
- `RailPlugin.Load` is the Unity Mod Manager entry point.
- `RAIL.csproj` references Unity/Railroader/UMM assemblies and has deploy support with `EnableModDeploy=true`.
- `README.md` documents the build command.

Already working:
- Load/unload hooks are present.
- Harmony is unpatched during shutdown.
- Build deploy target copies DLL, PDB, and Info/schema files.

Incomplete or risky:
- Contributor setup depends on local Railroader install paths.

Needs to be added:
- Contributor setup doc for `Local.Build.props`.

Priority: High

Suggested next action: Add `docs/building.md` with expected paths, common build failures, and deploy command.

### Route Mod Loading

Current status: PARTIAL

Evidence:
- `tools/rail_convert.py` converts legacy JSON files one file per source concern.
- `RailModLoader` applies tracks, areas, operations, world objects, progression, audio, and suppressions in phased transactions.
- Converted Appalachian route packages are present in the live Mods folder.

Already working:
- Multiple `.rail.json` fragments per package are supported through `RailDataFiles`.
- Converted map tiles mount and display.
- Converted tracks, roads, rivers, trestles, turntables, roundhouses, industries, passenger stops, and station icons have been exercised in game during development.

Incomplete or risky:
- Some fixes are based on one route and need broad route validation.
- Legacy span direction and distance rules need careful conversion.
- Full route packages can have many object families, so one missing asset or bad span can hide downstream issues.

Needs to be added:
- Route acceptance test script/checklist.
- Converter report output with counts, warnings, and manual-fix items.

Priority: Critical

Suggested next action: Use Appalachian as the golden route and run the conversion matrix after every loader/schema change.

### Rail Asset Loading

Current status: PARTIAL

Evidence:
- `Loading/RailAssetPackRegistry.cs` mounts asset packs.
- `API/RailPrefabResolver.cs` resolves `vanilla://`, `path://scene`, `scenery://`, and empty prefab references.
- `tools/convert_rail_audio.py` wraps audio asset-pack-only packages.

Already working:
- Asset-pack folders can be exposed through `RailAssetPacks`.
- Unknown scenery assets are recorded in `RailLoadReport`.
- Scene path clones support base-game object reuse.

Incomplete or risky:
- Asset identifiers are still easy for modders to get wrong.
- Asset-pack conventions need better examples.
- Rolling stock asset packs are not yet a first-class Rail schema target.

Needs to be added:
- Asset URI reference.
- Asset discovery/debug console commands.

Priority: High

Suggested next action: Write a guide that maps common legacy asset layouts to Rail `Info.json`, `RailAssetPacks`, and `assetIdentifier` usage.

### Track / Rail Systems

Current status: PARTIAL

Evidence:
- `API/TrackAPI.cs` adds/updates/removes nodes, segments, spans, areas, and graph rebuilds.
- `Validation/RailDefinitionValidator.cs` validates spans, speed limits, area order, and removal conflicts.
- `Loading/RailTrackRemovalSnapshotStore.cs` supports reversible track removals.

Already working:
- Nodes, segments, spans, areas, turntable-generated pit nodes, group IDs, speed limits, and removals exist in schema and runtime.
- Track batching avoids repeated graph rebuilds inside a transaction.
- Same-segment span validation rejects same-direction, crossed, zero-length, and out-of-length spans.
- Base-game removals are restored on package unload on a best-effort basis.

Incomplete or risky:
- Track graph edge cases need in-game testing around turnouts, turntables, and routes with deleted base segments.
- Reversible removals are lossy by design and need a user-facing explanation.
- Base-game removals must use exact graph IDs, not UI siding/spur names.

Needs to be added:
- Track authoring guide.
- Span diagram guide using the user's "what to do / what not to do" examples.
- Removal restore limitations doc.

Priority: Critical

Suggested next action: Add a span-focused validation sample package with one valid same-segment span, one cross-segment span, and several expected failures.

### Rolling Stock Support

Current status: PARTIAL

Evidence:
- `API/RailAudioAPI.cs` registers whistles, horns, and bells.
- `Patches/RailAudioPatches.cs` patches sound customization and component builders.
- No Rail data model/API for full car or locomotive definitions was found.

Already working:
- Loose horn, whistle, and bell packs can be converted to Rail audio packages.
- Custom sound choices can be exposed in the UI through Rail patches.
- Audio files are copied into Rail packages by `tools/convert_rail_audio.py`.

Incomplete or risky:
- Prime mover audio replacers are not yet covered.
- Full rolling stock/car packs still depend on the legacy ecosystem.
- Sound conversion has had only initial build/deploy validation.

Needs to be added:
- Decision: Rail alpha supports audio customization only, or starts full rolling stock schema.
- Prime mover audio support plan.
- Rolling stock compatibility doc.

Priority: High

Suggested next action: Treat horns/whistles/bells as Tier 1 audio support and write a separate design for prime movers and car/locomotive definitions.

### Scenery / Placeable Objects

Current status: PARTIAL

Evidence:
- `API/SceneryAPI.cs` supports scenery assets and span anchors.
- `API/SceneCloneAPI.cs` supports scene clones.
- `Authoring/RailSceneryEntity.cs` and `RailConfigurableStructureEntity` route runtime apply through authoring entities.
- `API/RailPrefabSanitizer.cs` sanitizes loaders, stations, turntables, and scene clones.

Already working:
- Scenery can be created by asset identifier.
- Scene clones can clone or retarget existing scene paths.
- Span-anchored scenery exists for crossings and similar objects.
- Unsupported runtime components can be stripped from clones.

Incomplete or risky:
- Unknown assets are common during conversion.
- Scene clone source paths are brittle across game updates.
- Span anchoring needs more tests with curved and multi-span track.

Needs to be added:
- Scenery asset catalog guide.
- Scene clone troubleshooting guide.
- Anchored scenery examples.

Priority: High

Suggested next action: Convert a small scenery pack and one crossing pack as dedicated tests, separate from full-route conversion.

### Map / Terrain Support

Current status: PARTIAL

Evidence:
- `Loading/RailMapTileRegistry.cs` and `Patches/MapStorePatches.cs` mount map tile overlays.
- `API/MapAPI.cs` handles map labels, speed-limit signs, map masks, telegraph poles, and telegraph pole movements.
- `API/StationAPI.cs` generates station map icons.
- `Loading/RailWorldSuppressor.cs` handles group, area, and experimental scene-path suppression.

Already working:
- Map tiles mount into `MapStore`.
- Map labels and speed-limit badges exist.
- Passenger station icons are generated rather than relying on legacy prefab-authored icons.
- Telegraph pole movement support exists for legacy mover data.
- Group and area suppression use shared claims.

Incomplete or risky:
- Scene-path suppression is experimental and can wedge scene loading if used carelessly.
- Station icon sizing/rotation/location has been tuned manually and needs route-wide validation.
- Map masks use simplified defaults, not every base-game terrain modifier field.

Needs to be added:
- Map authoring reference.
- Station icon acceptance screenshots.
- Suppression risk doc.

Priority: High

Suggested next action: Add a map visual QA checklist covering labels, station icons, speed signs, masks, tile overlays, and suppressed areas.

### Save/load Behavior

Current status: PARTIAL

Evidence:
- `Patches/StateManagerPatches.cs` reapplies packages around snapshot restore.
- `Patches/TrainControllerPatches.cs` handles turntable snapshot timing.
- `API/RailApiPersistence.cs` supports manual and autosave recording scopes.
- `Authoring/RailAuthoringPersistenceService.cs` can save/capture/apply authoring entities.

Already working:
- Packages are reapplied from resident definitions instead of disk reload during snapshot handling.
- Authoring persistence can update owning definitions and write JSON/BSON.
- Runtime edit persistence scaffolding exists.

Incomplete or risky:
- Save/load behavior needs repeated in-game testing after every major API family.
- Autosave/editor integration is not yet proven as a complete user workflow.
- Runtime objects created by different packages can interact in ways that snapshots expose.

Needs to be added:
- Save/load acceptance test for each object family.
- Editor persistence guide.

Priority: Critical

Suggested next action: Build a save/load test map that includes tracks, spans, a turntable, a station, industries, scene clones, map labels, audio, and suppression claims.

### Harmony Patching

Current status: DONE

Evidence:
- `Patches/RailPatchResilience.cs` applies patch classes individually and records failures.
- Patch classes cover map store, state manager, train controller, locations panel, audio, and experimental early loader.

Already working:
- One failed patch should not prevent Rail from loading.
- Patch reports are available through console commands.
- Experimental patches are marked and guarded.

Incomplete or risky:
- Patch coverage depends on Railroader method signatures and can break on game updates.

Needs to be added:
- Patch failure troubleshooting doc.
- Compatibility notes per Railroader version.

Priority: High

Suggested next action: Add patch report examples to documentation and include "bad patch" in the dirty-load test.

### Event Hooks / Lifecycle Hooks

Current status: PARTIAL

Evidence:
- `Events/RailEvents.cs` exposes Rail load/unload, graph rebuild, validation, mod loaded/unloaded, and object mutation events.
- `Lifecycle/RailLifecycle.cs` wires game lifecycle to Rail package operations.

Already working:
- Internal lifecycle hooks are centralized.
- Public static events exist.

Incomplete or risky:
- Not every API family may raise complete add/update/remove events.
- Event ordering is not documented.

Needs to be added:
- Event timing reference.
- Stable/unstable event list.

Priority: Medium

Suggested next action: Audit event raises per API and document the guaranteed lifecycle order.

### Public API Surface for Modders

Current status: NEEDS DOCUMENTATION

Evidence:
- Public API methods exist across `API/*.cs`.
- `README.md` focuses on runtime coverage and build basics, not modder usage.
- No `docs` folder was found.

Already working:
- APIs are callable for core object families.
- `GetDefinition` accessors exist for many runtime objects.
- Manual/autosave persistence scopes exist.

Incomplete or risky:
- Modders do not yet have a stable reference.
- It is unclear which APIs are safe before map load, after graph rebuild, or during editor mutation.

Needs to be added:
- API reference by object family.
- Examples for create, update, save, rebuild, and remove.

Priority: High

Suggested next action: Create docs from the public API index in this audit, starting with track, scenery, operations, and audio.

### Compatibility With Existing Mods

Current status: PARTIAL

Evidence:
- `tools/rail_convert.py` converts AMM/Strange Customs style route JSON.
- `tools/convert_rail_audio.py` converts loose horn/whistle/bell packs.
- `Data/Operations/RailIndustryComponentTypes.cs` normalizes legacy component aliases.

Already working:
- Appalachian route data has been converted and iteratively debugged.
- Map tile packages load.
- Several horn/whistle packages convert to Rail audio.
- Legacy `AlinasMapMod.PaxStationComponent` maps to Rail `passengerStop`.

Incomplete or risky:
- Legacy route mods can contain object families Rail only partially supports.
- Prime mover audio and full rolling stock packs are not migrated yet.
- Some compatibility relies on in-memory aliases and should not become new authoring style.

Needs to be added:
- Migration guide.
- Per-package conversion matrix.
- Converter report with "manual fix needed" output.

Priority: Critical

Suggested next action: Use `CONVERSION_TEST_MATRIX.md` as the master tracker and update status after each in-game pass.

### Error Handling and Logging

Current status: PARTIAL

Evidence:
- `Infrastructure/RailLog.cs`, `Loading/RailPackageFaultRegistry.cs`, `Loading/RailApplyTransaction.cs`, `Loading/RailLoadReport.cs`.
- Many APIs throw at boundaries and transactions catch per object.

Already working:
- Package faults are aggregated.
- Dirty load reports are visible in game.
- Unknown scenery assets are reported without opening `Player.log`.
- Apply reports include created, updated, removed, skipped, warning, error, fatal, and post-bind counts.

Incomplete or risky:
- Not every warning uses the same package/operation/object format.
- Some conversion failures still require reading logs.

Needs to be added:
- Log format policy.
- Console command docs for `/rail.report`, `/rail.loaded`, `/rail.conflicts`, `/rail.patches`.

Priority: High

Suggested next action: Add a logging checklist to pull requests and keep warning text package-first.

### Config / Settings Support

Current status: PARTIAL

Evidence:
- `RailSettings.cs` currently exposes experimental early scene-path suppression and timeout.
- `README.md` documents the experimental setting.

Already working:
- Settings can be loaded from UMM `Info.json`.
- Dangerous early-loader suppression is off by default.

Incomplete or risky:
- No general settings schema for Rail or data packages.
- No user-facing config UI.

Needs to be added:
- Settings schema and policy.
- Clear separation between Rail core settings and data package metadata.

Priority: Medium

Suggested next action: Keep settings minimal for alpha; document the one existing experimental setting.

### Dependency Handling Between Mods

Current status: DONE

Evidence:
- `RailDataPackageDiscovery.cs` reads `RailLoadPriority`, `RailLoadAfter`, and `RailLoadBefore`.
- It implements a topological sort, missing dependency warnings, duplicate id warnings, cycle detection, and priority/name fallback.

Already working:
- Data packages can express Rail-native ordering separate from UMM `LoadAfter`.
- Missing dependencies and cycles are logged.
- Disabled packages are skipped and reported.

Incomplete or risky:
- Duplicate package IDs are warned during ordering; duplicate definition IDs within a package are hard validation errors in `RailModLoader`.

Needs to be added:
- Dependency examples in `Info.json` docs.

Priority: High

Suggested next action: Add a dependency example package and a cycle example to troubleshooting docs.

### Documentation Quality

Current status: PARTIAL

Evidence:
- `README.md` gives a runtime coverage summary and build command.
- `schemas/RAIL_JSON_SCHEMA.md` is a useful schema reference.
- `schemas/rail-mod.example.json` exercises many fields.
- No dedicated docs folder was found.

Already working:
- Schema reference is stronger than most early mod APIs.
- Example JSON covers many current features.

Incomplete or risky:
- Modders still need task-based docs.
- Conversion docs are not yet organized as a workflow.
- The editor/authoring layer is effectively undocumented.

Needs to be added:
- See `MODDER_DOCUMENTATION_PLAN.md`.

Priority: High

Suggested next action: Create a `docs/` folder and move task-based pages there while keeping schema docs in `schemas/`.

### Examples / Sample Mods

Current status: PARTIAL

Evidence:
- `schemas/rail-mod.example.json` provides a comprehensive document.
- Live converted packages exist in the Railroader Mods folder.

Already working:
- A single schema example exists.

Incomplete or risky:
- No minimal runnable sample packages are checked into the repo.
- Converted full-route packages are too large for beginners.

Needs to be added:
- Minimal "Hello Rail" data package.
- One sample per feature family.

Priority: Medium

Suggested next action: Add `examples/` packages that can be copied directly into `Mods`.

### Migration Path From Legacy Mods To Rail

Current status: PARTIAL

Evidence:
- `tools/rail_convert.py` handles route-style legacy JSON.
- `tools/convert_rail_audio.py` handles horn/whistle/bell packs.
- Legacy component names are normalized in `RailIndustryComponentTypes`.

Already working:
- Converted route packages can keep one output file per source file.
- Null legacy entries map to Rail removals.
- SC/AMM handler families are mapped to Rail types.

Incomplete or risky:
- Converter reports are basic.
- Prime mover audio and rolling stock packs are not covered.
- Complex legacy progression and unlock behavior needs more validation.

Needs to be added:
- Full migration guide.
- Batch converter reports.
- Known unsupported feature list.

Priority: Critical

Suggested next action: Use `LEGACY_MIGRATION_PLAN.md` and `CONVERSION_TEST_MATRIX.md` as the alpha migration tracker.

## Highest Priority Next Actions

1. Create in-game regression passes for Appalachian route, map tiles, one scenery/asset pack, one spliney pack, and one audio pack.
2. Write task-based modder docs before inviting more public alpha users.
3. Decide alpha scope for rolling stock: audio-only now, full car/locomotive schema later.
4. Add converter reports that identify skipped fields and manual fix items.
5. Build a dirty-load test package set to prove fault handling without reading `Player.log`.
