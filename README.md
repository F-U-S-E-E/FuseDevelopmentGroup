# FUSE

FUSE is a Unity Mod Manager modding layer for Railroader. It loads FUSE data packages — route extensions, asset packs, audio packs, track graph changes, world scenery, operations, and progression data — and provides drop-in compatibility for legacy Railloader, Strange Customs, ConfusingSupplements, For Your Convenience, and Alina's Map Mod packages.

## Supported Game Version

- Supported Railroader line: `2025.1.x`
- Current verified build in logs: `2025.1.0`
- Current FUSE schema version: `1.0`
- Current converter version: `0.2.0`

FUSE logs its version report at startup in `FUSE.log`.

## Supported Package Types

FUSE supports these package categories:

- Route/data packages with `*.fuse.json` files
- Map tile overlays
- Custom map packages selectable from a dedicated New Game map dropdown, with Railroader's existing control retained separately for starting progression
- Asset packs, including direct nested asset pack discovery under converted mod folders
- Track graph nodes, segments, spans, areas, groups, removals, and turntables
- World scenery, scene clones, map labels, speed signs, map masks, splineys, telegraph poles, telegraph pole movements, and spawn points
- Operations data: loads, industries, loaders, stations, passenger stops, team tracks, repair tracks, interchanges, interchanged loaders/unloaders, teleport loading, formulaic components, progression industry components, and custom industry components from loaded assemblies
- Progression sections, delivery phases, unlock-triggered map features, mixinto requirements, and map feature enable/disable flows
- Audio packs for whistles, horns, and bells

The FUSE JSON schema lives at `schemas/fuse-mod.schema.json`. The hand-written schema notes live at `schemas/FUSE_JSON_SCHEMA.md`.

## Not Supported

- The retired in-game editor (use an external editor; FUSE still discovers and
  loads custom map packages at runtime)
- Multiplayer is compatibility-mode only. FUSE does not sync package contents over the network; instead every host/client applies its own local package stack, matching the legacy Railloader expectation that everyone has the same mods installed. Non-host clients log a warning the first time they apply runtime world changes. Servers that want strict client blocking can set `Settings.BlockNonHostMultiplayerClientWorldApply` to `true`.
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

For converter usage, see `docs/FUSE_CONVERTER.md`.

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

## Support Policy

When reporting an issue, include:

- `FUSE.log`
- `Player.log`
- The output of `/fuse.report`
- The output of `/fuse.loaded` when package state matters
- The output of `/fuse.conflicts` when conflicts are reported
- The converter `conversion-report.json` and `conversion-report.md` for the affected package
- Screenshots and the exact mod/package list

Support priority goes to issues reproducible with a minimal converted package or against a documented legacy route covered by the converter.

## Runtime Diagnostics

The top-bar FUSE icon opens the FUSE menu. The Status tab shows the latest load report; the Tools tab carries the Object Inspector, dependency/asset/diagnostics reports, scenery benchmarks, and Runtime Actions (`Reload Track/Data`, `Reload Terrain`, `Rebuild Caches`) for testing and recovery. Check `FUSE.log` after using a reload action.

Useful console commands:

- `/fuse.report` - show the current human-readable load report
- `/fuse.report json` - show the current load report as structured JSON
- `/fuse.loaded` - list loaded packages and apply state
- `/fuse.conflicts` - show ownership/conflict records
- `/fuse.graph` - summarize graph state
- `/fuse.operations` - summarize operations state
- `/fuse.progressions` - summarize progression state
- `/fuse.assets` - list discovered asset pack folders
- `/fuse.maps` - list maps registered by FUSE map packages and the active session map
- `/fuse.map.launch <mapId> [railroadName] [reportingMark]` - launch a new sandbox session on a registered FUSE map (main menu only)
- `/fuse.dumpgraph` - write the captured original graph to `FUSE-original-graph.json`
- `/fuse.dumpruntimegraph` - write the active post-FUSE graph to `FUSE-runtime-graph.json`
- `/fuse.dumpmandelas` - write scene clone and world path data to `FUSE-mandelas.json`

Experimental commands:

- `/fuse.reapply`
- `/fuse.restore`

Experimental commands should not be used during normal play unless testing recovery.

## Known Limitations

FUSE repairs many legacy data issues during conversion, but it does not silently hide authoring errors. Unsupported graph shapes, missing hard dependencies, invalid spans, and missing runtime component assemblies should be reported by the converter or runtime instead of being dropped.

See `docs/KNOWN_ISSUES.md` and `docs/TROUBLESHOOTING.md` for the current issue list and debugging workflow.

## License

Copyright (C) 2026 FUSE Development Group and contributors.

FUSE is free software: you can redistribute it and/or modify it under the terms
of the GNU Affero General Public License as published by the Free Software
Foundation, either version 3 of the License, or (at your option) any later
version.

FUSE is distributed in the hope that it will be useful, but WITHOUT ANY
WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A
PARTICULAR PURPOSE. See the GNU Affero General Public License for more details.

The full license text is in [`LICENSE`](LICENSE), and is also available at
<https://www.gnu.org/licenses/agpl-3.0.html>.

The AGPL covers FUSE's own source. It does not extend to Railroader, Unity, or
Unity Mod Manager, whose assemblies FUSE builds against and which remain under
their own proprietary terms; those assemblies are not redistributed here.

## Local Development

Requirements

- Visual Studio (in the installer, make sure you have SDKs for .NET Framework 4.8 and C# Support)

To start developing FUSE locally, follow these steps:

1. Clone this repo locally
2. Copy `Paths.user.example` and save it as `Paths.user`
3. Open this `Paths.user` file
4. Update the path entries for your local Railroader installation
5. Open the project in Visual Studio
6. Build the project
    - `EnableModDeploy` is set to true by default which will automatically build to your Railroader/Mods directory. This setting can be configured inside `Paths.user`
