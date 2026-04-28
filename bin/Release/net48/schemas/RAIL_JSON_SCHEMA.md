# RAIL JSON Schema v1

This folder defines the JSON side of the RAIL mod format.

- `rail-mod.schema.json` is the authoritative JSON Schema for hand-authored and editor-exported `.json` files.
- `rail-mod.example.json` is a compact example that exercises track, spans, areas, industry ordering, loaders, turntables, roundhouses, stations, scenery, splineys, telegraph poles, labels, speed signs, masks, map tiles, scene clones, progression data, and editor state.
- `umm-info.schema.json` documents the Unity Mod Manager `Info.json` shape RAIL expects for API mods and data packages.
- `umm-info.example.json` is a data-only map package manifest that depends on `RAIL`.

## Design Choices

The schema uses one document for the whole mod. The shipped binary `.bson` file should use the same logical model, even if the serializer writes a compact representation internally.

Top-level object groups:

- `tracks`: nodes, segments, spans, areas, and optional removals for deleting base-game track objects.
- `operations`: loads, industries, loaders, turntables, and passenger stations.
- `world`: scenery, splineys, telegraph poles, map labels, map masks, map tile overlays, and scene clones.
- `progression`: progression trees and map features.
- `editor`: optional editor-only state that RAIL can ignore at runtime.
- `extensions`: optional namespaced third-party data.

All editable game objects are stored in dictionaries keyed by object ID. The ID is not repeated inside the object body. This keeps editor updates simple: replacing `tracks.nodes["murphy:n:001"]` updates exactly one object without array searches or ID duplication.

`schemaVersion` is the RAIL data format version and drives migrations. `modVersion` is the author's release version and should not be used for schema migration decisions.

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

Track spans use structured locations rather than Strange Customs style strings:

```json
{
  "upper": { "segmentId": "murphy:s:001", "distance": 0.5, "end": "A" },
  "lower": { "segmentId": "murphy:s:001", "distance": 0.5, "end": "B" }
}
```

`end` is optional and defaults to `A`. Set it to `B` when the distance or normalized value is measured from the segment's far end. `Start` and `End` are also accepted on input and normalize to `A` and `B`.

Use `normalized` for editor-authored spans where possible. Use `distance` only when the exact distance along a runtime segment matters.

When translating legacy graph patches that delete existing base-game track objects, use `tracks.removals`:

```json
{
  "tracks": {
    "removals": {
      "nodes": ["Nold-switch"],
      "segments": ["Sold-siding"],
      "spans": ["Old Spur"]
    }
  }
}
```

RAIL applies span removals first, then segment removals, then node removals.

Progression delivery phases that contain deliveries must set `industryComponentId`. That ID should point at a runtime `ProgressionIndustryComponent` that RAIL or the base map can activate while the phase is pending.

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

Telegraph pole definitions use the existing Railroader telegraph pole and wire prefabs by default. `profile` selects the first vanilla pole prefab whose name contains that profile string. `polePrefab` and `wirePrefab` can override that with explicit prefab URIs.

Industry components currently supported by the runtime include `loader`, `unloader`, `formulaic`, `repairTrack`, `teamTrack`, `interchange`, `interchangedLoader`, and `passengerStop`. Formulaic components are attached directly to the industry object to match Strange Customs behavior; other component types get child objects.

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
- `world.mapLabels` are applied at runtime as vanilla `MapLabel` clones. Labels with `style: "speedLimit"` are rendered as circled speed signs.
- `world.mapTiles` are mounted into the live `MapStore` at map-load time and can override individual tile coordinates without copying files into `StreamingAssets`.
- `world.sceneClones` are applied at runtime by cloning an existing scene object path or retargeting an existing object hierarchy. `localPosition`, `localRotation`, and `localScale` are optional; when omitted or `null`, RAIL now preserves the existing transform value instead of forcing zero or one. RAIL also strips unsupported mover components such as `PhysicsMover` from these clones so static visual copies do not register live physics controllers by accident.

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
  "RailDataFiles": [
    "track.rail.json",
    "operations.rail.json",
    "world.rail.json"
  ]
}
```

If explicit data-file entries are missing, RAIL falls back to the first `.bson` file in the package root, then the first `.json` file other than `Info.json`.
