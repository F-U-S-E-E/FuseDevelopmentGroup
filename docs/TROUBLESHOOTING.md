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
- `C:\Users\<username>\AppData\LocalLow\Giraffe Lab LLC\Railroader\FUSE.log`
- `C:\Users\<username>\AppData\LocalLow\Giraffe Lab LLC\Railroader\Player.log`
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

### Faulted Package

Run `/fuse.loaded` and `/fuse.report`. Check `FUSE.log` for the package id and phase. Package failures should not prevent unrelated packages from loading.

### Unknown Scenery Asset

Run `/fuse.assets` and verify the required asset pack is installed. Do not replace the missing asset with a guessed alias unless the legacy source actually used the alias.

### Missing Or Wrong Building

Check whether it is a regular asset pack item or a scene clone. For scene clones, run `/fuse.dumpmandelas` and compare the source path with the base game scene path.

### Bad Track, Missing Span, Or Broken Segment

Run `/fuse.dumpgraph` and `/fuse.dumpruntimegraph`. The files are written to the main Railroader folder as `FUSE-original-graph.json` and `FUSE-runtime-graph.json`.

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
