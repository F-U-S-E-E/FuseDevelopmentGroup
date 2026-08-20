# Legacy replacement parity audit

This file is the release gate for claims that FUSE replaces an older loader or
library. A package is not "replaced" merely because FUSE satisfies its dependency
identifier, skips its DLL, or can deserialize some of its data. Every observable
data contract and runtime behavior must have a FUSE-owned implementation, tests,
and an in-game verification result.

The decompiled projects are used only to identify wire formats and observable
behavior. FUSE does not copy, ship, or execute their implementations.

Status meanings:

- **Verified**: FUSE-owned implementation, automated coverage, and in-game check.
- **Implemented**: FUSE-owned implementation and automated coverage; in-game check pending.
- **Partial**: useful support exists, but at least one listed behavior is absent or unverified.
- **Missing**: no FUSE-owned equivalent exists.
- **Hosted only**: FUSE can run the old code; this is not native replacement parity.
- **N/A**: build-time dependency or retired UI that is intentionally superseded by a named native workflow.

Do not move a row to Verified without recording the test and in-game fixture.

## AssetLoader

| Legacy contract | FUSE-owned implementation | Status | Release evidence still required |
|---|---|---|---|
| Register enabled UMM package roots/children containing `Catalog.json` (bundle optional) | Direct asset-pack registry | Implemented | Catalog-only and normal bundle in-game corpus; disabled-package behavior |
| Resolve mod-folder base paths without copying to LocalLow | `fuseasset://` direct stores and runtime-store path patches | Implemented | In-game pack/bundle fixture on every supported install path |
| Definitions-only child folder override keyed by store identifier | Native compatibility override registry plus `FuseDefinitionOverrides` | Implemented | Tender/rolling-stock swap in-game corpus; malformed and duplicate targets covered automatically |
| Legos/other `ContainerSerialization.Deserialize` edits | One cold native deserialize per direct store | Implemented | Full LLW/customization/save-reload in-game fixture |
| Historical UMM dependency id `AssetLoader` when old DLL is absent | Installer-generated data-only UMM alias requiring FUSE | Implemented | Real UMM startup with an old dependent package and no AssetLoader DLL |
| Coexistence with old AssetLoader | Runtime disables only the old Harmony owner; installer moves the old runtime to backup and supplies a data-only dependency alias | Implemented | Test manual coexistence in both load orders plus installer migration to a real no-DLL setup |


## Confusing Supplements

| Legacy contract | FUSE-owned implementation | Status | Release evidence still required |
|---|---|---|---|
| `ConfusingSupplements.Bodygroups` asset component, `cs.bodygroups.*` save keys, model-path switching, Customize UI | `FuseConfusingSupplementsBodygroups*` | Implemented | Asset-pack fixture with multiple groups; save/reload and multiplayer permission check |
| `ConfusingSupplements.LabelPrinter` asset component and `cs.labelprinter.*` text dictionaries | `FuseConfusingSupplementsLabelPrinter*` | Implemented | Road-number and lettering templates; save/reload; malformed group/name fixture |
| `ConfusingSupplements.Refiller` rolling-stock transfer component | `FuseConfusingSupplementsRefiller*` | Implemented | Coupled diesel/steam/tender transfer fixture; host/client behavior; air connection changes |
| `ConfusingSupplements.DestinationSign` model, decal, picker cycling, culling | `FuseConfusingSupplementsDestinationSign*` | Implemented | Real destination-sign prefab fixture; culling and destroy/reload check |
| `CS.LiverySwap`, `livery:<carId>` directory mixintos, `cs.livery` save key, Customize UI | `FuseConfusingSupplementsLivery*` | Implemented | Multi-car isolation, repeated switching, texture cleanup, save/reload fixture |
| Captive conversion loader/unloader | `FuseCaptiveConversionIndustryComponents` and component type normalization | Implemented | In-game production/storage/payment golden fixture |
| Pay-for-resource loader | `FusePay4ResourceIndustryComponent` | Implemented | In-game hours, rate, cost, book reason, and EOD golden fixture |
| Empty placeholder component, visible track marker, all auto-destination classes, inert service/order behavior | `FuseLegacyPlaceholderIndustryComponent` plus `IndustryAPI` legacy-empty handling | Implemented | In-game output-track highlighting/destination fixture and neighboring component ordering |
| `/cs-livery-refresh` and texture inspection diagnostics | `/cs-livery-refresh` and `/fuse.liveries` | Implemented | In-game texture edit/refresh and report fixture |

Automated coverage: `FUSE.Tests/Compatibility/FuseConfusingSupplementsCompatibilityTests.cs`,
`FUSE.Tests/Data/FuseIndustryComponentTypesTests.cs`,
`FUSE.Tests/API/FuseLegacyPlaceholderIndustryComponentTests.cs`, and the legacy
industry converter tests.

## RailLoader.Interchange public contract

| Contract | FUSE surface | Status | Known gap |
|---|---|---|---|
| `PluginBase`, `SingletonPluginBase`, enable/disable lifecycle | `RailloaderLegacySupport` + `FuseLegacyAssemblyHost` | Partial | Constructor/lifecycle matrix is not yet tested against every ZAMU/third-party assembly |
| `IModDefinition`, `IMod`, `ModReference`, version ranges | Compatibility models and manifest discovery | Partial | Full version-bound/load-order golden set pending |
| `IModdingContext.Mods`, `Requires`, `LoadAfter`, `LoadBefore`, `ConflictsWith` | Legacy manifest discovery and requirement resolver | Partial | Top-level conflict ids/bounds, conditional eligibility/ordering, and intentional-layer classification are covered; full hosted/version-bound in-game corpus remains open |
| File/directory/managed-object mixintos | Legacy host mixinto enumeration | Partial | Native/runtime-converted file mixintos preserve conditional requires/conflicts; managed-object and some hosted conditional shapes are not proven |
| Console command registration | Reflection bridge to game console | Partial | Command availability timing and command exception containment matrix pending |
| Settings load/save | FUSE legacy settings bridge | Implemented | Cross-version/in-game save fixture pending |
| JSON subtype and component-builder registration | `FuseLegacyTypeRegistry` | Implemented | Late-registration direct-store fixture pending |
| `IUIHelper` windows and panel population | `FuseLegacyUIHelper` | Partial | All overloads, resize, callbacks, and disposal need in-game verification |
| `IModTabHandler` | Hosted-plugin Mods UI integration | Partial | Rebuild/disposal and one-bad-tab containment fixture pending |
| `IUpdateHandler` | Hosted plugin update dispatch | Implemented | Per-frame fault isolation/in-game fixture pending |
| `WillCopyDebugInformation` event | Guarded event dispatch appended to copied `/fuse.report` health output | Implemented | Real hosted subscriber contribution and one-bad-listener in-game fixture |
| `UIPanelBuilderCompatibility` helpers | Compatibility extensions | Partial | Every original overload/arity needs a reflection test |

## RailLoader.Injector

| Injector responsibility | FUSE replacement | Status |
|---|---|---|
| Locate and parse legacy `Definition.json` packages | `FuseDataPackageDiscovery`, legacy converter, legacy assembly host | Partial |
| Dependency ordering, enable/disable, faults, conflicts | resolver, load report, package fault registry | Partial |
| Mixinto parsing including conditional requires/conflicts | legacy converter/host | Partial (data/runtime conversion implemented; hosted managed-object matrix pending) |
| Load old code mods without the injector assembly | assembly resolver and host | Partial |
| Register component builders and JSON discriminators | `FuseLegacyTypeRegistry` | Implemented |
| Console, mod settings, mod tabs, update handlers | legacy host/UI bridge | Partial |
| Loader status/settings window | FUSE Status/Mods/Settings tools | Partial |
| Live logging console | FUSE Live Diagnostics/live console | Implemented |
| Bulletin/update system | FUSE version check only | Partial |
| Drag-and-drop package installation | FUSE installer | Partial |
| Legacy patch-file detection/removal guidance | installer/install detector | Partial |

## Strange Customs

| Capability | FUSE replacement | Status | Known gap |
|---|---|---|---|
| Track node/segment/span patching and removals | legacy converter + `TrackAPI` apply planner | Partial | Conditional mixinto eligibility/order is covered; base-span ownership and destructive-edit in-game fixtures remain |
| Industries, loads, areas, operations components | legacy converter + native operations APIs | Partial | Complete custom component matrix and live payment/service fixtures |
| Scenery and mandelas/scene transforms | legacy converter + `SceneryAPI`/`SceneCloneAPI` | Partial | Road mandelas and several clone/path edge cases still reported |
| Roads/rivers/generic splineys | legacy converter + `SplineyAPI` + plugin host | Partial | Every handler/profile/end-style and hosted builder fixture |
| Auto trestles | world conversion/runtime bridge | Partial | Geometry golden fixture |
| Custom horns, whistles, and bells | FUSE audio API/patches | Partial | Full Strange Customs profile/keyframe compatibility matrix |
| Conditional visual controls/sliders | FUSE visual-condition patches | Partial | All condition combinations and save keys need fixtures |
| Direct asset-pack mounting and clone mixintos | direct-store registry/container mixinto registry | Partial | Duplicate-provider and late-registration stress fixtures |
| `FileCache` loose audio/texture runtime service | FUSE-owned timestamp-invalidating texture/audio cache with contained completion callbacks | Implemented | Real loose WAV/OGG/PNG package, edit-on-disk refresh, and unload fixture |
| `FlowyThingBuilder` ABI runtime build | Legacy DTO adapter backed by native `SplineyAPI` add/update | Implemented | Hosted plugin direct-build road/river fixture and parent-transform check |
| Graph-change events for hosted plugins | spliney plugin host and compatibility messages | Partial | Event ordering, mutation merge, and repeat-load fixtures |
| Legacy patch editor/undo/save API | ABI no-ops | N/A | Superseded by Tile Editor; editor parity is gated separately below |
| Reload/verify/dump commands | FUSE commands cover only some functions | Partial | Explicit command mapping and docs |

## Alina's Map Mod

| Capability | FUSE replacement | Status | Known gap |
|---|---|---|---|
| Nodes, segments, spans, groups, removals | legacy converter + `TrackAPI` | Partial | Full Alina fixture matrix and same-area competing packages |
| Scenery, map labels, map masks, map features | legacy converter + native world/progression APIs | Partial | Curve-mask and label edge fixtures |
| Areas, industries, loads, components | legacy converter + native operations APIs | Partial | Component ordering/replacement and disabled-industry fixtures |
| Loaders | legacy converter + loader API | Partial | Custom loader and track-snap authoring are editor gaps |
| Passenger stops and station agents | native passenger/station APIs | Partial | Complete timetable/neighbor/area golden fixture |
| Progressions, sections, delivery phases | native progression API | Partial | Full Alina progression corpus run |
| Turntables | `TurntableAPI` | Partial | Custom visual/controller and save fixture |
| Telegraph poles | `MapAPI` | Partial | Add/move/remove fixture |
| Early map-load application | early loader/lifecycle | Partial | Every supported scene/load entry point |
| Query tooltip extensions and validation window | FUSE diagnostics/inspector | Partial | One-to-one author-facing error coverage |
| Alina map editor | Tile Editor | N/A | Tile Editor completion gate below |

## Other ZAMU packages

These packages are not currently in FUSE's suppressed-package set. Most can be
hosted as old code mods, but hosted execution is not a native replacement.

| Package | Observable behavior inventory | Native FUSE status |
|---|---|---|
| AbsoluteMadness | outbound empty/loaded routing overrides; settings tab | Implemented: dependency-scoped native routing, industry capacity weighting, payment/grace calculation, and persisted settings; in-game route-generation fixture pending |
| ADRFDR | pay-for-resource industry component and picker title | Implemented: `ADRFDR.Pay4Resource` normalizes to the FUSE component and the picker honors `ICustomIndustryTitle`; in-game payment/title fixture pending |
| C1CD | configurable interchange service interval, hours, continuous delivery | Implemented: native bounded scheduling policy, overnight/daytime service windows, continuous extra-service scheduling, and persisted UI; in-game service-cycle fixture pending |
| CommandLine | third-party command-line parser dependency | N/A: load packaged dependency for hosted mods; no gameplay replacement |
| DediSevi | dedicated-server startup, RCON, player list, autosave, instant load, terrain/camera/UI suppression | Missing |
| FallFromGrace | grace-day calculation and inspector display | Implemented: native identity-safe grace transform, persisted compatibility settings, and due-time inspector row; in-game calculation/inspector fixture pending |
| ForYourConvenience | caboose map icons, clickable stations, live car tags, industry dashboard/settings | Implemented: dependency-scoped station-map actions, opt-in caboose icons and speed/load tag lines, persisted settings, and native read-only Industry Dashboard; in-game icon/click/tag/dashboard fixture pending |
| Interchange2Interchange | interchange contracts and car routing between interchanges | Implemented: dependency-scoped contracted interchanges, bounded daily cross-interchange orders, cargo discovery, inspector visibility, and persisted maximum-cut setting; in-game EOD/service fixture pending |
| SanityInitiative | unit/UI/auto-engineer/equipment/roster/placer/daily-report fixes | Missing |
| SerialTrafficControl | serial/TCP CTC protocol bridge and settings | Missing |
| SomeKindOfMadness | outbound routing hooks, target overrides, shuffle/prevent-blocking behavior/settings | Implemented: native candidate event, configurable chance/fill/payment/short-trip/origin behavior, safe order shuffling, and dependency-scoped activation; in-game route-generation and extension-event fixture pending |

## Tile Editor completion gate

The editor's canonical output is native FUSE schema. A visible export-mode
switch may select RailLoader legacy schema, but controls that cannot be expressed
faithfully must be disabled with a reason; native schema must never be reduced to
fit legacy limitations.

Completion requires a documented end-to-end fixture that starts with no map and:

1. acquires/imports terrain tiles and defines a new map;
2. sculpts terrain and paints vegetation, saves, reloads, and compares the result;
3. lays track, switches, spans, groups, grades, and interchanges;
4. places scenery, town labels/signs, roads/water, loaders, and custom loaders;
5. creates industries, operations, passenger stops, station agents, and progressions;
6. creates signals, routes, CTC logic, and a working CTC panel;
7. authors package feature/options groups for modular configurations;
8. launches the generated map under FUSE and validates save/reload behavior;
9. repeats the workflow from the published editor wiki without undocumented steps.
