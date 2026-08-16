# FUSE External Editor

The FUSE External Editor is a standalone desktop application for building FUSE
packages outside the game — terrain, track graph, and scenery, with a live link
that pushes changes into a running Railroader session.

It ships and versions independently of the mod. The in-game editor is not part of
this release.

## Download

Self-contained per-OS bundles are attached to each `externaleditor-v*` release on
the [Releases page](https://github.com/F-U-S-E-E/FuseDevelopmentGroup/releases):

- `FUSE.ExternalEditor-v<ver>-win-x64.zip`
- `FUSE.ExternalEditor-v<ver>-linux-x64.zip`
- `FUSE.ExternalEditor-v<ver>-osx-x64.zip`

The bundles are self-contained — no .NET runtime install required. Extract and run
`FUSE.ExternalEditor`.

The editor is game-free: it does not need Railroader installed to run. You only
need the game for the Live Bridge and for testing what you build.

Editor and mod version numbers are independent. A given editor build is not tied
to a matching mod build.

## First Run

Point the editor at your game's `Mods` folder with **Set Mods…**. This is what
lets it save packages where the game will find them and lets the Live Bridge
locate a running session. You only need to do it once.

Then either:

- **New Mod…** to start an empty package,
- **Open FUSE Mod…** to load an existing `*.FUSE` package, or
- **Convert Legacy…** to import a legacy mod directly, without running the
  standalone converter first.

**Save Mod…** writes the package back out.

## The Workspace

A central viewport with docked panels:

| Panel | What it shows |
| --- | --- |
| **Entities** | The package's object tree — nodes, segments, scenery, and the rest |
| **Selection** | Properties of what is currently selected |
| **Track Graph** | Graph-level view of nodes and connections |
| **Profile** | Elevation profile along the selection, with a running cut/fill figure in metres |
| **Calculator** | An expression box for arithmetic without leaving the app |
| **Live Bridge** | Connection state and the push control |

**Undo** and **Redo** apply across editing operations.

## Track Tools

Pick a tool from the toolbar, then work in the viewport.

| Tool | Purpose |
| --- | --- |
| **Select** | Pick nodes and segments |
| **Move** | Reposition the selection |
| **Add Node** | Place a new graph node |
| **Connect** | Join two nodes with a segment |
| **Curve** | Curve a connection |
| **Fit Arc** | Fit a circular arc through the selection |
| **Turnout** | Place a turnout |
| **Wye** | Place a wye |
| **Delete** | Remove the selection |
| **Measure** | Measure distance in the viewport |

**Scenery** places scenery objects.

Three-way switches are not available, because Railroader does not support them as
standard graph switches.

## Terrain

**Open Terrain…** loads terrain to edit; **Save Terrain** writes it back.

Sculpting tools share a **Brush** with **Radius** and **Strength** controls:

| Tool | Effect |
| --- | --- |
| **Raise** | Raise or lower ground |
| **Flatten** | Level toward a target height |
| **Smooth** | Soften sharp transitions |
| **Erode** | Simulated erosion |
| **Noise** | Add height variation |
| **Paint Terrain** | Paint surface types |

Watch the **Profile** panel's cut/fill readout while grading — it is the fastest
way to tell whether an alignment is buildable or whether you are moving an
unreasonable amount of earth.

### Generating Terrain

**Generate** builds terrain from real-world data for a given location and
dimensions (`gx`, `gy`, `w`, `h`, and height). Real-world elevation sources need a
Mapbox token, entered in the generation panel.

Additional layers:

- **NLCD** — land cover classification
- **Veg** — vegetation
- **Water** — water bodies
- **Hillshade** — shaded relief, for reading terrain shape in the viewport

### OSM Overlay

**Fetch OSM** pulls OpenStreetMap data for the current area and **OSM** toggles
the overlay. Use it to trace real alignments — roads, existing rail, and water
features — as a reference while placing track.

Both OSM and Mapbox data come from third-party services under their own licences
and terms. Check those terms before redistributing anything derived from them in a
published package.

## Live Bridge

The Live Bridge pushes edits into a running game session without a restart.

**Requires the `FUSE.LiveBridge` mod**, a separate zip attached to the mod release.
Install it in `Railroader/Mods` alongside FUSE.

With the game running and a map loaded, **Push to Game** sends the current package
for reload. **Refresh** re-reads the connection state.

The bridge is file-based: the editor writes a reload command into the package
folder and the in-game mod picks it up, reporting a heartbeat back. The editor
classifies the connection as stale when the heartbeat is more than five seconds
old — if the panel shows stale, confirm the game is running, a map is loaded, and
the LiveBridge mod is enabled.

The bridge is a development convenience. Verify a package by loading it normally
before publishing.

## Publishing A Package

1. **Save Mod…** into your `Mods` folder.
2. Load it in-game normally, without the bridge.
3. Run `/fuse.report` and `/fuse.validate <modId>` to confirm it is clean.
4. Check `/fuse.conflicts` if other packages touch the same route.

See [PACKAGE_AUTHOR_GUIDE.md](PACKAGE_AUTHOR_GUIDE.md) for the authoring contract
that governs ids, dependencies, and optional references.

## Related

- [PACKAGE_AUTHOR_GUIDE.md](PACKAGE_AUTHOR_GUIDE.md) — the authoring contract
- [`schemas/FUSE_JSON_SCHEMA.md`](../schemas/FUSE_JSON_SCHEMA.md) — the schema the editor writes
- [FUSE_CONVERTER.md](FUSE_CONVERTER.md) — batch and CLI conversion
- [CONSOLE_COMMANDS.md](CONSOLE_COMMANDS.md) — verifying in-game
