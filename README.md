# FUSE

FUSE is a modding layer for Railroader. It loads FUSE packages — custom maps,
routes, scenery, industries, and audio — and runs your existing legacy mods
through a built-in compatibility layer. It runs on Unity Mod Manager.

## Documentation

Full documentation is in **[docs/](docs/README.md)**.

- New to FUSE: [Getting Started](docs/GETTING_STARTED.md) · [FAQ](docs/FAQ.md)
- Coming from Railloader or another legacy stack: [Migrating From Legacy Mods](docs/MIGRATION_FROM_LEGACY.md)
- Reference: [Settings](docs/SETTINGS.md) · [Console Commands](docs/CONSOLE_COMMANDS.md) · [Troubleshooting](docs/TROUBLESHOOTING.md)
- Authoring: [Package Author Guide](docs/PACKAGE_AUTHOR_GUIDE.md) · [Converter](docs/FUSE_CONVERTER.md) · [External Editor](docs/EXTERNAL_EDITOR.md)
- Contributing: [CONTRIBUTING.md](CONTRIBUTING.md) · [Architecture](docs/ARCHITECTURE.md)

## What it adds

- **Custom maps** — whole replacement worlds, picked from a map dropdown on the
  New Game screen
- **Routes and track** — extensions, new segments, switches, turntables
- **World scenery** — buildings, map labels, speed signs, telegraph poles
- **Operations** — industries, loads, stations, passenger stops, team tracks,
  interchanges
- **Progression** — sections, delivery phases, and unlock-gated map features
- **Audio** — whistles, horns, and bells

FUSE itself adds no content. It is the layer that loads content packages.

## Your existing mods keep working

Packages built for **Railloader**, **Strange Customs**, **ConfusingSupplements**,
**For Your Convenience**, and **Alina's Map Mod** load through FUSE's
compatibility layer. Package authors can also convert them to the FUSE format
with the converter — see [`docs/FUSE_CONVERTER.md`](docs/FUSE_CONVERTER.md).

## Requirements

- Railroader `2025.1.x`
- [Unity Mod Manager](https://www.nexusmods.com/site/mods/21) (UMM)

## Installation

1. Install Unity Mod Manager and point it at your Railroader install.
2. Download `FUSE-v<version>.zip` from the
   [releases page](https://github.com/F-U-S-E-E/FuseDevelopmentGroup/releases)
   or from Nexus Mods.
3. Install it, either way:
   - **With UMM:** open UMM, go to the **Mods** tab, and drag the zip onto it.
   - **By hand:** extract the zip so the `FUSE` folder inside it lands at
     `Railroader/Mods/FUSE`. When you are done,
     `Railroader/Mods/FUSE/FUSE.dll` and `Railroader/Mods/FUSE/Info.json`
     should both exist.
4. Start Railroader and load a map.

Open the console and run `/fuse.report` to confirm it loaded. The FUSE icon
also appears in the top bar, and FUSE writes its own `FUSE.log` next to
Railroader's `Player.log`.

**Do not nest the folder.** `Railroader/Mods/FUSE/FUSE/FUSE.dll` is wrong and
FUSE will not load.

For the longer version, including the optional converter and installer tools,
see [`docs/INSTALL.md`](docs/INSTALL.md).

### Installing content packages

Each FUSE package is its own folder under `Railroader/Mods`, installed exactly
like FUSE itself. Install any asset packs a package lists as required, then run
`/fuse.loaded` to confirm FUSE picked them up.

FUSE respects Unity Mod Manager's enabled checkbox — a package you disable in
UMM is marked disabled by FUSE, and its track and data files are not loaded.

### Updating

1. Back up your saves and the current `Railroader/Mods/FUSE` folder.
2. Replace the FUSE mod folder with the new build.
3. Re-run the matching converter when the converter or schema version changes.
4. Start the game and check `/fuse.report`.

Do not mix legacy and converted FUSE copies of the same route unless you are
deliberately testing conflicts.

### Uninstalling

1. Exit Railroader.
2. Remove or disable converted `*.FUSE` packages first.
3. Remove or disable `Railroader/Mods/FUSE`.
4. Restore the previous mod folders and save backup if needed.
5. Start the game and verify the legacy stack or vanilla game loads normally.

## Good to know

- **Multiplayer is compatibility mode only.** FUSE does not sync package
  contents over the network. Every host and client applies its own local package
  stack, matching the legacy Railloader expectation that everyone has the same
  mods installed. Non-host clients log a warning the first time they apply
  runtime world changes. Servers that want strict client blocking can set
  `Settings.BlockNonHostMultiplayerClientWorldApply` to `true`.
- FUSE does not add rolling stock or locomotive/car mods, apart from audio
  definitions it can import.
- The in-game editor was retired in 1.0. Authoring happens in the standalone
  external editor; FUSE still discovers and loads custom map packages at
  runtime.
- Arbitrary legacy script mods are not supported — only data, asset, audio, and
  supported runtime component packages.
- Mid-session scene-path suppression cannot be re-enabled.
- Three-way switches are unsupported, because Railroader does not treat them as
  normal graph switches.

## Having trouble?

Work through [`docs/TROUBLESHOOTING.md`](docs/TROUBLESHOOTING.md) first, then
file a report with the
[bug report form](https://github.com/F-U-S-E-E/FuseDevelopmentGroup/issues/new/choose),
which requires the diagnostics below before it will submit:

- `FUSE.log`
- `Player.log`
- The output of `/fuse.report`
- The output of `/fuse.loaded` when package state matters
- The output of `/fuse.conflicts` when conflicts are reported
- The converter `conversion-report.json` and `conversion-report.md` for the
  affected package
- Screenshots and the exact mod/package list

Support priority goes to issues reproducible with a minimal converted package,
or against a documented legacy route covered by the converter.

## Runtime diagnostics

The top-bar FUSE icon opens the FUSE menu. The Status tab shows the latest load
report; the Tools tab carries the Object Inspector, dependency/asset/diagnostics
reports, scenery benchmarks, and Runtime Actions (`Reload Track/Data`,
`Reload Terrain`, `Rebuild Caches`) for testing and recovery. Check `FUSE.log`
after using a reload action.

The Dependency Graph includes equipment and asset/code packages, not only FUSE
map data. It reads local FUSE, UMM, RailLoader, and AssetLoader metadata plus the
installer's offline Nexus cache, and shows missing/disabled/incompatible
requirements with their version bounds. The in-game menu does not contact
Nexus.

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

Experimental commands should not be used during normal play unless testing
recovery.

The full reference, including the commands not listed above, is in [docs/CONSOLE_COMMANDS.md](docs/CONSOLE_COMMANDS.md). Settings are documented in [docs/SETTINGS.md](docs/SETTINGS.md).

## Supported package types

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

The FUSE JSON schema lives at `schemas/fuse-mod.schema.json`. The hand-written
schema notes live at `schemas/FUSE_JSON_SCHEMA.md`.

## Supported game version

- Supported Railroader line: `2025.1.x`
- Current verified build in logs: `2025.1.0`
- Current FUSE schema version: `1.0`
- Current converter version: `0.2.0`

FUSE logs its version report at startup in `FUSE.log`.

## Known limitations

FUSE repairs many legacy data issues during conversion, but it does not silently
hide authoring errors. Unsupported graph shapes, missing hard dependencies,
invalid spans, and missing runtime component assemblies should be reported by the
converter or runtime instead of being dropped.

See [`docs/KNOWN_ISSUES.md`](docs/KNOWN_ISSUES.md) and
[`docs/TROUBLESHOOTING.md`](docs/TROUBLESHOOTING.md) for the current issue list
and debugging workflow.

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

## Local development

Requirements

- Visual Studio (in the installer, make sure you have SDKs for .NET Framework 4.8 and C# Support)
- A .NET 10 SDK — several projects in the solution target `net10.0`

To start developing FUSE locally, follow these steps:

1. Clone this repo locally
2. Copy `Paths.user.example` and save it as `Paths.user`
3. Open this `Paths.user` file
4. Update the path entries for your local Railroader installation
5. Open the project in Visual Studio
6. Build the project
    - `EnableModDeploy` is set to true by default which will automatically build to your Railroader/Mods directory. This setting can be configured inside `Paths.user`

You do not need Railroader installed to build. Without `GameDir` set, the build falls back to the checked-in reference assemblies under `lib/refs`.

See [CONTRIBUTING.md](CONTRIBUTING.md) for the testing policy, code conventions, and PR process, and [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for how the projects fit together.

## Contributing

Contributions are welcome — start with [CONTRIBUTING.md](CONTRIBUTING.md). Security issues should be reported privately per [SECURITY.md](SECURITY.md), not through the public issue tracker.

See [`AGENTS.md`](AGENTS.md) for skill and sub-agent routing, and
[`docs/RELEASING.md`](docs/RELEASING.md) for how releases are cut.
