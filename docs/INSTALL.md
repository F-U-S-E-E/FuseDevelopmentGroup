# Installing FUSE

This covers the **core FUSE mod** and nothing else. It is all you need to run
FUSE and load FUSE packages. The converter, installer, and Live Bridge downloads
are optional extras for package authors — see [Optional extras](#optional-extras)
at the bottom if you think you need them, and skip them otherwise.

## Requirements

- Railroader `2025.1.x`
- [Unity Mod Manager](https://www.nexusmods.com/site/mods/21) (UMM), set up for
  Railroader

## Install

1. Install Unity Mod Manager and point it at your Railroader install.
2. Download `FUSE-v<version>.zip`.
3. Install it, either way:
   - **With UMM:** open UMM, go to the **Mods** tab, and drag
     `FUSE-v<version>.zip` onto it.
   - **By hand:** extract the zip so the `FUSE` folder inside it lands at
     `Railroader/Mods/FUSE`. When you are done, `Railroader/Mods/FUSE/FUSE.dll`
     and `Railroader/Mods/FUSE/Info.json` should both exist.
4. Start Railroader and load a map.

Do not nest the folder. `Railroader/Mods/FUSE/FUSE/FUSE.dll` is wrong and FUSE
will not load.

## Verify it worked

After a map loads, open the in-game console and run:

```
/fuse.report
```

You should get a load report. The FUSE icon also appears in the top bar, and
FUSE writes its own `FUSE.log` next to Railroader's `Player.log`.

If nothing happens, check that FUSE is enabled in UMM's Mods tab, then read
`FUSE.log`.

## Installing FUSE packages

FUSE itself does not add content — packages do. Each converted `*.FUSE` package
is its own folder under `Railroader/Mods`, installed exactly like the core mod.
Install any asset packs a package lists as required, then run `/fuse.loaded` to
confirm FUSE picked them up.

FUSE honors UMM's enabled checkbox: a package disabled in UMM is marked disabled
by FUSE and its track and data files are not loaded.

## Update

1. Back up your saves and your current `Railroader/Mods/FUSE` folder.
2. Replace `Railroader/Mods/FUSE` with the new version.
3. Start the game and run `/fuse.report`.

Do not leave a second copy of FUSE anywhere under `Railroader/Mods`.

## Uninstall

1. Exit Railroader.
2. Remove or disable your `*.FUSE` packages first.
3. Remove or disable `Railroader/Mods/FUSE`.
4. Start the game and confirm it loads normally.

Removing FUSE while packages that depend on it are still installed will leave
those packages inert.

## Reporting a problem

Include `FUSE.log`, Railroader's `Player.log`, and the output of `/fuse.report`.
Add `/fuse.loaded` when package state matters and `/fuse.conflicts` when
conflicts are reported. See [`TROUBLESHOOTING.md`](TROUBLESHOOTING.md).

## Optional extras

You do not need these to play. They are published on the
[GitHub releases page](https://github.com/F-U-S-E-E/FuseDevelopmentGroup/releases),
bundled together in `FUSE-Complete-v<version>.zip`:

- `FUSE-Converter.exe` — converts legacy Railloader / Strange Customs /
  ConfusingSupplements / For Your Convenience / Alina's Map Mod packages into
  FUSE packages.
- `FUSEConvertFolder.pyz` — the same conversion as a drag-and-drop folder tool.
- `FUSE-Installer.exe` — installs package zips into `Railroader/Mods` in bulk.
- `FUSE.LiveBridge-v<version>.zip` — a development-only mod that hot-reloads
  package edits into a running game. Not needed for normal play.
