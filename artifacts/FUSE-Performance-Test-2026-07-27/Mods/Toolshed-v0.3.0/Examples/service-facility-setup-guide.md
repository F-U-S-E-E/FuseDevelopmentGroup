# Universal Service Facility Setup Guide

This guide is for FUSE scenery asset authors who want one Toolshed dependency for service loaders instead of separate oil, coal, wood, diesel, or water loader mods.

For the full field-by-field editor reference, use `service-loader-component-blueprint.md`. It lists every `ToolshedServiceStorage` and `ToolshedServiceLoadPoint` setting in the same order shown by the component editor.

The short version: build the loader model with Toolshed service components in the definition editor, place the finished scenery asset with FUSE, then use `ToolshedServiceFacilities.json` only to bind the placed asset to the route-specific storage industry and delivery/unload span. Do not use a span to tell the chute where to find tenders. Just like the base game, the physical outlet point looks for a matching `CarLoadTarget` under or near the chute/pipe. The loader model can live in any asset pack. Toolshed binds to the placed scenery object id first and only uses `modelIdentifier` as a fallback/template hint, so the service logic is not tied to ALW, RailLoader, or any one scenery folder.

## Authoring Paths

| Path | Use when | What places the model | What adds service behavior |
| --- | --- | --- | --- |
| Editor-authored Toolshed components | Normal public setup for custom service assets | FUSE `world.scenery` | Toolshed reads the asset's `ToolshedServiceStorage` and `ToolshedServiceLoadPoint` components, then uses `ToolshedServiceFacilities.json` for the industry/span binding |
| Legacy JSON binding | Temporary testing or older packages | FUSE `world.scenery` | Toolshed runtime reads every loader detail from `ToolshedServiceFacilities.json` |
| Unity prefab MonoBehaviours | Private Unity prefab work | FUSE/Railroader prefab instancing | Components already on the prefab |

The important rule is that only Toolshed owns the service logic. FUSE should provide placement, delivery spans, and the route industry id. The asset definition should own the loader defaults: load id, capacity, outlet positions, click zones, chute/pipe animations, storage hover boxes, particle origins, and per-outlet loading rates. Toolshed then creates or updates the vanilla `IndustryUnloader` so delivered cars refill the same persisted industry storage that the chute/pipe drains.

## Preferred Editor Component Setup

Add these components in Railroader's definition editor on the scenery asset before shipping the asset pack.

| Component | Add this many | Put it where | Purpose |
| --- | --- | --- | --- |
| `Toolshed Service Storage` | One per storage bucket | Usually on the scenery root or a bin/tank empty | Default load id, capacity, infinite/finite behavior, and default loading rate |
| `Toolshed Service Load Point` | One per chute, pipe, hose, or standpipe outlet | On an empty at the actual outlet, parented to the moving chute/pipe if it animates | Builds one vanilla `CarLoadTargetLoader`, one `CarLoaderSequencer`, one click target, and optional loading effect |

For a dual coaling tower, add one `Toolshed Service Storage` component for the coal bin, then add two `Toolshed Service Load Point` components: one on the first chute outlet and one on the second chute outlet. Both load points can use the same `StorageId`, so they share the same industry storage but still have independent click targets, animations, particle origins, and tender detection.

For a tank loader, put the `Toolshed Service Load Point` component on the `FuelLoaderFill` empty at the pipe tip. If the pipe rotates or lowers, parent that empty under the animated pipe branch so the loader point and stream follow the pipe. The effect should normally use `EffectBoolKey = animateLoad`, with `RequireLoweredBeforeLoading = true`, so oil only appears after the pipe is down and a car/tender is actually transferring fuel.

Recommended load-point defaults:

| Field | Coal chute | Bunker-C pipe | Wood shed |
| --- | --- | --- | --- |
| `ServiceLoadId` | `coal` | `bunker-c` | `pulpwood` |
| `LoadingRate` | `500` to `2000` lb/s | `6` gal/s | `250` lb/s |
| `ServiceRadius` | `0.45` to `0.75` | `0.7` to `0.9` | `0.8` to `1.2` |
| `InteractionRadius` | `0.35` to `0.6` | `0.4` to `0.8` | `1.0` to `2.0` |
| `AnimationMapKey` | Chute animation key | Pipe animation key | Usually blank |
| `CreateParticleSystem` | Optional | Optional | Usually off |
| `CreateVisibleStream` | On for a visible coal run | On for oil | Off |

The component transform is the important part. If the click point is wrong, move the `Toolshed Service Load Point` component in the editor instead of editing JSON. If the stream is wrong, move or re-parent that same component to the real outlet empty. This keeps the setup portable across maps and asset packs.

Coal chutes should normally use a local chute stream instead of a straight world-down stream. Keep the load point at the chute mouth where the tender target should be found, set `StreamUsesWorldDown = false`, set `StreamLocalEnd = 0,0,0`, then set `StreamLocalStart` back/up the chute. A good first pass is `StreamLocalStart = -1.4,0.18,0` and `StreamWidth = 0.08` to `0.12`. If the stream runs backward, flip the sign on the long axis. This matches the base-game look where coal visibly runs down the chute before dropping into the tender.

Turn on `EditorPreviewEnabled` while building the asset. For load points, set `EditorPreviewAnimationPosition` to `0` for stored/up or `1` for lowered/down so you can line up the click box and outlet effect against the actual chute pose. For storage, use the preview click box to cover only the bin/tank/conveyor area that should show the storage tooltip.

## Supported Loads

| Load ID | Units | Typical use |
| --- | --- | --- |
| `coal` | Pounds | Coal towers, docks, chutes |
| `water` | Gallons | Water columns, wells, towers |
| `diesel-fuel` | Gallons | Diesel fuel stands |
| `bunker-c` | Gallons | Oil-firing stands, tank loaders |
| `pulpwood` | Pounds | Wood sheds and wood loaders |

Wood note: Toolshed uses the base-game `pulpwood` load as wood fuel. Loaders can transfer it, and steam equipment with a `pulpwood` fuel slot can consume it. The current rate is a first-pass estimate: `1.8` pulpwood pounds per coal pound.

## Runtime Or Prefab Components

| Component | Required | Purpose |
| --- | --- | --- |
| `UniversalServiceFacilityComponent` | Yes | Configures one service load |
| `CarLoadTargetLoader` | Yes | Vanilla physical transfer component |
| `KeyValueObject` | Yes | Stores request/loading booleans |
| `CarLoaderSequencer` | Recommended | Drives vanilla prepare/load animation flow |
| `GlobalKeyValueObject` | Recommended | Stable save/multiplayer control ID |
| `Industry` | Finite supply only | Owns persisted storage |
| `IndustryUnloader` | Optional | Lets delivery cars unload into storage |
| `InterchangedIndustryLoader` | Optional | Lets cars buy load off map |
| `Interchange` | Interchange only | Required by vanilla interchange purchase |
| `TrackSpan` | Industry ops only | Defines where delivery/interchange cars stand while filling the facility storage |

When using `ToolshedServiceFacilities.json`, Toolshed creates the loader-side `KeyValueObject`, `GlobalKeyValueObject`, `CarLoadTargetLoader`, `CarLoaderSequencer`, click toggle, and `UniversalServiceFacilityComponent` at runtime. For authored finite loaders, Toolshed also creates or updates the matching vanilla `IndustryUnloader` on the linked industry using the storage component's load id/capacity and the JSON delivery span. FUSE operations data can still define the industry and interchange purchase components, but it should not need per-loader animation, click-box, or capacity setup.

Toolshed also attaches a `ServiceFacilityPickable` to the visible scenery root. That matters because Railroader's object picker starts from the mesh collider that the mouse hit and searches upward for an `IPickable`; putting the pickable only on a hidden loader child will not work for normal asset meshes.

The car, tender, or locomotive being loaded also needs:

- A `LoadSlot` with `RequiredLoadIdentifier` matching the facility `serviceLoadId`.
- A `CarLoadTarget` whose `slotIndex` points at that slot.
- Enough target radius to overlap the loader radius when stopped under the loader.

## Key Flow

Use these default keys unless the prefab already has a different key scheme:

| Key | Written by | Read by | Purpose |
| --- | --- | --- | --- |
| `request` | Player toggle/control | `CarLoaderSequencer` | Player wants loading |
| `prepareLoad` | `CarLoaderSequencer` | Animator/effects | Move hose/chute/spout into position |
| `canLoad` | `CarLoaderSequencer` | `CarLoadTargetLoader` | Loader may search and transfer |
| `isLoading` | `CarLoadTargetLoader` | `CarLoaderSequencer` | Actual transfer is happening |
| `animateLoad` | `CarLoaderSequencer` | Animator/effects | Show flow/loading effect |

## UniversalServiceFacilityComponent Fields

| Field | Meaning |
| --- | --- |
| `serviceLoadId` | `coal`, `water`, `diesel-fuel`, `bunker-c`, or `pulpwood` |
| `infiniteSupply` | True means vanilla infinite loading; false drains linked industry storage |
| `facilityCapacity` | Storage capacity in the load's native units |
| `currentStorage` | Debug/seed mirror for linked industry storage |
| `loadingRate` | Units per real second transferred |
| `serviceRadius` | Loader-side radius used with car-side target radius |
| `enableExtendedTenderSearch` | Searches nearby cars for a matching tender/service `CarLoadTarget` when vanilla point loading sees the locomotive body or a supply car first |
| `extendedSearchRadius` | Nearby car discovery radius for the extended service search |
| `extendedLoadTargetRadius` | Max flat distance from the service point to the matching `CarLoadTarget` |
| `restrictLoadingToServiceTrackSpan` | Optional legacy guard that restricts tender loading to the configured span. Leave this false for normal authored assets; the chute/pipe position and matching `CarLoadTarget` should decide what gets loaded. |
| `serviceTrackRouteLimit` | Route distance used for the span restriction. `80` is a safe default for short service spans. |
| `maximumSpeedMph` | Max car speed allowed for loading |
| `serviceTrackSpan` | Track span for industry delivery/interchange cars that refill the facility storage |
| `linkedIndustry` | Industry storage used for finite loading |
| `requirePlayerOwnedCars` | Matches vanilla service behavior by default |
| `attachTargetPickable` | Attach hover/click behavior to the whole scenery root. Turn this off for multi-chute assets. |
| `createInteractionTrigger` | Create a dedicated click/hover sphere for this loader. |
| `interactionTransformName` | Optional transform/empty that owns the dedicated click/hover sphere, usually the chute or spout outlet empty. |
| `interactionRadius` | Radius of the dedicated click/hover sphere. Keep this small for individual chutes. |
| `useBoxInteractionCollider` | Use a box click/hover volume instead of a sphere. Helpful when the outlet empty is inside a chute or tower opening. |
| `interactionBoxCenter` / `interactionBoxSize` | Local box offset and size on the interaction transform. Use this to cover the physical chute without making the whole scenery clickable. |
| `interactionPickableTransformName` | Optional visible mesh branch that should also receive the pickable. Use this when the ray hits the chute mesh before the invisible click trigger. |
| `interactionPickableParentLevels` | If no explicit pickable transform is set, Toolshed climbs this many parents from the interaction transform. Default is `1`. |
| `loaderTransformName` | Optional transform/empty used as the actual loader service point. Use this for multi-chute assets instead of relying on track-span center. |
| `loaderTransformLocalPosition` | Local offset from `loaderTransformName` to the actual fill point. Useful when the exported empty is at the hinge but the coal/fluid should leave the chute mouth. |
| `requireLoaderTransform` | When true, the loader waits for the named transform instead of falling back to a guessed track-span point. |
| `canPurchaseThroughInterchange` | Enables vanilla off-map purchase when configured |
| `purchaseDelayDays` | Future/documentation value; vanilla currently uses about one day |
| `debugLogging` | Enables `[ServiceFacility]` and `[ServiceFacility][Loader]` logs |

`loadingRate` is the same unit used by the target slot: gallons per real second for liquids and pounds per real second for solid fuels. Vanilla `CarLoadTargetLoader` transfers once per second, so a `10,000` gallon bunker-C tender at `6` gal/s takes about 27 minutes 45 seconds from empty.

## Common Configurations

### Infinite Water

- `serviceLoadId = "water"`
- `infiniteSupply = true`
- `loadingRate = 60`
- `serviceRadius = 0.2`
- `maximumSpeedMph = 5`
- `canPurchaseThroughInterchange = false`

### Finite Bunker-C

- `serviceLoadId = "bunker-c"`
- `infiniteSupply = false`
- `facilityCapacity = 8000`
- `loadingRate = 6`
- `linkedIndustry = your service industry`
- `serviceTrackSpan = your delivery track span`
- `enableExtendedTenderSearch = true` for custom scenery where the service point may see the locomotive body or a nearby supply car before it sees the tender
- `configureReceivingUnloader = true`
- `configureInterchangeLoader = true` only if an `Interchange` exists
- `canPurchaseThroughInterchange = true` only if you want off-map purchase

### Diesel Fuel

- `serviceLoadId = "diesel-fuel"`
- `infiniteSupply = false`
- `facilityCapacity = 8000`
- `loadingRate = 60`
- `configureReceivingUnloader = true`
- `configureInterchangeLoader = optional`

Vanilla diesel locomotives consume slot 0 as `diesel-fuel`.

### Coal

- `serviceLoadId = "coal"`
- `infiniteSupply = false`
- `facilityCapacity = 100000`
- `loadingRate = 2000` to `5000`
- `configureReceivingUnloader = true`
- `configureInterchangeLoader = optional`

### Wood

- `serviceLoadId = "pulpwood"`
- `infiniteSupply = true` for a simple shed, or false for delivered wood storage
- `facilityCapacity = 100000` if finite
- `loadingRate = 250` to `500`
- `enableExtendedTenderSearch = true` for open sheds or loaders placed beside existing service tracks
- `configureReceivingUnloader = true` if wood should arrive by car

Wood-fired steam equipment must have a `pulpwood` load slot and a `water` load slot. Current consumption is intentionally simple and should be tuned after live testing.

For the shipped Toolshed wood shed, set the asset up with one `Toolshed Service Storage` component and one `Toolshed Service Load Point` component. The storage component owns the pile visibility: `StorageAnimationMapKey = Wood Pile`, `StorageAnimationLoadId = pulpwood`, and `StorageAnimationCapacity = 180000`. The load-point component owns the player prompt: `MessageWhenInactive = Click to Start Loading` and `MessageWhenActive = Click to Stop Loading`. The route JSON should only bind the placed shed to the route industry and delivery span, using `loadPointId = WoodLoadPoint` and `requireAuthoredLoadPoints = true`.

## Shared Runtime Binding

`ToolshedServiceFacilities.json` is the FUSE-to-Toolshed bridge. Put it at the root of the FUSE package folder, in `ServiceFacilities/*.json`, or inside an asset pack folder as an exact `ToolshedServiceFacilities.json` file. Toolshed scans installed mod folders and attaches service behavior after the scenery, loads, industries, and spans exist.

For editor-authored assets, keep this JSON small. The asset component owns `ServiceLoadId`, capacity, loading rate, animation key, click box, storage hover box, and particle setup. The route package only identifies which placed scenery object should drain which industry storage and which delivery span fills that storage.

```json
{
  "facilities": [
    {
      "id": "example-bunker-c-loader",
      "targetObjectName": "example:scenery:bunker-c-loader",
      "modelIdentifier": "ALW_Loader_TankLoader",
      "loadPointId": "FuelLoaderFill",
      "sourceIndustryId": "example:industry:engine-service",
      "serviceTrackSpanId": "example:span:bunker-c-unload",
      "requireAuthoredLoadPoints": true,
      "debugLogging": false
    }
  ]
}
```

Use `sourceIndustryId` when the loader should be finite and drain persisted industry storage. Leave it blank only for infinite water-style loaders authored that way on the asset.

`targetObjectName` should match the FUSE `world.scenery` id. Use `targetObjectNames`, `sourceIndustryIds`, and `serviceTrackSpanIds` only when you need aliases during a rename or migration. `modelIdentifier` and `modelIdentifiers` can reference a loader model from any installed asset pack, but stable placed object ids are better when a route has more than one copy of the same model.

For a dual-track loader with two chutes sharing one bin, use one JSON entry and let Toolshed bind every authored load point under that scenery object. The `serviceTrackSpanIds` list is for coal cars delivering into the bin, not for tender loading:

```json
{
  "facilities": [
    {
      "id": "whittier-coal-tower",
      "targetObjectName": "whittier:scenery:dual-coal-tower",
      "sourceIndustryId": "whittier-engine-service",
      "serviceTrackSpanIds": [
        "whittier:span:coal-delivery-a",
        "whittier:span:coal-delivery-b"
      ],
      "requireAuthoredLoadPoints": true
    }
  ]
}
```

If `loadPointId` is omitted, Toolshed binds every `Toolshed Service Load Point` component found under that scenery object. Use explicit `loadPointId` only when one placed asset has load points you intentionally do not want active on that route.

## Prefab Contract

A community loader prefab only needs a small public contract:

- A stable `modelIdentifier` in its asset pack catalog.
- Optional `AnimationMap` entries for moving parts, such as `PipeTurner`, `Chute1`, `Chute2`, or `Chute`.
- One `Toolshed Service Load Point` component at each outlet point, parented to the moving pipe or chute when the outlet moves.
- One `Toolshed Service Storage` component for each shared bin/tank/storage bucket.
- Normal colliders on the visible scenery. The precise service click point comes from the load-point component, so the whole tower does not need to be clickable.

The route package owns the map details: placed scenery id, source industry id, and service track span id. The asset owns the physical details: outlet position, animation key, loading rate, click zone, and particle/stream settings. That is what lets the same loader prefab work at Dillsboro, Whittier, or somebody else's map without a loader-specific C# mod.

For a model with more than one loader on the same scenery root, add one `Toolshed Service Load Point` per outlet. The load-point component gets its own collider and `ServiceFacilityPickable`, so chute 1 and chute 2 do not overwrite each other's hover text and do not need oversized invisible JSON click spheres.

Leave `RestrictLoadingToServiceTrackSpan` off for authored dual loaders. Chute 1 and chute 2 stay independent because their load-point components are physically located on different outlets and each has its own click box, animation key, and loader radius.

`animations` follows boolean loader flow. Use it for pipe, chute, hose, or gate movement:

- `prepareLoad`: move into loading position.
- `animateLoad`: show active loading flow.

Use `fallbackDurationSeconds` when `useTransformFallback` is enabled and the fallback transform should move at a fixed, scenery-friendly speed. If it is omitted, Toolshed uses the animation clip length when available and remembers the last known length during scenery reloads; the built-in safety default is one second.

Use `fallbackTransformNames` when the same asset exists in multiple community exports with different animated-empty names. Toolshed tries `fallbackTransformName` first, then each entry in `fallbackTransformNames`.

Use `fallbackTransformOverrides` when a fallback transform has a different local axis or rest pose than the primary export. This is useful for older community prefabs where `RotatePipe` and `PipeTurner` represent the same visual pipe but do not share the same local rotation basis.

Use `requireServiceCondition` when a chute, pipe, spout, or hose must be in the service position before transfer. For the ALW bunker-C loader, `request = true` means the pipe is down, so `serviceConditionBoolKey = "request"` blocks both vanilla loading and Toolshed's extended tender search while the pipe is raised.

Legacy JSON `particleEffects` can either target existing particle systems by name or create a small runtime particle system. For new assets, prefer the `Toolshed Service Load Point` effect fields instead. For oil loaders, use `EffectBoolKey = "animateLoad"` so the effect follows the vanilla loader-visible flow state, with `RequireLoweredBeforeLoading = true` when the pipe must be down.

`parentTransformNames` is an optional fallback list for community assets where the visible outlet empty may have different names between exports. The first matching transform is used. For precise oil/water flow, prefer a normal Unity empty at the spout tip. `Toolshed_FlowOrigin` is the generic Toolshed convention; asset-specific names are also fine when the JSON lists them. Current ALW loader exports use `FuelLoaderFill` for the tank loader, `CoalStartUnity` and `CoalStartUnity.001` for the dual-track tower, and `UnitySingleLoadChute` for the single-track tower. Older test exports used `FuelPoint`, `Chute.CoalStart`, `Chute.CoalStart.001`, and `SingleLoadChute`; keep those only as compatibility aliases when needed.

`flowOriginFollowTransformName` is available for editor-placed origins that need to ride along with an animated pipe/chute, but use it carefully. If the exported transform reports nonsense world coordinates after FUSE loads the model, the effect will jump off-map. For the ALW tank loader test asset, the stable setup is a root-local outlet offset and no follow transform.

Best practice is to put the `Toolshed Service Load Point` component at the exact pipe, chute, hose, or spout tip:

- In the Unity prefab, add an empty GameObject at the outlet. Parent it to the moving pipe/chute part so it follows the animation.
- In the definition editor, add `Toolshed Service Load Point` on that empty. Set `LoadPointId` to a stable value such as `FuelLoaderFill`, `CoalStartUnity`, or `CoalStartUnity.001`.
- Known shipped names: `FuelLoaderFill` for the ALW tank loader, `CoalStartUnity` and `CoalStartUnity.001` for a dual-track chute, and `UnitySingleLoadChute` for a tower loader.

Put the marker origin exactly where the fluid/coal/wood should leave the asset; Toolshed can then use zero local offsets and avoid guessing from a pivot that may be off-model. Set `requireParentTransform = true` for these effects when a bad fallback position would look worse than no effect.

`ToolshedFlowOrigin` still exists for older JSON-driven packages, but new public assets should use `Toolshed Service Load Point`. That component automatically creates the flow origin marker, the click target, and the vanilla loader bridge from the same editor-authored transform.

Temporary fallback for assets that do not ship with a Unity empty:

1. Set `requireParentTransform = false`.
2. Set `localPosition` to the outlet's local offset from the placed scenery root.
3. Keep `requiredBoolKey = "request"` or another service-position key so the effect only plays while the spout/chute is in the loading position.
4. Treat this as a stopgap. A real prefab empty parented to the moving part is the release-quality setup.

For dark liquid effects, `createVisibleStream = true` also creates a thin `LineRenderer` stream alongside the particles, which is much easier to see in shadow. The stream follows the same `localPosition` and `localEuler` as the particle emitter. Use `streamLength` to control how far the stream falls, `streamWidth` for thickness, and `streamColor` for color. Set `debugOriginMarker = true` only while tuning offsets; it shows a small yellow sphere at the effect origin.

For new assets, use the storage component's `StorageAnimation...` fields in the definition editor. Legacy JSON `storageAnimations` follows storage percentage instead of a boolean and remains supported for old test packages:

- 0% storage samples the start of the clip.
- 50% storage samples the middle of the clip.
- 100% storage samples the end of the clip.

```json
{
  "storageAnimations": [
    {
      "animationMapKey": "Wood Pile",
      "loadId": "pulpwood",
      "capacity": 180000
    }
  ]
}
```

## FUSE Pattern

FUSE should place the same custom model with `world.scenery`, then use the same `ToolshedServiceFacilities.json` binding.

```json
{
  "schemaVersion": "1.0",
  "id": "Example.ServiceFacilities",
  "name": "Example Service Facilities",
  "author": "Example",
  "modVersion": "1.0.0",
  "coordinateSpace": "world",
  "operations": {
    "industries": {
      "example:industry:engine-service": {
        "name": "Example Engine Service",
        "position": { "x": 0, "y": 0, "z": 0 },
        "components": {}
      }
    }
  },
  "world": {
    "scenery": {
      "example:scenery:bunker-c-loader": {
        "assetIdentifier": "scenery://ALW_Loader_TankLoader",
        "position": { "x": 0, "y": 0, "z": 0 },
        "rotation": { "x": 0, "y": 0, "z": 0 },
        "scale": { "x": 1, "y": 1, "z": 1 }
      }
    }
  }
}
```

Use FUSE `operations.loaders` only for cloning existing vanilla-style loader prefabs that already contain the right loader components. For custom Toolshed service assets, use `world.scenery` plus `ToolshedServiceFacilities.json`.

## Finite Storage Setup

1. Add a stable `Industry.identifier`.
2. Add a `TrackSpan` where delivery cars stand.
3. Put `sourceIndustryId` and `serviceTrackSpanId`/`serviceTrackSpanIds` in `ToolshedServiceFacilities.json`.
4. Put `ServiceLoadId`, `FacilityCapacity`, `InitialStorage`, and `InfiniteSupply = false` on the `Toolshed Service Storage` component.
5. Let Toolshed create or update the vanilla storage-only `IndustryUnloader` at runtime.

For a service facility, the receiving `IndustryUnloader` is storage only. Set its storage consumption rate to `0` and do not add a formulaic industry component just to represent wood, coal, diesel, or bunker-C use. Toolshed hides Railroader's misleading zero-rate `Consumes` panel row for this storage-only case.

## Interchange Purchase Setup

Use interchange for `coal`, `diesel-fuel`, `bunker-c`, and `pulpwood` for wood fuel.

Do not use interchange for `water`.

Requirements:

- Linked `Industry`.
- Child `Interchange`.
- `InterchangedIndustryLoader`.
- Assigned `serviceTrackSpan`.
- Sensible `load.costPerUnit` if purchase cost should matter.

Vanilla does not use one shared off-map fuel source. Each active interchange owns its own child
`InterchangedIndustryLoader` entries. If a load should be buyable from Sylva, Whittier, and Andrews,
add the same load-specific child loader under each interchange industry with that interchange's own
track spans.

For reference in the Dillsboro/Sylva/Andrews test graph:

- East Whittier interchange: `ewh-interchange`, spans `Pyih`, `Pcll`.
- Sylva interchange: `sylva-interchange`, spans `Pfaj`, `Pin7`, `P7ec`.
- Andrews interchange: `andrews-interchange`, spans `Puzp`, `P0ih`, `P0vg`, `Phcl`.

`pulpwood` is non-importable in vanilla and has `costPerUnit = 0`, so an interchange loader can fill
cars for free unless the test pack overrides the load with a fuel-purchase cost. Keep
`importable = false` so normal pulpwood industries still behave like captive/local traffic, but set
`costPerUnit` for the service-fuel test if buying wood fuel should charge the railroad.

Vanilla fills cars off map and returns them after roughly one game day.

## Asset Notes

Toolshed includes the loader component, fuel patches, and `SCAssetPacks` for public release.

The ALW tank loader spout animation is in the prefab animation map as `PipeTurner`. Older test bundles used `RotatePipe`; for new assets, put the correct key on the `Toolshed Service Load Point` component instead of carrying both names in JSON.

The wood shed prefab includes a `Wood Pile` animation-map entry. Use `wood-shed` for new scenery and keep `wood_fuel_shedp` only as a compatibility alias.

FUSE mounts the `SCAssetPacks` bundled in Toolshed and places the scenery ids used by the service binding.

## Troubleshooting

The loader never starts:

- Confirm Toolshed is enabled.
- Confirm `serviceLoadId` resolves.
- Confirm the control writes `request = true`.
- Confirm the sequencer writes the same `canLoad` key the loader reads.
- Confirm the game is host; vanilla loader logic is host-only.

The loader animates but does not fill:

- Confirm the car is below `maximumSpeedMph`.
- Confirm the car is player-owned if `requirePlayerOwnedCars` is true.
- Confirm the car has `CarLoadTarget`.
- Confirm the target `slotIndex` points at the matching load slot.
- Confirm `RequiredLoadIdentifier` exactly matches `serviceLoadId`.
- Confirm loader and target radii overlap.
- For custom scenery beside a longer locomotive/tender, set `enableExtendedTenderSearch = true` and tune `extendedSearchRadius`/`extendedLoadTargetRadius`.

Finite supply stops immediately:

- Confirm `linkedIndustry` is assigned.
- Confirm storage contains the same load ID.
- Confirm `IndustryUnloader` and loader both use shared storage.

Interchange does not buy:

- Confirm an `Interchange` exists under the linked industry.
- Confirm an `InterchangedIndustryLoader` exists.
- Confirm the load is not `water`.
- Confirm the railroad can afford the purchase.

## Release Checklist

- [ ] Add one `Toolshed Service Storage` component for each shared bin/tank/storage bucket.
- [ ] Add one `Toolshed Service Load Point` component for each chute, pipe, hose, or standpipe outlet.
- [ ] Parent each load-point component to the moving outlet branch if that outlet animates.
- [ ] Keep `ToolshedServiceFacilities.json` limited to placed scenery id, source industry id, delivery span id, and optional `loadPointId`.
- [ ] Use stable `GlobalKeyValueObject.globalObjectId` values.
- [ ] Keep `debugLogging` off for release unless intentional.
- [ ] Validate asset identifiers with `/fuse.assets` or FUSE health tooling.
- [ ] Test save/reload persistence.
- [ ] Test delivery, service loading, and interchange separately.

## Reusing an Existing Stand Loader (Diesel Stand -> Bunker C)

Set `useExistingTargetLoader: true` on a facility definition to adopt the
`CarLoadTargetLoader` that already exists on the target scenery — for example the
vanilla `dieselFuelingStand` spliney placed by a map mod — instead of creating a
second loader. The adopted loader keeps its own animations, key wiring, and
interaction, but its dispensed load is retargeted to `serviceLoadId`.

- `serviceLoadId` decides what the stand dispenses: `diesel-fuel` or `bunker-c`.
- Leave `loadingRate`/`serviceRadius` unset to keep the stand's authored values.
- `sourceIndustryId` + `infiniteSupply: false` makes supply finite from that
  industry's storage; omit the industry for an infinite stand.
- Set `attachTargetPickable: false` when the stand already has its own click
  interaction.

See `Examples/diesel-stand-loader-example.json` for a bunker-c stand and a
diesel stand configured from the same prefab.
