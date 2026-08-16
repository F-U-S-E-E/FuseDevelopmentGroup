# Changelog

## 1.0.0

First stable release. FUSE leaves the 0.x beta series; the package format and
runtime behavior described in `schemas/FUSE_JSON_SCHEMA.md` are now the
supported 1.0 baseline.

### Maps

- Added custom map packages. A FUSE package can now declare a replacement world,
  which appears in a dedicated New Game map dropdown; Railroader's existing
  control is retained separately for starting progression.
- Added base-world isolation so a launched FUSE map does not inherit the vanilla
  world's graph and scenery state.
- Added `/fuse.maps` to list registered map packages and the active session map,
  and `/fuse.map.launch <mapId> [railroadName] [reportingMark]` to start a
  sandbox session on a registered map from the main menu.
- Added `map` declaration validation and schema coverage for map packages.

### Editor

- Retired the embedded in-game runtime editor. `FUSE.Editor.dll` and
  `FUSE.Converter.dll` are no longer packaged in the mod zip, and the mod zip is
  now built from `FUSE/FUSE.csproj`. Authoring moves to the standalone external
  editor, which ships in its own `externaleditor-v*` release lane. FUSE still
  discovers and loads custom map packages at runtime.

### Licensing

- FUSE is now released under the GNU Affero General Public License v3.0. The
  full text is in `LICENSE`. Releases before 1.0.0 carried no license file.

## 2026-07-27 Performance, VRAM, and Load-Race Pass

This section covers the complete optimization cycle performed after the previous
changelog. The reference save is the 204-car E&A/Bryson install used throughout
the investigation.

### Measured Result

- Reduced the visible loading screen on the reference install from approximately
  21.9 seconds to a best validated run of 12.7 seconds.
- Reduced the FUSE map-load pipeline to 7.02 seconds in that run.
- Reduced the EFA progression apply phase from approximately 503 ms to 255 ms.
- Preserved all 204 saved cars, 960 final track segments, 154 switches, and 93
  bumpers.
- Preserved 69 loads, 50 industries, 117 industry components, 11 loaders, one
  station, three turntables, 12 progression sections, 12 map features, 13
  delivery phases, and 41 deliveries.
- Final validation reported 21 applied definitions with zero FUSE faults,
  conflicts, unknown/broken scenery assets, graph issues, progression transfer
  skips, scenery load failures, or orphaned cars.
- Removed two synchronous Unity crash-report paths. The final diagnostics-on
  pass had a 340 ms worst post-screen frame instead of the earlier 1,163-1,218
  ms exception/upload stalls.
- Fixed the teleport-only building stall where a structure could remain absent
  for five seconds or indefinitely until the camera moved. In the live EFA
  destination test, nearby structures appeared without a camera nudge; the
  first settled census had every requested FUSE model loaded (71/71), an empty
  scenery queue, and a bounded peak of eight in-flight tasks.
- The following deep-calm census reached 63.9 FPS with the scenery queue still
  empty and no additional frame spike. Load/save-swap work can still produce a
  large transition frame; the new path does not hide or mislabel that cost.

### Load Pipeline and Main-Thread Work

- Added package discovery, disk-load, apply-phase, graph-subscriber, map-load,
  scenery-wave, snapshot-rebuild, and settled-runtime timing so the load could be
  optimized from measured phases instead of total wall time alone.
- Reused resident definitions on map load instead of rereading package files.
- Suppressed per-object apply-report string construction unless verbose apply
  details are enabled; aggregate counts and warnings remain available.
- Skipped PrefabStore's startup-only `DefinitionChecker` logging pass after stores
  have already opened. Store discovery and real per-asset load validation remain
  enabled.
- Added a source-identifier index for asset-pack JSON so missing and
  case-normalized identifiers do not force every cold PrefabStore container to
  deserialize.
- Added positive and negative scenery-identifier caches, with invalidation when
  the mounted store population changes.
- Resolved known legacy scenery aliases before probing the old identifier.
- Added a narrow early-out for known optional legacy scenery references only
  after a complete source index proves that they are absent.
- Batched fallback progression scene-path resolution into one immutable
  Transform/path census per staged progression apply. Direct authored-object and
  normal scene lookups still run first. This cut the reference progression phase
  by roughly half.
- Moved Live Bridge heartbeat file writes off Unity's main thread. Live Bridge
  remains a development-only mod and is not shipped in the tester package.

### Scenery Streaming, Pop-In, and Load/Unload Races

- Integrated the standalone SceneryLoadRaceFix behavior directly into FUSE.
  A generation-owned `SetLoaded` replacement prevents a late async completion
  from resurrecting scenery that was unloaded while its bundle request was in
  flight.
- Added per-instance pending-load tokens so stale queued requests cannot start
  after an unload, replacement, map reset, or newer request supersedes them.
- Added a four-load concurrency gate around scenery AssetBundle work to stop the
  unbounded load race that exhausted 8 GB cards and spilled into shared RAM/page
  file.
- Added generation-safe gate leases so late task continuations cannot corrupt the
  active-load count after a map reset.
- Deferred asset-reference disposal until the frame after Unity destroys the old
  scenery model, avoiding unload-before-destroy races.
- Removed zero-reference completed asset requests from FUSE-managed stores on
  constrained-VRAM systems even when another asset keeps the same bundle open.
- Added a quiet-window unused-asset sweep for constrained systems. It runs only
  after enough zero-reference evictions or emergency texture pressure, never
  while scenery queues, loads, or activation waves are active, and no more than
  once every five minutes.
- Cached reflected scenery model/load state instead of repeatedly rediscovering
  private fields.
- Kept the gameplay camera as the hard activation anchor. FUSE pauses rather than
  activating scenery into the game's null-camera "nearest" band, which otherwise
  force-loads every model.
- Increased the initial activation-wave work budget and far-object drain cap so
  the full 2,584-object reference wave completes in three frames while the
  loading screen remains visible.
- Removed the old timeout bulk flush that could turn a slow scenery wave into one
  giant main-thread hitch.
- Kept nearest-first ordering and teleport re-sorting, while rapidly draining
  objects beyond the model-streaming distance where activation is bookkeeping
  only.
- Prioritized the 250 m immediate ring first, then scenery in front of the
  camera, then the remaining distance/FIFO order. A camera jump or meaningful
  queue growth re-sorts the pending tail.
- Fixed Railroader's missed world-shift culling update: `OnWorldDidMove`
  refreshed every sphere position but did not put the tokens back into
  `_needsUpdate`. FUSE now requests an exact distance-band/visibility
  reconciliation after world shifts and 100 m camera jumps. The 250 m
  destination ring is rechecked first; farther callbacks drain in bounded
  32-item batches with two follow-up passes to catch late registrations.
- Added a conservative pre-load structure classifier using placement/model
  identifiers, with an explicit vegetation/terrain veto. Nearby buildings use a
  separate four-start/four-in-flight destination lane while background scenery
  retains the existing four-task ceiling. Unknown assets stay on the normal
  lane, mask-bearing structures retain their existing immediate behavior, and
  all normal culling ranges remain unchanged.
- Held the enhanced loading screen until the first terrain/scenery activation
  batch drains, preventing the old multi-minute "settle" work from being hidden
  behind apparently unexplained gameplay hitches.
- Deferred destructive cleanup through the runtime pump so mass unload work does
  not collide with the same-frame load wave.
- Preserved normal scenery distances and did not ship shorter culling distances,
  building pop-in, missing-building shortcuts, or topology skips.

### Asset Compatibility and Toolshed

- Added a tolerant Unity `Vector2`, `Vector3`, and `Quaternion` JSON converter to
  the direct asset-container path. It accepts both Railroader's array form and
  object form (`x`, `y`, `z`, `w`) used by existing asset definitions.
- Fixed the Toolshed WoodShed direct-store fallback that reparsed the definition,
  dropped `ToolshedServiceLoadPoint`, and produced a repeatable approximately
  647 ms frame. The authored component is now preserved.
- Changed Toolshed's empty service-configuration search to run once per session
  instead of rescanning the entire Mods tree every two seconds forever.
- Preserved runtime-injected equipment definitions and the Equipment Purchase
  menu. Startup shortcuts that omitted equipment, broke the menu, or left track
  incomplete were rejected and are not included.
- Toolshed 0.3.0 is packaged as a separate mod folder so its existing settings
  remain independent of FUSE.

### VRAM Policy for 8 GB Cards

- Added automatic constrained texture-memory mode for GPUs reporting at or below
  9,216 MB of dedicated graphics memory, covering normal 8 GB reporting
  variance.
- The constrained policy applies one global mip limit and enables unused streamed
  mip discard. It does not shorten scenery distance or remove buildings.
- Added a manual `Force 8 GB VRAM Mode` setting strictly for reproducing the
  policy on larger cards.
- Verified the forced policy on the 16 GB development card at approximately
  3.29 GB physical dedicated VRAM with the complete 204-car world present.
- Restored the development install to automatic mode. A real 8 GB card enables
  the policy automatically; the 16 GB card does not.
- Restores prior texture settings on FUSE unload and does not overwrite a later
  in-game texture-quality change.

### Track, Snapshot, and Narrow Gauge

- Kept one merged package graph commit and moved curve invalidation to immediately
  after track-graph subscribers, so subscriber topology changes are consumed by
  the same rebuild.
- Added a collections-only graph refresh for span-only changes that do not alter
  rail geometry.
- Reduced unnecessary track-removal snapshot work and coalesced cache rebuild
  requests.
- Coalesced Railroader's next-frame snapshot track rebuild into the successful
  snapshot transaction. The stock coroutine is cancelled only after the
  equivalent immediate rebuild succeeds; failure leaves the stock retry intact.
- Removed the repeatable approximately one-second collision between the delayed
  snapshot rebuild and queued car/scenery completions. The validated immediate
  rebuild is approximately 385-400 ms with the final 960/154/93 track state
  unchanged.
- Coalesced nested snapshot rebind requests into one read-only rebind and avoided
  package reloads, scenery recreation, graph mutation, and
  `SceneryAssetInstance.ReloadComponents` during snapshot restoration.
- Replaced Railroader's unbounded `TrackRebuilder.WorkBuildQueue` loop. The stock
  loop compares against `Time.time`, which is constant for the whole frame and
  therefore drains a teleport-sized build wave at once. FUSE now uses real
  elapsed-time and item budgets, builds visible/nearest track first, drops stale
  pre-teleport work, and bounds the paired destroy wave.
- Added the optional Narrow Gauge 0.4.x performance compatibility layer without
  modifying or taking ownership of the Narrow Gauge project.
- Increased Narrow Gauge special-work rail sampling from 0.2 m to 4.0 m only
  when the exact expected method and constants are present. Unknown versions
  fail open to Narrow Gauge's original behavior.
- Replaced the Narrow Gauge curve-overlap LINQ hot path with an allocation-free
  equivalent that preserves its 0.1 m sample grid and tolerance.
- Filtered Narrow Gauge's per-item information log storm while retaining
  warnings, errors, validation results, lifecycle messages, and aggregate timing.
- Kept all 13 special-work plans valid. No track, switch, bumper, or rendered
  scenery was removed to obtain the speedup.

### Runtime Stutter and Hot Paths

- Replaced the stock car material uniqueness LINQ/repeated-search path with
  single-pass renderer/material snapshots and a source-to-clone dictionary while
  preserving per-car material ownership and `MaterialMap` behavior.
- Compacted car renderer arrays in place instead of allocating
  `Where().ToArray()` results for every body and truck model.
- Replaced the stock `CarCuller` pending loop and car model-completion burst with
  bounded, visible/nearest-first queues. Equipment starts and completions yield
  while destination scenery is still streaming so cars cannot steal the same
  frame from nearby buildings.
- Released completed equipment asset references abandoned by an unload and
  cleaned up runtime aggregate cargo materials with their owning model.
- Replaced `CullingManager.Update`'s live `HashSet` enumeration with a reusable
  snapshot drain. Handler-driven registration changes are deferred to the next
  frame instead of throwing `Collection was modified` and invoking Unity's
  synchronous crash reporter.
- Avoided writing the same turntable bridge rotation every frame.
- Replaced hot industry-cache LINQ checks with short-circuit loops.
- Increased decoupled terrain-mask watcher staggering from 16 to 64 buckets while
  retaining the same half-second visibility-check cadence.
- Coalesced repeated map-mask terrain refresh requests.
- Added load queue, in-flight load, stale-completion, model-residency, texture
  memory, process memory, car-count, and staged runtime-census diagnostics.
  Expensive diagnostics are opt-in and are disabled in the production package.
- Removed all temporary per-car, track-stage, and world-streamer profilers after
  the bottlenecks were identified.

### Separate Lego Library Performance Patch 0.4.0

- Kept the Lego-specific fix outside FUSE so it can be removed independently when
  Lego ships an upstream fix.
- Retained the existing Lego library log-storm suppression without disabling
  definition edits, clones, component groups, or exception reporting.
- Replaced Logos & Decorations' redundant
  `Image.FromFile -> PNG MemoryStream -> Unity decode` path with direct PNG byte
  loading into the same Unity texture format, mip setting, IDs, and static cache.
- Failures fall back to the original Lego method and log once per image ID.
- Limited Logos & Decorations prefab-model configure starts to four per frame,
  preventing a complex locomotive from launching dozens of bundle requests from
  one equipment-completion frame.
- Added controller-lifetime cleanup for cancellation tokens, retained asset
  references, and cloned Builtin car-shader materials.
- Guarded `LegosDecalHelper.OnEnable` when a decal belongs to building scenery
  and has no parent car. The building decal remains visible; only its invalid
  car-culling registration is skipped.
- Converted uncaught optional prefab failures from Lego's `async void`
  `Configure` path into one warning and a skipped decoration, preventing missing
  handrail assets from invoking Unity's crash uploader.
- Reduced the decorated E&A 85T tender's measured model completion from roughly
  455 ms to approximately 193-214 ms; other decorated cars also improved.

### Validation and Packaging

- Added unit coverage for constrained-VRAM decisions, unused-asset sweep
  thresholds, zero-reference eviction, source-identifier indexing, scenery
  concurrency leases, stale load tokens, progression paths, Narrow Gauge
  compatibility, and locked progression gates.
- Added Unity reflection-surface checks for private Railroader methods and fields
  used by fail-open patches.
- Passed 1,678 FUSE net48 tests, 63 FUSE.Core tests, 10 live-harness tests, and
  96 external-editor UI tests in the final production source state.
- The tester package contains separate `FUSE`, `Toolshed-v0.3.0`, and
  `LegosLibraryPerformancePatch` folders.
- The tester package intentionally excludes FUSE Live Bridge, FUSE Test Bridge,
  and the obsolete standalone SceneryLoadRaceFix mod.

### Investigated but Not Shipped

- Rejected a per-wave progression locked-object lookup because an A/B load showed
  no measurable scenery-wave improvement.
- Rejected lowering scenery concurrency from four to two because it would extend
  visible settling and increase pop-in risk.
- Rejected track rebuild skips, broader graph approximations, and an experimental
  quadratic bookkeeping rewrite because they regressed load time or risked broken
  topology.
- Rejected permanent texture limits on the 16 GB development card; automatic GPU
  detection remains the production behavior.
- Rejected RailLoader-style global building residency. The reference save has
  2,583 FUSE scenery instances but only 357 in the normal local working set;
  pinning all of them would multiply live scenery by roughly 7.2 and work
  directly against the 8 GB GPU target.

## Unreleased Beta

### Runtime

- Added FUSE-specific logging in `FUSE.log` beside `Player.log`.
- Added a compact public load report toast with full-word counters for faults, conflicts, assets, graph issues, transfers, and suppressions.
- Added startup version reporting for FUSE, schema, converter, Railroader, Unity, and build configuration.
- Split discovery, disk loading, and runtime apply so map reload and reapply can use resident definitions without needless disk reloads.
- Added package fault isolation and final package summaries.
- Added runtime graph and original graph dump commands.
- Added scene clone sanitizer behavior so collider-only meshes are not rendered over cloned buildings.
- Added direct asset pack discovery without mirroring asset packs into LocalLow by default.
- Inferred missing no-type legacy industry component patches from formula input/output terms so load-specific track patches materialize as real loaders or unloaders.
- Added clearly marked temporary legacy support for `container:<id>` mixinto fragments and old `zsc://...` asset-pack references, allowing legacy car/load-model patches to bind to FUSE direct asset stores.
- Applied legacy `$find`, `$replace`, `$add`, and `$remove` directives inside temporary `container:<id>` compatibility patches before passing cloned car definitions to the base deserializer.
- Added a robust aggregate material lookup path so exact installed material definitions such as `aggregateModelLoadId=gondola-woodchips` remain visible when custom asset packs are mounted.
- Added a hover-to-inspect scenery debug overlay (`FuseSettings.ShowSceneryDebugOverlay` master, `FuseSettings.ShowSceneryDebugAdvanced` sub-setting). Tooltip identifies the hovered scenery as FUSE scenery, scene clone, or vanilla — and reports owning package, asset identifier, scene path, and any suppressing packages so authoring conflicts on world buildings can be diagnosed without dumping the runtime graph.
- Both the scenery and track debug overlays now include a "Progressions impacting" block that lists every map feature and progression section whose unlock effects reference the hovered scenery game object or track group, so progression-gated buildings and track sections can be traced from the cursor.
- Routed legacy `industries: { id: null }` directives to a dedicated `operations.removals.industries` array in the converter, and added a staged `apply-operations-removals` phase plus `IndustryAPI.TryRemoveIndustry` so the vanilla industry is actually destroyed instead of left ticking with broken component references. Fixes the destroyed-GameObject `NullReferenceException` thrown from `Industry.Tick` and the matching authoring conflicts (e.g. AspenCrazyMap removing `whittier-stenzel` while its mandela disable of `Stenzel Mfg` previously failed to take effect).
- Added a per-car "Visual Condition" slider to the car inspector's Equipment tab so a car's weathering can be adjusted without touching its repair state. Purely cosmetic, persists in saves, replicates in multiplayer, and honors visual-condition values carried over from Strange Customs era saves. Returning the slider to 100% removes the override, and players without permission to change a car see its value read-only. By default the slider only makes cars look more worn than their mechanical condition; the `FuseSettings.DecoupleVisualConditionLimits` toggle (FUSE settings → General → Visual Condition) lets worn cars look fresh too and repaints overridden cars immediately when flipped.

### Converter

- Added drag/drop style folder conversion support through the FUSE converter tools.
- Preserved legacy source-file concerns instead of merging unrelated files into large generated outputs.
- Preserved no-type legacy industry component list patches as partial component patches so existing drop-off spans are not replaced.
- Named materialized legacy interchange aliases from overlapping interchange components so raw sub-ids such as `t1` do not surface as destination names.
- Added structured conversion reports with repaired, preserved, unresolved, unsupported, and dependency-required entries.
- Added source-file reporting for passenger stop and span warnings.
- Added route, map tile, asset pack, and audio pack conversion coverage for the current beta corpus.

### Schema

- Removed the non-negative floor on monetary fields so packages can express negative-cost (rebate/subsidy/penalty) values: `progression.sections[].deliveryPhases[].cost`, `operations.loads[].payPerQuantity`, `operations.loads[].costPerUnit`, and industry-component `costPerUnit`.
- Added `schemaVersion` handling and migration notes.
- Added mixinto metadata support.
- Added audio definitions for whistles, horns, and bells.
- Added telegraph pole movements.
- Added spawn points.
- Added span-anchored scenery.
- Added progression sections, delivery phases, unlock feature lists, area unlocks, game object unlocks, and track group unlocks.
- Added custom industry component support through fully-qualified component types and reflection-bound `fields`.

### Breaking / Compatibility Notes

- `RAIL` naming has been superseded by `FUSE`. Reconvert packages for clean public beta testing.
- Converter output should be regenerated with the matching converter version when schema/runtime behavior changes.
