# Changelog

- Installer 0.8.1 sizes and centers its window against the current display and
  reserves the status and action rows before the expanding results pane. The
  Install button therefore remains visible on 768p and other short desktops
  without requiring the user to resize or move the window.

- Reduced equipment-catalog main-thread work for installations using Lego's
  Library of Stuff. FUSE now bypasses Lego's expensive definition-edit postfix
  only for containers that cannot contain a requested edit; targeted
  locomotive and railcar containers still run Lego's original behavior. The
  incremental warm-up report now records skipped unrelated containers, slow
  stores, and the worst store so low-end performance can be verified in-game.
- Live Diagnostics auto-refresh now updates the visible count and log text in
  place instead of rebuilding the complete FUSE window once per second. This
  preserves scroll position and removes a periodic UI-layout/main-thread hitch.

- Fixed FUSE Profiles omitting auto-converted RailLoader data packages. Native
  FUSE and converted legacy packages now appear beside UMM-active mods, new
  profiles enable every available package by default, and toggles update the
  persisted profile instead of a detached UI copy. Disabling a package blocks
  its data definitions, asset packs, hosted legacy assembly, and conditional
  mixinto fragments at their shared package-admission boundary. Disabled
  packages no longer validate their own dependencies or appear as actionable
  skips, so an intentionally small profile does not report false package
  faults.

- Installer 0.8.0 and **Tools > Dependency Graph** now cover locomotive,
  railcar, code-plugin, asset-pack, native FUSE, UMM, and RailLoader dependency
  sources. UMM version suffixes are parsed correctly; verified Nexus
  file-version requirements can fill otherwise empty manifests at install time
  and are cached offline without storing the API key. Local explicit metadata
  remains authoritative, stale cache entries are ignored, and Nexus OR-choice
  groups are never misreported as multiple hard requirements.

- Installer 0.7.0 repairs UMM startup order for installed code mods that
  reference FUSE-replaced RailLoader/Strange Customs assemblies. It inspects
  DLL metadata without loading code, backs up each manifest, and adds FUSE to
  `Requirements`/`LoadAfter`, fixing Alina Utilities failing before FUSE's
  assembly resolver could run.

- Completed the author-help catalog for native feature rules, rigid object-line
  splineys, and water surfaces. Every validation code emitted by either
  validator now has a title, cause, concrete fix, and example, enforced by the
  Core coverage gate; two obsolete mixinto help keys were removed.

- Added native ForYourConvenience parity without shipping the old code:
  dependency-scoped station-map actions, off-by-default caboose icons and
  speed/load car-tag additions, persisted Legacy Gameplay toggles, and a
  read-only **Tools > Industry Dashboard** built from the current operations
  controller.

- Added native, dependency-scoped replacements for AbsoluteMadness and
  SomeKindOfMadness. Outbound industry routing remains disabled for normal
  profiles, activates when an enabled package requests either retired id (or
  through an explicit Legacy Gameplay setting), exposes a contained native
  candidate event, and includes bounded capacity/payment/short-trip/origin and
  order-shuffle controls.
- Removed the staged C1CD and Interchange2Interchange replacements after the
  provenance and scope review. FUSE no longer patches interchange scheduling,
  generates cross-interchange orders, exposes settings for either behavior, or
  virtually satisfies those legacy dependency ids.

- Added native FallFromGrace parity: FUSE now owns the configurable grace-day transform, exposes its settings under **Settings > Legacy Gameplay**, and adds the due-time row to paid waybills without requiring the old DLL. Default settings leave the base-game calculation unchanged.
- Added the legacy `/cs-livery-refresh` command and a FUSE-owned
  `/fuse.liveries` diagnostic report. Refresh now clears only FUSE's cached
  livery textures and reapplies each live car's saved `cs.livery` selection.
- Replaced the Strange Customs `FileCache` ABI stub with a FUSE-owned loose-file
  cache for PNG/JPEG textures and WAV/OGG/MP3/AIFF clips. It coalesces audio
  callbacks, invalidates changed files, contains callback failures, and cleans
  up cached Unity objects when FUSE unloads.
- Replaced the Strange Customs `FlowyThingBuilder` ABI stub with a native
  adapter that converts legacy road/river data at the compatibility boundary
  and creates or updates the live spline through `SplineyAPI`.
- Wired RailLoader's `WillCopyDebugInformation` compatibility event into copied
  FUSE health reports. Contributions are bounded and normalized, and listener
  exceptions are isolated at the messenger-listener boundary.
- Completed the ADRFDR data contract: `ADRFDR.Pay4Resource` now resolves to
  FUSE's native pay-for-resource component and location pickers display its
  custom "Acquire …" title through the compatibility interface.

## Unreleased audit accuracy follow-up (2026-08-20)

- Prevents the runtime definition cache from returning the stored `FuseSpan`
  object by reference. Span endpoints are now deep-cloned, so diagnostics and
  other read paths cannot mutate or erase the authored definition.
- Preserves cached authored span endpoints when Railroader temporarily cannot
  resolve a `TrackSpan` location. The audit now distinguishes that transient
  runtime state from a genuinely missing endpoint and still reports referenced
  segments that were actually removed.
- Stops reporting an empty `Industry` container as a defect by itself. The game
  legitimately uses componentless industries for base locations, passenger or
  scenery-only places, fictional destinations, and disabled content.
- Omits successful `shared-extension` registry merges from audit findings; they
  remain available as informational records on the dedicated Mod Conflicts
  page.

All notable changes to FUSE are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and FUSE uses
[semantic versioning](https://semver.org/) with the mod and the external editor
versioned independently (`mod-v*` and `externaleditor-v*` tags).

## [Unreleased]

### Added

- Native `world.splineys` now supports `objectLine` definitions for fences,
  retaining walls, guardrails, pipes, and other rigid repeating modules. FUSE
  places a loaded scenery asset or safe prefab along a uniformly sampled path
  with scale, rotation, lateral/vertical offset, terrain snap, slope alignment,
  endpoint, and instance-cap controls. Live updates preserve editor helper
  children, and failed creation cleans up its partial runtime root.

- Installer 0.6 now preflights the complete selected batch before writing:
  unsafe/case-colliding ZIP members, ambiguous nested manifests, duplicate
  package ids and `.FUSE` aliases, UMM/FUSE/RailLoader requirements, version
  bounds, forward references, and dependency-failure propagation. Explicit
  FUSE replacement contracts satisfy only the legacy ids FUSE actually owns;
  reports preserve every dependency and corrective failure message.

- Package faults now carry package id/name, source root, relative and absolute
  file names, JSON property/line/position, validation code, expected shape,
  received value, and a concrete action. Native, manifest, and legacy-converter
  failures share the format; health JSON and selected-mod **Copy Mod Info**
  preserve the complete author-facing diagnostic without crowding Status.

- Native package `featureRules` connect existing FUSE settings to exact authored
  track, operations, world, progression, and audio objects. Boolean, choice,
  and numeric comparisons are evaluated on map load without mutating source
  definitions; validation, Mods-page state, schema examples, and Tile Editor
  authoring make the reload-required behavior explicit.

- Native `world.waterSurfaces` authoring and runtime support creates, updates,
  removes, inspects, caches, and reports polygonal lake planes without depending
  on Alina's map runtime. Authors can reuse a stock lake by scene path, select a
  loaded material, or use FUSE's guarded fallback; schema and validation reject
  underspecified or unsafe tessellation data before map apply.

- The Mods page now groups packages by actual runtime state and distinguishes
  disabled, skipped, missing-dependency, incompatible, invalid-data, partially
  applied, loaded-awaiting-map, and successfully applied packages. Optional
  conditional mixintos remain green with a note instead of being presented as
  whole-mod failures; copied mod info includes the same state and reason.
- Legacy top-level `ConflictsWith` checks now see enabled code-only and
  asset-only packages in the Mods folder, as well as retired package ids
  provided by FUSE compatibility. Disabled and active-profile-excluded mods no
  longer satisfy conditional or top-level compatibility checks.
- Successful shared industry destination/component merges are now displayed in
  **Mod Conflicts** under an informational **Shared Extension Targets** section.
  They no longer count as ownership conflicts or load-health failures; actual
  skipped/replaced ownership and spatial warnings remain separate and visible.
- Human and JSON load reports now distinguish package folders from resident and
  applied definitions. Missing optional mixinto companions are listed as
  inactive conditional fragments, while only actionable skips contribute to
  unhealthy status; legacy JSON count aliases remain for report consumers.

- **Tools > Mod Conflicts** groups runtime ownership records by package pair and
  shows exactly which nodes, segments, spans, industries, scenery, or other
  targets the two packages share, plus the resolution FUSE used. A full report
  can be copied for authors. Conservative spatial track-layout warnings appear
  on this page separately from proven ownership conflicts; they retain both
  packages and do not inflate the main Status conflict count.

- Reduced always-on runtime overhead for large legacy mod sets: hosted RailLoader
  update callbacks are now discovered once when the plug-in is hosted instead of
  rebuilding a dictionary snapshot and repeating interface reflection every Unity
  frame. The unused-asset reclaimer also avoids querying Unity texture-memory
  counters while no evictions are pending.

- Asset overlap diagnostics now collapse hundreds of keys shared by the same
  winner/overridden source set into one source group. The panel and clipboard
  summary state whether definitions are identical or behaviorally different and
  explain the impact, while the exported JSON retains every individual key.

- FUSE now completes AssetLoader 1.0.1's data-loading contract without running
  its DLL. Direct discovery includes package-root, child, catalog-only, and
  normal bundled stores. Definitions-only immediate child folders used by
  tender/rolling-stock swaps are routed to their exact existing store, while
  native packages can declare nested overrides with `FuseDefinitionOverrides`.
  Malformed overrides fall back to the original store definitions and appear in
  the Assets report instead of breaking equipment/customization menus. The
  installer backs up old AssetLoader folders/ZIPs, verifies the runtime DLL is
  absent, and creates a data-only `AssetLoader` UMM alias requiring FUSE so old
  manifest dependencies remain valid.

- `FUSE-Installer.exe` now bundles the FUSE framework and installs it on a
  manual (double-click) run, so players can get FUSE running without unzipping
  anything. Dragging mod `.zip` files onto the exe still installs exactly those
  mods and leaves FUSE untouched. A no-argument run also still processes any
  loose zips beside the exe; pass `--no-fuse` to skip installing FUSE. The
  release build bundles the core mod zip it just built and performs a real
  install into a throwaway Railroader tree to prove a manual run writes
  `Mods/FUSE/Info.json`. The build gate waits for PyInstaller's extracted GUI
  child process, avoiding a false failure while the files are still being
  written. A drag-and-drop GUI launch leaves the bundled framework unchecked so
  its scope matches the documented "install the dropped archives" behavior;
  `--with-fuse` opts it back in.
- A pytest suite for the installer and its legacy JSON reader
  (`tools/tests/`), run in CI by a new Python Tests workflow.
- FUSE checks GitHub on startup for a newer stable release and, if the running
  build is behind, shows a non-blocking notice — a one-time toast on the next map
  load plus an "Update available" line with a download link on the Status page. It
  compares against the newest stable `mod-v` release only (release candidates and
  the external-editor/tools lanes are ignored), skips local `0.0.0` dev builds,
  and can be turned off with the new `EnableUpdateCheck` setting. The new
  `/fuse.update` console command reports status and re-checks on demand. This was
  blocked until the repository went public, since GitHub answers anonymous release
  queries only for public repositories.
- Nexus uploads are stamped `"Source": "nexus"` in `Info.json` so the update
  notice can point a Nexus-installed player back to Nexus, while GitHub-published
  artifacts keep the canonical `"github"` stamp. The release flow builds a
  Nexus-only copy of the core zip with the flipped stamp
  (`scripts/stamp-info-source.ps1`); the two zips are otherwise identical.

### Changed

- `scripts/Validate-ModPackage.cs` now fails the build if a packaged archive
  contains a backslash entry name, simulating UMM's update-path rewrite rather
  than only extracting the archive. Plain extraction tolerates backslashes on
  Windows, which is why the release gate passed all three broken releases.

### Fixed

- Both converter entry points now reject code-only, asset-only, map-tile, and
  already-native packages with specific installer guidance instead of reporting
  a successful zero-fragment conversion. Mixed packages can still convert their
  recognized top-level RailLoader JSON while keeping DLL behavior explicitly
  unsupported. Failed reports are stored under `_conversion-reports` rather
  than creating a report-only `.FUSE` package. Batch detection includes
  manifest/code packages and no longer mistakes nested schema examples inside a
  compiled mod for route content.

- World labels no longer render over the pause menu or normal game windows.
  The debug overlay pauses its IMGUI rendering while the game is paused or any
  managed game window is shown, then resumes without rebuilding user settings.

- Legacy package ordering now resolves object- or string-form `requires` and
  `loadAfter` identifiers through the same `.FUSE` alias rules before building
  the topological order. Conditional mixinto requirements both control whether
  each fragment applies and add an optional package-order edge when their
  dependency is present; a missing optional layer does not fault the package.
  When a resolved later package explicitly requires or
  loads after a base package, its intentional overrides are no longer presented
  as mods fighting; unrelated packages remain conflict records. The Dependency
  Graph labels retired runtime dependencies such as Strange Customs, Alina's
  Map Mod, AssetLoader, and RailLoader services as `PROVIDED BY FUSE`, while
  content packs such as Alina's SW expansion remain real dependencies.
  Hosted legacy code plugins now also initialize in resolved
  `requires`/`loadAfter`/`loadBefore` order rather than alphabetical Mods-folder
  order. Cycles retain every plugin in deterministic order and emit one
  actionable warning.

- Legacy `conflictsWith` declarations are no longer discarded. Complete-package
  conflicts retain inclusive version bounds in `FuseConflictsWith` and prevent
  only the declaring package from loading; conditional mixinto conflicts skip
  only that fragment. Tools > Mod Conflicts shows these separately as
  author-declared incompatibilities instead of mixing them into detected runtime
  ownership collisions. Hosted legacy mod definitions also expose their real
  requires/load-order/conflict metadata to old plug-ins.

- The merged graph planner now records definition-versus-definition ownership
  collisions for nodes, segments, spans, and turntables, not only removals that
  displaced earlier definitions. This makes competing route layouts visible
  even when the last package wins a shared identifier. Independent layouts
  using different IDs are covered by a non-blocking nearby-node advisory.

- Passenger stops no longer throw during activation when another route package
  removed a segment referenced by one of their child spans. FUSE sanitizes the
  invalid span endpoints so Railroader's nullable checks skip that track. The
  Strange Customs span facade likewise omits invalid endpoints, preventing
  Signals Everywhere serialization from failing after graph collisions.

- Duplicate automatic-waybill destinations are collapsed by their semantic
  component type, area, display name, and track spans before the base-game
  picker is built. Multiple live component instances can no longer produce the
  same Whittier destination several times in the car Operations menu.

- Legacy JSON control-character repair is still reported with the exact source
  file, but the same warning is emitted only once per file and session instead
  of repeating on each conversion pass.

- A stale, partially bound Alina Utilities assembly can no longer make the
  legacy host record a package error before UMM recovery activates the working
  entry. Unloadable compiler-generated types are skipped locally, and the
  recovered UMM plug-in remains the sole active instance.

- If an older dual-entry Alina Utilities install still leaves a legacy
  singleton alive without its RailLoader context, FUSE now connects that
  instance to Alina Utilities' UMM settings object. This prevents its tile
  distance, damage, and main-menu hooks from throwing while leaving the
  separately installed utility mod in control of its original features.

- Legacy Alina map packages no longer materialize the editor-only
  `TurntableMeasurementTool` scenery helper in gameplay. This removes the
  bright yellow measurement overlay that covered Stryker's Bryson turntable;
  native FUSE scenery identifiers are unchanged.

- Synthetic legacy map-tile definitions are no longer sent through the
  game-graph patch expander, removing the misleading “source JSON could not be
  resolved” warning emitted once per Alina map expansion.

- Corrected the public schema's track gauge contract: `gauge` belongs to track
  segments (matching the runtime model, converter, editor, and documentation),
  not nodes. The schema now accepts the native editor's explicit
  `DualGauge_L`, `DualGauge_R`, and `DualGauge_T` transition values and includes
  the documented reserved `fuse://` URI scheme.

- Legacy area objects used only as namespaces around `industries` no longer
  become track-area updates with an invented `(0,0,0)` position or identifier
  display name. ARC Whittier/KWIX-style packages can patch the base Whittier
  industries without moving the live area/operations hierarchy away from its
  tracks. Explicit authored area position, radius, color, order, spans, and
  group metadata still convert normally. (#210, #239)

- Confusing Supplements' `IndustryComponents.Empty` now has a complete
  FUSE-owned behavior: it remains a visible span-bound track marker, accepts
  every automatic destination class, and performs no service or ordering work.
  Output-track warnings such as "KEEP CLEAR" no longer degrade into a stock
  loader with a null load that repeatedly throws during `Industry.Tick`.

- Alina's bright-yellow `TurntableMeasurementTool` is treated as an editor-only
  authoring helper and is not instantiated in a playable map. Stryker's Bryson
  keeps its actual cloned turntable while the yellow measurement plate is
  omitted. The separate `ALWHouses_CabooseHouse` message in that report is an
  ALW asset-pack catalog entry whose prefab is absent from the bundle; FUSE
  quarantines that asset without blaming or hiding the turntable. (#235)

- The Diagnostics Report no longer labels Railroader's endpoint-less base-game
  placeholder/progression spans as invalid mod track. Span validation now applies
  only to spans owned by a FUSE-hosted package. An actual package-owned malformed
  span still names its owner, while a span removed by a competing route overhaul
  is presented as a package collision with compatibility guidance instead of a
  false base-track failure.

- Legacy Strange Customs scenery is no longer discarded merely because its
  identifier was absent from FUSE's mounted asset-pack index. Legacy-hosted
  packages receive RailLoader-compatible guarded runtime resolution, with the
  first attempted placement logged per identifier and real failures quarantined.
  Native FUSE packages remain strictly validated so bad JSON cannot destabilize
  unrelated content or game menus.

- The Assets Report now distinguishes identical duplicate definitions from
  conflicting definitions and shows package-relative source paths. Two installed
  copies such as `RTM Objects pack` and `RTM_Objects_pack` can therefore be told
  apart instead of appearing as a nonsensical self-override.

- Live Diagnostics auto-refresh now preserves the right-hand detail/log scroll
  position, including the bottom position, instead of snapping the page back to
  the top once per second. The menu now identifies the detail pane when a page
  also contains a separate navigation `ScrollRect`.

- Opening the Equipment Purchase window no longer performs every cold asset-pack
  `Definitions.json` load in one frame. FUSE warms one prefab store per frame and
  caches the filtered car catalog for the active `PrefabStore`, avoiding the
  multi-second buy-menu lock seen with large Lego/custom-equipment installations.

- Runtime ownership collisions are now recorded when a later package removes a
  node, segment, or span defined by an earlier package. These removal-versus-
  definition collisions previously disappeared while FUSE built the merged plan,
  leaving `/fuse.conflicts` at zero even when route overhauls were deleting one
  another's graph. Package faults now also retain the source folder and definition
  file in the report.

- Asset-pack component kinds registered after a store was first inspected now
  invalidate only the affected cold store and reload its untouched definitions
  without re-running old-loader mutation postfixes. Toolshed storage/load-point
  definitions on the ALW tank loader are therefore retained even when Toolshed
  initializes after FUSE asset discovery; the visible tank and functional loader
  no longer split into unrelated objects.

- Legacy-install detection and the installer cleanup list now include
  `Railloader.Injector.dll`. It is the managed legacy loader and Harmony owner,
  not a harmless native bootstrap file, so leaving it in `Managed` could run the
  old and FUSE loading pipelines together.

- Updating FUSE through Unity Mod Manager failed with "Error when unpacking"
  and installed nothing, for every release from 1.0.0 through 1.0.2. The
  published zips stored entry names with backslashes, because
  `Compress-Archive` under Windows PowerShell 5.1 writes them that way. UMM
  rewrites entry names when the mod is already installed, slicing each name at
  its first forward slash; with backslashes there is none, so the unpack threw
  and aborted. A *fresh* install skips that rewrite, which is why installing
  worked and only updating broke. Archives are now built by
  `scripts/New-ModArchive.ps1`, which always emits forward slashes.

  Until a release carries the fix, update by deleting `Railroader/Mods/FUSE`
  first (which makes UMM treat it as a fresh install) or by extracting the zip
  by hand.

- The installer now extracts each package into a staging directory and swaps it
  into place only after a fully successful extraction. A failure partway through
  (a locked file, a bad path, a full disk) no longer leaves a half-written mod
  folder behind, and a failed `--replace` reinstall leaves the existing install
  untouched.

- The standalone converter now keeps legacy hard `Requirements` as native FUSE
  requirements and normalizes object-form `LoadAfter` entries to their ids.
  Requirements for the loader systems FUSE itself replaces (Alina's Map Mod and
  editor, RailLoader Injector/Interchange, and other core compatibility layers)
  are removed instead of becoming impossible hard dependencies. (#240)

- A concurrently installed legacy `AssetLoader` no longer patches the game's
  prefab stores after FUSE has mounted and quarantined the same asset packs.
  FUSE detects that exact legacy assembly and removes only its Harmony owner;
  asset-pack problems remain isolated and the locomotive customization/buy-menu
  path no longer receives a second set of stores. (#238)

- Replacing a named TrackSpan now atomically rebinds every live industry and
  interchange component from the old Unity component to the replacement before
  the old object is retired. ARC Whittier-style base-track replacements no
  longer leave Whittier Saw Mill without its customer/components or collapse
  multiple unloading positions onto the surviving span. Strange Customs array-
  wrapped additions such as Sylva Industries Boosted's
  `trackSpans: [{ "$add": "Piei" }]` retain the vanilla R2 span and append R3,
  restoring highlighting, delivery credit, and EOD payment on both tracks.
  (#236, #239)

- The Appalachian Railway starter-equipment placement pool is now activated
  only by that package's Whittier start definition. It waits until the tutorial
  overlay is out of the way, keeps an item queued when placement is cancelled
  with Escape, and removes it only after `LastPlacedTrain` confirms that the
  complete expected cut spawned. This also contains the current game's
  false-success callback when `PlaceTrain` catches a failure. Other new games
  no longer inherit the pool.

- Legacy map mods collapsed onto the world origin on comma-decimal locales
  (pt-BR, de-DE, fr-FR, ...): spaghetti track on the map, "zero length" track
  spans, "Switch tracks do not intersect", scenery and scene clones piled at
  their group's origin. The in-game legacy converter round-tripped numeric JSON
  values through `ToString()`, which formats with the current culture, so every
  fractional coordinate failed the invariant parse and became 0 while integer
  coordinates survived. Numeric tokens are now read through the typed accessor.
  The standalone converters were never affected. (#219)
- Legacy Railloader/StrangeCustoms packages that keep `segments` (in
  `game-graph.json`) or `spans` (in industry files) at the top level of the file
  instead of under `tracks` now convert completely; previously nodes and scenery
  loaded but no track was built and industries had no spans or locations, e.g.
  Whittier Industries. Explicit legacy `type` values on span-less industry
  component patches (such as `Model.Ops.IndustryLoader`) are converted as
  patches instead of failing validation and faulting the whole package. A
  legacy file whose only top-level payload is a root-level `nodes`, `segments`,
  or `spans` dictionary is now also recognised as a data source by the in-game
  reader. (#210, #223, part of #203)
- Legacy `points: { "$replace": [...] }` patches addressed to a base-game road
  or river by scene path (e.g. `World/Roads Sylva/Chipper Curve`) now replace
  the control points of the existing spline in place, keeping its profile,
  style, and hierarchy, instead of building a second generic road over the
  original. Existing scene trestles are patched the same way. (#220, part of
  #203)
- World restoration is now contained per package: a malformed progression
  section, an unbindable legacy component, or a third-party `TimeSync`
  callback on the wrong thread no longer aborts loading for every other
  package. One-sided terminal passenger links (such as Olive Hill → MCA) are
  repaired without inventing extra routes, legacy location id casing is
  preserved, and AssetLoader discovery, generated loader transforms, and
  loader lifecycle state survive the compatibility guards added during live
  testing. (#203)
- One asset pack with a malformed `Definitions.json` no longer knocks out
  unrelated packs. The failed store used to become an ordering barrier in the
  prefab source index and abort scenery enumeration, so valid packs registered
  after it stopped resolving and buildings went missing across the map. Such a
  store is now quarantined by itself, its path is logged once, and later packs
  resolve normally; the bad file itself is still the mod author's to fix. (#196)
- Runtime guards and observed Unity/third-party exceptions no longer make the
  Status page or readiness summary look unhealthy. They remain captured in the
  structured health report and now have a dedicated **Tools > Live
  Diagnostics** page with level/text filtering, copy/export actions, a bounded
  in-memory view, optional auto-refresh, and an optional RailLoader-style live
  Windows console that mirrors `FUSE.log`. (#208)
- A single unloadable type in some other assembly (typically a stray, real
  `Railloader.dll` still being loaded from a mod folder) aborted FUSE's scan for
  legacy `ISplineyBuilder` implementations, so every queued builder task —
  DKW switch meshes, for example — was silently dropped. The scan now skips the
  offending type, keeps going, and logs which assembly it came from. (#207)
- The Dependency Graph page painted every load-order target that is not a FUSE
  data package as red `MISSING`, including installed asset-only packs and
  code-only plugins that satisfy the dependency, and the optional load-order
  hints on legacy-converted packages that the loader deliberately ignores.
  Installed asset/plugin mods now show `PRESENT`, optional legacy hints show
  `NOT INSTALLED (optional hint)` in grey, and only real missing requirements
  stay red. The matching "ignored legacy order reference" log lines are now
  informational instead of warnings. (#207, #223)
- Saves lost cars to "orphaned car" prompts (`PrefabStore UnknownIdentifierException`)
  and LLW tender swaps were missing for LLW locomotives, whenever the missing
  definitions were LegosLibraryOfStuff clones (repaint liveries, tender-swap
  variants) of cars that live in *mod* asset packs. FUSE mounts mod packs
  directly and loaded them through its own Newtonsoft path, which never passed
  through the game's `ContainerSerialization.Deserialize` entry point — the
  method LegosLibraryOfStuff hooks to inject its clones — so clones of vanilla
  cars existed but clones of mod-pack cars never did. The first load of each
  directly mounted pack per map load now goes through the game's entry point,
  exactly like a natively loaded pack; every re-deserialize FUSE does afterwards
  still bypasses it, so old-loader edits are applied once and per-car component
  toggles are unaffected. A pack whose only problem is a component kind from a
  missing library mod is retried through the entry point with just that
  component dropped, so it keeps its clones. As a consequence, LegosLibraryOfStuff
  in-place edits (and any other mod's `Deserialize` patch, e.g. BellsAndWhistles'
  connector extraction) now apply to mod asset packs the same way they do with
  AssetLoader-native loading. New `DirectStoreNativeDeserialize` setting (default
  on) restores the old behaviour if ever needed. (#224, #222)

## [1.0.2]

### Added

- The FUSE Mods page now lists hosted legacy plugins, including code-only
  packages that ship no data files, so a legacy mod running under FUSE is
  visible rather than silently absent. Hosted instances are de-duplicated by
  folder and then by id, and a data snapshot wins over a hosted entry on a
  folder collision.
- `docs/` is mirrored to the GitHub Wiki automatically on merge to `main`
  (`.github/workflows/sync-wiki.yml`). The repository stays the source of
  truth — pages edited in the wiki UI are overwritten on the next sync.

### Changed

- The source `FUSE/Info.json` is pinned at `0.0.0` and no longer tracks the
  latest release. Builds without `-p:ModVersion=...` skip stamping and mirror
  source, so `0.0.0` in Unity Mod Manager now reliably means "local or debug
  build", and a release always shows its real version. It had briefly been
  bumped to `1.0.0`, which made dev builds indistinguishable from releases.
- The Nexus mod page description links to the repository, releases, and the bug
  report form, now that the repository is public.

### Removed

- The `sync-info-json` workflow, which committed the released version back into
  the source manifest. It contradicted the pinned `0.0.0` rule above, and had
  never once completed: first because a release created with the default
  `GITHUB_TOKEN` does not trigger `release: [published]`, then because `main`
  became a protected branch and rejected its push.

## [1.0.1]

### Added

- `FUSE-Complete-v<ver>.zip` on GitHub releases: the core mod, the Live Bridge,
  and the converter/installer/folder-converter tools in one download, laid out
  so `FUSE-Installer.exe` can consume it directly as a multi-package zip.
- Both mod zips now carry `LICENSE`. FUSE ships under the AGPL-3.0, which
  requires conveying the license with the binary, and the package validator
  fails the release if it is missing.
- Bug reports and feature requests are filed through GitHub issue forms that
  require the diagnostics `docs/TROUBLESHOOTING.md` already asked for — FUSE and
  Railroader versions, reproduction steps, `/fuse.report` output, and
  confirmation that `FUSE.log` and `Player.log` are attached. Blank issues are
  disabled.
- `docs/INSTALL.md`, covering installation of the core mod only, with the
  optional authoring tools separated out.

### Changed

- Nexus receives the core mod zip and nothing else. A player installing from
  Nexus needs one file; the converter, installer and development bridge are
  published on GitHub for package authors.
- Release candidates publish as full GitHub releases rather than prereleases, so
  the newest RC carries the "Latest" badge and testers land on it. Any other
  prerelease suffix (`-beta.2`, `-alpha.1`) still publishes as a prerelease.
- The README's user-facing half mirrors the Nexus page section for section, with
  full install steps inline.

### Fixed

- Passenger stop neighbors are reconciled after a refresh instead of being
  dropped. Neighbor ids now resolve against the completed live registry by
  identifier or timetable code, ignoring case and surrounding whitespace.
- Legacy asset-pack identifiers keep their exact form. A mod alias is no longer
  applied when a base-game store already owns the incoming identifier, legacy
  and bare forms normalize to the base-game form, and the FUSE direct-store
  scheme is preserved.
- The package validator no longer reports a stale result. `dotnet run <file>.cs`
  caches the compiled script by path, and on the long-lived self-hosted runner
  the release gate had been validating with a previous build of its own rules.

## [1.0.0]

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

## [0.18.0] - 2026-07-29

A performance, VRAM, and load-race pass covering a complete optimization cycle.
The reference save is the 204-car E&A/Bryson install used throughout the
investigation.

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

## 0.10.0 – 0.17.0

Development history for the 0.x series before per-version changelog entries were
kept. Everything below shipped at some point across these releases; the entries
are not attributed to a specific version. Use `git log` between the `mod-v*` tags
for exact attribution.

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
- Added route, map tile, asset pack, and audio pack conversion coverage for the current corpus.

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

- `RAIL` naming has been superseded by `FUSE`. Reconvert packages for a clean install.
- Converter output should be regenerated with the matching converter version when schema/runtime behavior changes.

[Unreleased]: https://github.com/F-U-S-E-E/FuseDevelopmentGroup/compare/mod-v1.0.2...HEAD
[1.0.2]: https://github.com/F-U-S-E-E/FuseDevelopmentGroup/compare/mod-v1.0.1...mod-v1.0.2
[1.0.1]: https://github.com/F-U-S-E-E/FuseDevelopmentGroup/compare/mod-v1.0.0...mod-v1.0.1
[1.0.0]: https://github.com/F-U-S-E-E/FuseDevelopmentGroup/compare/mod-v0.18.0...mod-v1.0.0
[0.18.0]: https://github.com/F-U-S-E-E/FuseDevelopmentGroup/releases/tag/mod-v0.18.0
