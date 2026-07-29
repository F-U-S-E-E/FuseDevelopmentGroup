# Toolshed Service Facility Audit

Date: 2026-05-24

Scope: consolidate Oil Firing service-loader work into `Toolshed`, research vanilla Railroader loader/service systems, and document a reusable setup path for `coal`, `water`, `diesel-fuel`, `bunker-c`, and `pulpwood` wood fuel.

## 1. Files Inspected

Base game decompile:

- `C:\Hrogers_Railroader_mods_Projects\Decompiled dlls base game\Assembly-CSharp\RollingStock\CarLoadTargetLoader.cs`
- `C:\Hrogers_Railroader_mods_Projects\Decompiled dlls base game\Assembly-CSharp\RollingStock\CarLoaderSequencer.cs`
- `C:\Hrogers_Railroader_mods_Projects\Decompiled dlls base game\Assembly-CSharp\RollingStock\CarLoadTarget.cs`
- `C:\Hrogers_Railroader_mods_Projects\Decompiled dlls base game\Assembly-CSharp\Model\Ops\IndustryLoaderBase.cs`
- `C:\Hrogers_Railroader_mods_Projects\Decompiled dlls base game\Assembly-CSharp\Model\Ops\IndustryLoader.cs`
- `C:\Hrogers_Railroader_mods_Projects\Decompiled dlls base game\Assembly-CSharp\Model\Ops\IndustryUnloader.cs`
- `C:\Hrogers_Railroader_mods_Projects\Decompiled dlls base game\Assembly-CSharp\Model\Ops\InterchangedIndustryLoader.cs`
- `C:\Hrogers_Railroader_mods_Projects\Decompiled dlls base game\Assembly-CSharp\Model\Ops\Industry.cs`
- `C:\Hrogers_Railroader_mods_Projects\Decompiled dlls base game\Assembly-CSharp\Model\Ops\IndustryStorageHelper.cs`
- `C:\Hrogers_Railroader_mods_Projects\Decompiled dlls base game\Assembly-CSharp\Model\CarPrototypeLibrary.cs`
- `C:\Hrogers_Railroader_mods_Projects\Decompiled dlls base game\Assembly-CSharp\Model\SteamLocomotive.cs`
- `C:\Hrogers_Railroader_mods_Projects\Decompiled dlls base game\Assembly-CSharp\Model\DieselLocomotive.cs`
- `C:\Hrogers_Railroader_mods_Projects\Decompiled dlls base game\Assembly-CSharp\Model\BaseLocomotive.cs`
- `C:\Hrogers_Railroader_mods_Projects\Decompiled dlls base game\Assembly-CSharp\Model\AI\AutoEngineerFuelAlerter.cs`
- `C:\Hrogers_Railroader_mods_Projects\Decompiled dlls base game\Assembly-CSharp\Effects\WaterCylinderController.cs`
- `C:\Hrogers_Railroader_mods_Projects\Decompiled dlls base game\Assembly-CSharp\RollingStock\Controls\KeyValueBoolParticlePlayer.cs`

Mod projects and assets:

- `C:\Hrogers_Railroader_mods_Projects\Oil Firing\*.cs`
- `C:\Hrogers_Railroader_mods_Projects\Oil Firing\ServiceFacility_Audit.md`
- `C:\Hrogers_Railroader_mods_Projects\Oil Firing\ServiceFacility_SetupGuide.md`
- `C:\Hrogers_Railroader_mods_Projects\Oil Firing\ALW.SceneryAssets\SCAssetPacks\ALWLoaders`
- `C:\Hrogers_Railroader_mods_Projects\Oil Firing\WoodShed\wood shed`
- `C:\Hrogers_Railroader_mods_Projects\Toolshed\Toolshed.csproj`
- `C:\Hrogers_Railroader_mods_Projects\Toolshed\src\Main.cs`
- `C:\Hrogers_Railroader_mods_Projects\Toolshed\Examples`
- `C:\Hrogers_Railroader_mods_Projects\Toolshed\SCAssetPacks`
- `C:\Hrogers_Railroader_mods_Projects\Toolshed\src\ServiceFacilities\ServiceFacilityRuntime.cs`
- `C:\Hrogers_Railroader_mods_Projects\Toolshed\src\ServiceFacilities\ServiceFacilityDefinitionComponents.cs`
- `C:\Hrogers_Railroader_mods_Projects\Rail\API\LoaderAPI.cs`
- `C:\Hrogers_Railroader_mods_Projects\Rail\API\SceneryAPI.cs`
- `C:\Hrogers_Railroader_mods_Projects\Rail\Data\Operations\FuseOperationsDefinition.cs`
- `C:\Hrogers_Railroader_mods_Projects\Decompiled DLLs Not BASE GAME\LegoCompository\LegosLibraryOfStuff\*.cs`
- `C:\Users\roger\Downloads\Prefabs for Hunter.unitypackage`
- `C:\Users\roger\Downloads\Prefabs for Hunter (1).unitypackage`

## 2. Confirmed Call Flow

Physical loader flow:

1. A player control writes `request = true`.
2. `CarLoaderSequencer` observes `request`, sets `prepareLoad`, then `canLoad`, then uses `isLoading` to drive `animateLoad`.
3. `CarLoadTargetLoader` observes `canLoad` and runs host-only.
4. It checks nearby cars around the loader point using `TrainController.CheckForCarsAtPoint(point, radius + 2f, cars, null)`.
5. It rejects cars above `maximumSpeedInMph` and, when configured, non-player-owned cars.
6. It scans each car's child `CarLoadTarget` components.
7. A target matches only when `CarLoadTarget.slotIndex` points to a valid `LoadSlot`, `LoadSlot.RequiredLoadIdentifier == loader.load.id`, and flat distance is within `CarLoadTarget.radius + loader.radius`.
8. While the slot is not full and load is available, the loader sets `isLoading = true` and transfers once per second.
9. `Load()` updates `CarLoadInfo(load.id, quantity)` through `car.SetLoadInfo(slotIndex, ...)`.
10. `sourceIndustry == null` means infinite supply; `sourceIndustry != null` drains `sourceIndustry.Storage`.

Industry and interchange flow:

1. `Industry` owns an `IndustryStorageHelper` persisted through the industry's key-value object.
2. Shared storage is keyed by load ID under `storage`.
3. `IndustryUnloader` unloads railcars into storage and can order empties away.
4. `CarLoadTargetLoader` can drain that same storage when `sourceIndustry` is assigned.
5. `InterchangedIndustryLoader` sends empty/partial cars off map, fills them to capacity, charges `(capacity - currentQuantity) * load.costPerUnit`, and schedules return after `0.9583333` game days.

Locomotive consumption flow:

1. `SteamLocomotive.PeriodicUpdate()` hardcodes `coal` and `water`.
2. Toolshed oil support only intercepts oil-fired steam equipment, detected by a `bunker-c` fuel slot on the locomotive or tender.
3. Oil-fired steam drains `bunker-c` and `water`, using the existing coal consumption rate converted to gallons.
4. `DieselLocomotive` already drains slot 0 as `diesel-fuel`.
5. Wood-fired steam drains base-game `pulpwood` and `water`, using the existing coal consumption rate multiplied by the first-pass wood factor.

## 3. Base-Game Behavior Found

- Loader discovery is point/radius based.
- Industry delivery and ordering use `TrackSpan`; physical locomotive service does not.
- Load IDs match by exact string equality against `LoadSlot.RequiredLoadIdentifier`.
- Loading speed is effectively `outputRate` units per real second because the vanilla loop calls `Load(..., 1f)`.
- Animation is key-value driven with `request`, `prepareLoad`, `canLoad`, `isLoading`, and `animateLoad`.
- Vanilla authoring uses definition components and component builders for physical controls. `ToggleAnimationComponentBuilder` attaches a click target to the selected collider object and a `KeyValueBoolAnimator` to the model animator object; the route graph does not need to carry every chute/pivot detail.
- Water visual flow is not part of `CarLoadTargetLoader`; the base game uses a separate key-driven `WaterCylinderController` with a VFX rate set from the bool-driven flow value.
- Generic particle effects can reuse the same key-value pattern. Toolshed now supports `particleEffects` entries that target an existing particle object, create a runtime particle system, and optionally draw a visible stream fallback for dark liquids.
- Storage capacity is enforced by industry components such as `IndustryUnloader.maxStorage` and `IndustryLoaderBase.maxStorage`, not by the physical `CarLoadTargetLoader`.
- Cars persist load quantities per slot as `CarLoadInfo`.
- Industries persist storage and interchange return data in key-value state.

## 4. Missing Systems

- Wood consumption is a first-pass estimate, not a final balance pass. Current value: `1.8` wood pounds per coal pound.
- Wood validation accepts `pulpwood` plus `water` slots for steam equipment, but still needs in-game definition testing.
- Bunker-c economy values are still conservative runtime load definitions. `pulpwood` reuses the base-game economy load.
- Base-game `pulpwood` has `costPerUnit = 0`, so an `InterchangedIndustryLoader` can return loaded pulpwood cars without charging unless the FUSE test package overrides `pulpwood.costPerUnit`.
- The ALW tank-loader asset pack contains scenery metadata and a ladder, but no declared service loader components.
- The wood shed arrived as a bare Unity bundle. Toolshed now packages inferred catalog/definition metadata, but the internal prefab name must be validated in-game.
- Ash, sand, maintenance, service ordering, startup/warmup simulation, AI servicing, and economy rebalance remain future ideas.

## 5. Architecture Proposal

Implemented in Toolshed:

- Keep vanilla `CarLoadTargetLoader` for physical transfer.
- Keep vanilla `CarLoaderSequencer` for request and animation sequencing.
- Keep vanilla `CarLoadTarget` on cars/tenders as the load point marker.
- Keep vanilla `Industry.Storage` for finite storage.
- Keep vanilla `IndustryUnloader` for inbound delivery.
- Keep vanilla `InterchangedIndustryLoader` for purchase/interchange, except water.
- Add one reusable wrapper: `Toolshed.ServiceFacilities.UniversalServiceFacilityComponent`.
- Add one runtime bridge: `Toolshed.ServiceFacilities.ServiceFacilityRuntime`.
- Add editor-authored scenery definition components:
  - `ToolshedServiceStorage` stores the asset-side default load id, capacity, infinite/finite flag, and default loading rate.
  - `ToolshedServiceLoadPoint` marks one physical chute, pipe, hose, or standpipe outlet. Toolshed creates one vanilla loader/sequencer/click target/effect bridge for each load point.
- Let FUSE place normal scenery, then bind only route-specific data from `ToolshedServiceFacilities.json`: placed scenery id, source industry id, service span id, and optional `loadPointId` for multi-outlet assets.
- Allow alias arrays for scenery ids and span ids so service definitions can survive FUSE id renames during testing.
- Prefer load-point-local click colliders over root pickables for new assets. This keeps dual chutes independent and prevents a tender hover from showing the loader action.
- Use `storageAnimations` for visible inventory, such as sampling the wood pile clip from empty to full based on `Industry.Storage`.
- Use `particleEffects` for custom oil/diesel/solid loading effects. Bunker-C test config uses `animateLoad` plus `requiredBoolKey = "request"` so flow follows the vanilla visible-loading state and still requires the pipe to be down.
- Use `requireServiceCondition` for loaders that must be physically in position before transfer. The bunker-C test loader requires `request = true`, which is the pipe-down state.
- Suppress `IndustryUnloader` delivery revenue for storage-only service facilities, so base-game payable loads such as `pulpwood` can stock an engine-service wood pile without paying the player like a normal industry delivery.
- Keep Toolshed as the single Unity Mod Manager logic mod, with FUSE data packages providing placement and operations data.
- Keep oil/wood firing code separate in `Toolshed.OilWoodFiring`.

Supported load IDs:

- `coal`
- `water`
- `diesel-fuel`
- `bunker-c`
- `pulpwood` for wood fuel

Key wrapper fields:

- `serviceLoadId`
- `infiniteSupply`
- `facilityCapacity`
- `currentStorage`
- `loadingRate`
- `serviceRadius`
- `enableExtendedTenderSearch`
- `extendedSearchRadius`
- `extendedLoadTargetRadius`
- `maximumSpeedMph`
- `serviceTrackSpan`
- `linkedIndustry`
- `requirePlayerOwnedCars`
- `canPurchaseThroughInterchange`
- `purchaseDelayDays`
- `canLoadBoolKey`
- `isLoadingBoolKey`

## 6. Risks

- Runtime-added industry components may miss cached industry component arrays; the wrapper clears the private cache, but operations-side storage and interchange should still be authored in FUSE data where possible.
- `purchaseDelayDays` is documented for future compatibility; vanilla interchange currently hardcodes the return delay.
- Off-map purchase is per active interchange. To make bunker-C or pulpwood buyable from Andrews as well as Sylva/Whittier, add separate `InterchangedIndustryLoader` children under `andrews-interchange` using Andrews' own spans.
- Water interchange is intentionally blocked.
- Custom cars/tenders must have matching load slots and `CarLoadTarget.slotIndex` values.
- Vanilla `CarLoadTargetLoader` can lock onto the first exact car at the service point. Custom scenery placed beside longer locomotive/tender sets should enable Toolshed's extended tender search so the loader can skip nearby supply cars and find the matching tender target.
- If the separate `OilFiring` mod is also installed, oil/wood patches may run twice. Use Toolshed as the consolidated dependency and remove the old mod from active testing.
- Wood shed metadata was corrected from `Prefabs for Hunter.unitypackage`: canonical prefab `wood-shed.prefab`, public model ID `wood-shed`, compatibility alias `wood_fuel_shedp`, and animation clip `Wood Pile`.
- ALW tank loader spout animation now uses animation-map key `PipeTurner`. The earlier test bundle used `RotatePipe`, so the JSON binding must match the active bundle.
- The FUSE test package patches the existing base-game `dillsboro-roundhouse-ops` industry so bunker-c and wood show under Dillsboro Engine Service instead of creating a separate Dillsboro Wood Shed location.
- Loader rates were tuned down from debug values. Current Dillsboro test values are `6` gal/s for bunker-C and `250` lb/s for pulpwood.

## 7. Implementation Checklist

- [x] Research vanilla loader, sequencer, storage, interchange, and locomotive systems.
- [x] Move Oil Firing service/fuel code into Toolshed.
- [x] Register runtime `bunker-c` and use base-game `pulpwood` for wood fuel.
- [x] Preserve oil-fired steam bunker-c consumption.
- [x] Add wood-fired steam detection, validation, and first-pass consumption.
- [x] Add reusable `UniversalServiceFacilityComponent`.
- [x] Add optional `[ServiceFacility]` and `[ServiceFacility][Loader]` logs.
- [x] Package ALW loader assets into Toolshed.
- [x] Package wood shed bundle with inferred asset-pack metadata.
- [x] Keep legacy placement wrappers out of the deployed Toolshed package.
- [x] Add community setup documentation.
- [x] Add runtime service binding for FUSE placed scenery.
- [x] Remove hard FUSE dependency from Toolshed logic.
- [x] Add visible scenery-root service pickables for hover/action prompts.
- [x] Add storage-percentage animation support for the wood pile.
- [x] Add key-driven particle-effect support for bunker-C/oil loading visuals.
- [x] Add pipe-down service gating so bunker-C does not load unless the spout is in position.
- [x] Slow Dillsboro bunker-C and wood loader rates from debug values to testable service rates.
- [x] Suppress delivery payout for service-facility storage-only unloaders.
- [x] Build Toolshed successfully.

Remaining:

- [ ] Validate `WoodShed` prefab identifier in-game.
- [ ] Validate `ToolshedServiceFacilities.json` bindings in-game with FUSE-placed assets.
- [ ] Validate the same bindings from a FUSE `world.scenery` package.
- [x] Receive/update ALW loader prefabs with real outlet empties parented to the moving parts. Current names are `FuelLoaderFill` for the tank loader, `CoalStartUnity` and `CoalStartUnity.001` for the dual-track tower, and `UnitySingleLoadChute` for the single-track tower. The FUSE/test configs should require real parents and avoid guessed fallback stream origins.
- [ ] Tune `bunker-c` `costPerUnit` later.
- [ ] Tune wood consumption after live testing.

## 8. Exact Files Created Or Modified

Created:

- `C:\Hrogers_Railroader_mods_Projects\Toolshed\README.md`
- `C:\Hrogers_Railroader_mods_Projects\Toolshed\ServiceFacility_Audit.md`
- `C:\Hrogers_Railroader_mods_Projects\Toolshed\Examples\service-facility-setup-guide.md`
- `C:\Hrogers_Railroader_mods_Projects\Toolshed\src\ServiceFacilities\ServiceFacilityDefinitionComponents.cs`
- `C:\Hrogers_Railroader_mods_Projects\Toolshed\src\ServiceFacilities\*.cs`
- `C:\Hrogers_Railroader_mods_Projects\Toolshed\src\OilWoodFiring\*.cs`
- `C:\Hrogers_Railroader_mods_Projects\Toolshed\SCAssetPacks\WoodShed\*`
- ALW loader models are no longer hosted by Toolshed; they should come from `ALW.SceneryAssets` or another external scenery asset pack.
- `C:\Hrogers_Railroader_mods_Projects\Toolshed\FuseServiceFacilityTest\Info.json`
- `C:\Hrogers_Railroader_mods_Projects\Toolshed\FuseServiceFacilityTest\service-facility-test.fuse.json`
- `C:\Hrogers_Railroader_mods_Projects\Toolshed\FuseServiceFacilityTest\ToolshedServiceFacilities.json`
- `C:\Hrogers_Railroader_mods_Projects\Toolshed\FuseServiceFacilityTest\README.md`

Modified:

- `C:\Hrogers_Railroader_mods_Projects\Toolshed\src\Main.cs`
- `C:\Hrogers_Railroader_mods_Projects\Toolshed\src\ServiceFacilities\ServiceFacilityRuntime.cs`
- `C:\Hrogers_Railroader_mods_Projects\Toolshed\src\ServiceFacilities\UniversalServiceFacilityComponent.cs`
- `C:\Hrogers_Railroader_mods_Projects\Toolshed\src\ServiceFacilities\ServiceFacilityParticleEffectDriver.cs`
- `C:\Hrogers_Railroader_mods_Projects\Toolshed\src\ServiceFacilities\ServiceFacilityDefinitionComponents.cs`
- `C:\Hrogers_Railroader_mods_Projects\Toolshed\Examples\service-facility-setup-guide.md`
- `C:\Hrogers_Railroader_mods_Projects\Toolshed\ServiceFacility_Audit.md`
- `C:\Hrogers_Railroader_mods_Projects\Toolshed\FuseServiceFacilityTest\ToolshedServiceFacilities.json`
- `C:\Hrogers_Railroader_mods_Projects\Toolshed\FuseServiceFacilityTest\service-facility-test.fuse.json`
- `C:\Hrogers_Railroader_mods_Projects\Toolshed\Toolshed.csproj`
- `C:\Hrogers_Railroader_mods_Projects\Toolshed\Info.json`

Packaging note:

- Toolshed remains the single Unity Mod Manager logic mod.
- FUSE should use Toolshed's packaged `SCAssetPacks` directly and bind service definitions through `ToolshedServiceFacilities.json`.

Verification:

- `dotnet build .\Toolshed.csproj -c Release` succeeded with 0 warnings and 0 errors.

## 9. Manual Unity Setup Needed

For each finished service asset using explicit Unity components:

1. Add `KeyValueObject`.
2. Add `CarLoadTargetLoader`.
3. Add `CarLoaderSequencer` when the model has moving parts or effects.
4. Add `Toolshed.ServiceFacilities.UniversalServiceFacilityComponent`.
5. Set `serviceLoadId`.
6. Match key names between controls, sequencer, and loader.
7. If the loader has a physical service position, set `requireServiceCondition` and point it at the key that means pipe/chute/hose down.
8. For oil or other visible flow, add a particle object to the prefab or configure `particleEffects` to create one at runtime. Best practice for community assets is a normal Unity empty at the exact spout/chute tip, parented to the moving pipe/chute. Use `Toolshed_FlowOrigin` as the generic name, or list asset-specific names such as `FuelLoaderFill`, `CoalStartUnity`, `CoalStartUnity.001`, or `UnitySingleLoadChute` in JSON. Avoid custom `ToolshedFlowOrigin` component entries in FUSE-mounted asset-pack definitions because FUSE strips unsupported component kinds. Use `debugOriginMarker` only while lining up the outlet, then turn it off. Use `streamLength` to adjust the visible oil fall distance, `createVisibleStream` when particle-only oil is too dark or hidden against the asset, and `requireParentTransform` when the effect should stay hidden until a real parent transform exists.
9. For finite storage, assign a stable `Industry` and `linkedIndustry`.
10. Add `IndustryUnloader` for delivered storage.
11. Add `Interchange` plus `InterchangedIndustryLoader` for off-map purchase.
12. Assign `serviceTrackSpan` for industry/interchange car discovery.
13. Confirm the cars/tenders being loaded have matching load slots and `CarLoadTarget` markers.

For FUSE JSON binding:

1. Place the visible model as scenery.
2. Define the industry, storage components, interchange components, and delivery track span in FUSE operations data.
3. Add a `ToolshedServiceFacilities.json` entry targeting the scenery id.
4. Set `serviceLoadId`, `sourceIndustryId`, `serviceTrackSpanId`, and animation map keys.
5. Restart Railroader or reload the mod so UMM uses the new Toolshed DLL.

## 10. Testing Checklist

- [x] Build Toolshed.
- [ ] Install Toolshed and required asset packs; remove active OilFiring duplicate.
- [ ] Start a host game and confirm custom service loads register.
- [ ] Place or bind a `UniversalServiceFacilityComponent` loader with debug logging enabled.
- [ ] Confirm `request`, `prepareLoad`, `canLoad`, `isLoading`, and `animateLoad` change in sequence.
- [ ] Confirm bunker-C particle effects start only while fuel is actually transferring.
- [ ] Confirm bunker-C cannot transfer while the pipe is raised.
- [ ] Load `coal`, `water`, `diesel-fuel`, `bunker-c`, and `pulpwood` into matching slots.
- [ ] Test finite storage drain from `Industry.Storage`.
- [ ] Test inbound car unloading with `IndustryUnloader`.
- [ ] Test interchange purchase for `coal`, `diesel-fuel`, `bunker-c`, and `pulpwood`.
- [ ] Confirm water does not configure interchange.
- [ ] Save/reload and confirm industry storage persists.
- [ ] Validate ALW loader model and wood shed asset identifiers in game.
- [ ] Test through FUSE `world.scenery`.
