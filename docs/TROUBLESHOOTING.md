# Troubleshooting

## First Checks

1. Start the game and load the map.
2. Read the startup FUSE toast.
3. Run `/fuse.report`.
4. If the report shows faults, conflicts, asset issues, graph issues, or transfer skips, gather the files listed below.

## Files To Attach

Attach these for almost every report:

- `%USERPROFILE%\AppData\LocalLow\Giraffe Lab LLC\Railroader\FUSE.log`
- `%USERPROFILE%\AppData\LocalLow\Giraffe Lab LLC\Railroader\Player.log`
- The affected package folder or zip when possible
- The package conversion report: `conversion-report.json`
- The package conversion report: `conversion-report.md`
- Screenshots of visible map, scenery, company window, or progression issues

Also include console output or screenshots for:

- `/fuse.report`
- `/fuse.loaded`
- `/fuse.conflicts`
- `/fuse.graph` for graph or track issues
- `/fuse.operations` for industries, stations, loaders, or turntables
- `/fuse.progressions` for map feature/progression visibility issues
- `/fuse.assets` for missing scenery assets

## Common Symptoms

### I Want A Live Debug Console

Open **FUSE → Tools → Live Diagnostics → Open Live Console**. On Windows this
opens a continuously scrolling mirror of `FUSE.log` for a second monitor. Stop
it from the same page before exiting; the console's Close command is disabled so
closing that optional diagnostics window cannot terminate Railroader. On other
platforms, use the filtered in-game viewer or tail `FUSE.log` with a text tool.

The compatibility-guard and observed-exception lists on this page are evidence,
not automatic bug verdicts. Include them when they repeat alongside a visible
problem.

### A Two- To Four-Second Stutter Keeps Repeating

Enable **Frame Spike Diagnostics** in FUSE Settings, reproduce for at least a
minute, then attach `FUSE.log` and `Player.log`. Each retained spike includes GC
deltas, streaming and track queue depths, equipment completion depth, and the
slowest measured FUSE runtime-pump phase. Do not infer the cause from ordinary
Unity exceptions alone. A named phase identifies where to investigate; `none
measured` is equally useful because it points outside FUSE's pump.

For a buy-menu stall, wait for the equipment-catalog warm-up completion line and
include its total work, slow-store count, and worst-store fields. Report whether
the first click happened before or after that line.

### Faulted Package

Run `/fuse.loaded` and `/fuse.report`. Check `FUSE.log` for the package id and phase. Package failures should not prevent unrelated packages from loading.

For an author-ready report, open **FUSE → Mods**, select the affected package,
and use **Copy Mod Info**. Its actionable diagnostic names the package and
source root, shows both relative and absolute filenames, includes JSON
property/line/position and validation code when available, contrasts the
expected shape with the received value, and ends with the concrete correction.
This package-scoped copy is usually easier to send to an author than the full
health report.

### Progression Is Unlocked But Towns Or Industries Stay Disabled

Run `/fuse.progressions`, then check the package folder for backup definitions.
FUSE applies every top-level `*.fuse.json` file. A file named, for example,
`progressions-old1.fuse.json` is still live and can overwrite the current
progression's section-to-feature links. Rename or remove archival definitions,
reload the save, and test again before changing the saved progression state.

### Unknown Scenery Asset

Run `/fuse.assets` and verify the required asset pack is installed. Do not replace the missing asset with a guessed alias unless the legacy source actually used the alias.

### Missing Or Wrong Building

Check whether it is a regular asset pack item or a scene clone. For scene clones, run `/fuse.dumpmandelas` and compare the source path with the base game scene path.

### Bad Track, Missing Span, Or Broken Segment

Run `/fuse.dumpgraph` and `/fuse.dumpruntimegraph`. The files are written to the main Railroader folder as `FUSE-original-graph.json` and `FUSE-runtime-graph.json`.

Open **FUSE → Tools → Mod Conflicts** before assuming one track mod is broken.
The page groups conflicts by the two packages involved and lists the exact
objects and winning/merge behavior. "Potential track-layout overlap" is an
advisory: FUSE found multiple nearby authored nodes but kept both packages
because an intentional connection can look similar. If the map contains
overlapping switches, floating rails, or dead stubs, temporarily enable only one
primary layout for that area and retest. For example, East Whittier Yard Revamp
and AMW East Whittier are alternative yard layouts; add East Whittier Crossover
only when its author declares it compatible with the chosen layout.

### Company Window Or Location List Looks Wrong

Run `/fuse.operations`. Include screenshots of the Locations tab and the route map. Area ordering may depend on converted area order data and runtime base-game ordering.

### Progression Object Visible Too Early

Run `/fuse.progressions`. Include the affected package, progression id, map feature id, and screenshot of the object.

### Audio Missing Or Wrong

Check that the converted audio package is installed and that the source audio files were copied beside the generated `audio.fuse.json`.

## When To Reconvert

Reconvert packages when:

- The FUSE converter version changes.
- The schema version changes.
- A converter repair changed from warning-only to an actual runtime fix.
- A legacy package was updated or redownloaded.

## What Not To Do

- Do not load the legacy and FUSE versions of the same route for normal play.
- Do not delete warnings from converter reports just to get a clean report.
- Do not patch a missing asset to a different name if the correct asset pack exists.
- Do not treat unsupported three-way switches as a FUSE runtime bug.
