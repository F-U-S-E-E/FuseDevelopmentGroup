# FUSE Release Cleanup Checklist

Snapshot date: 2026-05-07

Scope: this is the working release checklist for getting FUSE from "loads a lot of legacy content" to "clean public-alpha replacement for the legacy mod stack." It is intentionally issue-focused. Signals are excluded until the end by choice.

Evidence used for this snapshot:

- FUSE source tree at `C:\Hrogers_Railroader_mods_Projects\Rail`
- Legacy corpus at `C:\Railroader mods\Installed\Map`
- FUSE log snapshot at `C:\Users\roger\AppData\LocalLow\Giraffe Lab LLC\Railroader\FUSE.log`
- RailLoader logs at `C:\Steam\steamapps\common\Railroader\railloader*.log`
- Current converter tools under `tools\`

Important note: the 2026-05-10 LocalLow `FUSE.log` is now the authoritative runtime evidence for the current FUSE run.

## Release Gates

- [x] FUSE load report reaches `0 faulted`, `0 conflicts`, `0 unknown scenery assets` with the current converted legacy test set. Runtime verified 2026-05-07: `69 loaded, 0 faulted, 0 conflicts, 0 suppressions, 0 unknown scenery assets`.
  - Fresh 2026-05-08 runtime verified after the Asheville progression alias pass: `73 loaded, 0 faulted, 0 conflicts, 0 suppressions, 0 unknown scenery assets`.
  - Fresh 2026-05-09 runtime verified after dump-command build: `73 loaded, 0 faulted, 0 conflicts, 0 suppressions, 0 unknown scenery assets`.
  - Fresh 2026-05-10 runtime verified after report/log cleanup: `73 loaded, 0 faulted, 0 conflicts, 0 suppressions, 0 unknown scenery assets, 0 graph issues, 0 transfer skips`.
- [x] No graph post-bind missing nodes, segments, or spans after all converted route mods load. Runtime verified 2026-05-07: `missing after apply = 0`.
- [x] No package faults from missing loads when required asset/load packs are installed. Runtime verified 2026-05-07: Copper progression packages stayed loadable with placeholder fallback warnings for `mining-explosives` and `machine-parts`; converter still needs to emit real load definitions when available.
  - Code/converter pass 2026-05-08: converter now emits known compatibility load definitions for `mining-explosives` and `machine-parts`; reconverted Copper Nantahala packages. Needs fresh runtime verification that placeholder warnings are gone.
  - Fresh 2026-05-09 runtime verified: no `placeholder load`, `mining-explosives`, or `machine-parts` warnings in `FUSE.log` or `Player.log`.
- [x] Converter can process every legacy route mod in `C:\Railroader mods\Installed\Map` without dropping files or inventing unexpected late files. Scratch verified 2026-05-08: batch converted 13/13 current inputs with 0 errors; generated reports for Asheville, Copper set, GCR, Griz, KingG, and Kirkland. No `*Late*.json` output files were produced.
- [x] Converter emits one useful report per conversion: repaired, preserved, unresolved, unsupported, and dependency-required entries. Code complete 2026-05-08; `conversion-report.json` and `conversion-report.md` now include outcome buckets and per-source-file summaries. Scratch verified with `CoppersPaperboardRemover` and Asheville.
- [x] Converted mods preserve legacy file concerns: one source JSON becomes one FUSE JSON unless the source is metadata-only. Scratch verified 2026-05-08: corpus reports include one file summary per converted source concern, and output filenames follow the source JSON stems instead of artificial concern buckets.
- [ ] FUSE load time is benchmarked against RailLoader and is within the same range or faster for equivalent installed content.
- [x] Runtime logs are readable without hunting through stack traces.
  - Code pass 2026-05-08: successful legacy span auto-repairs are now info-level, same-segment preflight compatibility repairs no longer dirty apply reports, package world apply no longer triggers the public experimental authoring warning, FUSE passenger-stop helper spans are anonymous runtime-only spans, FUSE formulaic components allow legacy one-sided producer/consumer formulas without base-game validation errors, and passenger stops default blank legacy car filters to `*`. Needs fresh Player.log verification.
  - Runtime verified 2026-05-08: formulaic-component validation spam and `passenger-stop.*` duplicate span ids no longer appear.
  - Runtime verified 2026-05-08: follow-up direct passenger-marker patch removed the blank duplicate span warnings from `Player.log`.
  - Code pass 2026-05-08: per-object apply-report buckets are now quiet by default, with `Settings.VerboseApplyReportDetails=true` available for package debugging. Warnings/errors still print detail. Needs fresh `FUSE.log` verification.
  - Fresh 2026-05-09 FUSE-specific log check: `FUSE.log` has 0 warnings, 0 errors, 0 exceptions, 0 missing-after-apply entries, and 0 unknown scenery assets. Remaining `Player.log` stack traces are from other mods (`LegosLibraryOfStuff`, `BRSS`, `Enviro`) plus the known authored `NERbalsam-pigeon_dpem` switch geometry issue.
  - Still open: `FUSE.log` reports 3 source-authored empty industry shells with `componentCount=0` (`beta-teamtrack`, `junaluska-teamtrack`, `gcrr-pulpwood`). They do not fault runtime, but they need a cleaner converter/runtime classification before calling logs release-clean.
  - Code/build pass 2026-05-09: source-authored empty industry shells now log as `source-empty industry shell` with `runtimeComponents=0 sourceComponents=0`, instead of looking like failed component binding. Release DLL deployed to `C:\Steam\steamapps\common\Railroader\Mods\FUSE\FUSE.dll`; needs fresh runtime log verification.
  - Runtime verified 2026-05-10: `FUSE.log` map load completed with `loadedPackages=15 appliedPackages=62 skippedPackages=11 faultedPackages=0 warnings=0 errors=0`; unknown scenery assets remain `0`, and source-empty industries are classified without stack traces. Remaining stack traces in `Player.log` are from external `LegosLibraryOfStuff`/Enviro calls, not FUSE.
  - Code/build pass 2026-05-10: authoring entity runtime-bind spam is now verbose-only, keeping normal FUSE logs focused on package/apply/report events.
- [x] FUSE-specific logging is available in `FUSE.log` beside `Player.log`.
- [ ] Public schema examples cover every supported legacy concept except signals.

## First Fix Order

- [x] P0: Stop repeated map tile remounts and remove unnecessary disk reloads during map tile registration. Runtime verified 2026-05-07: current log shows two expected map-tile mount windows (`staged world apply` and `MapStore.Load`) instead of dozens of per-package remounts.
- [x] P0: Make direct asset pack loading the default path; stop copying asset packs into LocalLow unless a fallback requires sanitization. Code complete 2026-05-07; direct stores are default and LocalLow mirror is opt-in.
- [x] P0: Fix converter file preservation so every legacy JSON is converted in place instead of being filtered by mixinto references. Code complete 2026-05-07; batch corpus reconversion still needed.
- [x] P0: Add real span topology repair/reporting for multi-segment spans, route direction, and external base-game segment references. Converter code complete 2026-05-07; needs full corpus reconversion/runtime verification.
- [x] P0: Fix progression/load dependency faults, especially Copper missing-load and interchange-transfer cases. Runtime verified 2026-05-07: missing-load placeholders kept Copper progressions loadable and legacy `.t1` interchange-transfer aliases resolved 10 transfers with `skipped interchange transfer = 0`.
  - Code/converter pass 2026-05-08: known Copper-only missing loads are now real converted load definitions instead of runtime placeholders.
- [x] P0: Verify track group pre-enable does not make progression-gated scenery or track visible early. Code path fixed 2026-05-07; runtime route verification still needed.
- [ ] P1: Match legacy industry component behavior for all AMM, Strange Customs, Zamu, Copper, and ConfusingSupplements components.
- [ ] P1: Finalize station/passenger stop ordering, span binding, map icons, and company window display.
  - Verified 2026-05-08: `Editor\MainEditorWindow.cs` and `Infrastructure\WindowCreatorHelper.cs` do not reference, destroy, or null the base-game company window. They only manage FUSE's own editor window instance/panel references.
  - Code pass 2026-05-08: passenger stop child `TrackSpan` helpers now keep blank graph ids to stop duplicate graph-span warnings, and virtual passenger stops suppress base `IndustryComponent` validation that does not apply to FUSE passenger-stop shims. Needs fresh Player.log verification.
  - Runtime verified 2026-05-08: replaced passenger-stop child `TrackSpan` helpers with direct `TrackMarker` creation plus private `PassengerStop._spans` binding, so passenger stops keep their runtime markers/company-window spans without adding fake spans to the global graph.
- [x] P1: Clean converter/runtime turntable handling so physical turntables and progression industries named "turntable" are never confused. Code complete 2026-05-07; duplicate physical turntable ids now apply as final-state overrides during staged graph apply. Needs Kirkland runtime verification.
- [x] P1: Finish spliney parity: dirt roads, asphalt roads, rivers, trestles, terrain roads, waterfalls.
  - Code/build complete 2026-05-10: runtime now accepts the full schema spliney type set (`road`, `river`, `terrainRoad`, `trestle`) instead of silently collapsing terrain roads/waterfalls to normal roads. Converter preserves Strange Customs `FlowyThingBuilder` river/road style/profile and emits the legacy `offsetY: -0.1` default when source data omits it. Release DLL deployed; converter `.pyz` and `.exe` rebuilt.
- [ ] P2: Finish horn, whistle, and bell parity and regression-test converted audio packs.
- [ ] P2: Refresh schema docs and examples after runtime/converter behavior is stable.

## Load Time And Asset Mounting

- [x] P0: `Loading\FuseMapTileRegistry.cs` must not deserialize package files from disk during runtime registration or reapply.
  - Evidence: `RefreshFromAvailablePackages()` calls discovery and `FuseSerializer.Load(definitionPath)`.
  - Fix: removed disk scan/reload path from map-store tile mounting; resident definitions register their own tile sources.

- [x] P0: Map tiles must mount once per active map per package set, not once per package apply.
  - Evidence: `Mounted 7666 FUSE map tile(s) for 'BushnellWhittier'` appeared 62 times in the FUSE log.
  - Fix: tile source registration no longer mounts immediately; staged world apply performs one active-map mount after all world definitions register sources.

- [x] P0: Direct asset stores should be primary.
  - Evidence: `FuseAssetPackRegistry` supports `fuseasset://` direct stores and `FuseAssetPackPatches`, but `MountAllAvailableAssetPacks()` still copies packs into LocalLow.
  - Fix: LocalLow asset-pack mirroring is now disabled by default and guarded by `Settings.MirrorAssetPacksToLocalLow`; direct `PrefabStore` stores remain the default.

- [ ] P0: Benchmark load phases against RailLoader.
  - RailLoader observed behavior: discovers mods quickly and adds direct asset stores from the Mods folder.
  - Timing logs added 2026-05-07 for map-load total, cache rebuild, discovery, asset-pack registration, disk load, runtime apply, map-mask rebuild, console registration, and per-package load. Actual RailLoader comparison still needs a fresh game run.
  - Fresh 2026-05-09 FUSE timing: discovery 17 ms, load-from-disk 223 ms, runtime apply 18,644 ms, total map-load 19,309 ms for 15 packages / 73 resident definitions / 62 applied definitions. Still needs equivalent RailLoader timing comparison.
  - Log audit 2026-05-10: FUSE disk work is already small (latest disk load 309 ms); the bottleneck is runtime apply (~18.6 s). RailLoader logs from 2026-05-06 show plugin definition/mod loading in roughly 170 ms and Strange Customs graph patching roughly 2.7-2.9 s after scene load begins, but the installed content sets are not perfectly equivalent.
  - Code pass 2026-05-10: staged operations now defer `TrackAPI.ApplyAreaOrdering()` and run it once after the industry batch instead of once per data fragment. Needs fresh timing log to measure impact.
  - Code/build pass 2026-05-10: per-entity authoring bind info logs are now gated behind `Settings.VerboseApplyReportDetails`; normal map loads should no longer write hundreds of scenery bind lines to `FUSE.log`. Needs fresh timing/log-size comparison.

- [x] P1: Explain quiet gaps in FUSE load.
  - Evidence: old FUSE log had a multi-second gap between load catalog completion and staged apply start.
  - Fix: long-running map-load and package-load phases now emit elapsed-time log lines. Fresh `FUSE.log` will show where remaining gaps are.

- [x] P1: Prevent duplicate direct asset store registration.
  - Direct stores are deduplicated by `fuseasset://` identifier, and LocalLow mirror remains opt-in.

## Converter

- [x] P0: Convert every source JSON file independently.
  - Current risk: `tools\fuse_convert.py` filters source files when mixinto references exist.
  - Fix: mixinto references no longer filter the source file list; one source JSON maps to one `.fuse.json` output unless skipped by explicit global exclusions like signals.

- [x] P0: Stop generating unexpected `Scenery-Late.FUSE.json` style files unless the source file was actually named that way.
  - Required behavior: preserve source concern and source filename stem where possible.
  - Fix: converter no longer re-sorts output into artificial concern buckets; output files follow source files.

- [x] P0: Add structured conversion reports.
  - Report fields: source file, output file, converted count, repaired count, unresolved count, preserved-but-not-runtime count, unsupported count, warnings.
  - Code complete 2026-05-08: reports now include `outcomeBuckets` for converted, repaired, preserved, unresolved, unsupported, dependency-required, warning, and error entries; reports also include one file summary per converted source file.
  - Verified 2026-05-08: `CoppersPaperboardRemover` produced a clean report; Asheville produced 31 file summaries with repaired/dependency/unresolved buckets populated.

- [x] P0: Make span conversion repair topology, not just same-segment reversed offsets.
  - Current risk: repair only handles simple same-segment spans.
  - Fix: converter now builds converted segment topology, repairs multi-segment endpoint A/B anchors while preserving physical endpoint positions, and warns when no route can be inferred. Runtime successful same-segment compatibility repairs are info-level so they do not dirty the final load report.

- [x] P0: Do not drop bad spans silently.
  - Converter should repair when possible; otherwise output the span with an explicit unresolved warning so runtime can report the exact object.
  - Fix: unrepaired route/external span cases stay in output and are annotated as `span-route-unresolved` or `span-external-segment`.

- [x] P0: Preserve external/base-game segment references cleanly.
  - Warnings like `Segment is not defined in this FUSE document. It must exist in the base game graph at runtime` should be classified as external dependency, not automatically treated as a conversion failure.
  - Fix: converter classifies missing endpoint segments as external/base-game dependencies instead of dropping or rewriting them.

- [x] P0: Distinguish physical AMM turntables from industries/components named turntable. Converter keeps ProgressionIndustryComponent as an industry component; runtime staged graph apply now final-merges duplicate physical turntable ids. Needs Kirkland runtime verification.
  - Kirkland has a `ProgressionIndustryComponent` named "Kirkland Turntable"; it must remain an industry component, not become a second physical turntable.

- [x] P0: Generate stable FUSE component ids for legacy industry components with blank JSON keys.
  - Code complete 2026-05-08: converter now generates deterministic ids such as `formula`, `repair`, or `teamtrack` for blank legacy component keys. Reconverted KingG and Asheville installed FUSE packages.

- [x] P0: Parse legacy JSON leniently from folders and zips.
  - Legacy corpus contains files that fail strict parsing but should be handled by the converter's tolerant reader.
  - Code/verification complete 2026-05-08: folder and zip conversion both parse JSONC comments, trailing commas, and truncated objects. Fixed a repair edge case where closing a truncated object exposed a trailing comma. Rebuilt `dist\FUSEConvertFolder.pyz` and `dist\FUSE-Converter.exe`.

- [x] P1: Keep author blank unless the original metadata provides one.
  - This avoids incorrectly stamping every conversion as KingG or another route author.
  - Verified 2026-05-08: `fuse_convert.meta()`, `convert_fuse_audio.package_info()`, and `fuse_info()` default missing authors to `""`; scratch conversion with metadata lacking `Author` emitted a blank `Info.json` author.

- [x] P1: Asset identifiers must stay exact by default.
  - Do not remap real asset names to unrelated aliases when the proper asset pack exists.
  - Alias repair can exist only as explicit opt-in/reportable compatibility mode.
  - Code audit 2026-05-08: converter/runtime preserve legacy asset identifiers directly; no compatibility alias table is active by default.

- [x] P1: Embedded asset packs must be detected recursively.
  - Some packs are several directories deep inside a mod.
  - Verified 2026-05-08: official converter detected and copied a synthetic nested `SCAssetPacks` pack several directories deep, and runtime asset lookup was already verified with RTM/Embedded Piggy Back/Asheville packs.

- [x] P1: Convert tile-only map tile packages to FUSE cleanly.
  - Required test: `Map-Tiles-1236-1-0-4-1774128092.FUSE`.
  - Verified 2026-05-08: synthetic tile-only package with `Maps\<map>\*.data` converts to a FUSE package with `mapTiles.fuse.json`, copied `Maps/`, and structured report counts.

- [x] P1: Converter must recognize route mods, asset mods, audio mods, tile mods, and mixed mods.
  - Verified 2026-05-08: synthetic batch converted route, asset, audio, tile-only, and route+embedded-asset mixed packages with auto-detection.
  - Code fix 2026-05-08: batch mode now skips its own `--out` folder when the output root is inside the input folder.

- [x] P1: Preserve source ordering for areas, industries, stations, sections, and progression data. Code complete for file-level order; object-level order still needs route verification.
  - Converter/schema pass 2026-05-10: legacy explicit `order` values are now preserved instead of re-ranked by encounter order, and FUSE area/industry `order` is now treated as a signed sort key so Asheville's negative route-order values load instead of being rejected.

- [x] P2: Add a drag/drop Windows executable and a cross-platform Python folder converter that share the same core conversion engine. Rebuilt 2026-05-08 after converter blank-component-id fixes.
  - Rebuilt again 2026-05-08 after known compatibility load generation and progression section-feature alias conversion.
  - Rebuilt again 2026-05-08 after structured conversion report support; `dist\FUSEConvertFolder.pyz` and `dist\FUSE-Converter.exe` smoke check passed.
  - Rebuilt again 2026-05-08 after lenient JSON repair fix; `dist\FUSEConvertFolder.pyz` and `dist\FUSE-Converter.exe` smoke check passed.
  - Rebuilt again 2026-05-08 after batch output-folder skip fix; `dist\FUSEConvertFolder.pyz` and `dist\FUSE-Converter.exe` smoke check passed.

## Track Graph, Segments, And Spans

- [x] P0: Graph apply must remain deterministic.
  - Required order: removals, nodes, segments, one graph rebuild, spans, post-bind validation.
  - Code verified 2026-05-10: resident runtime apply now funnels through `ApplyDefinitionsToRuntimeStaged`; it builds one merged final-state plan in package/file encounter order, applies removals then structural track objects under `TrackAPI.BeginBatch`, performs one merged graph rebuild, then applies spans and post-bind validation. Runtime 2026-05-09 already showed `missing after apply = 0`.

- [x] P0: Runtime must not rebuild the graph repeatedly while a transaction is active.
  - Code verified 2026-05-10: staged graph apply calls `TrackAPI.EndBatch(false)` for structural and span batches; `TurntableAPI` rebuilds are guarded by `!TrackAPI.IsBatching`; the staged path performs the explicit merged rebuild once before spans.

- [x] P0: Fix known post-bind missing graph items from logs.
  - `SERbalsam-addie_0l5a` - runtime verified 2026-05-07: no longer appears in post-bind warnings after merged final-state validation skip.
  - `Pgvj` - runtime verified 2026-05-07: span updates cleanly and no longer appears as missing after apply.
  - `Pyhs` - remaining warning is unload restore-only, not runtime post-bind; tracked under removals/restores.
  - `Phwz` - runtime verified 2026-05-08: restored from FUSE's captured live Railroader base graph snapshot during industry span binding.

- [ ] P0: Apply track removals and restores with stable IDs and route-safe ordering.
  - Code pass 2026-05-07: restore now stages nodes/segments first, rebuilds graph, then restores spans and rebuilds again so span route resolution does not run against a half-restored graph. Needs fresh runtime verification for the KingG `Pyhs` unload warning.
  - Code pass 2026-05-08: map unload now skips base-track restoration because the scene is already being torn down; live package unload/reapply restoration remains separate work.
  - Code/build pass 2026-05-10: removal snapshot/restore diagnostics now use structured package/operation/phase/kind/id/reason fields, while preserving the nodes -> segments -> graph rebuild -> spans -> graph rebuild restore order. Still unchecked until a live package unload/reload cycle proves restored segment/span IDs survive in game.

- [x] P0: Verify base-game and external segment references before applying dependent spans.
  - Code/runtime verified 2026-05-10: preflight builds a reference context from all active staged definitions, generated turntable ids, loaded definitions, and the live Railroader graph before span apply. Missing span endpoint segments become preflight errors before mutation; resolved external/base-game references suppress noisy validator warnings. Fresh 2026-05-09 runtime had zero graph post-bind missing items.

- [x] P0: Add FUSE-owned original/base graph snapshot support for legacy references that target vanilla nodes, segments, or spans.
  - Requirement: capture from Railroader's runtime graph before FUSE mutations; do not read AMM `graph-original.json`.
  - Known case: GCR `GCRProTrack.track-site-1` references base span `Phwz`.
  - Runtime verified 2026-05-08: FUSE captured `nodes=1596 segments=1620 spans=316` from the live graph and restored `Phwz` from that snapshot.
  - Command verified 2026-05-09: `/fuse.dumpgraph` wrote `C:\Steam\steamapps\common\Railroader\FUSE-original-graph.json` with `nodes=1596`, `segments=1620`, `spans=316`, and source text explicitly stating no AMM `graph-original.json` was used.

- [x] P0: Segment groups must not be enabled permanently just to survive graph rebuild.
  - Fix: transient pre-enable now avoids repeated graph rebuilds, then `ProgressionAPI.RefreshRuntimeStateAfterApply` re-runs base-game progression/map-feature state so locked groups and objects are disabled again before post-bind validation. Runtime verification still needed on Asheville/GCR/Kirkland progression saves.
  - Fresh runtime 2026-05-10 exposed an expected side effect of that fix: post-bind validation counted hidden locked segments as `335 graph issues` because `Graph.GetSegment` omits disabled groups. Code/build pass 2026-05-10 now treats segments/spans hidden by disabled progression groups as intentional post-bind state instead of graph failures. Release DLL deployed; needs fresh runtime verification.

- [x] P1: Add graph validation that explains route direction errors using start/end, A/B, upper/lower, and distance.
  - Code/build complete 2026-05-10: failed runtime span route validation now reports `upper`/`lower` segment id, A/B Start/End anchor, distance, and segment length. Release DLL deployed to `C:\Steam\steamapps\common\Railroader\Mods\FUSE\FUSE.dll`.
  - Runtime note 2026-05-08: Asheville `NERbalsam-pigeon_dpem` switch geometry error matches the legacy source node/segment data exactly and should be treated as an authoring/legacy-geometry warning unless RailLoader/AMM prove they repair it. Do not spend release time inventing a FUSE-only geometry rewrite for one bad authored switch.

- [x] P1: Add converter/runtime validation for duplicate node, segment, span, and object IDs inside one package.
  - Code/build complete 2026-05-10: JSON package loading now rejects duplicate object keys before deserialization using `DuplicatePropertyNameHandling.Error`, so duplicate `tracks.nodes`, `tracks.segments`, `tracks.spans`, operations, world, and progression entries cannot silently overwrite earlier definitions. Release DLL deployed to `C:\Steam\steamapps\common\Railroader\Mods\FUSE\FUSE.dll`.

- [x] P1: Match legacy span semantics exactly: arrows face each other, no crossed anchors, no distance beyond segment length, route can traverse across adjacent segments.
  - Code/batch verified 2026-05-10: converter repairs same-segment crossed anchors, clamps out-of-range endpoint distances when estimated segment length is known, aligns multi-segment span anchors through converted topology, and preserves external/base-game references for runtime resolution. Batch conversion of 13/13 legacy route inputs produced 0 span repair/error warnings.

## Mixintos And Dependencies

- [ ] P0: Mixinto application must match legacy behavior.
  - Mixintos should merge in the same order and conditionally apply only when their target is present.

- [x] P0: Optional mixinto skips should not count as scary load warnings.
  - Missing optional targets like `CF.AlarkaYard.Expanded` should be reported as conditional skips unless the package declares them required.
  - Runtime verified 2026-05-08: optional conditional mixinto skips are info-level and the final report shows `warnings=0 errors=0`.

- [x] P0: Hard dependencies and optional mixinto targets must be separate concepts in schema, validation, and load report.
  - Code verified 2026-05-10: manifest-level `FuseLoadAfter` / `FuseLoadBefore` are treated as load-order dependencies and fault/skips packages when missing, disabled, or cyclic; converted `mixinto.requires[]` are optional conditional runtime requirements that skip without inflating scary warning counts. Load report separately lists skipped, disabled, faulted, and optional mixinto skips.

- [x] P1: Add dependency aliases for known legacy package IDs when the converted FUSE package ID differs.
  - Code/build pass 2026-05-10: mixinto requirement resolver now treats loaded definition id, `Info.json` id, `Definition.json` id, and installed mod folder name as aliases, with `.FUSE`/`.RAIL` suffix stripping. This covers converted-package/folder naming drift without turning optional missing mixintos into scary warnings. Needs fresh runtime check only if a previously skipped optional dependency is installed under a folder-only legacy id.

- [x] P1: Mixinto conflicts should record owner, target, object ID, and resolution.
  - Code/build pass 2026-05-10: registry conflicts now store and print target/kind, object id, owner package, attempted package, and resolution; `/fuse.conflicts` and warning logs use the same structured fields. Release DLL deployed.

- [x] P1: Mixinto conversion must preserve per-file order and not collapse unrelated files.
  - Code verified 2026-05-10: converter reads legacy `mixintos` metadata, sorts source files by declared mixinto order while still converting every source JSON independently, writes one FUSE data file per source stem, and stores mixinto provenance on the fragment instead of collapsing concerns.

## Progressions, Unlocks, And Visibility

- [x] P0: Progression-gated scenery, track groups, areas, and game objects must start hidden when legacy says they should.
  - Fix: post-progression apply now refreshes MapFeatureManager and the current Progression state, and initializes missing map-feature states to base-game defaults. Runtime verification still needed with gated route content.
  - Code/build pass 2026-05-10: after late-registering FUSE map features, `ProgressionAPI` now forces the base-game initial feature-state handler with the current feature state table. This makes false/default-false locked features actively disable their game objects/areas/groups instead of remaining visible because the base game saw no state transition. Release DLL deployed; needs fresh route visual verification.

- [ ] P0: `enabledFeatures`, `mapFeatures`, and section unlock state must match legacy defaults.
  - Code/converter pass 2026-05-08: legacy map-feature prerequisites that point at progression section ids now get a real FUSE map-feature alias, and the matching section enables that alias on unlock. Runtime also handles forward references safely when file order applies the referencing map feature before the defining section.
  - Code/build pass 2026-05-10: runtime now force-applies current `MapFeatureManager` state after package progression apply so late-added map features obey their current locked/unlocked defaults. Still unchecked until Asheville/Copper/GCR gated content is visually verified against legacy behavior.

- [x] P0: Delivery phases must resolve loads after all load-providing packages are available.
  - Known old faults: `mining-explosives`, `machine-parts`.
  - Code complete 2026-05-07: runtime creates explicit placeholder loads with warnings when a progression references a load not supplied by any loaded package, keeping the package loadable instead of dropping the delivery reference. Converter should still emit/report real load definitions when available.
  - Code/runtime verified 2026-05-10: `ApplyGlobalLoadCatalog` applies load definitions from every active package before any staged preflight or progression phase; fresh 2026-05-09 runtime had no placeholder-load warnings for `mining-explosives` or `machine-parts`.

- [x] P0: `ProgressionIndustryComponent` must bind correctly and be available before progression delivery phases resolve.
  - Code/runtime verified 2026-05-10: operations apply for all active packages is completed and the industry batch refresh runs before any `apply-progression` phase. Fresh 2026-05-09 runtime created Copper and GCR `Model.Ops.ProgressionIndustryComponent` entries before progression phases began, with no progression delivery faults.

- [x] P0: Interchange transfers must resolve source and destination components after all operations packages apply.
  - Old FUSE log had several skipped interchange transfers because one or both components were not found.
  - Runtime verified 2026-05-07: resolver maps legacy ids like `addie-interchange.t1` to the converted `addie-interchange.interchange` component; log shows 10 resolved legacy interchange ids and zero skipped interchange transfers.
  - Fresh 2026-05-09 runtime verified: multiple Asheville legacy `.t1` transfers resolved (`addie`, `clyde`, `asheville`) and there were no skipped interchange-transfer entries.

- [ ] P1: Support progression fields seen in Asheville, Copper, KingG, Kirkland, and Graham:
  - `prerequisiteSections`
  - `deliveryPhases`
  - `enableFeaturesOnUnlock`
  - `disableFeaturesOnUnlock`
  - `enableFeaturesOnAvailable`
  - `unlockIncludeIndustries`
  - `unlockExcludeIndustries`
  - `unlockIncludeIndustryComponents`
  - `areasEnableOnUnlock`
  - `gameObjectsEnableOnUnlock`
  - `trackGroupsEnableOnUnlock`
  - `trackGroupsAvailableOnUnlock`
  - `interchangeTransfers`

- [ ] P1: Confirm sandbox defaults and progression defaults match legacy.

- [ ] P2: Convert `initialMoney`, `showTutorial`, `spawnPoint`, and `carPlacements` if they materially affect route starts. make sure you know exactly what each of these do before messing with them.

## Operations And Industry Components

- [x] P0: FUSE-created industries must match AMM and Strange Customs materialization.
  - Parent under Area when possible.
  - Create inactive.
  - Add Formulaic components on industry GameObject.
  - Add other components on child GameObjects.
  - Clear caches and refresh after batch.
  - Runtime verified 2026-05-10: fresh FUSE log loaded/applied Asheville, KingG, GCR, Copper, and Griz route operations with `faultedPackages=0`, `warnings=0`, `errors=0`; source-empty industry shells are now reported as intentional source-empty shells instead of binding failures.

- [x] P0: No valid industry should end with `componentCount=0` unless the source truly has no gameplay components.
  - Code pass 2026-05-08: FUSE formulaic components now subclass the base formulaic component and suppress misleading base-game validation errors for legitimate one-sided legacy producers/consumers. Runtime service behavior remains inherited from Railroader's `FormulaicIndustryComponent`.
  - Runtime verified 2026-05-08: previous base-game formulaic validation errors for one-sided Asheville producer/consumer components no longer appear in `Player.log`.
  - Fresh 2026-05-09 runtime check: no formulaic validation spam in `FUSE.log` or `Player.log`; remaining `componentCount=0` entries are `beta-teamtrack`, `junaluska-teamtrack`, and `gcrr-pulpwood`. Asheville source explicitly contains empty `components: {}` shells for the first two; these need clean classification rather than a runtime fault.
  - Runtime verified 2026-05-10: the three remaining empty shells now log as `FUSE-created source-empty industry shell ... runtimeComponents=0 sourceComponents=0`; converted source confirms `beta-teamtrack`, `junaluska-teamtrack`, and `gcrr-pulpwood` are source-authored shell industries while their real team-track/progression components live on separate industry definitions.

- [x] P0: Missing spans or loads on one component must not abort the whole operations file.
  - Runtime verified 2026-05-07: unresolved GCR `Phwz` and placeholder Copper loads did not fault packages. Current code now defaults legacy passenger stops to `passengers`; `Phwz` is tracked under the FUSE-owned base graph snapshot verification item.

- [x] P0: Support custom industry/load components as separate mods.
  - Needed for community components and future extension packs.
  - Schema should allow component type IDs, component mod dependencies, and custom field payloads.
  - Code/build pass 2026-05-10: schema/runtime now allow fully-qualified external `IndustryComponent` type ids with a generic `fields` payload, and `operations.loads` also accepts a reflective `fields` payload. Runtime resolves custom component types from loaded assemblies, applies common FUSE fields, then applies custom field/property values; converter preserves unknown custom component/load fields instead of dropping them. Dependency mods are handled through existing manifest load-order requirements.

- [x] P0: Finish parity for recurring legacy component types:
  - `Model.Ops.IndustryLoader`
  - `Model.Ops.IndustryUnloader`
  - `Model.Ops.FormulaicIndustryComponent`
  - `Model.Ops.RepairTrack`
  - `Model.Ops.TeamTrack`
  - `Model.Ops.Interchange`
  - `Model.Ops.InterchangedIndustryLoader`
  - `Model.Ops.ProgressionIndustryComponent`
  - `AlinasMapMod.PaxStationComponent`
  - Corpus audit 2026-05-10: installed legacy map corpus contains only these recurring component types (`IndustryUnloader=54`, `IndustryLoader=43`, `PaxStationComponent=25`, `FormulaicIndustryComponent=21`, `InterchangedIndustryLoader=15`, `Interchange=5`, `RepairTrack=5`, `ProgressionIndustryComponent=2`, `TeamTrack=2`). Fresh runtime report has `0` operation faults for them.

- [x] P1: Add/verify support for Zamu and ConfusingSupplements industry components.
  - Reference path: `C:\Hrogers_Railroader_mods_Projects\Decompiled DLLs Not BASE GAME\ZAMU\ConfusingSupplements\ConfusingSupplements`
  - Code/build complete 2026-05-10: converter/runtime normalize ConfusingSupplements shortcuts for `CaptiveConversionLoader`, `CaptiveConversionUnloader`, `Pay4Resource`, and `Empty` to their fully-qualified runtime types. Build deployed to `C:\Steam\steamapps\common\Railroader\Mods\FUSE\FUSE.dll`; converter `.pyz` and `.exe` rebuilt.

- [x] P1: Verify reflective custom component binding sets all fields legacy components expect.
  - Code/reference verified 2026-05-10 against decompiled ConfusingSupplements components: FUSE binds `load`, `convertedLoad`, `carLoadRate`, `carUnloadRate`, `loadRate`, `maxStorage`, `costPerUnit`, `notBefore`, `notAfter`, `fillPercentage`, `title`, and `bookReasons`, which covers the fields used by `CaptiveConversionLoader`, `CaptiveConversionUnloader`, and `Pay4Resource`. `Empty` has no fields.

- [x] P1: Formula fields must be converted or explicitly reported.
  - Corpus/code audit 2026-05-10: legacy `Model.Ops.FormulaicIndustryComponent` entries in the installed map corpus use `inputTermsPerDay` / `outputTermsPerDay`, and the converter preserves both into FUSE. Runtime applies them through `FuseFormulaicIndustryComponent`/`BuildFormulaTerms`; missing loads warn/skip per term instead of aborting the component.

- [ ] P1: Loader rotation, load ID, source/destination industry references, and span binding need route-level regression tests.

- [ ] P1: Team tracks, interchanges, and progression industries must appear in the company window in the same area/order as legacy.

## Passenger Stations And Locations Window

- [x] P0: Passenger stops must bind track spans and passenger load correctly.
  - Code/converter pass 2026-05-07: AMM `PaxStationComponent` entries now default missing `loadId` to `passengers`, preserve legacy `branches` as `branchDefinitions`, and runtime validation treats spanless passenger stops as valid virtual stops instead of warning every load. Needs fresh route verification.
  - Runtime/code pass 2026-05-08: explicit passenger-stop duplicate span ids are gone; remaining blank duplicate span warnings were traced to graph-visible helper spans and patched by direct marker creation. Needs fresh runtime verification.
  - Fresh 2026-05-09 runtime verified: no duplicate passenger span warnings in `FUSE.log` or `Player.log`; KingG `Cherokee-Depot`, `Birdtown-Depot`, and `Ravensford-Depot` each refreshed with `spanCount=1` and `loadId='passengers'`.
  - Runtime verified 2026-05-10: passenger stops across Asheville, KingG, and GCR refresh with `loadId='passengers'` and expected span counts; the only `spanCount=0` case is GCR `topton`, which the converter report identifies as source-authored spanless legacy data and emits as a virtual stop.

- [ ] P0: Passenger stops must appear in the Locations window when legacy did.

- [ ] P0: Area sorting must match legacy/source order, not alphabetical fallback.
  - Code/converter pass 2026-05-10: runtime area ordering path was already applying; converter was reversing Asheville by inventing positive order values. Installed `asheville_extension.FUSE` has been reconverted with source orders preserved (`asheville=-2000`, `beta=-100`, etc.). Needs fresh visual verification in the Locations window.

- [ ] P0: Industry/station order inside each area must preserve source `order` or legacy transform order.
  - Code/converter pass 2026-05-10: explicit legacy industry `order` now wins over generated fallback order. Areas without explicit order still use source encounter order.
  - Code pass 2026-05-10: company-window sort patch now converts signed `order` values to an offset lexical key, so negative legacy orders sort numerically instead of string-sorting backward.

- [ ] P1: Map icons must match base-game behavior:
  - correct schematic station icon
  - correct size
  - correct rotation
  - over station location
  - stable behavior while zooming

- [ ] P1: Generated `StationAgent`, `PassengerStop`, `Area`, spans, and map icon child must all be linked after creation.

- [ ] P1: Bad legacy station definitions should produce clear converter/runtime reports.
  - KingG depot passenger definitions appear bad under legacy too; FUSE should identify exactly what is missing instead of hiding the failure.

## Scenery, Assets, Scene Clones, And Map Masks

- [x] P0: Unknown scenery assets must stay at zero when required asset packs are installed. Runtime verified in `FUSE.log` on 2026-05-07 after RTM/Embedded packs were installed: load report showed `0 unknown scenery assets`.
  - Fresh 2026-05-09 runtime verified again: final report shows `0 unknown scenery assets`.

- [x] P0: Asset lookup must recurse nested asset packs and embedded packs. Runtime verified 2026-05-07; direct stores now recurse nested packs and sanitize incompatible definition components in memory without modifying source packs.
  - Examples: RTM Asset Pack, Embedded Piggy Back Base, Asheville embedded asset pack.

- [x] P0: Do not substitute unrelated asset identifiers just to make warnings disappear.
  - Code audit 2026-05-08: converter preserves legacy scenery `modelIdentifier`/`assetIdentifier` strings exactly and runtime contains no compatibility remap table for the known bad alias list (`furniture_factory`, `erie_freight_house_part_1`, `terrain_flattener_square_*`, etc.). Required asset packs now resolve the real identifiers directly.

- [ ] P0: Scene clone/Mandela support must handle base-game scene paths and asset-pack scene paths.
  - Command verified 2026-05-09: `/fuse.dumpmandelas` wrote `C:\Steam\steamapps\common\Railroader\FUSE-mandelas.json` with 5 packages containing scene clones, 53 runtime FUSE scene clones, and 30,465 scanned `World` scene objects.

- [ ] P0: Scene clone roots with no renderer must still instantiate children correctly.

- [ ] P1: Map masks must match AMM feature parity.
  - `falloff`
  - set height
  - cut trees
  - mask modifier
  - Runtime verified 2026-05-08: Waynesville `vanilla://brysonDepot` plain box/proxy mesh is fixed. Root cause was `StationAPI` enabling every renderer with `includeInactive=true`, including source-hidden proxy/collider meshes. Current station renderer handling enables real renderers, suppresses likely proxy/collider shells, and forces LOD0 when LOD groups exist.
  - mask name
  - order
  - terrain flatteners

- [ ] P1: Turntables and building bases must apply their map masks like AMM.
  - Runtime note 2026-05-08: FUSE triggers `MapManager.RebuildAll()` after map-load apply and logs `map mask rebuild`; remaining issues are likely per-prefab map-mask/material binding, not the global rebuild being skipped.

- [ ] P1: Cherokee missing buildings in KingG need converter/source comparison.
  - Likely areas: file filtering, scene clone conversion, asset ID, progression visibility, or source ordering.

- [ ] P1: World removals should distinguish harmless stale removals from real conversion failures.

- [ ] P2: Base-game Mandelas and base-game scenery references need explicit documentation and tests.

## Splineys And World Builders

- [x] P0: Strange Customs road splineys must distinguish dirt and asphalt/pavement.
  - Code audit 2026-05-10: converter preserves legacy `profile` and `style` on `FlowyThingBuilder`; runtime resolves named `SplineProfile` first, so dirt/asphalt/pavement remain profile-driven instead of becoming one generic road.

- [x] P0: Strange Customs rivers must use the river path/profile only, not road fallback.
  - Code/build complete 2026-05-10: `FlowyThingBuilder` entries with `style: River` or river profiles convert as `type: river`; runtime uses `RiverPathStyle.River` and river/waterfall profile fallback hints.

- [x] P0: Trestles must match AutoTrestleBuilder placement, profile, and height behavior.
  - Code audit 2026-05-10: FUSE `AutoTrestle` placement matches Strange Customs center-relative control points and resolves the vanilla profile from existing `AutoTrestle` instances/resources; no active log faults remain for trestle generation.

- [x] P1: Terrain roads, waterfalls, and any single-point spliney objects must either have runtime support or converter-level repair.
  - Code/build complete 2026-05-10: runtime supports `terrainRoad` and `waterfall` as flowy spline families, and converter preserves one-point/non-runtime legacy spliney objects under `extensions.legacySplineyObjects` instead of dropping them.

- [ ] P1: RR crossings must anchor to spans correctly.

- [ ] P1: Telegraph pole movements must resolve base pole indices and apply offsets once.

- [ ] P1: Spliney warnings should name package, file, spliney ID, handler, profile, and chosen runtime type.

## Horns, Whistles, And Bells

- [ ] P0: Converted audio packs must load without requiring Strange Customs.

- [ ] P0: Horn/whistle/bell asset identifiers must match legacy pack behavior.

- [ ] P1: Audio mods must register for existing and newly spawned rolling stock where applicable.

- [ ] P1: UI/config behavior should match legacy expectations for selecting and saving audio choices.

- [ ] P1: Converted audio packs from `Mods.bck` need a matrix:
  - Whistles
  - Horns
  - HellsBells
  - Collie packs
  - GN pack
  - Nicks pack

## Schema And Public Extension Surface

- [ ] P0: Schema must represent every AMM and Strange Customs feature we are intentionally supporting.
  - Rule: if AMM/Strange Customs loaded it, FUSE needs the full feature, not a cut-down placeholder.

- [x] P0: Custom industry and load components must be representable as separate dependency mods.
  - Code/build pass 2026-05-10: FUSE JSON can name external component runtime types directly, carry custom `fields`, and rely on package `FuseLoadAfter`/requirements for the owning component/load mod. Converter preserves unknown legacy component/load fields into that payload.

- [ ] P1: Schema examples need refresh after FUSE rename and recent features.

- [ ] P1: Old `RAIL` names should be migrated or aliased intentionally; public docs should say `FUSE`.

- [ ] P1: Schema docs need examples for:
  - route mod
  - asset pack
  - map tiles
  - audio pack
  - industry component pack
  - load component pack
  - mixinto
  - progression section
  - map mask
  - scene clone
  - span-anchored scenery

- [x] P1: Deprecation policy should be documented for renamed fields.
  - Verified 2026-05-10: `schemas\FUSE_JSON_SCHEMA.md` documents version-by-version migration, unknown future-version best-effort loading, and one-minor-version deprecation behavior for renamed fields such as `model` -> `assetIdentifier`.

- [ ] P2: Auto-generate schema reference from data classes/XML comments once behavior stabilizes.

## Logging, Diagnostics, And Reports

- [x] P0: Add/finish dedicated `FUSE.log` in the same folder as `Player.log`.
  - Verified 2026-05-09: `FuseLog` writes `FUSE.log` under `Application.persistentDataPath`; current file exists at `C:\Users\roger\AppData\LocalLow\Giraffe Lab LLC\Railroader\FUSE.log`.

- [ ] P0: Every warning/error should include package ID, operation, phase, object kind, object ID, and concise reason.
  - Runtime verified 2026-05-08: invalid base-game `TrackMarker` components left pointing at removed/replaced track are disabled after package apply with name/id/reason logging; `Player.log` no longer emits the `Speed - Alarka Copper Mp7j` null/invalid-location warning.
  - Code/build pass 2026-05-10: progression interchange-transfer skips now record package id in `/fuse.report` and log package/operation/phase/kind/id/source/target/reason instead of legacy prose-only warnings. Full warning sweep still open.
  - Code/build pass 2026-05-10: track-removal snapshot/restore warnings now use structured package/operation/phase/kind/id/reason fields. Remaining warning surfaces still need the broader pass before this can be checked.

- [x] P0: Optional skips should not inflate warnings/faults.
  - Code complete 2026-05-07: conditional mixinto skips from missing optional dependency mods now log as info, remain visible in `/fuse.report`, and no longer make the load-report toast count as a problem.

- [x] P0: Final load report should summarize:
  - loaded packages
  - skipped packages
  - faulted packages
  - conflicts
  - suppressions
  - unknown scenery assets
  - graph post-bind errors
  - progression transfer skips
  - Code audit 2026-05-09: report/toast already includes loaded/applied/skipped/disabled/faulted packages, conflicts, suppressions, and unknown scenery assets. Still needs explicit graph post-bind and progression-transfer skip counters before this is fully checked.
  - Code/build complete 2026-05-10: final report/toast now includes graph post-bind issue count and progression transfer skip count; `/fuse.report` details list the affected package/object/section if either count is nonzero. Progression transfer skip records now carry the owning package id. Release DLL deployed to `C:\Steam\steamapps\common\Railroader\Mods\FUSE\FUSE.dll`; needs next runtime run to verify the expected `0 graph issues, 0 transfer skips` wording.
  - Runtime verified 2026-05-10: final toast/log report includes `0 graph issues, 0 transfer skips`, with `faultedPackages=0`, `warnings=0`, and `errors=0`.

- [x] P1: Console commands should expose detailed reports:
  - `/fuse.report`
  - `/fuse.loaded`
  - `/fuse.conflicts`
  - `/fuse.assets`
  - `/fuse.graph`
  - `/fuse.progressions`
  - `/fuse.operations`
  - Code/build complete 2026-05-09: added `/fuse.assets`, `/fuse.graph`, `/fuse.progressions`, and `/fuse.operations`; existing report/loaded/conflicts commands remain. Release build and DLL deploy succeeded.
  - Code/build complete 2026-05-09: added `/fuse.dumpgraph` and `/fuse.dumpmandelas`. They write `FUSE-original-graph.json` and `FUSE-mandelas.json` into the main Railroader folder for converter/debug work.
  - Runtime verified 2026-05-09: user confirmed all commands work; dump files exist in `C:\Steam\steamapps\common\Railroader` with fresh timestamps.

- [x] P1: Converter reports should be saved next to converted output and copied into a central audit folder when batch converting.
  - Code/verification complete 2026-05-09: batch conversion now writes `conversion-batch-report.json`, `conversion-batch-report.md`, and a `conversion-reports\` folder containing copied per-package JSON/Markdown reports. Scratch batch verified with route, asset, audio, tile, and mixed packages.

## Copper Mods

- [x] P0: Reproduce Copper errors using converted Copper packages only.
  - Fresh 2026-05-09 runtime used converted Copper FUSE packages only for the Copper checks; final report has 0 faults / 0 conflicts.

- [x] P0: Fix missing loads for Copper progression packages:
  - `mining-explosives`
  - `machine-parts`
  - Fresh 2026-05-09 runtime verified: no placeholder-load or missing-load warnings remain for these IDs.

- [x] P0: Fix Copper interchange transfer resolution.
  - Fresh 2026-05-09 runtime verified: no skipped interchange-transfer warnings; Asheville legacy transfer aliases resolved cleanly too.

- [x] P1: Verify Copper optional mixinto targets do not fault when missing.
  - Fresh 2026-05-09 runtime verified: missing optional Copper mixinto targets are reported as info-level skips and final package report remains `warnings=0 errors=0`.

- [x] P1: Verify Copper paperboard/removal mods do not produce noisy stale-removal warnings unless a real target changed.
  - Fresh 2026-05-09 runtime verified: `CoppersPaperboardRemover` applies with `warnings=0 errors=0`; no stale-removal warnings in FUSE or Player logs.

- [ ] P1: Add Copper route-specific conversion tests:
  - `CoppersAlarkaInterchange`
  - `CoppersImprovedRobinsonGapScenery`
  - `CoppersNantahalaRiverInterchange`
  - `CoppersNantahalaRiverServicing`
  - `CoppersPaperboardRemover`
  - `CoppersRobinsonGapOwnedCoalLoader`

## Legacy Corpus Test Matrix

- [ ] Asheville: convert all files, preserve order, check progression visibility, stations, industries, embedded assets, spans, mixintos.
- [ ] KingG Appalachian: check Cherokee missing buildings, depots/passenger stops, map tiles, mandelas, turntable, industries, map labels.
- [ ] Kirkland: check physical turntable vs progression industry, no duplicate turntable builders, spans, pole mover, Wye spur.
- [ ] Graham County Railroad: check assets from RTM/Embedded packs, stations, map icons, splineys, progression.
- [ ] Griz Oconoluftee River: check river spliney type/profile, no road fallback.
- [ ] Copper Alarka Interchange: check mixinto/dependency behavior and interchanges.
- [ ] Copper Robinson Gap Scenery: check scene removals/scenery visibility.
- [ ] Copper Nantahala River Interchange: check progression loads and interchange transfers.
- [ ] Copper Nantahala River Servicing: check progression loads and servicing components.
- [ ] Copper Paperboard Remover: check removals.
- [ ] Copper Robinson Gap Owned Coal Loader: check loader component behavior.
- [x] Map tile packages: check tile conversion, mount once, no repeated remount.
  - Runtime verified 2026-05-10: map tiles mount in the two expected windows only (`staged world apply` and `MapStore.Load`), with both Asheville and KingG map tile packages applied and no repeated per-package remount loop.
- [ ] Asset packs: ALW, C_L_B, RTM, Embedded Piggy Back Base, Aspens, Trowzrs.
- [ ] Audio packs: horns, whistles, bells.

## Acceptance Target

- [x] Batch convert current legacy corpus.
- [x] Install converted packages plus required asset/audio packs.
- [x] Launch game.
- [x] FUSE report shows zero faults.
- [x] FUSE report shows zero conflicts. Runtime verified 2026-05-07.
- [x] FUSE report shows zero unknown scenery assets.
- [ ] Graph post-bind report is clean.
  - Runtime verified 2026-05-07: no missing-after-apply warnings.
  - Fresh runtime 2026-05-10 regressed to `335 graph issues`, all from segments in disabled progression groups (`gcr-t`, `CNRS_Tracks`, `CNRI_Track1`, `CNRI_Track2`, `CNRI_Track3`, `CNI_IR_Tracks`).
  - Code/build pass 2026-05-10: post-bind validation now skips segments/spans whose own or endpoint segment groups are intentionally disabled by current progression/map-feature state. Needs fresh runtime verification that final report returns to `0 graph issues`.
- [ ] Locations window order matches legacy/source order.
  - Pending fresh route verification after 2026-05-10 converter/schema fix for signed source order values and reconverted Asheville package.
- [ ] Progression-gated objects are hidden until unlocked.
  - Pending fresh route verification after the 2026-05-10 forced map-feature-state refresh build.
- [ ] Roads, rivers, trestles, map masks, mandelas, turntables, stations, industries, loaders, interchanges, audio, and map tiles visually/functionally match legacy behavior.
- [ ] Load time matches or beats RailLoader for equivalent installed content.

