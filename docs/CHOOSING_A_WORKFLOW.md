# Install, Convert, Or Author?

These are three different jobs. Choose the row that matches what you are trying
to do before downloading a tool.

| Goal | Use | What happens |
| --- | --- | --- |
| Play with one or many downloaded mods | `FUSE-Installer.exe` | Inspects each archive and installs native FUSE, UMM, or supported RailLoader packages. It does not rewrite the mod. |
| Keep using an existing legacy data mod | Install it normally | FUSE can host supported RailLoader/Strange Customs data and compatibility APIs in memory. Conversion is optional while the package works correctly. |
| Turn legacy JSON into native FUSE JSON | FUSE Converter | Converts route, graph, scenery, operations, and supported audio JSON, then writes a conversion report. |
| Install an asset pack or Alina map-tile package | `FUSE-Installer.exe` | Installs the original package so FUSE can load it directly; these packages are not converter inputs. |
| Convert a DLL/code mod | Do **not** use the converter | A converter cannot translate compiled program logic. Install the original package for FUSE compatibility hosting, or port its behavior as new independently written code. |
| Build or edit a map visually | Railroader Tile Editor | Authors track, scenery, roads, operations, signals, and related JSON. Players do not need the Editor. |
| Hand-author a native package | FUSE schema + author guide | Create `Info.json` and one or more `*.fuse.json` files. |
| Add working coal/water/diesel/bunker-C/wood service | Toolshed authoring guide | FUSE places the world/operations data; Toolshed owns the service-facility behavior. |
| Render narrow or dual gauge | FUSE Narrow Gauge | Optional runtime companion. It is not part of the Editor and is not required by ordinary FUSE packages. |

## What The Converter Does Not Do

The converter reads data. It does not decompile, translate, patch, or rebuild a
`.dll`, `.pdb`, executable, Harmony patch, UMM entry point, or arbitrary C# mod.
If a package contains code and data, only the recognized data can be converted.
The report lists code binaries as manual/unsupported work so they cannot be
mistaken for converted behavior.

FUSE's compatibility host is a separate runtime feature. It independently
implements supported legacy contracts so an installed legacy code mod can call
familiar APIs. That is why installing an old package and converting its JSON are
not the same operation.

## Recommended Release Shape

For players, distribute one FUSE Suite download/installer with clearly marked
components:

- FUSE Core — required
- Toolshed — optional gameplay companion
- FUSE Narrow Gauge — optional gauge renderer
- Tile Editor — optional authoring tool; never required to play a FUSE map

Keep those as separate assemblies and projects. A bug in an authoring tool or
optional gameplay feature should not prevent FUSE Core from loading a save or
opening a game menu.

## Next

- Players: [Getting Started](GETTING_STARTED.md)
- Existing mods: [Migrating From Legacy Mods](MIGRATION_FROM_LEGACY.md)
- Conversion: [FUSE Converter](FUSE_CONVERTER.md)
- Authors: [Package Author Guide](PACKAGE_AUTHOR_GUIDE.md)
- Common tasks: [Authoring Recipes](AUTHORING_RECIPES.md)
