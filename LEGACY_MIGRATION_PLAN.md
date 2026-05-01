# Legacy Migration Plan

Goal: Convert legacy Railroader mod packages into Rail-native packages without copying legacy code or preserving legacy implementation structure. Legacy formats are input data only. Rail output should use Rail schema, Rail names, and Rail runtime APIs.

## Migration Principles

1. Keep one converted file per source concern when possible.
2. Preserve source object IDs when they are true game graph or scene IDs.
3. Use Rail-native type names in output.
4. Keep legacy aliases only as compatibility input, not as new authoring style.
5. Emit conversion warnings for skipped fields and manual-fix items.
6. Validate converted packages before copying them to the live Mods folder.
7. Test conversion in game with the Rail load report, not only by schema validation.

## Legacy Families

### AMM-Style Route Mods

Typical inputs:

- `game.graph` or game graph JSON files.
- `industry` files.
- `loads` files.
- `scenery` files.
- `splineys`, `roads`, `rivers`, and trestles.
- `mandelas`.
- `texts` / map labels.
- Turntables and roundhouses.
- Passenger stations and passenger stop components.

Rail target:

- `tracks.nodes`, `tracks.segments`, `tracks.spans`, `tracks.areas`, `tracks.removals`.
- `operations.loads`, `operations.industries`, `operations.loaders`, `operations.turntables`, `operations.stations`.
- `world.scenery`, `world.splineys`, `world.sceneClones`, `world.mapLabels`, `world.mapMasks`, `world.mapTiles`.
- `progression.sections`, `progression.progressions`, `progression.mapFeatures`.

High-risk items:

- Span endpoint direction.
- Base-game track removals that use wrong IDs.
- Industry component type aliases.
- Area and industry ordering.
- Turntable/roundhouse generated node and segment names.
- Station map icons and passenger stop binding.
- Scene clone source paths that differ by game version.

### Strange Customs Style Data

Typical inputs:

- Spliney handlers.
- Roads, rivers, waterfalls, trestles.
- Telegraph pole mover handlers.
- Horn, whistle, and bell loose-file packs.
- Railroad crossing helpers.

Rail target:

- `world.splineys` with Rail type `road`, `terrainRoad`, `river`, `waterfall`, or `trestle`.
- `world.telegraphPoleMovements`.
- `audio.whistles`, `audio.horns`, `audio.bells`.
- `world.scenery` with optional `anchorSpanIds`.

High-risk items:

- `FlowyThingBuilder` can mean road or river. Use style/profile to infer type.
- Roads have material families such as dirt/pavement through style/profile. River is its own type.
- Audio source files must be copied into the Rail package.
- Prime mover audio is not covered by horn/whistle/bell conversion.

### RailLoader / Asset Pack Style Mods

Typical inputs:

- Asset pack folders with bundle, catalog, and definitions.
- Data packages that depend on separate shared assets.
- Map tile overlays.

Rail target:

- `Info.json` with `RailAssetPacks`.
- `world.scenery.*.assetIdentifier`.
- `world.mapTiles`.

High-risk items:

- Asset identifier mismatches.
- Asset pack load order.
- Packages that contain rolling stock definitions Rail does not yet model.

### Loose Scenery And Object Packs

Rail target:

- Asset-pack wrappers first.
- Optional world examples that place one object from the pack.

High-risk items:

- Some packs are libraries only and should not create world objects.
- Object names in source packs may not match PrefabStore identifiers.

### Rolling Stock And Prime Mover Audio

Current status: PARTIAL / MISSING

Rail supports horn, whistle, and bell audio packages. Rail does not yet provide a complete car/locomotive definition migration path or prime mover audio replacement schema.

Migration rule for now:

- Convert horn/whistle/bell packs with `tools/convert_rail_audio.py`.
- Leave prime mover and full rolling stock packages in legacy stack until Rail has a dedicated design.

## Standard Conversion Workflow

1. Inventory the source package.
2. Identify required legacy dependencies.
3. Choose converter:
   - Route/world JSON: `tools/rail_convert.py`.
   - Horn/whistle/bell loose audio: `tools/convert_rail_audio.py`.
   - Asset-pack-only package: wrapper package with `RailAssetPacks`.
4. Convert into a new `.RAIL` folder.
5. Preserve one Rail data file per source file unless the source file is empty.
6. Validate JSON against `schemas/rail-mod.schema.json`.
7. Load in game with only Rail and the converted package if possible.
8. Check the Rail toast and `/rail.report`.
9. Test in-game features.
10. Save, exit to menu, reload, and retest.
11. Remove the legacy dependency only after equivalent Rail behavior is verified.

## Package Naming

Recommended converted package folder:

```text
Original.Mod.Id.RAIL/
  Info.json
  track.rail.json
  industry.rail.json
  scenery.rail.json
  splineys.rail.json
```

`Info.json`:

```json
{
  "Id": "Original.Mod.Id.RAIL",
  "DisplayName": "Original Mod Name (RAIL)",
  "Requirements": ["RAIL"],
  "LoadAfter": ["RAIL"],
  "RailLoadPriority": 100,
  "RailDataFiles": [
    "loads.rail.json",
    "game-graph.rail.json",
    "industry.rail.json",
    "scenery.rail.json"
  ]
}
```

## Manual Fix Points

### Spans

Check every converted span:

- `segmentId` must be the real segment ID.
- Use `end: "A"` / `Start` for distance from segment start.
- Use `end: "B"` / `End` for distance from segment end.
- Same-segment spans must use opposite ends and must not cross.

### Removals

Track removals must use real graph IDs. Base-game nodes are often short four-character IDs. Do not use station names, siding names, or user-facing labels unless the graph truly uses that exact ID.

### Industries

Use canonical component types:

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

Set `areaId` and `order` when the legacy source had an area wrapper or display order.

### Splineys

Road and river conversion must inspect style/profile:

- River profile or `style: "River"` -> `type: "river"`.
- Dirt or paved road -> `type: "road"` or `terrainRoad`, with material in `style`/`profile`.
- Auto trestle -> `type: "trestle"`.

### Scene Clones

Legacy `mandelas` become `world.sceneClones`. `source` should use `path://scene/...`. If a source path is wrong, the object will not clone even if the JSON is valid.

### Stations

Passenger stations usually require both:

- A passenger stop industry component.
- A station agent entry bound to that passenger stop.

Rail creates map icons for Rail-created stations. Validate icon location, scale, rotation, and click behavior in map view.

### Turntables

Set `legacyIdentifier` when converted track references legacy turntable helper IDs. Validate:

- Pit node count.
- Roundhouse stall count and angle.
- Bridge texture/rendering.
- Save/load after turning bridge.

## Unsupported Or Not Yet Complete

- Full rolling stock package conversion.
- Prime mover audio replacement.
- Unknown third-party component scripts that execute custom code.
- Full base-game terrain modifier matrix for map masks.
- Scene-path suppression is experimental and disabled by default.

## Acceptance Criteria For A Converted Legacy Route

- Package loads without fatal Rail faults.
- Track graph rebuild succeeds.
- Route opens from a new game and from a saved game.
- Areas and industries appear in correct order.
- Loads and industry components function.
- Stations appear and have map icons.
- Turntables and roundhouses render and function.
- Roads, rivers, trestles, map masks, and scene clones appear.
- Map tiles mount.
- Rail report shows no unknown scenery assets unless intentionally accepted.
- Legacy mod dependency can be removed without missing type errors.
