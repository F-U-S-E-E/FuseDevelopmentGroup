# Beta Test Matrix

Use this matrix for the supported beta corpus. A pass means conversion, load, visual check, save/load, unload/reload, and relevant diagnostics are clean for that row.

| Area | Test | Evidence | Status |
| --- | --- | --- | --- |
| Core load | Current supported stack loads with no faults, conflicts, asset issues, graph issues, transfer skips, or suppressions | `FUSE.log`, `/fuse.report` | Passing on 2026-05-14 runtime log |
| Converter | Batch convert legacy route corpus without crashes | converter reports | Passing on latest 12/12 scratch run |
| Asset packs | Direct asset pack discovery finds nested packs without LocalLow mirroring | `/fuse.assets`, `FUSE.log` | Passing for current stack |
| Graph | Original and runtime graph dumps produce valid JSON | `/fuse.dumpgraph`, `/fuse.dumpruntimegraph` | Passing for current stack |
| Spans | Runtime graph dump has no endpointless spans and no spans pointing to missing segments | `FUSE-runtime-graph.json` | Passing for current stack |
| Operations | Industries, stations, loaders, interchanges, passenger stops, and custom components bind without FUSE warnings | `/fuse.operations`, company window | Needs repeated visual verification |
| Progression | Hidden map features stay hidden until unlock, delivery phases bind, transfer skips are zero | `/fuse.progressions`, in-game progression UI | Needs repeated visual verification |
| Map UI | Area/location ordering matches route order and company window can reopen after close | company window screenshots | In progress |
| World visuals | Scenery, mandelas, map masks, roundhouses, turntables, roads, rivers, trestles, and map labels visually match legacy where legacy is valid | screenshots | In progress |
| Audio | Horn, whistle, and bell packs load and sound like legacy packs | audio test in game | Needs testing |
| Save/load | Save, exit, reload, and continue without FUSE faults or duplicate objects | `FUSE.log`, save reload test | Needs testing |
| Unload/reload | Map unload restores runtime-owned suppressions and releases audio/object claims | `FUSE.log` | Needs testing |
| Clean install | Install, update, uninstall, and rollback on clean Railroader install | manual test notes | Needs testing |

## Current Supported Package Stack

The current beta test stack includes the converted packages installed under `Railroader/Mods` that are visible in `/fuse.loaded`, plus required asset packs. The latest known local stack includes Asheville, KingG Appalachian, KingG map tiles, Griz Oconoluftee River, GCR, Copper route modules, RTM/Embedded/ALW/C_L_B/Trowzrs/Aspens asset packs, and converted audio packs.
