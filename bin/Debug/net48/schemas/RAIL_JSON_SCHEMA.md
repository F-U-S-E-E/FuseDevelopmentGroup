# RAIL JSON Schema v1

This folder defines the JSON side of the RAIL mod format.

- `rail-mod.schema.json` is the authoritative JSON Schema for hand-authored and editor-exported `.json` files.
- `rail-mod.example.json` is a compact example that exercises track, span, industry, loader, scenery, spliney, label, mask, and progression data.
- `umm-info.schema.json` documents the Unity Mod Manager `Info.json` shape RAIL expects for API mods and data packages.
- `umm-info.example.json` is a data-only map package manifest that depends on `RAIL`.

## Design Choices

The schema uses one document for the whole mod. The shipped binary `.bson` file should use the same logical model, even if the serializer writes a compact representation internally.

Top-level object groups:

- `tracks`: nodes, segments, spans, and areas.
- `operations`: loads, industries, loaders, turntables, and passenger stations.
- `world`: scenery, rivers, roads, trestles, telegraph poles, map labels, and map masks.
- `progression`: progression trees and map features.
- `editor`: optional editor-only state that RAIL can ignore at runtime.
- `extensions`: optional namespaced third-party data.

All editable game objects are stored in dictionaries keyed by object ID. The ID is not repeated inside the object body. This keeps editor updates simple: replacing `tracks.nodes["murphy:n:001"]` updates exactly one object without array searches or ID duplication.

`schemaVersion` is the RAIL data format version and drives migrations. `modVersion` is the author's release version and should not be used for schema migration decisions.

Vector values are always objects:

```json
{ "x": 0, "y": 0, "z": 0 }
```

Track spans use structured locations rather than Strange Customs style strings:

```json
{
  "upper": { "segmentId": "murphy:s:001", "normalized": 0.1 },
  "lower": { "segmentId": "murphy:s:001", "normalized": 0.8 }
}
```

Use `normalized` for editor-authored spans where possible. Use `distance` only when the exact distance along a runtime segment matters.

Progression delivery phases that contain deliveries must set `industryComponentId`. That ID should point at a runtime `ProgressionIndustryComponent` that RAIL or the base map can activate while the phase is pending.

Asset and prefab references are URI strings:

- `vanilla://waterTower`
- `asset://HunterR.MurphyBranch/depotSmall`
- `rail://some-shared-catalog/object-id`

Validation that depends on Railroader runtime state, such as whether a prefab exists or whether a base-game passenger stop ID is valid, belongs in RAIL's validation layer rather than this JSON Schema.

## UMM Package Layout

Recommended data package layout:

```text
MurphyBranch/
  Info.json
  MurphyBranch.bson
  MurphyBranch.json
  Assets/
    Bundle
    Catalog.json
```

`Info.json` stays in Unity Mod Manager's normal PascalCase style. The RAIL data file stays in lower camelCase JSON style. RAIL should discover packages by reading `Info.json`, checking `Requirements` or `LoadAfter` for `RAIL`, then loading the optional `RailDataFile` value. If `RailDataFile` is missing, RAIL can fall back to the first `.bson` file in the package root, then the first `.json` file.
