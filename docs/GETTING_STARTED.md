# Getting Started

This guide takes you from a clean Railroader install to a working FUSE setup with
a package loaded.

Migrating from Railloader or another legacy stack? Read
[MIGRATION_FROM_LEGACY.md](MIGRATION_FROM_LEGACY.md) first — the order of
operations matters there.

## Before You Start

- **Railroader `2025.1.x`.** FUSE targets this line. Other versions are not
  supported.
- **Unity Mod Manager** for Railroader, installed and working.
- **A backup of your saves.** Any mod that changes the world changes what your
  saves depend on.

## Install FUSE

1. Download the latest `FUSE-v<version>.zip` from
   [Releases](https://github.com/F-U-S-E-E/FuseDevelopmentGroup/releases).
2. Extract it so the mod lands at `Railroader/Mods/FUSE`.
3. Start Railroader.
4. Check that FUSE appears in Unity Mod Manager's mod list and is enabled.

`FUSE-Installer.exe` can do this for you: put it beside `Railroader.exe`, then
double-click it or drag one or more mod zips onto it. It validates the game/UMM
install, backs up updates, reports each package separately, and detects leftover
legacy loader DLLs. It also replaces the old AssetLoader runtime with a
data-only dependency alias, so use the installer when migrating an existing
AssetLoader-based setup. See [FUSE_INSTALLER.md](FUSE_INSTALLER.md).

## Install Packages

Native FUSE, UMM, and hosted RailLoader packages all go in `Railroader/Mods`
alongside FUSE itself. A native package does not have to use `.FUSE` in its
folder name; its `Info.json` and declared data files identify it.

1. Place each `*.FUSE` package folder in `Railroader/Mods`.
2. Install any asset packs the package requires — route packages usually depend on
   at least one.
3. Start the game and load a map.

FUSE respects UMM's enabled checkbox for any package folder UMM can see. Unchecking
a converted route/audio package or a directly installed asset package there marks
it disabled in FUSE too, and its track, data, or assets are not loaded.

Converting a legacy mod into a `*.FUSE` package is the converter's job — see
[FUSE_CONVERTER.md](FUSE_CONVERTER.md).

## Verify It Worked

Load a map, then open the in-game console and run:

```
/fuse.report
```

A healthy load reports zero faults, conflicts, asset issues, graph issues, and
transfer skips. The startup toast shows an abbreviated version of the same thing.

Then confirm your packages are actually present:

```
/fuse.loaded
```

Every package you installed should be listed and applied. A package that is
missing here was never discovered — check that it is in `Railroader/Mods` and
enabled in UMM. A package listed as faulted was found but isolated.
`/fuse.report` names the package and source file; `/fuse.report json` includes
structured paths and JSON locations for mod authors.

## The FUSE Menu

The FUSE icon in the top bar opens the in-game menu.

- **Status** — the latest load report.
- **Tools** — the Object Inspector, dependency/asset reports, **Live
  Diagnostics**, scenery benchmarks, and Runtime Actions (`Reload Track/Data`,
  `Reload Terrain`, `Rebuild Caches`). Live Diagnostics can filter/copy the
  recent FUSE log or open an optional continuously scrolling Windows console.

Routine Unity exceptions and compatibility-guard counters belong under **Tools
→ Live Diagnostics**. They do not by themselves make the Status page unhealthy;
use the symptom and package/asset/graph findings to decide whether a report is
actionable.

Runtime Actions are recovery and testing tools. Check `FUSE.log` after using one.

Settings changed through this menu persist to your own settings file and survive
mod updates. See [SETTINGS.md](SETTINGS.md).

## Where The Files Are

| What | Where |
| --- | --- |
| FUSE log | `%USERPROFILE%\AppData\LocalLow\Giraffe Lab LLC\Railroader\FUSE.log` |
| Unity log | `%USERPROFILE%\AppData\LocalLow\Giraffe Lab LLC\Railroader\Player.log` |
| Your settings | `%USERPROFILE%\AppData\LocalLow\Giraffe Lab LLC\Railroader\FUSE\settings.json` |
| Mods and packages | `Railroader\Mods` |
| Dump command output | The main Railroader folder |

Paste `%USERPROFILE%\AppData\LocalLow\Giraffe Lab LLC\Railroader` into Explorer's
address bar to jump straight there.

## Updating FUSE

1. Back up your saves and your current `Railroader/Mods/FUSE` folder.
2. Replace the FUSE mod folder with the new build.
3. Re-run the matching converter if the converter or schema version changed.
4. Start the game and check `/fuse.report`.

Keep the converter and the mod in step. Both ship in the same release, so taking
them from the same release tag keeps their versions matched.

## Uninstalling

1. Exit Railroader.
2. Remove or disable your `*.FUSE` packages **first**.
3. Remove or disable `Railroader/Mods/FUSE`.
4. Restore your previous mod folders and save backup if needed.
5. Start the game and confirm the vanilla game or your legacy stack loads normally.

Order matters: removing FUSE while packages remain leaves package folders nothing
will load.

## If Something Goes Wrong

Start with [TROUBLESHOOTING.md](TROUBLESHOOTING.md) — it maps symptoms to the
command that explains them. [FAQ.md](FAQ.md) covers the common questions.

When reporting a problem, include `FUSE.log`, `Player.log`, and the output of
`/fuse.report`.

## Next Steps

- [SETTINGS.md](SETTINGS.md) — what you can configure
- [CONSOLE_COMMANDS.md](CONSOLE_COMMANDS.md) — full command reference
- [PACKAGE_AUTHOR_GUIDE.md](PACKAGE_AUTHOR_GUIDE.md) — making your own packages
