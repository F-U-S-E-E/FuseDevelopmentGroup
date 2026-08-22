# FUSE Authoring Recipes

This page answers the common “how do I do this?” questions. The
[JSON schema reference](../schemas/FUSE_JSON_SCHEMA.md) remains the field-by-field
contract.

## Start A Package

The easiest route is Tile Editor → **New Mod**. For hand-authored native data,
create one package folder containing `Info.json` and one or more
`*.fuse.json` files. Point `FuseDataFiles` at every fragment:

```json
{
  "Id": "Author.Route",
  "DisplayName": "Author Route",
  "Author": "Author",
  "Version": "1.0.0",
  "ManagerVersion": "0.27.10",
  "Requirements": ["FUSE"],
  "LoadAfter": ["FUSE"],
  "FuseDataFiles": ["track.fuse.json", "industry.fuse.json"]
}
```

Use `FuseRequires` for a hard data-package dependency. Use `FuseLoadAfter` or
`FuseLoadBefore` only for ordering. A missing hard dependency faults/skips the
affected package and appears in `/fuse.report`; it must not break unrelated
packages or game menus.

## Move Or Replace Track

1. Open the package in Tile Editor and connect to the game.
2. Press `F9`, open **Geo**, then select the node or segment in the world.
3. Use the node/segment transform controls. The relationship list shows every
   segment using a selected node and both endpoints of a selected segment.
4. Save and force a track reload if the live preview ever differs from the
   selected JSON. The editor now rebuilds affected track objects after topology
   changes so stale copies do not accumulate.
5. Reopen the package and verify the same IDs and geometry before release.

Do not create a second segment with the same ID to replace the first one. FUSE
uses atomic replacement for existing span/segment IDs so industries keep their
references, but duplicate definitions inside one package are still authoring
errors.

## Remove A Base-Game Industry

Use the exact runtime industry ID, not its company-window display name:

```json
{
  "schemaVersion": "1.0",
  "id": "Author.Route.industry-removals",
  "name": "Industry removals",
  "modVersion": "1.0.0",
  "operations": {
    "removals": {
      "industries": ["exact-runtime-industry-id"]
    }
  }
}
```

Run `/fuse.dumpgraph` and `/fuse.dumpruntimegraph` to find and compare IDs. The
Tile Editor Operations page can discover/select industries, but manual JSON is
still the safest way to remove a stock industry when the editor cannot identify
an unambiguous owning layer.

## Add Freight Loading Or Unloading

A rail industry component changes freight on cars spotted over a TrackSpan. It
does **not** place a visible coal chute, water column, or fuel stand.

1. Create/select a town and industry in Tile Editor → `F9` → **Operations**.
2. Create a whole, partial, or multi-segment TrackSpan from selected track.
3. Add a `loader`, `unloader`, `formulaic`, `teamTrack`, `interchange`, or other
   component to the industry.
4. Select the span and load ID, then configure capacity/rates.
5. Verify `/fuse.operations` and the company window in a new sandbox save.

## Place A Visible Service Loader

Choose the implementation that matches the asset:

- An existing vanilla-style loader prefab that already contains Railroader's
  loader components can use native `operations.loaders`.
- A custom coal/water/diesel/bunker-C/wood facility should be placed through
  `world.scenery`, then bound with `ToolshedServiceFacilities.json`. Toolshed owns
  its outlet, animation, transfer, storage, and interaction behavior.

Do not split one custom facility between a scenery clone and an unrelated
operations loader. That creates the “tank in one place, loading point in another”
failure. The visible object and its service points must share the same placed
root/transform contract.

## Move A Base-Game Object Or Town Sign

Open `F9` → **Objects**, disable Track Geometry if it blocks selection, then click
the object. Save its move/rotate/scale/visibility override as a scene clone or
mandela entry. Thin signs have a screen-space picking halo, and shared town/map
roots are blocked so one click cannot move an entire scene hierarchy.

## Edit Roads And Other Mandelas

Use **Spliney** for roads, rivers, bridges, and trestles. Base-game spline control
points are editable; the first change writes a same-name override into the active
layer. A legacy scene path such as `World/Roads Sylva/Chipper Curve` remains a
scene path—it is not an asset identifier.

Use **Objects/Mandela** for individual scene objects and **Scenery** for asset-pack
models. They have different ownership and runtime rebuild paths.

## Build A Fence, Retaining Wall, Or Other Repeated Object Line

Use a native `world.splineys` entry with `type: "objectLine"`. This samples a
path at uniform spacing and places rigid modules; it does not stretch a fence
panel or retaining-wall block into a distorted mesh.

```json
{
  "world": {
    "splineys": {
      "yard:fence:north": {
        "type": "objectLine",
        "assetIdentifier": "your-pack:fence-panel",
        "spacing": 3.0,
        "instanceScale": { "x": 1, "y": 1, "z": 1 },
        "rotationOffset": { "x": 0, "y": 90, "z": 0 },
        "lateralOffset": 0,
        "verticalOffset": 0,
        "snapToTerrain": true,
        "alignToSlope": false,
        "placeAtEnd": true,
        "maximumInstances": 1024,
        "points": [
          { "position": { "x": 10, "y": 2, "z": 15 } },
          { "position": { "x": 55, "y": 3, "z": 22 } }
        ]
      }
    }
  }
}
```

Choose exactly one source: `assetIdentifier` is portable and recommended;
`prefab` accepts a safe FUSE prefab/scene-path URI for advanced stock-object
cloning. The Tile Editor exposes this as **Geo → Spliney → Fence / Wall** and
rebuilds the repeated modules while its point handles remain editable. This is
native FUSE only; the legacy RailLoader spline schema has no equivalent.

## Conditional Legacy Mixins

Converted conditional fragments use `mixinto.requires`:

```json
{
  "mixinto": {
    "target": "game-graph",
    "sourceFile": "OptionalYard.json",
    "requires": [
      { "id": "Author.BaseYard", "notBefore": "1.2.0" }
    ]
  }
}
```

When the dependency is missing or outside the allowed range, FUSE skips this
fragment and reports the package, folder, source file, dependency ID, and action.
`loadAfter` is ordering and must not be flattened into a hard requirement.

## Package Options And Modular Mods

Top-level `settings` creates controls in FUSE's per-mod settings page. Native
code can read them through `FuseModSettingsAPI`. The current v1 data schema does
not yet make arbitrary JSON sections conditional on a setting, so do not imply
that a slider automatically enables track/spans. Until a declarative condition
format is released, use separate optional data packages/fragments with explicit
dependencies, or a small independently authored code component that reads the
setting and applies supported API operations.

## Diagnose Bad JSON

Run `/fuse.report` for a readable report or `/fuse.report json` for structured
output. A definition failure includes:

- package ID
- absolute package folder and source file
- JSON path
- line and column when Newtonsoft can provide them
- validation code/message
- suggested action

FUSE isolates the bad definition and continues loading unrelated packages. Fix
the first syntax error first; later errors can be consequences of that one.
