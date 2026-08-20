# Railroader base-game map-authoring audit

This audit compares the observable Railroader map contract in the decompiled
2026.1 source with FUSE native schema, FUSE runtime behavior, and both Tile
Editor surfaces. Decompiled code is used only to identify public data shapes,
identifiers, initialization order, and observable behavior; no implementation
code is copied.

The installed game on the audit machine still exposes the earlier Unity
`TrackSegment.Style` contract. The 2026.1 source changes track objects and uses
combinable structure flags. Compatibility therefore has two distinct gates:

1. Native documents must retain every known field now.
2. A FUSE/editor binary compiled for a released game generation must pass its
   own live acceptance run. A source audit is not evidence that an unreleased
   binary contract already works in game.

## Coverage matrix

| Base-game concern | Observable contract | FUSE/editor coverage | Remaining acceptance |
|---|---|---|---|
| Map identity and launch | `MapDefinition` supplies map data, terrain tile sets, layers, and attribution; a session selects one registered map | Native `map` declaration, registered-map browser, launch command, map identity/start-state authoring, standalone-map golden fixture | Launch the generated fixture, save, reload, and reopen it in both editors |
| Layering | Base data plus JSON merge-patch layers; tombstones remove inherited values | FUSE owns its canonical native document and explicit removals; legacy mixintos convert at the boundary; native modular feature rules conditionally apply authored objects | Mixed native/legacy live reload and one full generated-map round trip |
| Folders | Map folders group objects/spawns and folder enabled state gates them | FUSE feature groups provide stronger package-owned conditional gating; editor retains unknown official-map fields when editing | Add an official-map import crosswalk so folder names/enabled state are visible instead of opaque retained data |
| Map objects | Registered DTO kinds include scenery, circle/rectangle/curve masks, and built-ins | Native scenery, masks, labels, scene clones, telegraph, water, splineys, and object lines; selectable base objects and town signs | Dense-scene object picking and stock-object move/remove live pass |
| Built-in car loader | Built-in id + track anchor + parameters, including infinite or industry supply | Native operations loaders plus Toolshed custom service bindings, track/span snap, offsets, source industry and load points | Live loading/unloading, storage, payment, and custom-model pass |
| Built-in spline loft | Ordered control points, point width/rotation, width/Y offsets, end extension, cap falloff, and generated mesh; used by road/river content | Deformable road/river splineys plus rigid repeated-object lines for fences, walls, guardrails, and pipes | Profile/material family inventory, cap/falloff UI acceptance, and in-game save/reload |
| Terrain tiles | Height, splat, vegetation, statistics, streaming and map tile-set ownership | Tile import/download workflow, sculpt/paint, categorical vegetation masks, atomic save/backup, statistics rebuild, cache invalidation, terrain registration | Seam/falloff/undo fidelity and live paint-save-reload across adjacent tiles |
| Water | Lake polygons/flat water surfaces reuse game material/profile data; rivers can also be spline-driven | Native water surfaces with CRUD, stock replacement, point editing, schema, validation, diagnostics; river spline authoring | Reflection/collider/material behavior and save/reload in game |
| Track nodes | Position, rotation, switch-stand flip, and diamond-crossing state | Native position/rotation/flip plus additive `isDiamond`; converter and editor preserve the field | Diamond routing/rendering in the game build that exposes it |
| Track segments | Endpoints, group, priority, speed, class, style or newer bridge/steel/tunnel/yard flags | Existing `style` stays canonical for released builds; additive steel-support and independent-yard metadata preserve the newer contract; gauge metadata, groups, grade labels, validation | Compile/run against the released flags-era assembly; live bridges, tunnels, yards, and DKW/crossing geometry |
| Turntables | Transform, radius, subdivisions, node ids, default stop and bridge group | Native turntable authoring and editor workflow | Custom model/track alignment, roundhouse, save/reload live pass |
| Track spans and areas | Locations anchor by segment/end/distance; areas group spans and UI/operations identity | Native spans/areas, normalization, node/segment relationships, orphan validation, legacy repair/rebinding | Sylva/Kater base-span cases and delivery/highlight/payment agreement |
| Industry operations | Areas, industries, loads, contracts, components, storage, loaders/unloaders, teams, interchange, passenger/timetable services | Native models, editors, schema, pre-publish cross-file validation, package-local fault reporting | Every component kind, EOD processing, contract changes and company-window live pass |
| Passenger service | Stops, span membership, agents/timetables and neighboring-stop caches | Native passenger/station models, invalid-span sanitation, editor workflows and validation | Neighbor reconciliation, timetable/UI, save/reload and modded-map live pass |
| Interchanges | Industry/service components, track/span routing and exchange behavior | Native interchange editor/runtime models and compatibility shims | Multi-interchange ordering, inbound/outbound/EOD and legacy interchange corpus |
| Signals and CTC | Signals, heads/aspects, blocks, routes, switch locks, interlockings and CTC panel components | Portable signal/CTC schema and runtime, editor placement/routes/blocks/dispatcher panel, cross-file validation | Native FUSE ownership, broader signal/custom-asset families, and a complete live territory |
| Progression | Map features, milestones, gating, industry/track availability and starting state | Native progression, transfer diagnostics, feature options and start-equipment scoping | Fresh career/sandbox reload behavior and complete generated-map progression pass |
| Starting equipment | Session setup places starting cuts and tutorial owns early UI flow | Package/map-scoped equipment pool; tutorial wait; cancellation retains queued cuts; successful placement requires full car count | Unrelated-new-save test and tutorial/Escape/cancel/resume/place live pass |
| Spawns/portals | Spawn DTOs and map bootstrap locations; interchange/portal concepts participate in session creation | Native spawn/start-point and interchange authoring in the complete-map fixture | Multiple spawn choices and portal/interchange launch behavior in game |
| Save identity | Stable object ids, graph ids, KVO/save identities and post-load binding are required | Stable native ids, duplicate/collision validation, atomic writes, backups, undo/redo, runtime definition cache | Generated map save, reload, edit, re-export and second reload |
| Runtime caches | Terrain, graph, scenery, operations and UI caches have subsystem-specific invalidation/rebuild order | Targeted track rebuild, terrain invalidation, scenery/asset indexes, operations refresh, live diagnostics and test bridge | Every-tab live acceptance using the newest deployed DLL |
| Multiplayer authority | Several operations/signals/session changes are authority-sensitive | Signal/CTC portable model includes multiplayer state; most authoring runs on local editor/session | Host/client ownership and synchronization pass |

## Canonical FUSE track additions from this audit

The following properties are additive and do not change existing native
documents:

- `tracks.nodes.<id>.isDiamond`
- `tracks.segments.<id>.bridgeSupportsSteel`
- `tracks.segments.<id>.yard`
- the corresponding `preserveBridgeSupportsSteel` and `preserveYard` controls
  for partial legacy overlays

Legacy numeric flags are decoded at conversion time. Existing `style` values
remain valid and continue to drive the installed released game. Editors update
copies of node/segment objects so unknown companion and future fields survive
ordinary position/property edits.

## What a from-scratch map must prove

A map-authoring release is accepted only when one fixture completes this whole
sequence without hand-editing:

1. Create map identity, attribution and coordinate/tile set.
2. Acquire/import adjacent terrain tiles and preserve source/licensing data.
3. Sculpt terrain, paint texture/vegetation, add/edit water, then save/reload.
4. Lay main, branch, yard, bridge/tunnel, turnout, crossing, span, group,
   turntable and interchange track; inspect grades and node relationships.
5. Add roads/rivers plus rigid fence/wall/guardrail/pipe object lines.
6. Add scenery, masks, labels, town signs, loaders and custom Toolshed models.
7. Configure areas, loads, industries, contracts, storage, delivery/payment,
   passenger service, interchange service and progression.
8. Build a working signal territory and CTC panel with routes/interlockings.
9. Configure spawn/start state and package-scoped starting equipment.
10. Validate, export, install, launch, operate, save, reload, reopen in the
    editor, change content, re-export and reload again.
11. Repeat from the published wiki using a clean installation.

Automated fixtures currently prove document creation, validation, export and
reopen. The live stages remain deliberately separate so a green unit test can
never be mistaken for rendered/operational proof.
