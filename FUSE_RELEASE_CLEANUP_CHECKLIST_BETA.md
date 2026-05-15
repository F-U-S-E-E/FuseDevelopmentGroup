# FUSE Release Cleanup Checklist

Snapshot date: 2026-05-07

Scope: this is the working release checklist for getting FUSE from "loads a lot of legacy content" to "clean public-alpha replacement for the legacy mod stack." It is intentionally issue-focused. Signals are excluded until the end by choice.

Evidence used for this snapshot:

- FUSE source tree at `C:\Hrogers_Railroader_mods_Projects\Rail`
- Legacy corpus at `C:\Railroader mods\Installed\Map`
- FUSE log snapshot at `C:\Users\roger\AppData\LocalLow\Giraffe Lab LLC\Railroader\FUSE.log`
- Legacy mod loader logs from the Railroader install folder
- Current converter tools under `tools\`

Important note: the 2026-05-15 LocalLow `FUSE.log` is now the authoritative runtime evidence for the current FUSE run.

## Release Gates

- [x] FUSE load report reaches `0 faulted`, `0 conflicts`, `0 unknown scenery assets` with the current converted legacy test set. Runtime verified 2026-05-07: `69 loaded, 0 faulted, 0 conflicts, 0 suppressions, 0 unknown scenery assets`.
  - Fresh 2026-05-08 runtime verified after the Asheville progression alias pass: `73 loaded, 0 faulted, 0 conflicts, 0 suppressions, 0 unknown scenery assets`.
  - Fresh 2026-05-09 runtime verified after dump-command build: `73 loaded, 0 faulted, 0 conflicts, 0 suppressions, 0 unknown scenery assets`.
  - Fresh 2026-05-10 runtime verified after report/log cleanup: `73 loaded, 0 faulted, 0 conflicts, 0 suppressions, 0 unknown scenery assets, 0 graph issues, 0 transfer skips`.
  - Fresh 2026-05-14 runtime verified after public wording cleanup: `73 loaded | faults 0 | conflicts 0 | assets 0 | graph 0 | transfers 0 | suppressions 0`.
  - Fresh 2026-05-15 runtime verified: `73 loaded | faults 0 | conflicts 0 | assets 0 | graph 0 | transfers 0 | suppressions 0`; conditional mixinto packages with missing optional route dependencies are skipped as info-level package skips, not counted as transfer failures or faults.
- [x] No graph post-bind missing nodes, segments, or spans after all converted route mods load. Runtime verified 2026-05-07: `missing after apply = 0`.
  - Fresh runtime verified 2026-05-10 after hidden progression-group validation fix: final report shows `0 graph issues`, and no `missing after apply` warnings were present.
- [x] No package faults from missing loads when required asset/load packs are installed. Runtime verified 2026-05-07: Copper progression packages stayed loadable with placeholder fallback warnings for `mining-explosives` and `machine-parts`; converter still needs to emit real load definitions when available.
  - Code/converter pass 2026-05-08: converter now emits known compatibility load definitions for `mining-explosives` and `machine-parts`; reconverted Copper Nantahala packages. Needs fresh runtime verification that placeholder warnings are gone.
  - Fresh 2026-05-09 runtime verified: no `placeholder load`, `mining-explosives`, or `machine-parts` warnings in `FUSE.log` or `Player.log`.
- [x] Converter can process every legacy route mod in `C:\Railroader mods\Installed\Map` without dropping files or inventing unexpected late files. Scratch verified 2026-05-08: batch converted 13/13 current inputs with 0 errors; generated reports for Asheville, Copper set, GCR, Griz, KingG, and Kirkland. No `*Late*.json` output files were produced.
  - Scratch verified again 2026-05-11 after the area-order conversion fix: batch converted 12/12 current inputs with 0 errors into `_work\full-corpus-conversion-20260511`; no artificial late files were produced.
  - Scratch verified again 2026-05-11 after downgrading runtime-handled hidden track-group notes: batch converted 12/12 current inputs with 0 errors into `_work\full-corpus-conversion-20260511-info-groups`; warnings dropped from 26 to 20 and Copper route packages are now clean conversions.
  - Scratch verified again 2026-05-11 after classifying successful converter repairs as info instead of warnings: batch converted 12/12 current inputs with 0 errors into `_work\full-corpus-conversion-20260511-clean-reporting`; warnings dropped to 4, all tied to unresolved/source-authored issues instead of repaired content.
- [x] Converter emits one useful report per conversion: repaired, preserved, unresolved, unsupported, and dependency-required entries. Code complete 2026-05-08; `conversion-report.json` and `conversion-report.md` now include outcome buckets and per-source-file summaries. Scratch verified with `CoppersPaperboardRemover` and Asheville.
- [x] Converted mods preserve legacy file concerns: one source JSON becomes one FUSE JSON unless the source is metadata-only. Scratch verified 2026-05-08: corpus reports include one file summary per converted source concern, and output filenames follow the source JSON stems instead of artificial concern buckets.
- [ ] FUSE load time is benchmarked against the legacy mod loader and is within the same range or faster for equivalent installed content.
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
  - Code/build pass 2026-05-11: audio unload cleanup now logs `release audio definitions` only when a package actually owned horn/whistle/bell entries, reducing no-op unload noise in `FUSE.log`.
  - Fresh 2026-05-13 runtime verified: `FUSE.log` has no warnings, errors, exceptions, missing-after-apply lines, graph issues, progression transfer skips, conflicts, or unknown scenery assets for the current supported test stack.
  - Fresh 2026-05-14 runtime verified: `FUSE.log` has 0 real FUSE warning/error/exception entries and the public load report uses full words for `transfers` and `suppressions`.
  - Fresh 2026-05-15 runtime verified: `FUSE.log` has 0 FUSE warnings, 0 FUSE errors, 0 FUSE exceptions, 0 `missing after apply`, 0 unknown scenery assets, 0 graph issues, and 0 transfer skips. Remaining `Player.log` exceptions are external mod/game-content errors (`LegosLibraryOfStuff`, `UtilitiesMod`, `BRSS`, `Enviro`, missing rolling-stock identifiers), not FUSE-owned stack traces.
- [x] FUSE-specific logging is available in `FUSE.log` beside `Player.log`.
- [x] Public schema examples cover every supported legacy concept except signals.
  - Documentation pass 2026-05-14: `schemas\fuse-mod.example.json`, `schemas\umm-info.example.json`, and `schemas\FUSE_JSON_SCHEMA.md` now cover route packages, asset packs, map tiles, audio packs, built-in/custom industry components, custom load fields, mixinto, progression sections, map masks, scene clones, span-anchored scenery, suppressions, and signal deferral.

## Validation Gates

These gates are about making FUSE better than the legacy stack, not copying it. Legacy content can be accepted, repaired, or rejected, but FUSE should avoid mutating the live game until the package plan is understood and diagnostics are actionable.

- [ ] P0: Enforce the core validation pipeline before live mutation.
  - Required order: discover, parse, normalize, validate, plan, dry-run/preflight, apply, post-validate, report, cleanup.
  - Validation and dry-run/preflight must not mutate the live Railroader scene.
  - Runtime mutation should happen only in the apply stage.
  - Any intentional exception must be documented with package id, operation, phase, and cleanup owner.

- [ ] P0: Add cross-reference validation across all FUSE definition domains.
  - References must report source package, source definition id, source field, expected target kind, target id, and resolution result.
  - Hard references should block the owning package or dependency chain only, not the entire load, unless the package is a root dependency.
  - Optional references must be explicitly marked optional and reported as info-level skips when missing.
  - Missing references should be grouped into one actionable report instead of scattered log spam.

- [ ] P0: Add definition identity validation.
  - Every package-level and object-level definition must have a stable id.
  - Duplicate ids must be rejected or deterministically overridden with clear owner/package reporting.
  - Package id, file id, object id, and runtime claim id should be traceable from diagnostics.
  - Renames should preserve stable ids where possible; id changes should be treated as migrations, not silent replacements.

- [ ] P0: Add package dependency and fault-isolation validation.
  - Required dependencies must be present before apply.
  - Optional dependencies must not fault unrelated packages.
  - One bad package must not prevent unrelated valid packages from loading.
  - Faulted package reports should list dependency chain, blocked packages, and still-applied independent packages.

- [ ] P0: Add graph preflight validation before graph mutation.
  - Nodes, segments, spans, groups, switches, removals, restores, and generated helper objects must validate as a final-state plan before apply.
  - Segment endpoints must resolve to planned or live nodes.
  - Spans must not reference removed segments unless the same plan restores/replaces them.
  - Industries, passenger stops, progressions, scenery bindings, and map features must not reference graph objects removed by the same plan.
  - Dry-run graph results should match committed apply results unless the difference is explicitly explained.

- [ ] P0: Add runtime-binding validation.
  - Every authored object that needs a live object must either bind to the expected live object or report a missing binding.
  - Runtime binding must not silently fall back to the wrong base-game object.
  - FUSE-created objects must record package owner, system owner, claim kind, and cleanup owner at creation time.
  - Base-game objects claimed by FUSE must have one active owner unless shared ownership is explicitly allowed.

- [ ] P0: Add cleanup validation as a first-class validation pass.
  - Every registry claim created during load must be released during unload.
  - Audio definitions, world suppressions, scene clones, runtime objects, graph claims, component bindings, and progression bindings must report released/unresolved counts.
  - Repeated load/unload cycles must not increase resident definitions, claims, duplicate objects, warnings, or errors.
  - Cleanup reports must include package id, system owner, claim type, count released, and unresolved leftovers.

- [ ] P1: Add progression validation before purchase or completion is allowed.
  - Progression ids must be unique.
  - Prerequisites must resolve.
  - Delivery targets must resolve.
  - Required load ids must resolve.
  - Unlock effects must resolve before the project can be purchased.
  - Track groups, scenery groups, industries, areas, map features, passenger stops, and game objects referenced by progression must validate.
  - Progression save state must migrate or degrade cleanly if a package is removed.

- [ ] P1: Add industry/component validation.
  - Industry ids must be unique.
  - Industry tracks, spans, and spots must resolve.
  - Loading/unloading/passenger/formulaic components must resolve load ids and component kind ids.
  - Component builders must be registered before definitions apply.
  - Duplicate component builders must be rejected or explicitly overridden.
  - Missing component builders must fail loudly with package/id/kind context.

- [ ] P1: Add editor/export validation contract.
  - Editor export must run the same validator as runtime load.
  - Converter output and editor output must normalize into the same FUSE definition model for equivalent content.
  - Editor rename operations must preserve stable ids.
  - Editor delete operations must detect dependent references before export.

## Separation And Ownership Gates

These gates keep FUSE from becoming a larger version of the legacy stack. The goal is not just to load content, but to keep package loading, validation, runtime mutation, cleanup, and editor authoring in separate lanes.

- [ ] P0: Keep `FuseModLoader` as coordinator, not domain owner.
  - `FuseModLoader` should coordinate startup, shutdown, lifecycle entry points, and top-level pipeline calls.
  - Package discovery, schema loading, dependency resolution, validation, apply, cleanup, and diagnostics should be owned by dedicated services.
  - No new feature-specific logic should be added directly to `FuseModLoader` unless it is temporary and tagged with extraction criteria.

- [ ] P0: Enforce pipeline separation.
  - Package discovery finds packages only.
  - Parsing reads files only.
  - Normalization converts source data into FUSE definitions only.
  - Validation checks definitions and references only.
  - Planning builds an intended final state only.
  - Apply mutates the live game only after validation/preflight succeeds.
  - Cleanup releases claims and runtime objects only.
  - Reporting summarizes results without changing state.

- [ ] P0: Enforce definition/runtime separation.
  - Definition classes should contain authored data only.
  - Runtime binding classes should resolve definitions to live game/base-game objects.
  - Apply systems should perform mutations.
  - Save-state classes should contain persistent state only.
  - Pure definition models should not require Unity `GameObject`, `Component`, `Transform`, or scene references.

- [ ] P0: Add domain ownership checks.
  - Track logic belongs to the track system.
  - Scenery/world-object logic belongs to the scenery/world system.
  - Industry/component logic belongs to the industry/component system.
  - Progression logic belongs to the progression system.
  - Audio logic belongs to the audio system.
  - Registry/claim logic belongs to the registry system.
  - Diagnostics formatting/reporting belongs to diagnostics, not each domain ad hoc.

- [ ] P1: Add authority boundary documentation per system.
  - Each system should declare what it owns, observes, mutates, persists, and cleans up.
  - Shared concepts such as ids, claims, diagnostics, schema versioning, and dependency resolution should have one canonical owner.
  - No system should silently create a duplicate registry, id resolver, validation framework, or package-state model.
  - Cross-system changes should go through a shared plan/apply contract instead of direct calls into unrelated systems.

- [ ] P1: Add authoring/runtime separation checks.
  - Editor models must export definitions without needing a live game scene.
  - Runtime systems must load definitions without editor-only classes.
  - Editor metadata must not become required runtime data unless explicitly promoted to the schema.
  - Stable ids must survive editor rename, move, group, and regroup operations.

- [ ] P1: Add cleanup ownership checks.
  - Anything created by a system must have a matching cleanup owner.
  - Cleanup ownership must be registered at creation time.
  - Unload reports should show which system released each claim type.
  - Orphaned runtime objects should report original package owner and system owner.

- [ ] P2: Track refactor debt explicitly.
  - Temporary compatibility paths must be tagged with removal criteria.
  - Any method that both validates and mutates should be flagged for split.
  - Any class that owns more than one domain should get an extraction note.
  - Any god-class growth should be visible in this checklist instead of living only in code comments.


## First Fix Order

- [x] P0: Stop repeated map tile remounts and remove unnecessary disk reloads during map tile registration. Runtime verified 2026-05-07: current log shows two expected map-tile mount windows (`staged world apply` and `MapStore.Load`) instead of dozens of per-package remounts.
- [x] P0: Make direct asset pack loading the default path; stop copying asset packs into LocalLow unless a fallback requires sanitization. Code complete 2026-05-07; direct stores are default and LocalLow mirror is opt-in.
- [x] P0: Fix converter file preservation so every legacy JSON is converted in place instead of being filtered by mixinto references. Code complete 2026-05-07; batch corpus reconversion still needed.
- [x] P0: Add real span topology repair/reporting for multi-segment spans, route direction, and external base-game segment references. Converter code complete 2026-05-07; needs full corpus reconversion/runtime verification.
- [x] P0: Fix progression/load dependency faults, especially Copper missing-load and interchange-transfer cases. Runtime verified 2026-05-07: missing-load placeholders kept Copper progressions loadable and legacy `.t1` interchange-transfer aliases resolved 10 transfers with `skipped interchange transfer = 0`.
  - Code/converter pass 2026-05-08: known Copper-only missing loads are now real converted load definitions instead of runtime placeholders.
- [x] P0: Verify track group pre-enable does not make progression-gated scenery or track visible early. Code path fixed 2026-05-07; runtime route verification still needed.
- [ ] P1: Match legacy industry component behavior for all AMM, legacy custom content framework, Zamu, Copper, and ConfusingSupplements components.
- [ ] P1: Finalize station/passenger stop ordering, span binding, map icons, and company window display.
  - Verified 2026-05-08: `Editor\MainEditorWindow.cs` and `Infrastructure\WindowCreatorHelper.cs` do not reference, destroy, or null the base-game company window. They only manage FUSE's own editor window instance/panel references.
  - Code pass 2026-05-08: passenger stop child `TrackSpan` helpers now keep blank graph ids to stop duplicate graph-span warnings, and virtual passenger stops suppress base `IndustryComponent` validation that does not apply to FUSE passenger-stop shims. Needs fresh Player.log verification.
  - Runtime verified 2026-05-08: replaced passenger-stop child `TrackSpan` helpers with direct `TrackMarker` creation plus private `PassengerStop._spans` binding, so passenger stops keep their runtime markers/company-window spans without adding fake spans to the global graph.
  - Code/build pass 2026-05-11: company-window reopen failure was traced to base-game `IndustryTrackDisplayableExtensions.ShortName` crashing on legacy-style component names such as `Cherokee` under `Cherokee Depot`; FUSE now patches that helper to return a safe display name instead of leaving the window half-built. Needs fresh runtime verification.
  - Fresh 2026-05-15 log verification: `Player.log` contains no `CompanyWindow`, `ShortName`, `LocationsPanelBuilder`, or `OpsController.get_Areas` failures after the ordering/company-window patch. FUSE cached 60 areas with first area preview `Asheville > Emma > Boswell > Sulfur Springs > Enka > Hominy > Candler > Luthers > Coburn > Canton > Starney > Moore`. Still needs visual verification that the Locations tab order and reopen behavior match this.
- [x] P1: Clean converter/runtime turntable handling so physical turntables and progression industries named "turntable" are never confused. Code complete 2026-05-07; duplicate physical turntable ids now apply as final-state overrides during staged graph apply. Needs Kirkland runtime verification.
- [x] P1: Finish spliney parity: dirt roads, asphalt roads, rivers, trestles, terrain roads, waterfalls.
  - Code/build complete 2026-05-10: runtime now accepts the full schema spliney type set (`road`, `river`, `terrainRoad`, `trestle`) instead of silently collapsing terrain roads/waterfalls to normal roads. Converter preserves legacy custom content `FlowyThingBuilder` river/road style/profile and emits the legacy `offsetY: -0.1` default when source data omits it. Release DLL deployed; converter `.pyz` and `.exe` rebuilt.
- [ ] P2: Finish horn, whistle, and bell parity and regression-test converted audio packs.
- [x] P2: Refresh schema docs and examples after runtime/converter behavior is stable.
  - Documentation pass 2026-05-14: refreshed `schemas\fuse-mod.example.json`, `schemas\umm-info.example.json`, and `schemas\FUSE_JSON_SCHEMA.md` for the current beta runtime/converter surface.

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

- [ ] P0: Benchmark load phases against the legacy mod loader.
  - The legacy mod loader's observed behavior: discovers mods quickly and adds direct asset stores from the Mods folder.
  - Timing logs added 2026-05-07 for map-load total, cache rebuild, discovery, asset-pack registration, disk load, runtime apply, map-mask rebuild, console registration, and per-package load. Actual legacy mod loader comparison still needs a fresh game run.
  - Fresh 2026-05-09 FUSE timing: discovery 17 ms, load-from-disk 223 ms, runtime apply 18,644 ms, total map-load 19,309 ms for 15 packages / 73 resident definitions / 62 applied definitions. Still needs equivalent legacy mod loader timing comparison.
  - Log audit 2026-05-10: FUSE disk work is already small (latest disk load 309 ms); the bottleneck is runtime apply (~18.6 s). Legacy mod loader logs from 2026-05-06 show plugin definition/mod loading in roughly 170 ms and legacy custom content graph patching roughly 2.7-2.9 s after scene load begins, but the installed content sets are not perfectly equivalent.
  - Code pass 2026-05-10: staged operations now defer `TrackAPI.ApplyAreaOrdering()` and run it once after the industry batch instead of once per data fragment. Needs fresh timing log to measure impact.
  - Code/build pass 2026-05-10: per-entity authoring bind info logs are now gated behind `Settings.VerboseApplyReportDetails`; normal map loads should no longer write hundreds of scenery bind lines to `FUSE.log`. Needs fresh timing/log-size comparison.
  - Fresh 2026-05-15 FUSE timing: discovery 18 ms, asset-pack registration 0 ms, total map-load pipeline 11,307 ms for 15 package folders / 62 applied definitions. Direct asset stores are active and LocalLow mirroring remains disabled by default; equivalent legacy mod loader timing comparison is still required.
  - Timing audit 2026-05-11: latest available run spent most runtime-apply time in `apply-world-objects` (9,067 ms total), then `apply-operations` (2,557 ms), and the single graph rebuild (1,730 ms). Top world rows were KingG scenery (2,474 ms), Griz game-graph world objects (2,377 ms), Asheville game-graph world objects (839 ms), and GCR track world objects (759 ms).
  - Code/build pass 2026-05-11: scenery and spliney existence checks now trust the rebuilt FUSE runtime indexes during package apply instead of falling back to repeated full-scene `FindObjectsOfType` scans for every new object. Spline/trestle profile discovery is cached per map session. Release DLL deployed; needs fresh timing log to measure improvement.

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
  - Converter pass 2026-05-11: stopped inventing global area `order` values for legacy files that do not provide one. Reconverted Asheville, KingG, GCR, Griz Oconoluftee, and Copper route packages; KingG/Copper now omit fake `0..N` area orders while Asheville/GCR keep real source orders.

- [x] P2: Add a drag/drop Windows executable and a cross-platform Python folder converter that share the same core conversion engine. Rebuilt 2026-05-08 after converter blank-component-id fixes.
  - Rebuilt again 2026-05-08 after known compatibility load generation and progression section-feature alias conversion.
  - Rebuilt again 2026-05-08 after structured conversion report support; `dist\FUSEConvertFolder.pyz` and `dist\FUSE-Converter.exe` smoke check passed.
  - Rebuilt again 2026-05-08 after lenient JSON repair fix; `dist\FUSEConvertFolder.pyz` and `dist\FUSE-Converter.exe` smoke check passed.
  - Rebuilt again 2026-05-08 after batch output-folder skip fix; `dist\FUSEConvertFolder.pyz` and `dist\FUSE-Converter.exe` smoke check passed.
  - Rebuilt again 2026-05-11 after area-order conversion fix; `dist\FUSEConvertFolder.pyz` and `dist\FUSE-Converter.exe` smoke check passed.
  - Rebuilt again 2026-05-11 after hidden track-group converter note cleanup; `dist\FUSEConvertFolder.pyz` and `dist\FUSE-Converter.exe` smoke check passed.
  - Rebuilt again 2026-05-11 after successful-repair reporting cleanup; `dist\FUSEConvertFolder.pyz` and `dist\FUSE-Converter.exe` smoke check passed.
  - Rebuilt again 2026-05-14 after warning source-file reporting fix; `dist\FUSE-Converter.exe --help` and `python dist\FUSEConvertFolder.pyz --help` smoke checks passed.
  - Packaging pass 2026-05-15: copied the rebuilt converter artifacts into `C:\Steam\steamapps\common\Railroader\Mods\FUSE`; deployed `FUSE-Converter.exe --help` and `python FUSEConvertFolder.pyz --help` both exit `0`.

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
  - Fresh runtime verified 2026-05-10: final report returned to `0 graph issues` while `forcedFeatureState=True` ran during progression refresh.

- [x] P1: Add graph validation that explains route direction errors using start/end, A/B, upper/lower, and distance.
  - Code/build complete 2026-05-10: failed runtime span route validation now reports `upper`/`lower` segment id, A/B Start/End anchor, distance, and segment length. Release DLL deployed to `C:\Steam\steamapps\common\Railroader\Mods\FUSE\FUSE.dll`.
  - Runtime note 2026-05-08: Asheville `NERbalsam-pigeon_dpem` switch geometry error matches the legacy source node/segment data exactly and should be treated as an authoring/legacy-geometry warning unless AMM or the legacy mod loader prove they repair it. Do not spend release time inventing a FUSE-only geometry rewrite for one bad authored switch.

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

- [x] P0: FUSE-created industries must match AMM and legacy custom content materialization.
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
  - Code/build pass 2026-05-11: generic invalid `TrackMarker` cleanup now preserves FUSE-owned passenger-stop marker helpers instead of disabling them for intentionally blank graph ids. This removes `PassengerStopMarker` cleanup noise and keeps generated `PassengerStop._markers` usable. Needs fresh runtime verification.
  - Code/build pass 2026-05-11: patched base-game `IndustryTrackDisplayableExtensions.ShortName` so short legacy component names such as `Cherokee` under `Cherokee Depot` cannot throw `ArgumentOutOfRangeException` and leave the Company window half-built. Needs fresh runtime verification that the Company window opens, closes, and reopens.

- [ ] P0: Area sorting must match legacy/source order, not alphabetical fallback.
  - Code/converter pass 2026-05-10: runtime area ordering path was already applying; converter was reversing Asheville by inventing positive order values. Installed `asheville_extension.FUSE` has been reconverted with source orders preserved (`asheville=-2000`, `beta=-100`, etc.). Needs fresh visual verification in the Locations window.
  - Code/build pass 2026-05-11: AMM parity check showed passenger/location ordering depends on `Area.transform.GetSiblingIndex()`, because the base game walks `OpsController.Shared.Areas` from the scene hierarchy. FUSE now applies source area order to `Area` sibling indexes after runtime apply and keeps the `ListController.SetData` sort patch as a UI fallback. Release DLL deployed; needs fresh visual verification for Asheville first and Ela not falling to the Thornton end of the base list.
  - Code/build pass 2026-05-11: legacy area IDs now alias to an existing same-name base/FUSE area instead of creating duplicate sections. Known case: KingG `APPA-Ela` now binds to the existing `ela` Area, keeping Appalachian Engine Service and Ela Station in the same logical town. Release DLL deployed; needs fresh visual verification.
  - Code/build pass 2026-05-11: area order/alias runtime metadata is now cleared on unload so one map/save cannot leak location ordering into the next map session.
  - Code/build pass 2026-05-11: fixed the Locations-list comparator so unordered/base areas no longer interleave ahead of explicit FUSE areas with order `1+`; source-ordered areas now sort as one ordered block, then unordered/base areas retain their own relative order. Release DLL deployed; needs fresh visual verification.
  - Code/build/data pass 2026-05-11: corrected the comparator again to place explicit source orders in the same order space as existing base-game area sibling indexes (`siblingIndex * 100`) instead of moving every ordered mod area ahead of all unordered areas. Installed route packages were reconverted/replaced with the no-fake-area-order converter output. Release DLL deployed; needs fresh visual verification.
  - Runtime log verified 2026-05-13: FUSE area-order cache reports `60 area(s)`, `explicitOrdered=35`, `moved=38`, and preview starts `Asheville > Emma > Boswell > Sulfur Springs > Enka > Hominy > Candler > Luthers > Coburn > Canton > Starney > Moore`. Still needs visual Locations-window confirmation.
  - Code/build pass 2026-05-15: patched `OpsController.Areas` getter so the base company-window builder receives areas in FUSE/source order instead of raw scene traversal order. Release DLL deployed; needs fresh visual verification that Asheville is first and Ela stays near the correct Appalachian block.

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

- [x] P0: Legacy custom content road splineys must distinguish dirt and asphalt/pavement.
  - Code audit 2026-05-10: converter preserves legacy `profile` and `style` on `FlowyThingBuilder`; runtime resolves named `SplineProfile` first, so dirt/asphalt/pavement remain profile-driven instead of becoming one generic road.

- [x] P0: Legacy custom content rivers must use the river path/profile only, not road fallback.
  - Code/build complete 2026-05-10: `FlowyThingBuilder` entries with `style: River` or river profiles convert as `type: river`; runtime uses `RiverPathStyle.River` and river/waterfall profile fallback hints.

- [x] P0: Trestles must match AutoTrestleBuilder placement, profile, and height behavior.
  - Code audit 2026-05-10: FUSE `AutoTrestle` placement matches legacy custom content center-relative control points and resolves the vanilla profile from existing `AutoTrestle` instances/resources; no active log faults remain for trestle generation.

- [x] P1: Terrain roads, waterfalls, and any single-point spliney objects must either have runtime support or converter-level repair.
  - Code/build complete 2026-05-10: runtime supports `terrainRoad` and `waterfall` as flowy spline families, and converter preserves one-point/non-runtime legacy spliney objects under `extensions.legacySplineyObjects` instead of dropping them.

- [ ] P1: RR crossings must anchor to spans correctly.

- [ ] P1: Telegraph pole movements must resolve base pole indices and apply offsets once.

- [ ] P1: Spliney warnings should name package, file, spliney ID, handler, profile, and chosen runtime type.

## Horns, Whistles, And Bells

- [ ] P0: Converted audio packs must load without requiring the legacy custom content framework.

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

- [ ] P0: Schema must represent every AMM and legacy custom content framework feature we are intentionally supporting.
  - Rule: if AMM or the legacy custom content framework loaded it, FUSE needs the full feature, not a cut-down placeholder.

- [x] P0: Custom industry and load components must be representable as separate dependency mods.
  - Code/build pass 2026-05-10: FUSE JSON can name external component runtime types directly, carry custom `fields`, and rely on package `FuseLoadAfter`/requirements for the owning component/load mod. Converter preserves unknown legacy component/load fields into that payload.

- [x] P1: Schema examples need refresh after FUSE rename and recent features.
  - Documentation pass 2026-05-14: refreshed public examples to use FUSE naming and recent beta features including audio, mixinto, custom components, suppressions, and full spliney type coverage.

- [x] P1: Old `RAIL` names should be migrated or aliased intentionally; public docs should say `FUSE`.
  - Code/docs pass 2026-05-11: loader local variables now use `fuseDataFile` names, and schema docs now document the remaining intentional aliases: `.RAIL` suffix compatibility for dependency/mixinto resolution, and Railroader's built-in asset-pack id string `rail`. Public docs should continue using FUSE names only.
  - Audit pass 2026-05-15: public docs/schema no longer contain accidental `RAIL` mod-name commands or `HunterR` sample ids. Remaining `RAIL` strings are intentional rename/compatibility notes or code aliases for already-converted packages.

- [x] P1: Schema docs need examples for:
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
  - Documentation pass 2026-05-14: added `Example Coverage` table to `schemas\FUSE_JSON_SCHEMA.md`; detailed examples live in `schemas\fuse-mod.example.json` and `schemas\umm-info.example.json`.

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
  - Code/build pass 2026-05-14: user-facing toast/one-line report was shortened to fit startup screens while keeping full detail in `/fuse.report`. Needs fresh runtime visual/log verification.

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
  - Code/build complete 2026-05-14: added `/fuse.dumpruntimegraph`, which writes `FUSE-runtime-graph.json` with the active post-FUSE graph for original-vs-runtime comparison. Needs in-game command verification.
  - Runtime verified 2026-05-14: `/fuse.dumpgraph` wrote `FUSE-original-graph.json` and `/fuse.dumpruntimegraph` wrote `FUSE-runtime-graph.json`; both files have fresh timestamps in the Railroader folder. Follow-up code/build pass tightened the runtime dump to resolved graph spans only; rerun `/fuse.dumpruntimegraph` once before closing the graph comparison gate.

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
- [x] Graph post-bind report is clean.
  - Runtime verified 2026-05-07: no missing-after-apply warnings.
  - Fresh runtime 2026-05-10 regressed to `335 graph issues`, all from segments in disabled progression groups (`gcr-t`, `CNRS_Tracks`, `CNRI_Track1`, `CNRI_Track2`, `CNRI_Track3`, `CNI_IR_Tracks`).
  - Code/build pass 2026-05-10: post-bind validation now skips segments/spans whose own or endpoint segment groups are intentionally disabled by current progression/map-feature state. Needs fresh runtime verification that final report returns to `0 graph issues`.
  - Runtime verified 2026-05-10: final report shows `0 graph issues`, `0 transfer skips`, `warnings=0`, and `errors=0`.
- [ ] Locations window order matches legacy/source order.
  - Pending fresh route verification after 2026-05-10 converter/schema fix for signed source order values and reconverted Asheville package.
  - Code/build pass 2026-05-11: restored AMM-style area sibling ordering so explicit negative area orders can move ahead of unordered/base areas in the company Locations list, with UI-level sorting still present as a fallback. Release DLL deployed; needs fresh game run to verify Asheville appears first, Ela placement is correct, and the Company window still reopens cleanly.
  - Code/build/data pass 2026-05-11: converter now preserves only real legacy area orders, runtime compares those orders against base sibling order, and installed route packages were refreshed. Needs fresh game run to verify the full flow from Asheville through Andrews.
- [ ] Progression-gated objects are hidden until unlocked.
  - Pending fresh route verification after the 2026-05-10 forced map-feature-state refresh build.
- [ ] Roads, rivers, trestles, map masks, mandelas, turntables, stations, industries, loaders, interchanges, audio, and map tiles visually/functionally match legacy behavior.
- [ ] Load time matches or beats the legacy mod loader for equivalent installed content.

---

# Full Beta Release Addendum

Scope: these gates are for a real beta release, not the current dev-tree cleanup pass. The editor is intentionally non-blocking until it is available for review. These gates focus on FUSE itself: converter, runtime, validation, separation, cleanup, persistence, diagnostics, compatibility, and release confidence.

## Beta Release Definition

A FUSE beta release means the project is no longer only proving that converted legacy content can load. It must be predictable enough that testers can install it, run supported converted packages, report meaningful bugs, unload/reload maps, and trust that one broken package will not corrupt the entire session.

- [x] Beta scope is written in the README: supported game version, supported FUSE package types, unsupported systems, known limitations, and signal status.
  - Documentation pass 2026-05-14: added `README.md` with supported Railroader version, supported package types, unsupported/deferred systems, known limitations, signal status, and beta package stack notes.
- [x] Beta support policy is written: what logs/reports testers must attach, where to report issues, and what package combinations are considered supported.
  - Documentation pass 2026-05-14: added `README.md` and `TROUBLESHOOTING.md` with required attachments, diagnostic commands, package-list expectations, and current supported stack notes.
- [ ] Beta install/uninstall instructions are tested on a clean Railroader install.
- [ ] Beta upgrade instructions are tested from the previous internal/dev build.
- [ ] Beta rollback instructions are tested so users can return to a known-good state.

## P0 Beta Blockers

These must be clean before a beta build is called beta.

- [x] Current supported legacy corpus converts without converter crashes.
  - Scratch verified 2026-05-14: `python .\tools\fuse_converter.py --batch --clean --out .\_work\beta-corpus-conversion-20260514 "C:\Railroader mods\Installed\Map"` converted `12/12` inputs with `0` errors. Remaining `4` warnings are known source-authored/actionable content issues.
  - Scratch verified again 2026-05-14 after source-file reporting fix: `_work\beta-corpus-conversion-20260514-report-paths` converted `12/12` inputs with `0` errors.
- [x] Current supported legacy corpus loads with `0 faultedPackages`, `0 conflicts`, `0 unknown scenery assets`, `0 graph issues`, and `0 progression transfer skips`.
  - Fresh 2026-05-13 runtime verified: final report shows `73 loaded, 0 faulted, 0 conflicts, 0 suppressions, 0 unknown scenery assets, 0 graph issues, 0 transfer skips`.
  - Fresh 2026-05-14 runtime verified: final report again shows `73 loaded, 0 faulted, 0 conflicts, 0 suppressions, 0 unknown scenery assets, 0 graph issues, 0 transfer skips`; `FUSE.log` warning/error/exception scan was clean.
- [x] FUSE respects Unity Mod Manager disabled state for converted data packages.
  - Code/build complete 2026-05-15: package discovery now checks UMM `modEntries` by folder path and marks matching disabled package folders as disabled before disk load/apply. This covers track/data packages as well as other converted package folders visible to UMM.
- [ ] FUSE-specific log has no unhandled exceptions during map load, map unload, save, reload, or quit.
  - Fresh 2026-05-13 runtime verified for map load and map unload: no warning/error/exception matches in `FUSE.log`. Save, reload, and quit still need explicit verification before this gate can close.
- [ ] One broken non-required package does not prevent unrelated valid packages from loading.
- [ ] Runtime mutation only happens after package parse, normalize, validation, and plan/preflight have completed.
- [ ] Cleanup releases all claims created by FUSE-owned systems during unload.
  - Fresh 2026-05-13 runtime evidence: map unload restored world suppressions, released package registry claims, released audio definitions, and cleared runtime state. Needs repeated load/unload count comparison before marking this fully closed.
- [ ] Repeated load/unload cycles do not grow resident definitions, registry claims, duplicate runtime objects, warnings, or errors.
- [ ] Save/load preserves FUSE-created or FUSE-bound state for supported systems.
- [x] Multiplayer behavior is explicitly blocked, supported, or degraded-safe.
  - Code/build complete 2026-05-15: `FuseMultiplayerGuard` detects non-host multiplayer clients via `Network.Multiplayer` reflection. Default beta behavior is legacy-style compatibility mode: every client applies the same local package stack and gets a clear warning if detected as non-host. Strict non-host runtime blocking remains available through `Settings.BlockNonHostMultiplayerClientWorldApply=true`. `README.md` and `KNOWN_ISSUES.md` document the policy.
- [x] FUSE version, schema version, converter version, build configuration, and supported Railroader game version are logged at startup.
  - Code/build complete 2026-05-14: startup now logs `fuseVersion`, `schemaVersion`, `converterVersion`, `buildConfiguration`, `supportedRailroaderVersion`, `currentRailroaderVersion`, and Unity version; deployed `Info.json` now declares `GameVersion: 2025.1`. Needs fresh startup log verification.
  - Runtime verified 2026-05-14: startup log reports `fuseVersion='0.1.0'`, `schemaVersion='1.0'`, `converterVersion='0.2.0'`, `buildConfiguration='Release'`, `supportedRailroaderVersion='2025.1'`, `currentRailroaderVersion='2025.1.0'`, and `unityVersion='2022.3.62f2'`.

## Validation Architecture Gates

These are the core "better than legacy" gates. FUSE should not merely patch the live game and hope the result is okay.

- [ ] Validation follows the pipeline: discover -> parse -> normalize -> validate -> plan -> dry-run/preflight -> apply -> post-validate -> report -> cleanup.
- [ ] Parse/normalize/validate/preflight do not mutate the live Unity scene or base-game graph.
- [ ] Validation can run from command line or tooling without launching into a full game session where possible.
- [ ] Runtime validation, converter validation, and future editor validation share the same core validator contracts.
- [ ] Validation results use structured severities: info, warning, error, fatal.
- [ ] Validation results include package id, source file, definition id, field/path, expected kind, actual value, and recommended fix where possible.
- [ ] Hard errors block only the owning package or dependency chain unless the failure compromises global state.
- [ ] Optional references are explicitly marked optional and missing optional references are reported as info or controlled warnings.
- [ ] Unknown fields follow a written schema policy: ignored, warned, or rejected by schema version.
- [ ] Schema migrations are versioned and logged.
- [ ] Validator output can be copied directly into an issue report without requiring users to read stack traces.

## Cross-Reference Validation Gates

This is the area most likely to save future debugging pain.

- [ ] All cross-package references resolve before apply or are explicitly marked optional.
- [ ] Cross-reference report groups missing targets by source package and target kind.
- [ ] Cross-reference report shows source package, source definition, source field, target id, expected target kind, and resolution result.
- [ ] Track groups referenced by progressions, map features, industries, stations, scenery, or removals resolve correctly.
- [ ] Industry ids referenced by progressions, operations, interchanges, map icons, or components resolve correctly.
- [ ] Load ids referenced by industries, progression deliveries, formulas, or interchange transfers resolve correctly.
- [ ] Scenery/prefab/asset ids referenced by world objects and removals resolve correctly.
- [ ] Area/location ids referenced by stations, industries, progression display, and map icons resolve correctly.
- [ ] Passenger stop ids and station ids resolve correctly.
- [ ] Audio ids for whistles, horns, bells, and audio definitions resolve correctly.
- [ ] Removed objects cannot remain referenced by live definitions unless the same plan replaces them.
- [ ] Cyclic dependencies are detected and reported with the dependency chain.

## Separation / Ownership Gates

These keep FUSE from turning into another god-loader or pile of direct runtime hacks.

- [ ] `FuseModLoader` coordinates startup, shutdown, and pipeline entry points only.
- [ ] Package discovery, manifest parsing, dependency resolution, validation, apply, cleanup, diagnostics, and reporting are owned by separate services.
- [ ] Feature-specific apply logic is not added directly to `FuseModLoader` unless tagged as temporary with removal criteria.
- [ ] Each domain has an owner: track, scenery/world objects, industries/components, progression, audio, map tiles, operations, registry/claims, diagnostics, persistence.
- [ ] Definition models contain authored data only.
- [ ] Runtime binding models resolve authored data to live game/base objects.
- [ ] Apply systems perform live mutation.
- [ ] Persistence models contain saved state only.
- [ ] No Unity `GameObject`, `Component`, `Transform`, or scene reference leaks into pure definition/schema models.
- [ ] Shared services like id resolution, registry claims, diagnostics, schema migration, and dependency resolution have one canonical owner.
- [ ] No system creates a private duplicate registry, id resolver, validation framework, or dependency graph without an explicit reason.
- [ ] Any method that both validates and mutates is flagged for split.
- [ ] Cleanup ownership is registered when a runtime object/claim is created.
- [ ] Cleanup cannot destroy objects owned by another package or system.
- [ ] Unload reports identify which system released each claim type and how many leftovers remain.

## Runtime Invariants

These are conditions that should always be true after apply, post-validate, unload, and reload.

- [ ] Every live FUSE-owned runtime object has one authoritative owner package and owner system.
- [ ] Every stable id resolves to at most one live binding for its target kind.
- [ ] No two packages claim the same hard-owned runtime object unless an explicit override policy applies.
- [ ] Runtime graph collections contain no orphaned nodes, segments, spans, switches, or helper spans after apply.
- [ ] Disabled progression/map-feature groups do not report as graph errors when intentionally disabled.
- [ ] Enabled graph groups do not contain missing endpoint references.
- [ ] FUSE cleanup returns unloaded package-owned state to pre-load baseline where possible.
- [ ] Runtime apply is deterministic for the same package set, schema versions, dependency versions, and load order.
- [ ] Runtime systems never silently substitute a fallback target unless the definition explicitly allows fallback.
- [ ] All fallback behavior is logged with package id, source id, fallback id, and reason.
- [ ] No package can mutate another package's owned object except through a declared mixin/override/patch mechanism.
- [ ] All claim counts are stable across repeated load/unload cycles.

## Cleanup / Reload Gates

The old stack largely assumed content stayed loaded. FUSE needs to prove lifecycle ownership.

- [ ] Map unload releases registry claims, world suppression claims, audio definitions, map tile mounts, runtime object claims, and component bindings.
- [ ] Package unload order is deterministic and respects dependencies.
- [ ] Unload logs include package id, claim kind, released count, unresolved count, and cleanup owner.
- [ ] Reloading the same map after unload does not duplicate objects, components, audio entries, track objects, map icons, stations, or industries.
- [ ] Reloading with one package removed reports missing-owner or missing-reference behavior cleanly.
- [ ] Reloading with one package upgraded runs migrations or reports incompatible state cleanly.
- [ ] Cleanup routines are idempotent: running cleanup twice does not throw or corrupt state.
- [ ] Failed apply triggers rollback/cleanup of partial FUSE-owned changes where possible.
- [ ] Failed cleanup reports leftover claims instead of silently ignoring them.

## Save / Load / Persistence Gates

Beta needs to survive normal player behavior, not just one fresh startup.

- [ ] Save after loading supported FUSE packages.
- [ ] Exit to menu or quit game.
- [ ] Reload save and verify package state, graph state, industry state, progression state, station/location order, map icons, and components.
- [ ] Remove a non-critical package and reload save; missing-owner behavior is understandable and safe.
- [ ] Upgrade a package and reload save; migrations run or incompatible state is reported.
- [ ] Progression paid/completed/delivery counts persist correctly.
- [ ] FUSE-created industries/components do not duplicate after reload.
- [ ] FUSE-bound map features and progression-gated objects restore correct visibility after reload.
- [ ] Persistent state keys are versioned and namespaced by package/system.
- [ ] Persistence errors are reported without corrupting base-game save data.

## Progression Beta Gates

Progression setup is one of the areas FUSE should make less painful than base game authoring.

- [ ] Progression project ids are unique and stable.
- [ ] Progression prerequisites resolve and cycles are detected.
- [ ] Progression costs, payment state, delivery state, and completion state validate before purchase is allowed.
- [ ] Delivery target ids resolve to valid industries/tracks/components.
- [ ] Required load ids resolve before a progression can become purchasable.
- [ ] Unlock effects validate before runtime purchase is allowed.
- [ ] Track groups, scenery groups, industries, areas, map features, passenger stops, and objects referenced by unlock effects resolve.
- [ ] Hidden/unavailable progression-gated objects are hidden until unlocked.
- [ ] Completing a progression applies all effects once and only once.
- [ ] Removing a progression package from an existing save degrades cleanly or blocks with a clear message.
- [ ] Progression diagnostics show paid state, phase state, delivery counts, missing references, and unlock effect status.
- [ ] Progression authoring/conversion reports explain what the player must deliver and where.

## Industry / Component Beta Gates

- [ ] Industry ids are unique and stable.
- [ ] Industry tracks/spans resolve before apply.
- [ ] Empty source-authored industry shells are classified clearly and not treated as failed bindings.
- [ ] Component kinds are registered before definitions are applied.
- [ ] Duplicate component builders are rejected or explicitly overridden.
- [ ] Missing component builders fail loudly with package/id context.
- [ ] Loading and unloading components resolve load ids.
- [ ] Formulaic components validate producer/consumer behavior without base-game validation spam.
- [ ] Interchange transfer targets resolve or report actionable missing targets.
- [ ] Industry/component cleanup removes FUSE-created runtime bindings on unload.

## Graph / Track Beta Gates

- [ ] Full graph preflight validates nodes, segments, spans, switches, groups, removals, restores, and generated helper objects as a final-state plan.
- [ ] Dry-run graph apply result matches committed apply result.
- [ ] Segment endpoints resolve to planned or live nodes.
- [ ] Spans do not reference removed segments unless the same plan restores or replaces them.
- [ ] Same-segment and helper-span repairs are classified as compatibility repairs, not dirty failures.
- [ ] Graph rebuild does not produce duplicate nodes, duplicate segments, orphan spans, or invalid switch state.
- [ ] Track removals and restores produce structured diagnostics with package, operation, phase, kind, id, and reason.
- [ ] Base-game graph ownership is not silently overwritten without a declared patch/mixinto/override path.
- [x] Dump commands can export original graph and post-FUSE graph for comparison.
  - Code/build complete 2026-05-14: `/fuse.dumpgraph` exports `FUSE-original-graph.json`; `/fuse.dumpruntimegraph` now exports `FUSE-runtime-graph.json`. Needs in-game verification of the new runtime dump before closing.
  - Runtime verified 2026-05-14: original dump contains `1596` nodes, `1620` segments, and `316` spans; runtime dump contains `5571` nodes, `5234` segments, `589` spans, and `61` areas. Case-insensitive reference audit found `0` bad segment endpoints and `0` spans pointing at missing segments.
  - Runtime check 2026-05-14 before the resolved-endpoint filter found `0` bad references but `53` endpointless graph-registered helper/industry spans in the runtime dump.
  - Code/build follow-up 2026-05-14: runtime graph dump now filters to resolved graph spans with real upper/lower segment endpoints. Needs one fresh `/fuse.dumpruntimegraph` run to verify no endpointless helper spans appear in the dump.
  - Runtime verified 2026-05-14 after the resolved-endpoint filter: `FUSE-runtime-graph.json` contains `5571` nodes, `5234` segments, `536` resolved spans, and `61` areas; reference audit found `0` bad segment endpoints, `0` spans pointing at missing segments, and `0` endpointless spans.

## World Object / Scenery / Asset Beta Gates

- [ ] World object ids are unique and stable.
- [ ] Prefab/scenery/asset references resolve before apply or report controlled optional skips.
- [x] Unknown scenery asset count is zero for the supported beta corpus.
  - Fresh 2026-05-13 runtime verified: final report shows `0 unknown scenery assets`.
- [ ] Removals/suppressions are owned, counted, and released on unload.
- [ ] World object placement validates position, rotation, scale, parent/group, and required component kind.
- [ ] Map icons, labels, mandelas, roads, rivers, trestles, turntables, stations, loaders, and scenery objects visually/functionally match supported legacy behavior where claimed.
- [ ] Missing optional scenery does not fault unrelated packages.
- [x] Asset packs mount once and do not repeatedly remount during normal load.
  - Fresh 2026-05-15 runtime verified: LocalLow mirroring is skipped, 81 direct asset pack stores are added once to `PrefabStore`, package asset registration reports `elapsedMs=0`, and map tiles mount only in the two expected windows (`staged world apply` and `MapStore.Load`).

## Audio Beta Gates

- [ ] Whistle/horn/bell/audio definition ids are unique and stable.
- [ ] Audio packages register definitions once.
- [ ] Audio definitions release on package unload.
- [ ] No-op audio unload logs are quiet by default.
- [ ] Missing optional audio assets degrade cleanly.
- [ ] Audio diagnostics can list package-owned definitions and release counts.

## Converter Beta Gates

The converter is now part of the product, not just a helper script.

- [ ] Converter output is deterministic from identical source content and converter version.
- [ ] Converter preserves stable ids across reconversion where source identity is stable.
- [x] Converter never invents unexpected late files without an explicit report entry.
  - Scratch verified 2026-05-14: full supported corpus conversion produced no `*Late*.json` files in `_work\beta-corpus-conversion-20260514`.
- [x] Converter distinguishes source-authored issues, converter limitations, compatibility repairs, and runtime incompatibilities.
  - Code/reporting verified 2026-05-15: converter reports use outcome buckets for converted, repaired, preserved, unresolved, unsupported, dependency-required, warning, and error entries; known successful repairs are info-level instead of release-blocking warnings.
- [x] Converter writes per-package `conversion-report.json` and `conversion-report.md`.
  - Scratch verified 2026-05-14: each converted package folder under `_work\beta-corpus-conversion-20260514` contains both per-package report files.
- [x] Batch converter writes central batch reports and copies per-package reports into an audit folder.
  - Scratch verified 2026-05-14: `conversion-batch-report.json`, `conversion-batch-report.md`, and numbered copies under `conversion-reports\` were written for all `12` conversion inputs.
- [ ] Converter output passes runtime validator before being considered beta-supported.
- [x] Converter warnings are actionable and include source file references.
  - Code/scratch verified 2026-05-14: spanless passenger-stop warnings now include the source JSON (`GCR_Stations.json` for `topton-station`), and the supported corpus warning rows have `0` blank file references.
- [x] Converter preserves legacy file concerns unless the source is metadata-only.
  - Scratch verified 2026-05-15 from current corpus notes: each legacy data JSON converts to a matching FUSE data file by source stem, metadata-only files do not become fake content fragments, and artificial `*Late*.json` buckets are no longer generated.
- [ ] Converter has regression tests for the supported legacy corpus.
- [x] Converter has a documented unsupported-feature policy.
  - Documentation pass 2026-05-15: `tools\FUSE_CONVERTER.md` now defines `converted`, `repaired`, `preserved`, `dependency-required`, `unresolved`, `unsupported`, and `error` outcomes, and lists intentional beta unsupported/deferred areas so unsupported source data is reported instead of silently dropped.

## Diagnostics / Logging Beta Gates

- [x] FUSE writes a dedicated `FUSE.log` and keeps FUSE issues separated from unrelated mod stack traces where possible.
  - Fresh 2026-05-13 runtime verified from `C:\Users\roger\AppData\LocalLow\Giraffe Lab LLC\Railroader\FUSE.log`; FUSE warning/error scan is clean.
- [x] Final package report includes loaded, applied, skipped, disabled, faulted, warnings, errors, conflicts, suppressions, unknown assets, graph issues, and progression transfer skips.
  - Fresh 2026-05-13 runtime verified: final package report includes package counts/warnings/errors, and the user-facing report includes conflicts, suppressions, unknown scenery assets, graph issues, and transfer skips.
  - Code/build pass 2026-05-14: visible toast is now compact (`FUSE: <n> loaded | faults ... | /fuse.report`) so it fits on startup; full detailed report remains available from `/fuse.report`.
  - Code/build pass 2026-05-14: public toast wording now uses full labels `transfers` and `suppressions` instead of shorthand `xfers` and `supp`.
- [x] Normal logs are quiet enough for testers to read.
  - Fresh 2026-05-13 runtime verified: no warning/error/exception matches, per-object details remain suppressed behind `Settings.VerboseApplyReportDetails=true`, and remaining package/apply summaries are actionable.
- [x] Verbose mode exists for per-object apply details.
  - Code verified 2026-05-14: `Settings.VerboseApplyReportDetails` is read from `Info.json`; normal `FuseApplyReport` output suppresses per-object details and points testers to the setting, while verbose mode re-enables per-object apply and authoring bind details.
- [x] Console commands expose reports for loaded packages, conflicts, assets, graph, progressions, operations, and dumps.
  - Code/runtime verified previously for `/fuse.report`, `/fuse.loaded`, `/fuse.conflicts`, `/fuse.assets`, `/fuse.graph`, `/fuse.progressions`, `/fuse.operations`, `/fuse.dumpgraph`, and `/fuse.dumpmandelas`.
  - Code/build complete 2026-05-14: `/fuse.dumpruntimegraph` added, deployed, and then tightened to omit endpointless helper spans; needs one more in-game command verification after the resolved-endpoint filter.
  - Runtime verified 2026-05-14: `/fuse.dumpruntimegraph` wrote a fresh post-FUSE graph dump with no bad segment/span references and no endpointless helper spans.
- [x] Diagnostics are structured enough to be machine-parsed later.
  - Code/build complete 2026-05-15: `/fuse.report json` returns the last map-load report as structured JSON with summary, counts, package states, conflicts, suppressions, unknown assets, graph issues, transfer skips, and notices. Human `/fuse.report` output is unchanged.
- [x] In-game FUSE health surface exists for report viewing and safe runtime recovery actions.
  - Code/build complete 2026-05-15: FUSE adds a base-game HUD button under `TopRightArea/Strip` and opens a base-game `Window`/`UIPanelBuilder` Health page with status checks, settings state, problem counters, and `Reload Track` / `Reload Terrain` buttons routed through FUSE runtime services and multiplayer guard policy. Follow-up fix 2026-05-15 positions the icon at the end of the base-game strip, directly before cash when the cash transform is discoverable, gives the button an explicit layout/raycast target, fixes collapsed row layout in FUSE-created windows, flattens the health content into explicit rows inside a base-game scroll view, and adds live FPS/frame-time/memory counters.
  - Code/build complete 2026-05-15: Health UI now uses tabbed base-game pages (`Overview`, `Packages`, `Assets`, `Runtime`, `Logs`, `Settings`, `Mod Sets`). Added load-order/dependency display, asset-store/duplicate-key diagnostics, runtime object counts, last-log drilldown, health JSON export, active mod manifest export, package profile hash, and scroll-position restore after setting/mod-set button clicks.
  - Code/build pass 2026-05-15: Health UI tab buttons now fit on one top row, long timing/package/filter strings get wrap hints, and the Assets tab now explains duplicate asset keys as overlap diagnostics with copy/export actions for the full hidden store/key list.
  - Code/build pass 2026-05-15: Added persisted `Advanced Details` gating so healthy/default views stay curated, while asset duplicate winners/overrides, full store paths, registry internals, and live log tails remain available when explicitly enabled.
- [x] Server/mod-pack profile management exists for multiplayer package stacks.
  - Code/build complete 2026-05-15: FUSE Health now has a `Mod Sets` page backed by `LocalLow\...\FUSE\mod-sets.json`; UMM disabled mods remain invisible to FUSE, no selected set means all UMM-active packages/asset packs are enabled, and selected sets act as a FUSE-level filter over the UMM-active package stack.
  - Code/build complete 2026-05-15: Mod Sets now auto-create a server profile when toggling a mod from the all-active default state, persist across restarts, export `active-mod-set-manifest.json`, and show a local package fingerprint for multiplayer comparison. This is local compatibility/profile validation, not a FUSE-to-FUSE network handshake.
- [ ] Every warning has a clear owner: FUSE bug, source-authored issue, converter limitation, external mod issue, or unsupported beta feature.
- [x] External mod errors are not counted as FUSE faults unless FUSE caused or owns the failure.
  - Fresh 2026-05-15 runtime verified: `FUSE.log` and the public report stay at `faults 0` while `Player.log` still contains unrelated external/game-content errors from `LegosLibraryOfStuff`, `UtilitiesMod`, `BRSS`, `Enviro`, missing rolling-stock identifiers, and non-FUSE consist/track references.
- [ ] Crash/failure reports include last package phase and last apply operation where possible.

## Performance / Load-Time Beta Gates

- [ ] Load time benchmark is recorded against the legacy mod loader stack for equivalent content.
- [ ] Load time benchmark includes cold start and warm start where practical.
- [ ] Converter time is benchmarked separately from runtime load time.
- [x] FUSE avoids repeated map tile remount loops.
  - Fresh 2026-05-13 runtime verified: map tiles mounted twice for the expected windows only, once for `staged world apply` and once for `MapStore.Load`; no repeated remount loop.
- [x] FUSE avoids repeated graph rebuild loops.
  - Fresh 2026-05-15 runtime verified: staged graph apply performed one `merged-single-graph-rebuild` during package apply (`1677 ms`) and no repeated graph-rebuild loop appeared in `FUSE.log`.
- [ ] Validation overhead is measured and acceptable for beta content size.
- [ ] Memory growth across repeated load/unload cycles is checked.
- [ ] Logging volume is measured and does not balloon across repeated loads.
- [x] In-game performance counters expose practical beta diagnostics.
  - Code/build complete 2026-05-15: Health UI reports FPS, frame time, managed memory, Unity allocated/reserved memory, total FUSE map-load timing, disk load, runtime apply, slowest package/phase, direct asset store timing/count, map-mask rebuild timing, and console setup timing.

## Multiplayer / Network Policy Gates

Beta does not need full multiplayer support unless explicitly promised, but behavior must be safe.

- [x] README states multiplayer support level: unsupported, host-only, degraded-safe, or supported.
  - Documentation pass 2026-05-15: `README.md` states beta multiplayer is compatibility-mode only. FUSE does not sync packages; host and clients must have identical enabled FUSE packages and load order.
- [x] Clients apply local package mutations only under documented compatibility mode or strict-block settings.
  - Code/build complete 2026-05-15: default non-host clients apply the local package stack with a warning, matching legacy mod loader behavior when every player has the same mods. `Settings.BlockNonHostMultiplayerClientWorldApply=true` restores strict non-host mutation blocking for server tests.
- [x] Host/client package mismatch behavior is documented.
  - Documentation pass 2026-05-15: `README.md` and `KNOWN_ISSUES.md` state that FUSE does not negotiate host/client package mismatches; every player must use the same FUSE version, enabled packages, and load order.
- [x] Network-bound mutations are blocked or host-authoritative.
  - Code/build complete 2026-05-15: FUSE does not send graph/world/operations mutations over the network. In compatibility mode every peer applies the same local package stack; strict client blocking is available when desired.
- [ ] Multiplayer startup with FUSE installed does not silently corrupt world state.
  - Code/build complete 2026-05-15: compatibility-mode clients warn and apply local packages; strict mode blocks non-host apply. Still needs an actual multiplayer smoke test with identical package stacks before checking off the runtime verification gate.
- [x] If multiplayer is limited, the user gets a clear warning rather than undefined behavior.
  - Code/build complete 2026-05-15: first non-host client mutation attempt logs the compatibility-mode requirement for identical FUSE version/package stack; strict mode logs that client mutation was blocked.

## Compatibility / Legacy Comparison Gates

These prove FUSE is better than the old stack without copying it.

- [ ] FUSE supports the claimed legacy content categories through normalized definitions, not direct legacy shape dependency.
- [x] Legacy behaviors that are intentionally not supported are listed.
  - Documentation pass 2026-05-15: `README.md`, `KNOWN_ISSUES.md`, and `tools\FUSE_CONVERTER.md` list signals, arbitrary script mods, rolling stock outside audio, normal three-way switches, and unsupported custom components without their owning assembly as intentionally unsupported/deferred beta areas.
- [ ] Legacy conversion repairs are documented and deterministic.
- [ ] FUSE avoids live-game partial mutation failures that leave the session in an unknown state.
- [ ] FUSE diagnostics explain failures better than legacy stack logs.
- [ ] FUSE cleanup/unload behavior is demonstrably stronger than legacy behavior.
- [ ] No competitor decompiled code is copied; legacy mods are used only as behavioral/reference evidence.

## Future Editor Compatibility — Non-Blocking For Current Beta

The editor is not a current release blocker because it is not ready for review. These gates protect FUSE so the future editor has a clean target.

- [ ] FUSE definitions use stable ids suitable for future editor ownership.
- [ ] Runtime validation is reusable by future tools.
- [ ] Converter output uses normalized FUSE definitions instead of one-off legacy shapes.
- [ ] No runtime-only hack becomes required authoring data.
- [ ] Deleting or renaming definitions can be validated as dependency-impact operations later.
- [ ] Editor-authored packages and converter-authored packages are expected to flow through the same runtime pipeline when the editor exists.

## Documentation / Release Notes Gates

- [x] README explains install, uninstall, update, rollback, supported game version, supported content types, and known limitations.
  - Documentation pass 2026-05-14: added `README.md`.
- [x] CHANGELOG lists major fixes, breaking changes, schema changes, and converter changes.
  - Documentation pass 2026-05-14: added `CHANGELOG.md`.
- [x] Known issues document lists unsupported systems and external-mod conflicts.
  - Documentation pass 2026-05-14: added `KNOWN_ISSUES.md`.
- [x] Troubleshooting guide explains which logs/reports to attach.
  - Documentation pass 2026-05-14: added `TROUBLESHOOTING.md`.
- [x] Package author guide explains ids, dependencies, optional references, validation, and diagnostics.
  - Documentation pass 2026-05-14: added `PACKAGE_AUTHOR_GUIDE.md`.
- [x] Converter guide explains how to run conversion, read reports, and classify warnings.
  - Existing `tools/FUSE_CONVERTER.md` covers converter usage and report reading; `README.md` now links it from the root docs.
- [x] Beta test matrix is included or linked.
  - Documentation pass 2026-05-14: added `BETA_TEST_MATRIX.md`.
- [ ] License and contribution policy are present before public distribution.

## Beta Exit Criteria

These define when beta is healthy enough to move toward a release candidate.

- [ ] Supported corpus passes conversion, validation, runtime load, save/load, unload/reload, and cleanup gates on a clean install.
- [ ] At least one full regression pass is repeated after no code changes except version/release metadata.
- [ ] All P0 bugs are closed or explicitly deferred with a public known-issue note.
- [ ] P1 bugs are triaged and do not block normal supported content usage.
- [ ] Tester-reported issues can be reproduced from logs/reports without private context.
- [ ] FUSE can be removed from the game install without leaving required base-game files modified.
