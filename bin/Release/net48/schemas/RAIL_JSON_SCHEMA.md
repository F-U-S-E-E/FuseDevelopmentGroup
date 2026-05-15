# RAIL JSON Schema v1

This folder defines the JSON side of the RAIL mod format.

- `rail-mod.schema.json` is the authoritative JSON Schema for hand-authored and editor-exported `.json` files.
- `rail-mod.example.json` is a compact example that exercises track, spans, areas, industry ordering, loaders, turntables, roundhouses, stations, scenery, spawn points, span-anchored scenery, splineys, telegraph poles, labels, speed signs, masks, map tiles, scene clones, world removals, progression data, and editor state.
- `umm-info.schema.json` documents the Unity Mod Manager `Info.json` shape RAIL expects for API mods, data packages, and asset-pack packages.
- `umm-info.example.json` is a data-only map package manifest that depends on `RAIL`.

## Design Choices

The schema uses one document for the whole mod. The shipped binary `.bson` file should use the same logical model, even if the serializer writes a compact representation internally.

Top-level object groups:

- `tracks`: nodes, segments, spans, areas, and optional removals for deleting base-game track objects.
- `operations`: loads, industries, loaders, turntables, and passenger stations.
- `world`: scenery, spawn points, splineys, telegraph poles, map labels, map masks, map tile overlays, scene clones, and optional removals for base scene objects.
- `progression`: progression trees and map features.
- `editor`: optional editor-only state that RAIL can ignore at runtime.
- `extensions`: optional namespaced third-party data.

All editable game objects are stored in dictionaries keyed by object ID. The ID is not repeated inside the object body. This keeps editor updates simple: replacing `tracks.nodes["murphy:n:001"]` updates exactly one object without array searches or ID duplication.

`schemaVersion` is the RAIL data format version and drives migrations. Use the string form, for example `"1.0"`. Integer `1` remains accepted by the runtime for v1.0 packages already in the wild. `modVersion` is the author's release version and should not be used for schema migration decisions.

## Versioning And Deprecation

RAIL migrations run version by version on load. Unknown future schema versions are logged as warnings and loaded on a best-effort basis instead of failing immediately.

When a field is renamed, RAIL keeps the old field readable for one minor version, logs one deprecation warning per package when it migrates that field, and removes the old field in the following minor version. For example, scenery asset keys were renamed from `model` to `assetIdentifier`; v1.0 packages with only `model` still load, and RAIL fills `assetIdentifier` in memory.

Areas are defined under `tracks.areas` and can be used to group industries in the company window. `order` controls the display order of areas and industries:

```json
{
  "tracks": {
    "areas": {
      "murphy:area:town": {
        "name": "Murphy",
        "order": 0,
        "position": { "x": 25, "y": 0, "z": 0 },
        "radius": 350,
        "spanIds": ["murphy:span:depot"]
      }
    }
  },
  "operations": {
    "industries": {
      "murphy:ind:depot": {
        "name": "Murphy Depot",
        "areaId": "murphy:area:town",
        "order": 1,
        "position": { "x": 12, "y": 0, "z": -9 }
      }
    }
  }
}
```

Vector values are always objects:

```json
{ "x": 0, "y": 0, "z": 0 }
```

Track spans use two structured locations rather than legacy custom content style strings. Think of each location as an arrow measured from one segment end into the span. `Start`/`A` points from the segment's start toward its end; `End`/`B` points from the segment's end toward its start. The two endpoint arrows must face each other and the measured distance must stay within that segment.

```json
{
  "upper": { "segmentId": "murphy:s:001", "distance": 10, "end": "Start" },
  "lower": { "segmentId": "murphy:s:001", "distance": 5, "end": "End" }
}
```

`end` is optional and defaults to `A`/`Start`. Set it to `B`/`End` when the distance or normalized value is measured from the segment's far end. On the same segment, one endpoint must use `Start`/`A` and the other must use `End`/`B`; RAIL rejects same-direction endpoints, crossed endpoints, zero-length spans, and distances outside the segment length.

Use `normalized` for editor-authored spans where possible. Use `distance` only when the exact distance along a runtime segment matters.

When translating legacy graph patches that delete existing base-game track objects, use `tracks.removals`. These values must be exact graph object IDs from the source game graph. Base-game track nodes usually look like short four-character alphanumeric IDs; do not use UI names such as siding, spur, or station names unless the source graph actually uses that string as the object ID.

```json
{
  "tracks": {
    "removals": {
      "nodes": ["A1B2"],
      "segments": ["C3D4"],
      "spans": ["E5F6"]
    }
  }
}
```

RAIL-authored nodes, segments, and spans can use descriptive package-owned IDs such as `murphy:n:001`, but base-game removals must use the IDs that already exist in Railroader's live graph. RAIL applies span removals first, then segment removals, then node removals.

Legacy source files can also use `null` entries to delete world objects such as old roads or scenery. RAIL stores those as `world.removals`, using either RAIL IDs or full scene paths:

```json
{
  "world": {
    "removals": {
      "scenery": ["World/Large Scenery/Murphy/Old Depot"],
      "splineys": ["World/Large Scenery/Murphy/Old Road"],
      "mapLabels": [],
      "mapMasks": [],
      "telegraphPoles": [],
      "sceneClones": []
    }
  }
}
```

The converter maps legacy `null` entries from `scenery`, `splineys`, `mandelas`, and `texts` into these arrays.

Progression can be authored either in the older keyed form under `progressions.<id>.sections` or in the flatter root `progression.sections[]` form. Root sections must set `id`; `progression.progressionId` selects the runtime `Progression.identifier`, and defaults to the package id when omitted. RAIL normalizes root sections into the existing Railroader progression system at load time.

Section unlock payloads such as `areasEnableOnUnlock`, `gameObjectsEnableOnUnlock`, `unlockIncludeIndustries`, `unlockExcludeIndustries`, `unlockIncludeIndustryComponents`, `trackGroupsEnableOnUnlock`, and `trackGroupsAvailableOnUnlock` are materialized through a synthetic RAIL `MapFeature` that is enabled when the section unlocks. This keeps narrative packages on the base game's progression path instead of inventing a parallel unlock system.

Progression delivery phases that contain deliveries should set `industryComponentId` when the target progression industry component is known. If omitted, RAIL can infer it when every delivery in the phase points at the same `destinationIndustryId` and that industry has exactly one `ProgressionIndustryComponent`.

Turntables generate deterministic pit node IDs at load time:

- `operations.turntables["bryson"].subdivisions = 16`
- generated pit nodes: `bryson.pit.00` through `bryson.pit.15`

Track segments may reference those generated IDs directly. This keeps turntable-connected track stable across editor saves.

When translating legacy turntables that already have authored helper node IDs, set `legacyIdentifier` so RAIL reproduces the original `NTurntableNode*`, `NRoundhouseNode*`, and `SRoundhouseSegment*` naming pattern. Roundhouses can be generated from vanilla prefab aliases:

```json
{
  "operations": {
    "turntables": {
      "murphy:turntable": {
        "position": { "x": 90, "y": 0, "z": 25 },
        "radius": 15,
        "subdivisions": 16,
        "legacyIdentifier": "MurphyTurntable",
        "roundhouse": {
          "stalls": 3,
          "startAngle": -18,
          "stallAngle": 12,
          "trackLength": 46,
          "startPrefab": "vanilla://roundhouseStart",
          "endPrefab": "vanilla://roundhouseEnd",
          "stallPrefab": "vanilla://roundhouseStall"
        }
      }
    }
  }
}
```

Track segment `speedLimit` may be `0` through `80`. Some legacy graphs use `0` for unrestricted or placeholder track speeds, and RAIL preserves that value during translation.

Asset and prefab references are URI strings:

- `vanilla://waterTower`
- `scenery://aspen-corner-drug`
- `path://scene/World/Large Scenery/Whittier/Coal Conveyor`
- `asset://HunterR.MurphyBranch/depotSmall`
- `rail://some-shared-catalog/object-id`

The current runtime directly understands `vanilla://`, `path://`, `scenery://`, and `empty://`. Other schemes may be reserved by tools or future loaders.

Scenery can optionally set `anchorSpanIds` for track-bound props such as railroad crossings. RAIL averages the referenced span centers, aligns the scenery to the average span tangent, then applies `position` and `rotation` as offsets:

```json
{
  "world": {
    "scenery": {
      "murphy:crossing:depot": {
        "assetIdentifier": "scenery://crossing-board",
        "anchorSpanIds": ["murphy:span:depot"],
        "position": { "x": 0, "y": 0, "z": 0 },
        "rotation": { "x": 0, "y": 90, "z": 0 }
      }
    }
  }
}
```

`world.spawnPoints[]` registers Railroader `Character.SpawnPoint` components. `name` and `position` are required; `rotation`, `radius`, and `priority` mirror the base-game component fields. Higher priority spawn points sort ahead of lower-priority entries in the game's spawn-point list.

Spliney `type` describes the physical spline family, not its material flavor:

- `river`: water spline, with one river behavior family.
- `road`: normal road spline. Use `style`/`profile` for dirt versus pavement.
- `terrainRoad`: terrain-carved road spline. Use `style`/`profile` for dirt versus pavement.
- `trestle`: auto-generated trestle spline.

Converted legacy custom content `FlowyThingBuilder` data must inspect `style` and `profile`; entries with `style: "River"` or river profiles should be emitted as `type: "river"`, not as roads.

Telegraph pole definitions use the existing Railroader telegraph pole and wire prefabs by default. `profile` selects the first vanilla pole prefab whose name contains that profile string. `polePrefab` and `wirePrefab` can override that with explicit prefab URIs.

`world.telegraphPoleMovements[]` adjusts existing base-game telegraph pole graph nodes by pole/node index. This is intentionally separate from `world.telegraphPoles`, which creates new pole sets. Use it for legacy `TelegraphPoleMover` style data:

```json
{
  "world": {
    "telegraphPoleMovements": [
      {
        "poleIndices": [585, 583],
        "offset": { "x": 0, "y": 3, "z": 0 }
      }
    ]
  }
}
```

RAIL applies pole movements idempotently per package, so snapshot reapply does not stack the same offset repeatedly. Unloading the package restores the captured base pole positions, then reapplies any remaining package claims.

Industry components currently supported by the runtime include `loader`, `unloader`, `formulaic`, `repairTrack`, `teamTrack`, `interchange`, `interchangedLoader`, `interchangedUnloader`, `teleportLoading`, `progression`, and `passengerStop`. Formulaic components are attached directly to the industry object to match the base game component layout expectations; other component types get child objects.

The converter emits canonical RAIL component type names. Legacy aliases such as `Model.Ops.IndustryLoader`, `Model.OpsNew.InterchangedIndustryUnloader`, and `AlinasMapMod.PaxStationComponent` are normalized at load time for compatibility, but new JSON should use the RAIL names above so it does not depend on external mod assemblies.

Passenger stop components can carry timetable metadata:

```json
{
  "type": "passengerStop",
  "name": "Murphy Passenger Stop",
  "trackSpanIds": ["murphy:span:depot"],
  "loadId": "passengers",
  "passengerStopId": "murphy:depot",
  "timetableCode": "MUR",
  "basePopulation": 120,
  "branch": "Murphy Branch"
}
```

Map labels support normal text labels and speed-limit badges. Legacy converted labels such as `"15 MPH"` are auto-detected at runtime, but new data should use the explicit style:

```json
{
  "text": "15",
  "style": "speedLimit",
  "speedLimitMph": 15,
  "position": { "x": 44, "y": 0, "z": 2 }
}
```

Map masks default to RAIL's built-in terrain/object behavior:

- `MaskName.Object`
- cut trees enabled
- terrain height blending disabled

That default is intentional because the simplified RAIL schema stores mask shape, not the full base-game modifier matrix.

Map tile overlays mount additional `tile_XXX_YYY.data` files into Railroader's existing `MapStore`:

- `directory` must match `MapManager.directoryName`, for example `BushnellWhittier`
- `sourceFolder` usually points at a package-relative folder such as `Maps/BushnellWhittier`
- `priority` resolves overlaps when multiple packages provide the same tile; higher numbers win

RAIL treats these as overlays on top of the base game's `StreamingAssets/Maps/<directory>` store. That means a converted tile package does not need to replace `Map.json`; it only needs the extra `.data` tiles it wants to contribute.

## Runtime Notes

- `tracks.areas` are applied at runtime and are used as preferred parents for RAIL-created industries. Area and industry `order` values are also used when rebuilding company-window location ordering.
- `operations.loads` are applied at runtime by creating or updating `CarPrototypeLibrary.instance.opsLoads` entries. Use `units`, `density`, `unitWeightInPounds`, `importable`, `payPerQuantity`, and `costPerUnit` when the custom load needs full behavior parity with legacy mod data.
- `world.mapMasks` are applied at runtime using the default behavior above.
- `world.telegraphPoles` are applied at runtime by generating pole instances along the provided point path at the requested spacing.
- `world.telegraphPoleMovements` are applied at runtime by translating existing base-game telegraph pole graph nodes by index. RAIL forces the telegraph manager to refresh when possible.
- `world.spawnPoints` are applied at runtime by creating or updating `Character.SpawnPoint` components under a RAIL-owned world root.
- `world.scenery.*.anchorSpanIds` makes the scenery track-bound at apply time. Missing spans produce warnings and the object falls back to its explicit transform if no anchor resolves.
- `world.mapLabels` are applied at runtime as vanilla `MapLabel` clones. Labels with `style: "speedLimit"` are rendered as circled speed signs.
- `world.mapTiles` are mounted into the live `MapStore` at map-load time and can override individual tile coordinates without copying files into `StreamingAssets`.
- `world.sceneClones` are applied at runtime by cloning an existing scene object path or retargeting an existing object hierarchy. `localPosition`, `localRotation`, and `localScale` are optional; when omitted or `null`, RAIL now preserves the existing transform value instead of forcing zero or one. RAIL also strips unsupported mover components such as `PhysicsMover` from these clones so static visual copies do not register live physics controllers by accident.
- `world.removals` are applied before new world objects are created. Removal IDs may be RAIL-created object IDs or full scene paths such as `World/Large Scenery/Sylva/Road (1)`.

Validation that depends on Railroader runtime state, such as whether a prefab exists or whether a base-game passenger stop ID is valid, belongs in RAIL's validation layer rather than this JSON Schema.

## UMM Package Layout

Recommended data package layout:

```text
MurphyBranch/
  Info.json
  track.rail.json
  operations.rail.json
  world.rail.json
  progression.rail.json
  Maps/
    BushnellWhittier/
      tile_003_015.data
      tile_003_016.data
  Assets/
    Bundle
    Catalog.json
```

`Info.json` stays in Unity Mod Manager's normal PascalCase style. RAIL data files stay in lower camelCase JSON style. RAIL discovers packages by reading `Info.json`, checking `Requirements` or `LoadAfter` for `RAIL`, then loading `RailDataFile` or each entry in `RailDataFiles`. `RailDataFiles` is preferred for converted packages that keep one file per source concern:

```json
{
  "Requirements": [{ "Id": "RAIL", "NotBefore": "1.0.0" }],
  "LoadAfter": ["RAIL"],
  "RailLoadPriority": 100,
  "RailLoadAfter": ["Shared.Track.Package"],
  "RailLoadBefore": [],
  "RailDataFiles": [
    "track.rail.json",
    "operations.rail.json",
    "world.rail.json"
  ]
}
```

RAIL-specific package ordering uses `RailLoadPriority`, `RailLoadAfter`, and `RailLoadBefore`. Lower priority values load earlier; packages with the same priority fall back to dependency order and then package id. Dependency cycles are reported in the log and the involved packages fall back to priority/name order.

If explicit data-file entries are missing, RAIL falls back to the first `.bson` file in the package root, then the first `.json` file other than `Info.json`.

Asset-pack-only packages can expose existing Railroader `AssetPack` runtime stores with `RailAssetPacks`. RAIL mirrors those folders into Railroader's external `AssetPacks` directory before the game builds its prefab catalog:

```json
{
  "Requirements": ["RAIL"],
  "LoadAfter": ["RAIL"],
  "RailAssetPacks": ["SCAssetPacks"]
}
```
