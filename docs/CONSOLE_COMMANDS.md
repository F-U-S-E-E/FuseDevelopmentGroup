# Console Command Reference

FUSE registers its commands with Railroader's in-game console. Open the console,
type the command, and press Enter. Every command returns text in the console; the
`dump*` family also writes a file to the main Railroader folder.

All commands are safe to run while a map is loaded **except** the two experimental
recovery commands at the end of this page.

## Quick Index

| Command | Purpose |
| --- | --- |
| [`/fuse.report`](#fusereport) | Last map-load report (human or JSON) |
| [`/fuse.loaded`](#fuseloaded) | Loaded packages and applied/faulted state |
| [`/fuse.update`](#fuseupdate) | Version/update status and re-check |
| [`/fuse.conflicts`](#fuseconflicts) | Recorded ownership collisions |
| [`/fuse.validate`](#fusevalidate) | Re-run the validator for one package |
| [`/fuse.graph`](#fusegraph) | Track graph summary |
| [`/fuse.groups`](#fusegroups) | Runtime track groups |
| [`/fuse.segments.audit`](#fusesegmentsaudit) | Segment renderer/visibility audit |
| [`/fuse.track.rebuild`](#fusetrackrebuild) | Force a full track geometry rebuild |
| [`/fuse.operations`](#fuseoperations) | Loads, industries, loaders, stations, turntables |
| [`/fuse.progressions`](#fuseprogressions) | Progression sections, features, delivery phases |
| [`/fuse.assets`](#fuseassets) | Discovered asset pack folders |
| [`/fuse.suppressions`](#fusesuppressions) | Active world suppressions |
| [`/fuse.patches`](#fusepatches) | Harmony patches applied or skipped |
| [`/fuse.dumpgraph`](#fusedumpgraph) | Write the captured original graph |
| [`/fuse.dumpruntimegraph`](#fusedumpruntimegraph) | Write the active post-FUSE graph |
| [`/fuse.dumpmandelas`](#fusedumpmandelas) | Write scene clone and world path data |
| [`/fuse.dumpprogression`](#fusedumpprogression) | Write the live progression graph |
| [`/fuse.reapply`](#fusereapply-experimental) | **Experimental** — re-apply resident definitions |
| [`/fuse.restore`](#fuserestore-experimental) | **Experimental** — reload from disk and reapply |

## Status And Packages

### `/fuse.report`

Shows the last FUSE map-load report — the same summary the startup toast
abbreviates.

```
/fuse.report
```

Pass `json` for machine-readable output, which is the better form to paste into a
bug report because it does not wrap or truncate:

```
/fuse.report json
```

### `/fuse.loaded`

Lists every loaded FUSE package with its applied/faulted state. This is the first
command to run when a package's content is missing from the world — a package
that faulted during apply still appears here, with the fault recorded.

A faulted package does not stop unrelated packages from loading.

### `/fuse.update`

Reports the running FUSE version, the detected install source (GitHub or Nexus),
and whether a newer stable release is available, then kicks a fresh check against
GitHub. The result also lands in `FUSE.log` and on the FUSE window's Status page.

```
/fuse.update
```

A newer *stable* release shows a download link; release candidates are ignored,
and a local or development build (version `0.0.0`) reports that the check does not
run for it. The `EnableUpdateCheck` setting only governs the automatic startup
check; this command always checks on demand — see
[SETTINGS.md](SETTINGS.md#enableupdatecheck).

### `/fuse.conflicts`

Lists registry conflicts — cases where two packages claimed ownership of the same
object id. An empty list is the healthy result.

Conflicts are the expected outcome of loading a legacy route and its converted
FUSE equivalent at the same time. See
[MIGRATION_FROM_LEGACY.md](MIGRATION_FROM_LEGACY.md).

### `/fuse.validate`

Re-runs the FUSE validator against one already-loaded package and prints errors
and warnings with the offending field and error code.

```
/fuse.validate <modId>
```

The `<modId>` argument is required; without it the command prints its usage line.
If the id is not loaded, the command says so rather than failing silently — check
the spelling against `/fuse.loaded`.

Each line is formatted as `[error]` or `[warn ]` followed by the field, message,
and code.

## Track Graph

### `/fuse.graph`

Summarizes the active Railroader graph alongside FUSE's own track definitions.
Use it to confirm FUSE applied the node/segment counts you expect.

### `/fuse.groups`

Lists the runtime track groups discovered on the active graph. Group ids (`s2`,
for example) are the filter values `/fuse.segments.audit` accepts.

### `/fuse.segments.audit`

Audits FUSE-tracked segments for visual renderer state — the command to reach for
when track exists in the graph but nothing is drawn on screen. It reports, per
group, how many segments have no renderer, have renderers but none enabled, or are
inactive, and flags which segments FUSE claims versus vanilla.

```
/fuse.segments.audit
/fuse.segments.audit s2
/fuse.segments.audit s2 verbose
```

The first non-flag argument filters by group id. Adding `verbose` (or `-v` /
`--verbose`) emits per-segment lines instead of group summaries — expect long
output on a full route.

Requires a populated graph; before a map finishes loading it reports that and
stops.

### `/fuse.track.rebuild`

Invalidates every segment's cached bezier curve and forces a full
`TrackObjectManager` rebuild.

This is the recovery for the "switch tracks do not intersect" geometry failure,
where stale cached curves no longer reflect post-migration node positions and
rotations. Invalidating the curves forces the next access to recompute against the
current node transforms.

The command reports how many curves it invalidated and the segment count before
and after. If previously-invisible switches and their connected segments render
afterward, stale curve caches were the cause.

## Operations, Progression, And World

### `/fuse.operations`

Summarizes loads, industries, industry components, loaders, stations, and
turntables. Run this for a wrong-looking company window, Locations tab, or
industry.

### `/fuse.progressions`

Summarizes progression sections, map features, and delivery phases. Run this when
an object is visible earlier than its progression should allow.

### `/fuse.assets`

Lists the FUSE asset pack folders discovered for direct `PrefabStore` loading. If
scenery is missing, confirm here that the required pack was actually found before
assuming the package is at fault.

### `/fuse.suppressions`

Lists active FUSE world suppressions — base-game world objects a package asked
FUSE to hide.

### `/fuse.patches`

Lists the Harmony patch classes FUSE applied or skipped, with a failure reason for
each skipped patch. Worth including in any bug report that involves another mod,
since a skipped patch usually means a conflict with something else in the load
order.

## Dump Commands

Each writes a JSON file to the main Railroader folder. These files are large;
attach them to bug reports rather than pasting their contents.

### `/fuse.dumpgraph`

Writes FUSE's captured *original* Railroader track graph to
`FUSE-original-graph.json` — the graph as it existed before FUSE applied anything.

### `/fuse.dumpruntimegraph`

Writes the active *post-FUSE* track graph to `FUSE-runtime-graph.json`.

Diffing this against `FUSE-original-graph.json` is the standard way to see exactly
what a package changed about the track.

### `/fuse.dumpmandelas`

Writes loaded scene-clone definitions and the current World scene paths to
`FUSE-mandelas.json`. Use it when a scene clone resolves to the wrong building —
compare the recorded source path against the base game scene path.

### `/fuse.dumpprogression`

Writes the live progression graph to `FUSE-progression.json`, covering features,
sections, track-group ownership, areas, industries, and passenger stops.

## Experimental Commands

These two exist for testing and recovery. They are not part of normal play, and
each logs a warning the first time it is used in a session.

Both refuse to run while a map is loaded unless you pass `--force`. That guard is
deliberate: re-applying definitions mid-session can destabilize a running save.
Back up the save before overriding it.

### `/fuse.reapply` (experimental)

Rebuilds FUSE's caches and re-applies the definitions already resident in memory.
It does not re-read anything from disk. Reports how many definitions it applied.

```
/fuse.reapply
/fuse.reapply --force
```

### `/fuse.restore` (experimental)

The heavier operation: unloads every package, clears all caches, reloads packages
from disk, rebuilds caches, and re-applies. Use it after editing package files on
disk and wanting them picked up without a game restart.

Reports both how many packages loaded from disk and how many applied to the
runtime.

```
/fuse.restore
/fuse.restore --force
```

Restarting the game is the safer path in every case where you are not specifically
testing reload behavior.

## Related

- [TROUBLESHOOTING.md](TROUBLESHOOTING.md) — which command to run for which symptom
- [SETTINGS.md](SETTINGS.md) — the debug overlays that complement these commands
- [PACKAGE_AUTHOR_GUIDE.md](PACKAGE_AUTHOR_GUIDE.md) — using these while authoring
