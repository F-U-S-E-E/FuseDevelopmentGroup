# RAIL

Railroader API Integration Layer.

RAIL is the Unity Mod Manager backend API for editor-first Railroader map modding. The first implementation phase establishes the mod package, schema model, serialization, validation primitives, cache base classes, and lifecycle events.

Current runtime coverage includes:

- custom load catalog entries injected into `CarPrototypeLibrary`
- track nodes, segments, spans, areas, removals, and graph rebuild batching
- industries, area parenting, ordered company-window locations, and loader/unloader/formulaic/team track/repair/passenger components
- loader placement from vanilla prefabs
- turntables with deterministic pit node IDs and roundhouse prefab support
- scenery, splineys, map labels, circled speed-limit map signs, map masks, passenger stations, telegraph pole sets, scene clones, and map tile overlays
- progression sections and map features

Map tile packages can ship extra `tile_XXX_YYY.data` files inside a normal RAIL mod package and mount them with `world.mapTiles`. Those overlays are applied directly to Railroader's `MapStore`, so converted tile mods do not need to copy files into `StreamingAssets`.

## Build

```powershell
dotnet build
```

The project defaults to:

```text
C:\Steam\steamapps\common\Railroader
```

Copy `Local.Build.props.example` to `Local.Build.props` if your install path differs.

To build and deploy directly into Unity Mod Manager's mod folder:

```powershell
dotnet build .\RAIL.csproj -c Release /p:EnableModDeploy=true
```
