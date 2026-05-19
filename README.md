# FUSE

FUSE is a Unity Mod Manager modding layer for Railroader. It loads FUSE data packages for route extensions, asset packs, audio packs, track graph changes, world scenery, operations, progression data, and compatibility imports from the legacy AMM, Strange Customs, and RailLoader ecosystem.

This repository is currently targeting a beta release. Signals are intentionally deferred until the rest of the legacy surface is stable.

## Supported Game Version

- Supported Railroader line: `2025.1.x`
- Current verified build in logs: `2025.1.0`
- Current FUSE schema version: `1.0`
- Current converter version: `0.2.0`

FUSE logs its version report at startup in `FUSE.log`.

## Supported Package Types

FUSE beta supports these package categories:

- Route/data packages with `*.fuse.json` files
- Map tile overlays
- Asset packs, including direct nested asset pack discovery under converted mod folders
- Track graph nodes, segments, spans, areas, groups, removals, and turntables
- World scenery, scene clones, map labels, speed signs, map masks, splineys, telegraph poles, telegraph pole movements, and spawn points
- Operations data: loads, industries, loaders, stations, passenger stops, team tracks, repair tracks, interchanges, interchanged loaders/unloaders, teleport loading, formulaic components, progression industry components, and custom industry components from loaded assemblies
- Progression sections, delivery phases, unlock-triggered map features, mixinto requirements, and map feature enable/disable flows
- Audio packs for whistles, horns, and bells

The FUSE JSON schema lives at `schemas/fuse-mod.schema.json`. The hand-written schema notes live at `schemas/FUSE_JSON_SCHEMA.md`.

## Not Supported In This Beta

- Signals
- Full public in-game editor workflow
- Multiplayer is compatibility-mode only for beta. FUSE does not sync package contents over the network; instead every host/client applies its own local package stack, matching the legacy RailLoader expectation that everyone has the same mods installed. Non-host clients log a warning the first time they apply runtime world changes. Servers that want strict client blocking can set `Settings.BlockNonHostMultiplayerClientWorldApply` to `true`.
- Arbitrary legacy script mods that are not data, asset, audio, or supported runtime component packages
- Rolling stock and locomotive/car mods, except audio definitions that FUSE can import
- Mid-session scene-path suppression re-enable
- Three-way switches, because Railroader does not support them as normal graph switches

## Install

1. Install Unity Mod Manager for Railroader.
2. Place the `FUSE` mod folder in `Railroader/Mods/FUSE`.
3. Place converted `*.FUSE` package folders in `Railroader/Mods`.
4. Install any asset packs required by the converted route packages.
5. Start Railroader and load a map.
6. Check the startup toast or run `/fuse.report`.

FUSE respects Unity Mod Manager's enabled checkbox for converted package folders that UMM can see. If a converted route/audio/asset package is disabled in UMM, FUSE marks that package disabled and does not load its track or data files.

For converter usage, see `tools/FUSE_CONVERTER.md`.

## Update

1. Back up saves and the current `Railroader/Mods/FUSE` folder.
2. Replace the FUSE mod folder with the new build.
3. Re-run the matching converter when the converter or schema version changes.
4. Start the game and check `/fuse.report`.

Do not mix legacy and converted FUSE copies of the same route unless you are deliberately testing conflicts.

## Uninstall Or Roll Back

1. Exit Railroader.
2. Remove or disable converted `*.FUSE` packages first.
3. Remove or disable `Railroader/Mods/FUSE`.
4. Restore the previous mod folders and save backup if needed.
5. Start the game and verify the legacy stack or vanilla game loads normally.

## Beta Support Policy

When reporting a beta issue, include:

- `FUSE.log`
- `Player.log`
- The output of `/fuse.report`
- The output of `/fuse.loaded` when package state matters
- The output of `/fuse.conflicts` when conflicts are reported
- The converter `conversion-report.json` and `conversion-report.md` for the affected package
- Screenshots and the exact mod/package list

The current supported test stack is the converted corpus under `C:\Railroader mods\Installed\Map` plus the asset packs required by those packages. Public support priority goes to issues reproducible with that stack or with a minimal converted package.

## Runtime Diagnostics

The top-bar FUSE icon opens the FUSE Health page. It shows the latest load report and exposes runtime `Reload Track` and `Reload Terrain` buttons for testing and recovery. Check `FUSE.log` after using either reload button.

Useful console commands:

- `/fuse.report` - show the current human-readable load report
- `/fuse.report json` - show the current load report as structured JSON
- `/fuse.loaded` - list loaded packages and apply state
- `/fuse.conflicts` - show ownership/conflict records
- `/fuse.graph` - summarize graph state
- `/fuse.operations` - summarize operations state
- `/fuse.progressions` - summarize progression state
- `/fuse.assets` - list discovered asset pack folders
- `/fuse.dumpgraph` - write the captured original graph to `FUSE-original-graph.json`
- `/fuse.dumpruntimegraph` - write the active post-FUSE graph to `FUSE-runtime-graph.json`
- `/fuse.dumpmandelas` - write scene clone and world path data to `FUSE-mandelas.json`

Experimental commands:

- `/fuse.reapply`
- `/fuse.restore`

Experimental commands should not be used during normal beta play unless testing recovery.

## Known Limitations

FUSE repairs many legacy data issues during conversion, but it does not silently hide authoring errors. Unsupported graph shapes, missing hard dependencies, invalid spans, and missing runtime component assemblies should be reported by the converter or runtime instead of being dropped.

See `docs/KNOWN_ISSUES.md` and `docs/TROUBLESHOOTING.md` for the current issue list and debugging workflow.
