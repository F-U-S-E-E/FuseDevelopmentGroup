# Conversion Test Matrix

Use this table to track legacy conversion coverage. Status values should use: DONE, PARTIAL, IN PROGRESS, BROKEN, MISSING, NEEDS TESTING, NEEDS DOCUMENTATION, UNKNOWN.

## Package Matrix

| Legacy package / feature | Source | FUSE target | What it tests | Expected result | Status | Priority |
| --- | --- | --- | --- | --- | --- | --- |
| Appalachian route | `C:\Steam\steamapps\common\Railroader\Mods.bck\KingG.Appalachian-Railway` | `KingG.Appalachian-Railway.FUSE` | Full route graph, industries, loaders, stations, turntables, roads, rivers, trestles, scene clones, labels | Route loads with all major objects and no fatal package faults | NEEDS TESTING | Critical |
| Appalachian map tiles | `C:\Steam\steamapps\common\Railroader\Mods.bck\KingG.Appalachian.MapTiles` | `KingG.Appalachian.MapTiles.FUSE` | `world.mapTiles`, map store patch, tile priority | 5148 tiles mount for `BushnellWhittier` | DONE | Critical |
| C_L_B assets | `C:\Steam\steamapps\common\Railroader\Mods.bck\C_L_B.ASSETS` | `C_L_B.ASSETS.FUSE` | Asset-pack wrapper, scenery asset identifiers | Asset pack mounts and objects resolve by asset ID | NEEDS TESTING | High |
| Aspens assets core | `C:\Steam\steamapps\common\Railroader\Mods.bck\aspensassets` | `aspensassets.FUSE` | Asset-pack conversion/wrapper | Assets available to scenery packages | NEEDS TESTING | High |
| Aspens assets for J | `C:\Steam\steamapps\common\Railroader\Mods.bck\Aspens Assets for J` | `aspensjassets.FUSE` | Shared dependency asset pack | Dependent scenery resolves assets | NEEDS TESTING | High |
| Aspens common resources | `C:\Steam\steamapps\common\Railroader\Mods.bck\Aspens Common Resources` | Asset-pack wrapper | Shared resources | Shared assets mount before dependents | PARTIAL | Medium |
| Aspen RDG CNJ resources | `C:\Steam\steamapps\common\Railroader\Mods.bck\aspenrdgcnjresources` | Existing asset/scenery package | Asset identifiers and dependency order | Assets resolve without unknown scenery report | UNKNOWN | Medium |
| Oconaluftee River | `C:\Steam\steamapps\common\Railroader\Mods.bck\Griz1231.Ocanaluftee_River` | `Griz1231.Ocanaluftee_River.FUSE` | Spliney river inference and terrain interaction | River uses `type: "river"`, not road, and renders without road material artifacts | NEEDS TESTING | High |
| RTM object pack | `C:\Steam\steamapps\common\Railroader\Mods.bck\RTM Objects pack` | `RTM.Objects.Oconaluftee.FUSE` or wrapper | Asset-pack and scenery placement | Objects resolve and place correctly | NEEDS TESTING | Medium |
| Trowzrs buildings | `C:\Steam\steamapps\common\Railroader\Mods.bck\Trowzrs_Buildings` | Asset/scenery wrapper | Building asset library | Buildings resolve by asset ID | UNKNOWN | Medium |
| TrackBridge | `C:\Steam\steamapps\common\Railroader\Mods.bck\TrackBridge` | Unknown | Bridge/scenery/track-adjacent object support | Determine if schema can represent it | UNKNOWN | Medium |
| SceneryAnimationFix | `C:\Steam\steamapps\common\Railroader\Mods.bck\SceneryAnimationFix` | Probably not data-only | Compatibility with script mods | Decide if this remains external | UNKNOWN | Low |
| Standard Catalog of Load IDs | `C:\Steam\steamapps\common\Railroader\Mods.bck\The Standard Catalog of Load IDs` | FUSE load reference doc/package | Load IDs and compatibility naming | Load IDs documented or imported | UNKNOWN | Medium |
| AMW CPLW whistles | `C:\Steam\steamapps\common\Railroader\Mods.bck\AMW_CPLW_Whistles` | `AMW_CPLW_Whistles.FUSE` | Whistle loose-file conversion | 48 whistles appear in customization UI and play | NEEDS TESTING | High |
| Collie whistle pack | `C:\Steam\steamapps\common\Railroader\Mods.bck\CollieWhistlePack` | `CollieWhistlePack.FUSE` | Large whistle pack conversion | 168 whistles appear and clips load on demand | NEEDS TESTING | High |
| Collie quill horn pack | `C:\Steam\steamapps\common\Railroader\Mods.bck\CollieQuillHornPack` | `CollieQuillHornPack.FUSE` | Horn layers and keyframes | 8 horns appear and play through FUSE horn controller | NEEDS TESTING | High |
| Nick's horn pack | `C:\Steam\steamapps\common\Railroader\Mods.bck\NicksHornPack` | `NicksHornPack.FUSE` | Large horn pack conversion | 47 horns appear and play | NEEDS TESTING | High |
| GN Better Whistles | `C:\Steam\steamapps\common\Railroader\Mods.bck\GN Better Whistles` | `GN Better Whistles.FUSE` | Asset-pack-only audio wrapper | Asset packs mount and definitions remain available | NEEDS TESTING | Medium |
| EMD 16-567C Audio | `C:\Steam\steamapps\common\Railroader\Mods.bck\EMD 16-567C Audio` | Not supported yet | Prime mover replacement | Define FUSE prime mover schema before conversion | MISSING | High |
| EMD 6-567C Audio | `C:\Steam\steamapps\common\Railroader\Mods.bck\EMD 6-567C Audio` | Not supported yet | Prime mover replacement | Define FUSE prime mover schema before conversion | MISSING | High |
| Salty CAT 8-3608 Prime Mover | `C:\Steam\steamapps\common\Railroader\Mods.bck\Salty's CAT 8-3608 Prime Mover` | Not supported yet | Prime mover replacement | Define FUSE prime mover schema before conversion | MISSING | High |
| Cars for MP | `C:\Steam\steamapps\common\Railroader\Mods.bck\Cars for MP` | Not supported yet | Rolling stock definitions | Decide FUSE rolling stock scope | MISSING | High |
| Collie Bryson route | `C:\Steam\steamapps\common\Railroader\Mods.bck\CollieBryson` | Future FUSE package | Route conversion variance | Confirms converter is not Appalachian-specific | UNKNOWN | High |
| Collie Barking Up The Wrong Creek | `C:\Steam\steamapps\common\Railroader\Mods.bck\CollieBarkingUpTheWrongCreek` | Future FUSE package | Route/world conversion variance | Confirms route conversion across another package | UNKNOWN | High |
| GRIZ Whittier Populated | `C:\Steam\steamapps\common\Railroader\Mods.bck\GRIZ_Whittier_Populated` | Future FUSE package | Scenery-heavy populated route | No unknown asset explosion; scene clones/scenery work | UNKNOWN | Medium |
| AMW East Whittier | `C:\Steam\steamapps\common\Railroader\Mods.bck\AMW_East Whittier` | Future FUSE package | Route extension and dependency order | Loads after required assets and route data | UNKNOWN | Medium |

## Feature Matrix

| Feature | FUSE evidence | Test package | Expected result | Status | Priority |
| --- | --- | --- | --- | --- | --- |
| Multiple data files per package | `FuseDataFiles`, `ResolveDefinitionPaths` | Appalachian | Each source concern loads separately, not merged into one giant file | DONE | Critical |
| Package dependency sort | `FuseLoadPriority`, `FuseLoadAfter`, `FuseLoadBefore` | Synthetic dependency packages | Correct topological order; cycle warning on bad set | NEEDS TESTING | High |
| Package fault isolation | `FusePackageFaultRegistry`, `FuseApplyTransaction` | Dirty load set | Bad package faults while unrelated packages apply | NEEDS TESTING | Critical |
| Track node conversion | `TrackAPI.AddNode` | Appalachian | Nodes exist after graph rebuild | NEEDS TESTING | Critical |
| Track segment conversion | `TrackAPI.AddSegment` | Appalachian | Segments connect expected nodes | NEEDS TESTING | Critical |
| Track span conversion | `TrackAPI.AddSpan`, span validator | Span sample | Valid spans create; bad spans produce clear errors | NEEDS TESTING | Critical |
| Turnouts | Base graph plus segment/node apply | Appalachian/Cherokee area | Turnouts work and switch stands are oriented correctly | NEEDS TESTING | Critical |
| Reversible track removals | `FuseTrackRemovalSnapshotStore` | Removal sample | Load removes base segment; unload restores it | NEEDS TESTING | Critical |
| Areas and industry order | `TrackAPI.ApplyAreaOrdering`, location panel patch | Appalachian | Company list matches source area and industry order | NEEDS TESTING | High |
| Loader/unloader components | `IndustryAPI` | Appalachian | Cars load/unload at converted spans | NEEDS TESTING | Critical |
| Formulaic components | `IndustryAPI` | Appalachian | Production works and component cache is valid | NEEDS TESTING | High |
| Team track components | `IndustryAPI` | Appalachian | Team track profiles appear/work | NEEDS TESTING | High |
| Interchange components | `IndustryAPI` | Appalachian | Interchange setup does not fail and behaves generically | NEEDS TESTING | High |
| Passenger stop components | `FusePassengerStopComponent`, `StationAPI` | Appalachian | Passenger stops are usable and timetable metadata survives | NEEDS TESTING | High |
| Station map icons | `StationAPI.ConfigureMapIcons` | Appalachian | Icons appear over station side, correct size/rotation, click jumps camera | NEEDS TESTING | High |
| Turntables | `TurntableAPI` | Appalachian | Bridge, pit, and controls render and operate | NEEDS TESTING | Critical |
| Roundhouses | `TurntableAPI`, prefab sanitizer | Appalachian | Stalls align; no stray beams or scaling errors | NEEDS TESTING | Critical |
| Scene clones / mandelas | `SceneCloneAPI` | Appalachian | Clones appear at intended path/transform | NEEDS TESTING | High |
| Scenery assets | `SceneryAPI` | Asset packs | Known assets instantiate; unknown assets reported | NEEDS TESTING | High |
| Span-anchored scenery | `FuseScenery.AnchorSpanIds` | Crossing sample | Crossing aligns to span tangent | NEEDS TESTING | Medium |
| Roads | `SplineyAPI`, converter type inference | Appalachian | Dirt/paved roads render with correct family | NEEDS TESTING | High |
| Rivers | `SplineyAPI`, converter type inference | Oconaluftee River | River renders as water, not road | NEEDS TESTING | High |
| Trestles | `SplineyAPI` | Appalachian | Trestles generate correct visuals | NEEDS TESTING | High |
| Telegraph pole sets | `MapAPI.AddTelegraphPoles` | Sample | New pole set appears | NEEDS TESTING | Medium |
| Telegraph pole movements | `MapAPI.ApplyTelegraphPoleMovements` | Appalachian/SC sample | Existing poles move idempotently and restore on unload | NEEDS TESTING | Medium |
| Map labels | `MapAPI.AddMapLabel` | Appalachian | Names are not split incorrectly and appear at right positions | NEEDS TESTING | High |
| Speed signs | `MapAPI` speed-limit labels | Appalachian | Circled speed signs match base game size/background | NEEDS TESTING | Medium |
| Map masks | `MapAPI.AddMapMask` | Appalachian | Terrain/object masks appear as expected | NEEDS TESTING | Medium |
| Map tiles | `FuseMapTileRegistry`, `MapStorePatches` | Appalachian map tiles | Tiles mount and override expected coordinates | DONE | Critical |
| Spawn points | `SpawnPointAPI` | Spawn sample | Spawn point appears in game spawn list | NEEDS TESTING | Medium |
| Progression sections | `ProgressionAPI`, `FuseProgressionRoot` | Narrative sample | Sections unlock features and deliveries | NEEDS TESTING | High |
| Group suppression | `FuseWorldSuppressor` | Suppression sample | Group hides, shared owners refcount, restores on unload | NEEDS TESTING | High |
| Area suppression | `FuseWorldSuppressor` | Suppression sample | Area is disabled via synthetic MapFeature and restored | NEEDS TESTING | High |
| Scene-path suppression | `FuseEarlyLoader` | Experimental sample | With opt-in setting, path suppresses before activation; timeout releases | NEEDS TESTING | Medium |
| Whistles | `FuseAudioAPI`, `FuseAudioPatches` | AMW/Collie packs | Choices appear and play | NEEDS TESTING | High |
| Horns | `FuseAudioAPI`, `FuseAudioPatches` | Collie/Nick packs | Choices appear and layered audio plays | NEEDS TESTING | High |
| Bells | `FuseAudioAPI`, `FuseAudioPatches` | Need source bell pack | Choices appear and loop/index times work | UNKNOWN | Medium |
| Prime movers | No FUSE schema/API yet | EMD/CAT packs | Not supported until design exists | MISSING | High |
| Full rolling stock | No FUSE schema/API yet | Cars for MP | Not supported until design exists | MISSING | High |

## Minimum Public Alpha Pass

- [ ] Appalachian route loads from a clean game launch.
- [ ] Appalachian route survives save and reload.
- [ ] Map tile package mounts.
- [ ] One converted asset pack resolves scenery assets.
- [ ] One converted audio package exposes choices in customization UI.
- [ ] Dirty package set produces readable in-game FUSE report.
- [ ] No required migrated package depends on AMM or Strange Customs assemblies.

