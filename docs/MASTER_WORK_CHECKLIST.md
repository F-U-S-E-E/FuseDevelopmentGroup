# FUSE ecosystem master work checklist

This is the durable intake list for the FUSE, Tile Editor, Toolshed, and Narrow
Gauge workstreams. It records reports and requested outcomes; an item being
listed does not mean the report has been confirmed as a FUSE bug.

Status key: **Open**, **Investigating**, **Implemented**, **Needs in-game test**,
**External/content issue**, **Complete**.

## Current automated release gate (2026-08-20)

- FUSE net48 suite: **2,269 passed, 9 opt-in real-pack fixtures skipped, 0 failed**.
- FUSE Core/schema/help suite: **126 passed, 0 failed**.
- External editor UI suite: **96 passed, 0 failed**.
- Tile Editor desktop suite: **128 passed, 0 failed**.
- Installer/tools suite: **88 passed, 0 failed**.
- FUSE editor bridge, Toolshed, Narrow Gauge, and Railroad Operations validation
  harness all build/validate successfully; companion-mod builds were run with
  deployment disabled.
- Release ZIP validation passed. The bundled `FUSE-Installer.exe` and
  `FUSE-Converter.exe` build and pass their smoke checks; the installer also
  completes a throwaway self-install with the DLL-free AssetLoader dependency
  alias and no obsolete AssetLoader runtime.
- Slopwatch: **0 findings**. Most remaining unchecked acceptance boxes require a
  fresh Railroader session or external wiki/release publication. The parity
  matrix separately identifies the hosted/version-bound compatibility cases
  that still need broader fixtures; neither category is represented as
  automated proof.

The detailed legacy behavior release gate lives in
[`LEGACY_REPLACEMENT_PARITY.md`](LEGACY_REPLACEMENT_PARITY.md). A legacy package
is not considered replaced just because its dependency is satisfied or its DLL
is suppressed.

## Non-negotiable compatibility rules

- [x] Keep native FUSE schema canonical and regression-tested.
- [x] Parse legacy formats only at an isolated compatibility boundary and adapt
  them into FUSE-owned models/runtime behavior.
- [x] Do not copy implementation code from decompiled projects. Use observable
  contracts, identifiers, file formats, and behavior only.
- [x] Do not claim replacement parity until automated and in-game fixtures pass.
- [x] A bad package must fail locally, identify the package/folder/file/location,
  explain the JSON/schema/dependency problem, and leave game menus usable.
- [x] Separate genuine package/load failures from ordinary Unity/runtime
  diagnostics so normal exceptions do not alarm users.

## Performance and main-thread acceptance targets

These targets apply to FUSE, Tile Editor, and Toolshed. Narrow Gauge is outside
this performance pass. Measurements must include a large real-world mod set and
a representative low-end system; a fast developer PC is not sufficient proof.

Clean-profile baseline captured 2026-08-20 on the deployed review FUSE build:
0 FUSE data packages, 82 equipment stores prepared in 227.3 ms, worst store
10.3 ms, no store over the 25 ms slow threshold, and one 397 ms startup frame
whose measured FUSE-pump portion was 1.4 ms. This proves the instrumentation and
base-game path, but does not replace the required large-mod-set/low-end run.

- [ ] Attribute recurring 2–4 second game-tick stalls to a named FUSE,
  companion-mod, game, or third-party phase. No FUSE-owned periodic callback may
  perform an unbounded scene scan, package scan, file read, JSON parse, report
  rebuild, or asset-store traversal on Unity's main thread.
  FUSE frame-spike logs now include the slowest measured runtime-pump phase;
  Toolshed's exact two-second facility/interchange scans were made
  change-driven, lookup-cached, and exponentially backed off. A fresh field
  capture is still required to attribute any stall that remains.
- [ ] Move thread-safe file I/O, JSON/schema parsing, dependency indexing, IPC
  decoding, report preparation, and pure calculations off the Unity main
  thread where ordering permits. Marshal only the final Unity/game-database
  mutation back to the main thread.
- [ ] Keep Unity objects, scene hierarchy access, game databases, Harmony
  mutation, and AssetBundle/renderer operations on the main thread, but make
  them change-driven, cached, nearest-first where visual priority matters, and
  bounded by a measured per-frame budget.
  Current implementation keeps Unity access on the main thread, shares lazy
  scene indexes across unresolved Toolshed definitions, drains waybill repair
  in a two-car-per-frame queue, and centralizes Tile Editor grade-label camera
  work at 20 Hz.
- [ ] Eliminate the cold buy-menu freeze with the full equipment corpus while
  preserving Lego definition edits, locomotive/railcar availability, and
  customization behavior. Record total warm-up work, worst store, slow-store
  count, and click-to-open latency.
  The warm-up now skips Lego's expensive container postfix for stores that
  cannot contain a Lego-authored identifier and logs slow-store count, worst
  store, and total work. Full-corpus click timing remains an in-game gate.
- [ ] Improve package discovery, definition load, map apply, track rebuild, and
  editor save/reload times without changing native FUSE schema behavior or
  legacy load order.
- [ ] Prevent FUSE scenery from visibly appearing near or in front of the active
  camera after player control begins. Loading-screen release and streaming
  budgets must prioritize camera-near content without creating a single-frame
  activation spike.
- [ ] Tile Editor overlays, previews, IPC, caches, vegetation/terrain workflows,
  and save monitoring must remain responsive on a whole-map project and must
  not poll or rebuild unchanged state every frame.
  Grade-label billboarding is centralized, heartbeat disk writes are coalesced
  off-thread, and the desktop whole-map renderer idles at 15 Hz (5 Hz minimized)
  while immediately returning to 60 Hz for active editing and generation.
- [ ] Toolshed facilities, storage/particle animations, turntables, and rolling-
  stock helpers must avoid duplicate Update/LateUpdate work and unchanged
  renderer/GameObject writes.
  Link-and-pin visuals now refresh at 10 Hz and only write changed state;
  service animations cache normalized storage/transform state; startup loader,
  facility, and selective-interchange discovery reuse bounded scene scans.
- [ ] Capture before/after frame-time, main-thread time, load time, worst spike,
  queue depth, and memory/GC evidence in the in-game performance report. A code
  review or passing unit suite alone does not close this gate.
  The reproducible team procedure and required evidence bundle are published in
  [`PERFORMANCE_TESTING.md`](PERFORMANCE_TESTING.md).

## Live GitHub issue intake (2026-08-19)

- [ ] #249 Equipment roster will not open in single- or multiplayer — report is
  against public 1.0.4 and contains no usable `/fuse.report` or downloadable log
  link in the issue body. Retest the current review build after catalog warm-up;
  capture the click-time exception and package set before attributing it to FUSE.
- [x] #240 Convertor error — converter fix and regression fixture implemented;
  issue remains open pending release/user confirmation.
- [x] #239 Whittier Sawmill Trackmod Issue (ARC Whittier) — span rebinding,
  array-wrapped additions, and industry-only area-wrapper fixes implemented;
  needs in-game confirmation with ARC Whittier.
- [x] #238 Not able to customize locomotives — AssetLoader replacement and
  malformed-store containment implemented. The supplied log also identified a
  partially-bound Alina Utilities singleton breaking map/camera updates; FUSE
  now reconnects that instance to its working UMM settings without replacing
  the mod. Needs the restarted in-game equipment/camera corpus.
- [x] #236 Multiple unloading areas — real DW5 and Sylva patch shapes covered by
  converter tests; needs in-game delivery/EOD confirmation.
- [x] #235 Stryker's Bryson Turntable is yellow/other issues — the supplied log
  confirms the actual turntable was cloned, sanitized, and applied; the
  editor-only yellow measurement plate is now filtered. ALW CabooseHouse is a
  confirmed bad external asset bundle entry. Visual in-game confirmation remains.
- [ ] #221 FPS drop — logs do not contain a captured frame profile and frame-spike
  diagnostics were disabled, so a sustained-FPS root cause is not proven. Two
  always-on costs were removed (legacy update callback reflection/snapshots and
  zero-work Unity texture-memory polling); needs an in-game before/after capture.
- [x] #220 Legacy in-game road is not altered — in-place base-road patch and
  converter fixture implemented; needs in-game confirmation.
- [x] #210 Whittier Industries assets load but track/location tags do not —
  root-level spans and industry namespace wrappers fixed in tests; needs
  in-game confirmation.
- [x] #206 Issues With Mods — supplied logs show 36 packages/56 definitions
  applied with zero FUSE faults. Optional mixins were correctly skipped; BRSS
  threw its own exception, while malformed k50parts/ALW catalogs and missing
  bundle entries are content faults. Current guarded asset resolution and
  quarantine paths need in-game confirmation with the same corpus.
- [x] #198 RMROC451's Tweaks and Things — the real RMROC451 archive now passes
  the opt-in assembly-load, Interchange-shim, and Harmony-target compatibility
  fixture against the installed game; needs in-game behavior confirmation.
- [x] #186 Cross-platform ZIP path separators — archive builder and package
  validation gate implemented; issue remains open pending release confirmation.
- [ ] #21 Custom CTC Signaling — portable signal/CTC runtime, editor authoring,
  authoritative schemas, automatic runtime dependency declaration, and
  cross-file pre-publish validation are implemented. Native FUSE ownership and
  an end-to-end in-game territory acceptance run remain open.
- [ ] Recheck recently closed reports #232, #230, #224, #223, #222, #219,
  #208, #207, #202, #201, #199, and #196 against the regression suite rather
  than assuming closure proves the current build.

## FUSE runtime and compatibility reports

- [x] Complete AssetLoader replacement implementation: catalog/bundle stores,
  catalog-plus-definitions stores without a local bundle, and legacy
  definitions-only store overrides (including tender/rolling-stock swaps).
- [x] AssetLoader-dependent packages have an explicit migration/dependency
  path when the old DLL is absent; merely disabling the old Harmony patches
  while retaining its assembly is not full replacement. The installer writes a
  data-only `AssetLoader` UMM alias that requires FUSE.
- [x] Update the installer to
  detect old `AssetLoader` folders, ZIPs, DLLs, and active patches; present a
  clear migration result; and verify the old runtime is no longer installed.
  Removal/quarantine must be scoped and recoverable, and legacy UMM dependency
  declarations must remain satisfiable through a FUSE-owned compatibility path.
  (Offline files are handled by the installer; any still-loaded Harmony owner is
  handled by FUSE's runtime replacement guard.)
- [ ] Verify locomotives, rolling stock, tender swaps, native customization
  controls, Legos library clones, save-car restoration, and the buy menu both
  with and without the old AssetLoader installed.
- [ ] Company window/menu lockout: reproduce with malformed and conflicting
  packages; verify package-local containment and usable buy/company menus.
- [x] Buy menu freezes for roughly seven seconds: cold prefab stores now warm
  incrementally and the filtered equipment catalog is cached. Large real-world
  equipment sets still need an in-game timing comparison.
- [x] Converter failures: it only converts supported RailLoader JSON,
  rejects code/native/asset-only/map-tile packages with the correct installer
  guidance, and provides file/line/path/actionable diagnostics. The C# and
  Python entry points share this policy; failed reports are written outside the
  proposed `.FUSE` package so a report-only directory cannot be installed by
  mistake. A real EFA corpus pass converted 38 JSON packages, produced 93
  explicit unsupported/failure reports, wrote every report, and produced zero
  successful output folders without a native fragment.
- [ ] Conditional `mixInto` patches and `LoadAfter` dependencies (ARC Sylva
  Terminal and related OttoSeer packages): object-form ids, hard-requirement
  eligibility, conditional fragment requirements, optional ordering edges,
  alias matching, and topological ordering are implemented and
  regression-tested in the runtime and both converters. Hosted legacy code
  plugins now use the same requires/loadAfter/loadBefore topological order
  instead of Mods-folder enumeration order, with deterministic cycle fallback.
  Declared
  base/extension layering is excluded from Mod Conflicts. Needs an in-game pass
  with ARC/OttoSeer/Katers packages before acceptance.
- [ ] Base spans edited by legacy mods must remain renderable/owned correctly;
  recover missing Sylva interchange tracks including
  `SInterchange_Track_4` through `_9`.
- [x] Diagnostics no longer flag unowned base-game placeholder/progression spans
  as invalid mod content. Audit reads preserve authored FUSE span endpoints when
  Railroader temporarily cannot resolve a runtime location, and report a true
  orphan only when the referenced segment is actually gone. Componentless base,
  passenger/scenery-only, fictional, and disabled location containers are not
  treated as broken industries; malformed FUSE-owned spans remain actionable.
- [x] Legacy base-road point replacements preserve and update the scene road in
  place; broader road/river handler coverage remains in the parity matrix.
- [ ] DKW nodes/segments that are valid must render; authoring geometry that is
  too tight must produce an actionable warning rather than a false runtime bug.
- [x] Preserve the flags-era graph fields found in the 2026.1 base-game audit:
  diamond nodes, steel bridge supports, and independent yard designation.
  Native schema/runtime conversion, partial-patch merging, removal snapshots,
  desktop editor round-trip, and schema tests are implemented without changing
  the released four-way `style` contract. A flags-era game build still requires
  its own compile/live acceptance run.
- [ ] Bjorn/ARC Whittier Sawmill track grouping: S01 cars must not resolve onto
  S03 tracks; verify 1.0.3/1.0.4 behavior difference.
- [ ] Sylva Industries Boosted: R2/R3 highlighting, car recognition, delivery
  greying, and payment must all agree with the configured spans.
- [ ] Kater interchange packages: preserve/merge referenced base-game spans.
- [x] East Whittier overlapping/stranded track diagnosis: the supplied profile
  simultaneously applied East Whittier Crossover, East Whittier Yard Revamp,
  and AMW East Whittier. These are independent track layouts, not one malformed
  switch. FUSE now reports cross-package same-ID replacements and separately
  shows conservative spatial-layout warnings without auto-disabling either mod.
  The older "PAC-MAN" gap still needs a one-layout-at-a-time in-game retest.
- [x] Missing-mod visibility: the Mods page now groups and labels disabled,
  skipped, dependency-missing, invalid, incompatible, partially applied,
  loaded-but-awaiting-map, and successfully applied packages. Optional mixinto
  omissions remain a successful apply with an explanatory note rather than a
  whole-package failure, and the exact state is included by **Copy Mod Info**.
- [ ] Legacy conflicts: preserve `ConflictsWith`, plus report actual runtime
  ownership collisions without treating intentional same-package aliases or
  declared `requires`/`loadAfter` extension layers as user-facing failures.
  Ownership/layer classification is implemented. Top-level and conditional
  `ConflictsWith` ids/version bounds are now preserved by both converters and
  runtime conversion; matching top-level declarations skip the declaring
  package, conditional declarations skip only their fragment, and Tools > Mod
  Conflicts labels them as author-declared incompatibilities. Top-level checks
  now include enabled code-only and asset-only mod manifests plus FUSE runtime
  replacements, while ignoring disabled/profile-excluded packages. Successful
  shared industry merges (including the Andrews Coal Power + Tuckasegee Steel
  Works Kirkland targets from the 2026-08-20 run) are now informational
  extension layers and do not inflate conflicts/load health. The incompatible-
  mod in-game corpus remains open.
- [ ] Test deliberately incompatible track mods that move/delete/claim the same
  nodes, spans, industries, and scenery; conflict handling must remain
  deterministic and menus must remain usable. The 2026-08-20 mixed-pack run
  kept the game/report UI alive with 114 resident definitions and correctly
  attributed the Bryson delete-vs-definition damage to
  `jclass.Collie Bryson Removal` versus `StrykerBryson` (1 contained package
  fault, 11 post-bind graph issues). Seven absent-companion mixintos remained
  optional inactive fragments. Retest the revised status/conflict counts and
  the affected in-game menus before closing this stress item.
- [x] Add **Tools > Mod Conflicts**, grouped by package pair, with object kinds,
  exact identifiers, resolution behavior, spatial-advisory separation, refresh,
  and a complete clipboard report. Definition-versus-definition replacement is
  now recorded in addition to delete-versus-definition conflicts.
- [x] Asset duplicate reports collapse keys by winner/overridden source set,
  distinguish identical copies from different definitions, identify the winner,
  show example keys and impact, and retain every key in the exported report.
- [ ] Strange Customs asset/audio/animation behavior and third-party rolling
  stock visuals (including Freeman/Salty reports) need package-specific fixtures.
- [ ] CLB, LEGO, Joo200, Signals Everywhere, and Alina Utilities must load and
  operate correctly. A recovered Alina Utilities entry no longer also produces
  a false legacy-host mod error, and a partially-bound legacy singleton is
  repaired from the mod's UMM settings so its map-distance/camera patches do not
  throw every frame. Actual in-game behavior still needs the restarted corpus
  pass. Griz and Competition are explicitly not replacement gates.
- [ ] Appy Railway starter equipment pool applies only when its package/map
  requests it; do not affect unrelated new saves. Definition/setup scoping and
  regression tests are implemented; the exact installed-game setup coroutine
  contract has been rechecked with ILSpy. Needs one unrelated-new-save in-game
  acceptance pass.
- [ ] Appy starter placement must survive the tutorial and Escape key without
  consuming/loss of unplaced equipment. Presentation waits for the tutorial,
  cancellation retains the queued cut, and the queue now consumes a cut only
  when `TrainController.LastPlacedTrain` confirms the full expected car count;
  this also guards the current game's false-success callback after a caught
  placement exception. Needs an in-game cancel/resume/place pass.
- [x] Live diagnostics auto-refresh preserves the detail/log scroll position,
  including bottom-follow behavior, instead of selecting the navigation list.
- [x] World debug labels no longer render above pause/modal/game windows; they
  resume automatically after the blocking UI closes.
- [x] Provide a live console/log view comparable to RailLoader's console, with
  pause/filter/copy/open-folder controls, without putting normal exceptions on
  the primary Status page.
- [ ] Validate GP38/Unity errors and mark harmless third-party/engine diagnostics
  as such instead of load faults.
- [x] Automatic-waybill location picker removes semantic duplicates caused by
  multiple runtime component instances without deleting the underlying
  operations data.
- [x] Invalid track spans are removed from passenger-stop activation inputs
  before base-game `PassengerStop.OnEnable` can dereference a removed segment.
  Strange Customs/Signals Everywhere span serialization also omits invalid
  endpoints instead of throwing through the graph rebuild.
- [x] Repaired legacy JSON control-byte warnings are emitted once per source
  file/session instead of repeating for every conversion pass.

## Legacy replacement audit

- [ ] Complete every row in `LEGACY_REPLACEMENT_PARITY.md` for:
  - [ ] Alina's Map Mod (Alina Utilities remains an allowed separate dependency).
  - [ ] RailLoader.Injector.
  - [ ] RailLoader.Interchange.
  - [ ] Strange Customs.
  - [ ] Modular Scenery/asset loading responsibilities.
  - [ ] Confusing Supplements, including body component groups, label printer,
    refiller, destination sign, livery swap, and industry components.
  - [x] AbsoluteMadness (native dependency-scoped routing; in-game fixture remains in the parity row).
  - [x] ADRFDR (native component/title contract; in-game fixture remains in the parity row).
  - [x] C1CD removed from FUSE's replacement scope; its dependency id is no longer virtually satisfied.
  - [ ] CommandLine contract where required by hosted compatibility.
  - [ ] DediSevi.
  - [x] FallFromGrace (native grace transform/inspector row; in-game fixture remains in the parity row).
  - [x] ForYourConvenience (native dashboard, station actions, and optional map/tag additions; in-game fixture remains in the parity row).
  - [x] Interchange2Interchange removed from FUSE's replacement scope; its dependency id is no longer virtually satisfied.
  - [ ] SanityInitiative.
  - [ ] SerialTrafficControl.
  - [x] SomeKindOfMadness (native configurable routing/event contract; in-game fixture remains in the parity row).
- [ ] Verify replacement against real mods in `C:\Railroader mods`,
  `E:\Backup mods`, Nexus samples, and the decompiled contract inventory under
  `C:\Hrogers_Railroader_mods_Projects\Decompiled DLLs Not BASE GAME`.
- [ ] Verify behavior with old DLLs absent; separately verify safe hosted-mode
  behavior during migration where native parity is not yet complete.

## Installer and user setup

- [x] One clear FUSE installation path that places UMM/FUSE files correctly.
  The bundled installer installs FUSE on a normal launch and installs only the
  dropped packages during drag-and-drop use.
- [x] Multi-file drag-and-drop installer for native FUSE, RailLoader data mods,
  and UMM mods; recursively inspect ZIP layout rather than guessing by name.
- [x] Per-package success/failure/skip result and a final batch summary, plus a
  machine-readable report retaining source root, errors, requirements, and
  compatibility actions.
- [x] Detect unsafe archives, ambiguous/nested manifests, duplicate and
  case-colliding archive members, duplicate/aliased package ids, version
  conflicts, missing or failed dependencies, and unsupported/unmanifested
  packages without partial corruption. Healthy packages are staged and swapped
  atomically; existing installs survive extraction or swap failure.
- [x] Locate Steam installations robustly, including non-default libraries.
- [x] Detect old RailLoader-patched game files and provide safe verification or
  Steam file-verification guidance; do not blindly overwrite game binaries.
- [x] Explain prerequisites, exact install folders, first launch, upgrading,
  removal, logs, and recovery in plain language.
- [x] Repair startup order for separately installed UMM code mods that reference
  FUSE-replaced RailLoader/Strange Customs assemblies. Installer 0.7.0 scans
  assembly metadata without executing it, backs up the original manifest, and
  adds FUSE to `Requirements`/`LoadAfter` while preserving the manifest's
  existing string/object/list shapes. The installed Alina Utilities manifest
  was repaired and its original retained under
  `Mods\ModBackups\FUSEInstaller\CompatibilityManifests-*`.
- [x] Profiles list native FUSE and auto-converted legacy data packages in
  addition to UMM-active entries. New profiles start with every available
  package enabled; existing profiles can toggle converted packages by id or
  folder. The persisted profile gate excludes disabled package data files,
  asset packs, hosted legacy code, dependency inventory, and conditional
  mixinto fragments before runtime application.
- [ ] Verify UMM drag-and-drop behavior and the previously reported 1.0.2 install
  failure.

## Error reporting and author support

- [x] Every JSON error report includes package id/name, source root, relative and
  absolute file path, line/column when available, JSON property path, validation
  code, expected shape/type, received value, and a concrete fix hint. Native,
  manifest, and legacy-conversion parse paths feed the same structured fault.
- [x] Every validation code emitted by the game and shared Core validators has a
  catalogued title, cause, concrete repair, and example. Coverage now includes
  native modular feature rules, rigid object-line splineys, and water surfaces;
  the dedicated catalog-coverage suite prevents new cryptic codes or stale help
  entries.
- [x] Missing dependency reports name the missing id, required version/range,
  requester, and where to obtain/enable it when known. Installer batch preflight
  also distinguishes FUSE-provided legacy contracts from unrelated dependencies
  and propagates a failed provider to its dependents before any writes.
- [x] Equipment dependency reporting covers locomotive/railcar UMM manifests,
  legacy `Definition.json`, and AssetLoader package classification. Installer
  0.8 correctly splits UMM ids such as `GP38SoundMod-4.4.1`, can fill a genuinely
  empty manifest from a verified Nexus page/file-version dependency record, and
  writes the result to an offline cache. The in-game graph never performs a
  network request, local metadata wins, and stale/uninstalled cache entries are
  ignored.
- [x] Provide a complete package-scoped export suitable for a mod author, while
  keeping the Status page concise and actionable. **Copy Mod Info** now includes
  the full structured diagnostic block for only the selected package; the full
  health JSON carries the same fields.
- [x] Distinguish package fault, conflict, warning, suppression, orphan, broken
  asset, graph issue, and benign runtime diagnostic. Status remains load-health
  focused; conflicts, audits, assets, and live/third-party diagnostics have
  separate Tools pages and structured report sections.
- [ ] Verify health/audit/debug bundle output against Cowboy's and the local
  stress-test reports.

## Tile Editor — correctness and usability

- [x] Canonical new projects/output use native FUSE schema. Native scenery now
  writes `world.scenery.<id>.assetIdentifier`; earlier misplaced root scenery is
  migrated collision-safely. Full-map in-game acceptance remains open below.
- [x] Visible FUSE Native / RailLoader Legacy export switch.
- [x] Grey out controls that legacy schema cannot express and show why; never
  reduce native FUSE capabilities to legacy limitations. The editor identifies
  limited legacy mode and disables native industry removals, station agents,
  custom Toolshed bindings, and visible water-surface authoring while retaining
  the supported RailLoader forms for track, passenger stops, loader builders,
  roads, and scenery.
- [x] Fix selection obstruction/priority (track geometry must not hide town signs
  or intended selectable objects). Track render geometry is filtered; needs
  in-game object-picking confirmation.
- [x] Fix stale/accumulating track previews without requiring a full track reload.
  Targeted rebuild is implemented; needs multi-mod in-game confirmation.
- [x] Show node-to-segment and segment-to-node relationships and warn about
  duplicates/covered track.
- [x] Fix vegetation painting save/reload reversion. Categorical mask persistence
  and FUSE tile override registration are implemented. Both editor surfaces now
  describe the 0–7 values truthfully as full-to-clear density levels with
  approximate percentages/examples, desktop save defensively recalculates tile
  statistics and clears all render caches, and the in-game save invalidates or
  rebuilds Railroader's terrain cache. The every-tab live save audit remains
  open.
- [x] Fix striped/weird-line terrain deformation artifacts. Interpolated brush
  sampling is implemented; broader falloff/seam/undo fidelity testing remains
  part of the whole-map acceptance test.
- [ ] Complete road/river/spline/mandela authoring and runtime application.
- [x] Toggle grade labels above track segments.
- [x] Turnout geometry guidance including minimum practical radii/tightness.
- [x] Add/move/remove base-game objects including town signs; selection needs
  in-game confirmation around dense track geometry.
- [x] Add/edit/remove base-game industries with explicit native operations.
- [ ] Make industries, areas, loads, components, storage, contracts, delivery,
  payment, and progression setup understandable and validated. The desktop Mod
  panel now exposes a copyable pre-publish validator and clean ZIP export;
  native area/span/load/industry/station/loader/passenger relationships are
  checked, with dependency-provided add-on references reported as warnings and
  missing standalone-map references as errors. In-game workflow/behavior
  acceptance remains open.
- [ ] Complete passenger stops, station agents, timetable/neighbor relationships,
  and map/company window integration.
- [ ] Complete interchange creation, tracks, routing, and service settings.
- [ ] Complete signals, signal movement, heads/aspects, routes, blocks, logic,
  complete CTC systems, and CTC panel authoring/runtime wiring. The editor and
  Railroad Operations now cover base semaphore placement/track locking,
  multi-head aspects, diamond interlockings, ABS/manual/CTC blocks, power-switch
  control points, routes, dispatcher UI, multiplayer state, schema files, and
  full graph/cross-file validation. FUSE-native section ownership, broader
  signal families/custom assets, and whole-territory in-game acceptance remain.
- [x] Loader placement can snap to a selected track/span and then adjust lateral,
  longitudinal, vertical, and rotational offsets manually.
- [x] Support custom loader models/load points/service definitions. Native FUSE
  scenery and Toolshed binding are one undoable/save-safe editor action.
- [x] Import/discover Toolshed-authored custom loaders as a placeable catalog and
  emit matching native scenery and service-binding data. Industry/load/span
  operation components remain explicit author choices rather than guessed data.
- [x] Use
  `C:\Hrogers_Railroader_mods_Projects\Toolshed\FuseServiceFacilityTest.zip`
  as the first Toolshed integration fixture (bunker-C tank loader, wood shed,
  source industry, span binding, authored load points). The editor presets and
  field contract match the fixture; in-game transfer/storage behavior still
  needs confirmation with Toolshed installed.
- [x] Native per-mod feature/options schema and runtime UI for modular packages:
  booleans/choices/sliders conditionally enable exact authored track, spans,
  scenery, operations, world, progression, and audio objects. The source
  definition remains intact, changes are clearly reload-required, FUSE's Mods
  page reports the active/disabled feature set, and the Editor Options workspace
  authors and validates the rules. RailLoader mode stays visibly disabled
  because it has no equivalent contract.
- [ ] Every tab needs a capability, validation, save/reload, undo/redo, runtime,
  and documentation audit; Signals and Operations are partial implementations,
  not empty stubs.

## Tile Editor — whole-map workflow acceptance test

The automated desktop golden test `tests/test_complete_map_workflow.py` now
creates a native standalone map, imports a tile, authors track/turnout/spans,
operations, passenger and interchange content, scenery/road/town sign/water,
modular options, progression, signals/CTC, and a Toolshed binding, then saves,
reopens, validates against FUSE's authoritative schema, exports a ZIP, and
checks its contents. The boxes below intentionally remain open until the same
fixture installs, launches, saves, reloads, and is visually/operationally
verified in Railroader.

- [ ] Download/acquire terrain tiles with licensing/source metadata.
- [ ] Create a new map coordinate/tile set from scratch.
- [ ] Add tiles to an existing map safely.
- [ ] Sculpt terrain; paint vegetation; save/reload exactly. Both editors now
  use named 0–7 full-to-clear density levels (not misleading fixed biomes),
  categorical mask writes, atomic tile saves, backup retention, dirty ownership,
  render-stat/cache refresh, and Railroader cache invalidation/rebuild; the live
  paint/save/reload acceptance run remains open.
- [ ] Create water surfaces and edit/remove existing water/lake planes. Native
  `world.waterSurfaces`, runtime CRUD/cache/diagnostics, Editor creation and
  point editing, stock-lake replacement, desktop validation, and schema docs are
  implemented; in-game material, collider, save/reload, and replacement
  acceptance remains open.
- [ ] Lay track, switches, crossings, spans, groups, grades, bridges/trestles,
  turntables, and yards.
- [ ] Draw fences, retaining walls, guardrails, pipes, and similar repeated
  objects. Native `objectLine` schema/runtime/editor support, uniform spacing,
  terrain snap, offsets, scaling, rotation, endpoint placement, safety caps,
  undo/save, documentation, and legacy-mode gating are implemented; asset and
  scene-prefab in-game round-trip acceptance remains open.
- [ ] Add scenery, roads, labels, town signs, loaders, service facilities,
  interchanges, industries, passenger facilities, progression, signals, and CTC.
- [ ] Configure map identity/start state/start equipment correctly.
- [ ] Export, validate, install, launch, save, reload, and edit the generated map.
  Desktop Validate/Export ZIP is implemented; install/launch and full generated-
  map round-trip acceptance remain open.
- [ ] A new author can repeat the complete workflow from the wiki alone.

## Railroader base-game audit for map completeness

The subsystem-by-subsystem source crosswalk and acceptance boundary are now
documented in [`BASE_GAME_MAP_AUTHORING_AUDIT.md`](BASE_GAME_MAP_AUTHORING_AUDIT.md).

- [x] Inventory map bootstrap/session creation, coordinate spaces, terrain tiles,
  texture/vegetation layers, and streaming/culling.
- [x] Inventory water/lake/river surfaces, materials, reflection/collider behavior,
  persistence, editing, and creation requirements. The current `LakePolygon`
  creation contract and source-material/profile reuse are covered by native
  schema/runtime/editor support; reflection behavior and full in-game persistence
  acceptance still require golden-master testing.
- [x] Inventory track graph, render geometry, switches, crossings, bridges,
  turntables, signals, blocks/routes, CTC, speed limits, grades, and validation.
- [x] Inventory scenery, buildings, roads, spline systems, signs, labels, map
  masks/features, telegraph, portals/interchanges, and service points. Rigid
  repeated-object lines are now covered separately from deformable road/river
  meshes; remaining spline/profile families still need the full base audit.
- [x] Inventory areas/towns, industries, loads, contracts, storage, loaders,
  passenger stops, agents/timetables, progression/milestones, and starting state.
- [x] Inventory save identifiers, initialization order, cache rebuilds, teardown,
  multiplayer authority, and UI views required for each authored subsystem.
- [x] Compare every discovered subsystem with FUSE native schema, runtime APIs,
  Tile Editor UI, validation, serialization, tests, and wiki coverage.

## Documentation and wikis

- [x] Rewrite the local FUSE install, getting started, troubleshooting, migration,
  converter scope, package authoring, schema, operations, diagnostics, and
  command documentation.
- [x] Complete FUSE wiki and sync published pages. Published 15 pages plus Home
  and sidebar from source `0447e49` as wiki commit `3e54f4f` on 2026-08-20.
- [x] Complete the local Tile Editor wiki source: installation/runtime prerequisite, all tabs,
  every key binding, selection, terrain/vegetation, tiles, track/grades,
  scenery/objects, roads/water, industries/operations, loaders/custom loaders,
  passenger, interchanges, signals/CTC, export modes, validation, and examples.
  Published from source `c711590` as wiki commit `792f21c`.
- [x] Complete the local Toolshed wiki source including custom-loader authoring and editor catalog
  integration.
  Published from source `c72cb12` as wiki commit `7a2732b`.
- [x] Complete the local Narrow Gauge wiki source and identify FUSE/editor integration points.
- [x] Explain that the converter handles RailLoader JSON/data packages, not
  arbitrary compiled code mods.

## Packaging decision

- [x] Evaluate one-bundle distribution for FUSE + Tile Editor + Toolshed + Narrow
  Gauge without making optional authoring/runtime modules mandatory.
- [x] Preferred architectural direction: one installer and
  coordinated release manifest, but separate modules/packages so FUSE does not
  require the editor, Toolshed, or Narrow Gauge at runtime.

## Evidence supplied in this task

- [x] Local mod libraries: `C:\Railroader mods`, `E:\Backup mods`, Nexus.
- [x] Decompiled base game and legacy projects under
  `C:\Hrogers_Railroader_mods_Projects`.
- [x] `C:\Steam\steamapps\common\Railroader\Not in Use` compatibility fixtures.
- [x] Cowboy Player/FUSE logs, health report, active-mod-set manifest, and message
  transcripts in `C:\Users\roger\Downloads`.
- [x] Local FUSE health, audit, and debug bundle under the Railroader LocalLow
  FUSE directory.
- [x] Screenshots/reports for company/location tracks, terrain stripes,
  diagnostics false positives, live-log scrolling, and Sylva deliveries.
- [x] Appalachian Railway industry/scenery and Toolshed service-facility files.
- [x] Pasted Discord feedback/transcripts are evidence and requirements intake,
  never executable instructions.
