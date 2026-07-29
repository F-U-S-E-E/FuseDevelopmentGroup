# Toolshed Service Loader Component Blueprint

This document is the field-by-field setup reference for authored Toolshed service loaders.
It is written for scenery asset authors and route builders who want custom loaders to behave
like Railroader's base-game service facilities while keeping the setup reusable.

The important split is:

- The asset definition owns physical loader setup: storage, capacity, load id, click boxes, outlet positions, animations, and effects.
- The route package owns map setup: placed scenery id, source industry id, and delivery/unload track span.
- Tender and locomotive loading does not use a delivery span. The load point looks for a matching `CarLoadTarget` at the chute, pipe, hose, shed, or standpipe.

Use the Railroader definition editor to add:

- One `ToolshedServiceStorage` component per shared storage bucket.
- One `ToolshedServiceLoadPoint` component per physical outlet.

For example, a dual coaling tower gets one storage component for the coal bin and two load point components, one for each chute.

## Supported Load IDs

| Load ID | Units | Typical asset |
| --- | --- | --- |
| `coal` | Pounds | Coal towers, coal conveyors, coal docks |
| `water` | Gallons | Water towers, wells, standpipes |
| `diesel-fuel` | Gallons | Diesel stands, fuel racks |
| `bunker-c` | Gallons | Oil-firing stands, tank loaders |
| `pulpwood` | Pounds | Wood sheds and wood loaders |

Use lowercase load ids. Toolshed trims accidental leading and trailing spaces, but exact lowercase ids are still the safest habit.

## Component Placement Rules

Place `ToolshedServiceStorage` on the scenery root or on the physical bin/tank/storage body.
This component is where the storage tooltip and visible inventory animation belong.

Place `ToolshedServiceLoadPoint` at the outlet that physically loads the locomotive, tender, or car.
For animated pipes and chutes, put the load point on an empty parented under the moving part so the click box, service point, and particle stream move with the animation.

Good outlet empty names:

| Asset type | Suggested empty name |
| --- | --- |
| Tank/oil loader | `FuelLoaderFill` |
| Dual coal chute 1 | `CoalStartUnity` |
| Dual coal chute 2 | `CoalStartUnity.001` |
| Single coal chute | `UnitySingleLoadChute` |
| Wood shed | `WoodLoadPoint` |

## ToolshedServiceStorage Fields

Add one `ToolshedServiceStorage` component for each storage bucket. Fields below are in the same order as the component editor.

| Field | Type | Use |
| --- | --- | --- |
| `Kind` | Read-only | Must be `ToolshedServiceStorage`. The editor fills this in. |
| `FacilityId` | Text | Stable id for this loader asset or facility group, such as `alw-dual-coal-tower`, `alw-tank-loader`, or `wood-shed`. Load points with the same `FacilityId` can share this storage. |
| `StorageId` | Text | Stable id for this storage bucket. Usually the load id: `coal`, `bunker-c`, `diesel-fuel`, `water`, or `pulpwood`. Multiple load points can share one `StorageId`. |
| `ServiceLoadId` | Text | Load id stored in this bucket. Use one supported load id. |
| `InfiniteSupply` | Checkbox | Checked means the loader does not drain industry storage. Use for wells or simple infinite water. Unchecked means finite storage backed by the linked route industry. |
| `FacilityCapacity` | Number | Storage capacity in the load's native units. Gallons for liquids, pounds for solid fuels. |
| `InitialStorage` | Number | First-time seed amount if the linked industry storage starts empty. Set `0` when the route should control inventory. |
| `DefaultLoadingRate` | Number | Fallback loading rate used by load points that leave `LoadingRate` at `0`. Units per real second. |
| `ShowStorageTooltip` | Checkbox | Checked gives the storage body a hover tag showing current inventory. |
| `DisplayTitle` | Text | Title shown in the storage hover tag, such as `Whittier Coaling Tower`, `Bunker-C Loader`, or `Wood Loader`. |
| `MaxPickDistance` | Number | Maximum distance from camera/player picker to show this storage tooltip. `50` is a good default. |
| `InteractionRadius` | Number | Radius for the storage hover collider when not using a box. |
| `UseBoxInteractionCollider` | Checkbox | Checked uses `InteractionBoxCenter` and `InteractionBoxSize` for the storage tooltip. Recommended for bins, sheds, and tanks. |
| `InteractionBoxCenter` | Vector3 | Local box center for the storage hover area. |
| `InteractionBoxSize` | Vector3 | Local box size for the storage hover area. Cover the storage body, not the whole scene. |
| `EditorPreviewEnabled` | Checkbox | Shows editor preview helpers for the storage component. Turn on while authoring, off if the helper visuals get distracting. |
| `EditorPreviewShowClickBox` | Checkbox | Shows the storage hover box in editor preview. |
| `StorageAnimationMapKey` | Text | Animation-map key for visible inventory, such as `Wood Pile`. Leave empty if this storage has no visible fill animation. |
| `StorageAnimationLoadId` | Text | Load id that drives the storage animation. Usually the same as `ServiceLoadId`. |
| `StorageAnimationCapacity` | Number | Capacity used to convert inventory into animation percentage. Usually same as `FacilityCapacity`. |
| `StorageAnimationInvert` | Checkbox | Checked reverses the fill percentage. Use if the animation is full at frame 0 and empty at frame 1. |
| `StorageAnimationUseTransformFallback` | Checkbox | Checked lets Toolshed move/scale a named transform if the animation clip is missing or not usable. Useful for simple wood piles. |
| `StorageAnimationFallbackTransformName` | Text | Transform to move/scale for fallback visible inventory, such as `Empty` on the wood pile. |
| `StorageAnimationEmptyLocalY` | Number | Local Y position for empty storage when using transform fallback. |
| `StorageAnimationFullLocalY` | Number | Local Y position for full storage when using transform fallback. |
| `StorageAnimationEmptyLocalScaleZ` | Number | Local Z scale for empty storage when using transform fallback. |
| `StorageAnimationFullLocalScaleZ` | Number | Local Z scale for full storage when using transform fallback. |
| `DebugLogging` | Checkbox | Writes detailed `[ServiceFacility]` logs for this storage. Turn on only while troubleshooting. |
| `Name` | Text | Component instance name. Useful in the editor only. |
| `Transform` | Position, rotation, scale | Component transform. If this component is on an authoring object, move it to line up the storage hover box. |
| `Parent` | Text | Parent transform in the definition editor. Leave blank unless intentionally parenting to a named empty. |
| `Enabled` | Checkbox | Checked means the component is active. |

### Storage Defaults

| Loader | ServiceLoadId | FacilityCapacity | InitialStorage | DefaultLoadingRate | InfiniteSupply |
| --- | --- | ---: | ---: | ---: | --- |
| Coal tower | `coal` | `100000` to `150000` | `0` to capacity | `500` to `2000` | Unchecked |
| Bunker-C tank | `bunker-c` | `8000` to `16000` | `0` to capacity | `6` | Unchecked |
| Diesel stand | `diesel-fuel` | `8000` | `0` to capacity | `30` to `60` | Unchecked |
| Wood shed | `pulpwood` | `180000` | `45000` | `50` to `250` | Unchecked |
| Water tower/well | `water` | `0` or tank size | `0` | `30` to `60` | Usually checked |

## ToolshedServiceLoadPoint Fields

Add one `ToolshedServiceLoadPoint` component for each chute, pipe, hose, standpipe, or loading zone. Fields below are in the same order as the component editor.

| Field | Type | Use |
| --- | --- | --- |
| `Kind` | Read-only | Must be `ToolshedServiceLoadPoint`. The editor fills this in. |
| `FacilityId` | Text | Must match the storage component's `FacilityId` when sharing storage. |
| `StorageId` | Text | Must match the storage component's `StorageId` that this outlet drains. |
| `LoadPointId` | Text | Stable id for this specific outlet, such as `FuelLoaderFill`, `CoalStartUnity`, `CoalStartUnity.001`, or `WoodLoadPoint`. Route JSON can target this id. |
| `ServiceLoadId` | Text | Load this outlet transfers. Usually same as the storage `ServiceLoadId`. |
| `LoadingRate` | Number | Transfer rate in units per real second. Gallons per second for liquids, pounds per second for solid fuels. |
| `ServiceRadius` | Number | Loader radius used by vanilla `CarLoadTargetLoader`. Keep small for precise chutes and pipes. |
| `MaximumSpeedMph` | Number | Maximum car/locomotive speed allowed while loading. `5` matches service-style behavior. |
| `RequirePlayerOwnedCars` | Checkbox | Checked matches normal service behavior. Leave checked for most public loaders. |
| `EnableExtendedTenderSearch` | Checkbox | Checked lets Toolshed search nearby cars/tenders when vanilla point loading hits the locomotive body or a nearby supply car first. Recommended for custom scenery. |
| `ExtendedSearchRadius` | Number | Radius to discover nearby cars. Use larger values for sheds or big service areas. |
| `ExtendedLoadTargetRadius` | Number | Max flat distance from service point to the matching `CarLoadTarget`. |
| `UseServiceTargetBox` | Checkbox | Checked uses a box volume for target detection instead of only radius. Helpful for sheds, tunnels, and loaders with wide service zones. |
| `ServiceTargetBoxCenter` | Vector3 | Local center of the target detection box. |
| `ServiceTargetBoxSize` | Vector3 | Local size of the target detection box. For a wood shed, fill the shed opening. |
| `RestrictLoadingToServiceTrackSpan` | Checkbox | Optional legacy guard. Leave unchecked for authored assets unless the loader must only work on a specific route span. |
| `ServiceTrackRouteLimit` | Number | Route-distance limit used only when span restriction is enabled. `80` is a safe default. |
| `DisplayTitle` | Text | Hover/click title shown over this outlet, such as `Bunker-C Loader`, `Whittier Coal Chute 1`, or `Wood Loader`. |
| `MessageWhenActive` | Text | Message shown when the request key is active. For chutes/pipes use `Raise`; for simple loaders use `Click to Stop Loading`. |
| `MessageWhenInactive` | Text | Message shown when the request key is inactive. For chutes/pipes use `Lower`; for simple loaders use `Click to Start Loading`. |
| `MaxPickDistance` | Number | Maximum picker distance for this outlet. `50` is a good default. |
| `InteractionRadius` | Number | Radius for the outlet click/hover sphere when not using a box. Keep small so only the chute/pipe is clickable. |
| `UseBoxInteractionCollider` | Checkbox | Checked uses `InteractionBoxCenter` and `InteractionBoxSize` for the outlet click box. Recommended for chutes and shed loading zones. |
| `InteractionBoxCenter` | Vector3 | Local center of the click/hover box. |
| `InteractionBoxSize` | Vector3 | Local size of the click/hover box. Cover the actual chute, pipe, hose, or loading zone. |
| `LoaderLocalPosition` | Vector3 | Local offset from the load point transform to the actual service point. Usually `0,0,0` if the component is on the outlet empty. |
| `LoaderLocalRotation` | Vector3 | Local rotation offset for the service point. Usually `0,0,0`. |
| `AnimationMapKey` | Text | Animation-map key for moving this outlet, such as `PipeTurner`, `Chute1`, or `Chute2`. Leave empty for non-animated loaders. |
| `AnimationSpeed` | Number | Playback speed for the outlet animation. `1` is normal speed. |
| `AnimationInvert` | Checkbox | Checked reverses the animation meaning. Use if active/up/down is backward. |
| `RequireLoweredBeforeLoading` | Checkbox | Checked blocks loading until the outlet request state is active. Use for pipes/chutes that must be lowered. Uncheck for simple wood sheds. |
| `CreateParticleSystem` | Checkbox | Checked creates a particle system for loading flow. Use for coal chunks, oil, water, or diesel if the asset has no authored particle object. |
| `CreateVisibleStream` | Checkbox | Checked creates a simple line/stream visual. Useful for oil, water, and chute-aligned coal flow. |
| `ExistingEffectObjectName` | Text | Existing particle/effect object to toggle instead of creating one. Leave empty when using generated effects. |
| `EffectBoolKey` | Text | Key that controls the effect. Use `animateLoad` for vanilla-style flow while actual transfer is happening. |
| `EffectEmissionRate` | Number | Particle emission rate. Higher means denser flow. |
| `EffectStartLifetime` | Number | Particle lifetime in seconds. Longer means particles travel farther. |
| `EffectStartSpeed` | Number | Particle starting speed. For visual chunks, lower values slow the apparent flow. |
| `FlowAnimationSpeed` | Number | Visual-only stream/chunk animation speed. Leave `0` to use `EffectStartSpeed`. Use this to slow visual coal without changing loading rate. |
| `EffectStartSize` | Number | Particle size. Coal chunks are usually larger than oil particles. |
| `EffectGravityModifier` | Number | Gravity applied to particles. Higher pulls coal/water down faster. |
| `EffectColorRgb` | Vector3 | RGB color values from `0` to `1`. Coal can be `0.03,0.025,0.02`; bunker-C can be `0.08,0.06,0.04`. |
| `EffectAlpha` | Number | Particle/stream opacity from `0` to `1`. |
| `EffectLocalEuler` | Vector3 | Local emitter rotation. Start with `90,0,0` for downward-oriented effects. |
| `StreamLength` | Number | Length of generated visible stream. For oil, short values like `0.6` to `1.0` work well. |
| `StreamWidth` | Number | Width of generated visible stream. Oil can be `0.04` to `0.08`; coal chute streams can be `0.08` to `0.12`. |
| `StreamUsesWorldDown` | Checkbox | Checked makes the stream fall straight down. Uncheck for coal that should run along a chute. |
| `StreamLocalStart` | Vector3 | Local start offset for chute-aligned streams. Use this to start particles back/up the chute. |
| `StreamLocalEnd` | Vector3 | Local end offset for chute-aligned streams. `0,0,0` means the load point origin. |
| `DebugOriginMarker` | Checkbox | Shows a marker at the flow origin while tuning. Turn off before release. |
| `EditorPreviewEnabled` | Checkbox | Shows editor preview helpers for this load point. |
| `EditorPreviewAnimation` | Checkbox | Allows the editor preview to sample the outlet animation. |
| `EditorPreviewAnimationPosition` | Number | Preview animation position. `0` means stored/up, `1` means lowered/down. |
| `EditorPreviewFlow` | Checkbox | Shows the generated flow in editor preview. |
| `EditorPreviewShowOrigin` | Checkbox | Shows the load point origin marker in editor preview. |
| `EditorPreviewShowClickBox` | Checkbox | Shows the outlet click box in editor preview. |
| `EditorPreviewShowServiceRadius` | Checkbox | Shows the target/service radius in editor preview. |
| `DebugLogging` | Checkbox | Writes detailed `[ServiceFacility]` and `[ServiceFacility][Loader]` logs for this load point. Turn on only while troubleshooting. |
| `Name` | Text | Component instance name. Useful in editor only. |
| `Transform` | Position, rotation, scale | Component transform. Put this at the real outlet or service zone. |
| `Parent` | Text | Parent transform in the definition editor. Use only when intentionally parenting under a named moving empty. |
| `Enabled` | Checkbox | Checked means the component is active. |

## Route Binding JSON

After the asset is authored, the route only needs a small `ToolshedServiceFacilities.json`.
The span here is where delivery cars unload into the facility storage, not where locomotives stand to be serviced.

```json
{
  "facilities": [
    {
      "id": "example-service-loader",
      "targetObjectName": "example:scenery:loader",
      "modelIdentifier": "your-model-id",
      "sourceIndustryId": "example-engine-service",
      "serviceTrackSpanId": "example:span:loader-delivery",
      "requireAuthoredLoadPoints": true,
      "debugLogging": false
    }
  ]
}
```

Use `loadPointId` only when the placed asset has several load points and the route should enable just one of them.
If `loadPointId` is omitted, Toolshed binds every authored load point found on that placed scenery object.

Use `serviceTrackSpanIds` when multiple delivery tracks fill the same storage bucket.

## Setup Recipe: Bunker-C Tank Loader

Storage component:

| Field | Value |
| --- | --- |
| `FacilityId` | `alw-tank-loader` |
| `StorageId` | `bunker-c` |
| `ServiceLoadId` | `bunker-c` |
| `InfiniteSupply` | Unchecked |
| `FacilityCapacity` | `8000` to `16000` |
| `InitialStorage` | Optional |
| `DefaultLoadingRate` | `6` |
| `ShowStorageTooltip` | Checked |
| `DisplayTitle` | `Bunker-C Loader` |

Load point component:

| Field | Value |
| --- | --- |
| `LoadPointId` | `FuelLoaderFill` |
| `ServiceLoadId` | `bunker-c` |
| `LoadingRate` | `6` |
| `ServiceRadius` | `0.7` to `0.9` |
| `EnableExtendedTenderSearch` | Checked |
| `ExtendedSearchRadius` | `20` |
| `ExtendedLoadTargetRadius` | `6` to `16` |
| `DisplayTitle` | `Bunker-C Loader` |
| `MessageWhenActive` | `Raise` |
| `MessageWhenInactive` | `Lower` |
| `AnimationMapKey` | `PipeTurner` |
| `RequireLoweredBeforeLoading` | Checked |
| `CreateVisibleStream` | Checked |
| `EffectBoolKey` | `animateLoad` |
| `StreamUsesWorldDown` | Checked |
| `StreamLength` | `0.6` to `1.0` |
| `StreamWidth` | `0.04` to `0.08` |

The load point should be on the pipe-tip empty and that empty should be parented under the moving pipe branch.

## Setup Recipe: Dual Coal Tower

Storage component:

| Field | Value |
| --- | --- |
| `FacilityId` | `alw-dual-coal-tower` |
| `StorageId` | `coal` |
| `ServiceLoadId` | `coal` |
| `InfiniteSupply` | Unchecked |
| `FacilityCapacity` | `100000` to `150000` |
| `DefaultLoadingRate` | `500` to `2000` |
| `DisplayTitle` | `Coaling Tower` |

Load point 1:

| Field | Value |
| --- | --- |
| `LoadPointId` | `CoalStartUnity` |
| `ServiceLoadId` | `coal` |
| `LoadingRate` | `500` to `2000` |
| `ServiceRadius` | `0.45` to `1.0` |
| `DisplayTitle` | `Coal Chute 1` |
| `MessageWhenActive` | `Raise` |
| `MessageWhenInactive` | `Lower` |
| `AnimationMapKey` | `Chute1` |
| `RequireLoweredBeforeLoading` | Checked |
| `CreateParticleSystem` | Checked |
| `CreateVisibleStream` | Checked |
| `StreamUsesWorldDown` | Unchecked |
| `StreamLocalStart` | `-1.4,0.18,0` as a first pass |
| `StreamLocalEnd` | `0,0,0` |

Load point 2:

| Field | Value |
| --- | --- |
| `LoadPointId` | `CoalStartUnity.001` |
| `StorageId` | `coal` |
| `ServiceLoadId` | `coal` |
| `DisplayTitle` | `Coal Chute 2` |
| `AnimationMapKey` | `Chute2` |

All other load point 2 values should mirror chute 1 unless the model needs different click boxes or stream offsets.

## Setup Recipe: Single Coal Tower

Storage is the same as the dual coal tower.

Load point:

| Field | Value |
| --- | --- |
| `LoadPointId` | `UnitySingleLoadChute` |
| `ServiceLoadId` | `coal` |
| `LoadingRate` | `500` to `2000` |
| `ServiceRadius` | `0.45` to `1.0` |
| `DisplayTitle` | `Coal Chute` |
| `MessageWhenActive` | `Raise` |
| `MessageWhenInactive` | `Lower` |
| `AnimationMapKey` | model's single chute key |
| `RequireLoweredBeforeLoading` | Checked |
| `CreateParticleSystem` | Checked |
| `CreateVisibleStream` | Checked |

If the chute drops straight down from above, `StreamUsesWorldDown` can stay checked.
If coal should visibly slide down a chute, uncheck it and use `StreamLocalStart` and `StreamLocalEnd`.

## Setup Recipe: Wood Shed

Storage component:

| Field | Value |
| --- | --- |
| `FacilityId` | `wood-shed` |
| `StorageId` | `pulpwood` |
| `ServiceLoadId` | `pulpwood` |
| `InfiniteSupply` | Unchecked |
| `FacilityCapacity` | `180000` |
| `InitialStorage` | `45000` |
| `DefaultLoadingRate` | `50` to `250` |
| `ShowStorageTooltip` | Checked |
| `DisplayTitle` | `Wood Loader` |
| `UseBoxInteractionCollider` | Checked |
| `InteractionBoxSize` | `15,5,6` as a first pass |
| `StorageAnimationMapKey` | `Wood Pile` |
| `StorageAnimationLoadId` | `pulpwood` |
| `StorageAnimationCapacity` | `180000` |
| `StorageAnimationUseTransformFallback` | Checked |
| `StorageAnimationFallbackTransformName` | `Empty` |
| `StorageAnimationEmptyLocalY` | `0.2793902` |
| `StorageAnimationFullLocalY` | `2.7911012` |
| `StorageAnimationEmptyLocalScaleZ` | `0.109580874` |
| `StorageAnimationFullLocalScaleZ` | `1` |

Load point component:

| Field | Value |
| --- | --- |
| `FacilityId` | `wood-shed` |
| `StorageId` | `pulpwood` |
| `LoadPointId` | `WoodLoadPoint` |
| `ServiceLoadId` | `pulpwood` |
| `LoadingRate` | `50` to `250` |
| `ServiceRadius` | `0.65` to `1.2` |
| `EnableExtendedTenderSearch` | Checked |
| `ExtendedSearchRadius` | `20` |
| `ExtendedLoadTargetRadius` | `10` |
| `UseServiceTargetBox` | Checked |
| `ServiceTargetBoxSize` | `15,6,3` or whatever fills the shed opening |
| `DisplayTitle` | `Wood Loader` |
| `MessageWhenActive` | `Click to Stop Loading` |
| `MessageWhenInactive` | `Click to Start Loading` |
| `UseBoxInteractionCollider` | Checked |
| `InteractionBoxSize` | Cover the shed loading area, not the whole scenery |
| `RequireLoweredBeforeLoading` | Unchecked |
| `CreateParticleSystem` | Unchecked |
| `CreateVisibleStream` | Unchecked |

## Setup Recipe: Infinite Water

Storage component:

| Field | Value |
| --- | --- |
| `StorageId` | `water` |
| `ServiceLoadId` | `water` |
| `InfiniteSupply` | Checked |
| `FacilityCapacity` | `0` or display capacity |
| `ShowStorageTooltip` | Optional |

Load point component:

| Field | Value |
| --- | --- |
| `ServiceLoadId` | `water` |
| `LoadingRate` | `30` to `60` |
| `ServiceRadius` | `0.2` to `0.5` |
| `DisplayTitle` | `Water Column` or `Water Tower` |
| `MessageWhenActive` | `Raise` or `Stop Loading` |
| `MessageWhenInactive` | `Lower` or `Start Loading` |
| `AnimationMapKey` | Water spout key if animated |
| `RequireLoweredBeforeLoading` | Checked for animated spouts |
| `CreateVisibleStream` | Optional |
| `StreamUsesWorldDown` | Checked |

Water should not use interchange purchase.

## Setup Recipe: Diesel Fuel

Storage component:

| Field | Value |
| --- | --- |
| `StorageId` | `diesel-fuel` |
| `ServiceLoadId` | `diesel-fuel` |
| `InfiniteSupply` | Unchecked |
| `FacilityCapacity` | `8000` |
| `DefaultLoadingRate` | `30` to `60` |
| `DisplayTitle` | `Diesel Fuel` |

Load point component:

| Field | Value |
| --- | --- |
| `ServiceLoadId` | `diesel-fuel` |
| `LoadingRate` | `30` to `60` |
| `ServiceRadius` | `0.5` to `0.9` |
| `DisplayTitle` | `Diesel Fuel` |
| `RequireLoweredBeforeLoading` | Depends on the asset |
| `CreateVisibleStream` | Optional |

Diesel can use interchange purchase when an interchange industry supplies `diesel-fuel`.

## Troubleshooting

If the loader tag does not show:

- Check `Enabled`.
- Check `MaxPickDistance`.
- Check `InteractionRadius` or `InteractionBoxSize`.
- Turn on `EditorPreviewShowClickBox`.
- Make sure the click box is on the chute, pipe, shed opening, or storage body.

If the loader tag shows but the tender does not fill:

- Confirm the tender has a `LoadSlot` for the same `ServiceLoadId`.
- Confirm the tender has a `CarLoadTarget` pointed at that slot.
- Increase `ExtendedSearchRadius` and `ExtendedLoadTargetRadius`.
- For sheds, turn on `UseServiceTargetBox` and fill the service area with the box.
- Leave `RestrictLoadingToServiceTrackSpan` unchecked unless you intentionally need it.

If the storage tooltip shows but supply cars do not unload:

- Check the route `sourceIndustryId`.
- Check `serviceTrackSpanId` or `serviceTrackSpanIds`.
- Confirm the car is assigned to unload at the facility industry.
- Confirm the car load id matches the storage `ServiceLoadId`.
- Confirm finite storage has capacity left.

If an animation or transform is not found:

- Check for trailing spaces in the editor fields.
- Check exact animation-map key names.
- Check exact empty/transform names.
- Turn on `DebugLogging` and read `Player.log` for `[ServiceFacility][Loader]`.

If chute 2 or another outlet has huge preview spheres/boxes:

- The outlet is probably under a scaled empty.
- Toolshed normalizes authored load point proxies at runtime, but in editor preview you may still need to move the component under a better unscaled child or use smaller local click-box values.

## Release Checklist

- Storage component has correct `FacilityId`, `StorageId`, `ServiceLoadId`, capacity, and tooltip.
- Every outlet has one load point with a unique `LoadPointId`.
- Multiple outlets that share one bin use the same `StorageId`.
- Animated outlets have a valid `AnimationMapKey`.
- Flow effects use `EffectBoolKey = animateLoad`.
- Pipes/chutes that must be lowered have `RequireLoweredBeforeLoading` checked.
- Simple sheds have `RequireLoweredBeforeLoading` unchecked.
- Debug markers and debug logging are off before public release.
- Route JSON only binds scenery id, source industry id, and delivery span.
