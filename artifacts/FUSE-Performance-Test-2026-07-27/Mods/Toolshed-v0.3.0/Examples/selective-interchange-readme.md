# Selective Interchanges

Toolshed selective interchanges let a route package turn a vanilla Railroader interchange into a specific off-map industry or connecting railroad.

Use this when a physical interchange track represents more than "generic cars appear here." For example, the Ocona-Lufty RR at Smokemont can behave like an off-map sawmill operation:

- empty flats and boxcars are ordered to Smokemont
- occasional loaded building-supplies cars are delivered to the mill
- the cars go off map
- loaded lumber, poles, or other mill products return at the same interchange
- those loaded cars are waybilled onward to the active base-game interchange
- logs can still be bought from Ocona-Lufty for Ravensford, but as a separate purchase/import flow

## Config Location

Put a config file in either place:

- `ToolshedSelectiveInterchanges.json` at the root of a mod folder
- any `.json` file inside a `SelectiveInterchanges` folder in a mod

Toolshed scans installed mod folders and applies matching definitions after the game has created the industries.

## Traffic Model

A selective interchange has four independent traffic types.

`emptyCarRequests` asks for empty cars to send to the off-map operation. For interchange-to-interchange traffic, set `originWeights` or another origin field so the empty cars spawn at the active external interchange instead of at the off-map interchange itself.

`inboundLoads` accepts loaded supplies the off-map operation consumes. By default Toolshed handles those cars only when that interchange is actually served, so cars sitting on the interchange track are not removed by the normal industry tick. For Smokemont, this is only `building-supplies`.

`purchaseLoads` creates or configures vanilla `InterchangedIndustryLoader` buy/import loaders. Use this only for loads the railroad can buy from the connecting railroad, such as Ocona-Lufty logs for Ravensford. Do not put normal mill production here unless it should be buyable.

`outboundRoutes` creates loaded-car orders at the interchange. Use this for off-map production returning loaded and continuing to another industry or interchange.

## Minimal Example

```json
{
  "interchanges": [
    {
      "id": "ocona-lufty-smokemont",
      "displayName": "Ocona-Lufty RR Co Interchange",
      "interchangeIndustryId": "Oconalufty-RR-INT",
      "trackSpanIds": [ "Smokemont-INT" ],
      "configurePurchases": true,
      "disableUnlistedPurchaseLoaders": true,
      "enableOutboundTraffic": true,
      "excludeFromGenericInterchangeSelection": true,
      "emptyCarRequests": [
        {
          "carTypeFilterQuery": "FL",
          "carModelIdentifier": "fl-alw40logging",
          "minCarsPerService": 6,
          "maxCarsPerService": 12,
          "originWeights": [
            { "originIndustryId": "sylva-interchange", "weight": 70 },
            { "originIndustryId": "ewh-interchange", "weight": 25 },
            { "originIndustryId": "andrews-interchange", "weight": 5 }
          ],
          "allowInactiveOriginFallback": true
        }
      ],
      "inboundLoads": [
        {
          "loadId": "building-supplies",
          "carTypeFilterQuery": "XM"
        }
      ],
      "purchaseLoads": [
        {
          "loadId": "logs",
          "componentId": "logs-e",
          "displayName": "Ocona-Lufty Logs for Ravensford",
          "carTypeFilterQuery": "FL",
          "createIfMissing": false
        }
      ],
      "outboundRoutes": [
        {
          "loadId": "lumber-dimensional",
          "destinationIndustryIds": [
            "sylva-interchange",
            "ewh-interchange",
            "andrews-interchange"
          ],
          "carTypeFilterQuery": "FM*",
          "carModelIdentifier": "fm-alw40",
          "minCarsPerService": 4,
          "maxCarsPerService": 8
        }
      ]
    }
  ]
}
```

## Full Smokemont-Style Example

See `KingG.Appalachian-Railway.ToolshedSelectiveInterchanges.json` for a complete setup using:

- separate flat and boxcar equipment pools
- startup reserves
- stockpile caps
- random empty-car requests
- rare building-supplies consumption
- random lumber, pole, and pulpwood output
- weighted active interchange destinations
- slower, unpaid supplemental Ocona-Lufty logs to Ravensford

## Example Patterns

### Buy-Only Interchange

Use this when the interchange only sells specific loads to the railroad.

```json
{
  "id": "ocona-lufty-buy-logs",
  "displayName": "Ocona-Lufty Logs",
  "interchangeIndustryId": "Oconalufty-RR-INT",
  "trackSpanIds": [ "Smokemont-INT" ],
  "configurePurchases": true,
  "disableUnlistedPurchaseLoaders": true,
  "enableOutboundTraffic": false,
  "purchaseLoads": [
    {
      "loadId": "logs",
      "componentId": "logs-e",
      "displayName": "Ocona-Lufty Logs",
      "carTypeFilterQuery": "FL",
      "createIfMissing": false
    }
  ]
}
```

What it does:

- keeps the existing `logs-e` loader
- disables unlisted buy/import loaders under the same interchange
- lets the player buy/import logs from Ocona-Lufty
- does not create any outbound mill production

### Off-Map Mill With Random Empty Cars

Use this when the off-map mill mostly needs empty equipment.

```json
{
  "id": "smokemont-empty-equipment",
  "displayName": "Smokemont Mill Equipment",
  "interchangeIndustryId": "Oconalufty-RR-INT",
  "trackSpanIds": [ "Smokemont-INT" ],
  "emptyCarRequests": [
    {
      "carTypeFilterQuery": "FL",
      "carModelIdentifier": "fl-alw40logging",
      "minCarsPerService": 6,
      "maxCarsPerService": 12,
      "orderTag": "smokemont-empty-flats"
    },
    {
      "carTypeFilterQuery": "XM",
      "minCarsPerService": 1,
      "maxCarsPerService": 3,
      "orderTag": "smokemont-empty-boxcars"
    }
  ]
}
```

What it does:

- asks the game's ops system for 6-12 empty logging flats
- asks for 1-3 empty boxcars
- pays/handles those empty moves using the normal game behavior
- does not itself create loaded return traffic unless `outboundRoutes` are also configured

### Mill Supplies In, Lumber Out

Use this when the off-map mill should consume supplies before producing loads.

```json
{
  "id": "smokemont-supply-cycle",
  "displayName": "Smokemont Mill",
  "interchangeIndustryId": "Oconalufty-RR-INT",
  "trackSpanIds": [ "Smokemont-INT" ],
  "enableOutboundTraffic": true,
  "enableProductionAccounting": true,
  "emptyCarRequests": [
    {
      "carTypeFilterQuery": "FL",
      "carModelIdentifier": "fl-alw40logging",
      "minCarsPerService": 6,
      "maxCarsPerService": 12,
      "equipmentCounterKey": "flat-equipment",
      "maxEquipmentCredits": 24.0
    }
  ],
  "inboundLoads": [
    {
      "loadId": "building-supplies",
      "displayName": "Smokemont Mill Building Supplies",
      "carTypeFilterQuery": "XM",
      "supplyCounterKey": "mill-supplies",
      "supplyCreditsPerCar": 12.0,
      "maxSupplyCredits": 24.0,
      "storageConsumptionRate": 3000.0,
      "maxStorage": 80000.0
    }
  ],
  "outboundRoutes": [
    {
      "loadId": "lumber-dimensional",
      "destinationIndustryId": "sylva-interchange",
      "carTypeFilterQuery": "FM*",
      "carModelIdentifier": "fm-alw40",
      "minCarsPerService": 4,
      "maxCarsPerService": 8,
      "equipmentCounterKey": "flat-equipment",
      "equipmentCreditsPerCar": 1.0,
      "supplyCounterKey": "mill-supplies",
      "supplyCreditsPerCar": 0.15,
      "minReturnDelayDays": 0.75,
      "maxReturnDelayDays": 1.75
    }
  ]
}
```

What it does:

- delivered empty flats become `flat-equipment` credits
- delivered building supplies become `mill-supplies` credits
- each loaded lumber car consumes 1 flat credit and 0.15 supply credits
- production waits roughly 1-2 days after each outbound batch
- production stops when equipment or supplies run out
- building supplies are deliberately slow-burning so the mill does not order them constantly

### Weighted Active Interchange Destinations

Use this when the same loaded traffic can leave the map through more than one interchange.

```json
{
  "loadId": "lumber-dimensional",
  "destinationWeights": [
    { "destinationIndustryId": "sylva-interchange", "weight": 70 },
    { "destinationIndustryId": "ewh-interchange", "weight": 25 },
    { "destinationIndustryId": "andrews-interchange", "weight": 5 }
  ],
  "carTypeFilterQuery": "FM*",
  "carModelIdentifier": "fm-alw40",
  "minCarsPerService": 4,
  "maxCarsPerService": 8
}
```

What it does:

- skips inactive or progression-disabled interchanges
- if only East Whittier is active, all traffic goes there
- if only Sylva is active, all traffic goes there
- if Sylva and Andrews are active, the low Andrews weight keeps most traffic moving through Sylva
- weights are relative among the active candidates only

### Supplemental Purchased Logs To A Mill

Use this when the off-map railroad can sell logs to an on-map mill, but it should not become the best-paying move.

```json
{
  "purchaseLoads": [
    {
      "loadId": "logs",
      "componentId": "logs-e",
      "displayName": "Ocona-Lufty Logs for Ravensford",
      "carTypeFilterQuery": "FL",
      "createIfMissing": false
    }
  ],
  "outboundRoutes": [
    {
      "loadId": "logs",
      "destinationIndustryId": "Ravensford-Sawmill",
      "destinationComponentId": "logs",
      "carTypeFilterQuery": "FL",
      "carModelIdentifier": "fl-alw40logging",
      "carsPerService": 1,
      "minReturnDelayDays": 2.0,
      "maxReturnDelayDays": 5.0,
      "slowDayChance": 0.35,
      "slowDayMultiplier": 0.0,
      "noPayment": true,
      "orderTag": "ocona-lufty-logs-to-ravensford"
    }
  ]
}
```

What it does:

- keeps logs buyable from Ocona-Lufty
- sends occasional logs to `Ravensford-Sawmill.logs`
- delays the route so it feels like supply from another railroad
- uses `noPayment` so the helper movement does not double-pay on top of bought/imported logs

## Interchange Fields

`id` is the Toolshed definition id. It only needs to be unique within the loaded configs.

`componentId` is the sub-id for the runtime Toolshed component. If omitted, Toolshed creates one from `id`.

`displayName` is shown by the component.

`interchangeIndustryId` is the existing industry that contains the vanilla `Interchange` component.

`interchangeIndustryIds` is a fallback list. Toolshed uses the first existing industry.

`trackSpanIds` are the physical interchange tracks where created helper components should work. If omitted, Toolshed uses the vanilla interchange spans.

`configurePurchases` enables `purchaseLoads`.

`disableUnlistedPurchaseLoaders` disables existing vanilla interchanged loaders under that interchange unless their component/load is listed in `purchaseLoads`.

`enableOutboundTraffic` enables `outboundRoutes`.

`enableProductionAccounting` enables off-map counters, caps, startup reserves, and production gating.

`requireEnabledInterchange` prevents routing to disabled/progression-locked interchanges. Keep this true for progression-aware routes.

`excludeFromGenericInterchangeSelection` keeps the selective interchange out of Railroader's normal interchange picker. Keep this true when the interchange should only handle configured traffic. Without it, generic house track, team track, and other industry orders may spawn from that interchange.

## Empty Car Requests

Use `emptyCarRequests` when the off-map operation needs empty equipment.

Common fields:

- `carTypeFilterQuery`: normal Railroader car type filter, such as `FL`, `XM`, `FM*`
- `carModelIdentifier`: optional exact car definition/model id, such as `fl-alw40logging`
- `carsPerService`: fixed number of empties to request
- `minCarsPerService` and `maxCarsPerService`: random requested count
- `originIndustryId`: interchange industry where the empty cars should spawn
- `originIndustryIds`: fallback list; Toolshed uses the first enabled interchange it can resolve
- `originWeights`: weighted random origin list; disabled/progression-locked interchanges are skipped
- `allowInactiveOriginFallback`: if no enabled origin resolves, allow the same origin list to use inactive/progression-locked interchanges; useful for sandbox testing
- `orderTag`: stable waybill/order tag; useful for debugging and accounting
- `noPayment`: passes through to the game empty-car order

When the selective interchange is attached to an interchange, use an explicit origin. Without one, the base game may choose the same interchange as the source, which creates a car for itself.

```json
"originWeights": [
  { "originIndustryId": "sylva-interchange", "weight": 70 },
  { "originIndustryId": "ewh-interchange", "weight": 25 },
  { "originIndustryId": "andrews-interchange", "weight": 5 }
],
"allowInactiveOriginFallback": true
```

If only Whittier is active, the Whittier entry gets all empty traffic. If Sylva replaces Whittier, Sylva gets it. If Sylva and Andrews are both active, the low Andrews weight keeps most requests favoring Sylva.

In sandbox, progression may leave the normal campaign starter interchange inactive. With `allowInactiveOriginFallback`, Toolshed first tries normal active origins; if none are usable, it falls back to the same configured origin list without the progression/disabled check.

Accounting fields:

- `equipmentCounterKey`: counter credited when a matching empty car is delivered
- `equipmentCreditsPerCar`: credits added per delivered car, default `1`
- `maxEquipmentCredits`: cap for that equipment pool
- `countOnlyTaggedCars`: only count cars ordered by this request, default `true`

Use separate equipment counters when the off-map industry needs different car types for different outputs:

```json
{
  "carTypeFilterQuery": "FL",
  "carModelIdentifier": "fl-alw40logging",
  "equipmentCounterKey": "flat-equipment",
  "maxEquipmentCredits": 10.0
}
```

## Specific Car Models

Use this when `FL` or `FM*` is too broad and you want the interchange to spawn a particular car from a car pack.

```json
{
  "carTypeFilterQuery": "FL",
  "carModelIdentifier": "fl-alw40logging",
  "minCarsPerService": 3,
  "maxCarsPerService": 7,
  "orderTag": "smokemont-empty-flats"
}
```

Toolshed checks the requested model against the normal `carTypeFilterQuery` and the requested load. If the model is missing, has the wrong car type, or cannot carry the load, Toolshed writes one warning and lets Railroader fall back to its normal random car selection.

Accepted aliases:

- `carModelIdentifier` or `modelIdentifier`: one exact definition/model id
- `carModelIdentifiers` or `modelIdentifiers`: list of acceptable ids
- `carPrototypeIdentifier`, `carPrototypeId`, or `carDefinitionIdentifier`: alternate names for the same idea
- `carModelWeights`: weighted model list

Weighted example:

```json
{
  "carTypeFilterQuery": "FM*",
  "carModelWeights": [
    { "carModelIdentifier": "fm-alw40", "weight": 4 },
    { "carModelIdentifier": "some-other-fm-flat", "weight": 1 }
  ],
  "loadId": "lumber-dimensional"
}
```

This applies to cars spawned by `emptyCarRequests` and `outboundRoutes`. `purchaseLoads` only controls what an import loader can load onto cars already handled by that loader.

## Inbound Loads

Use `inboundLoads` for loaded cars consumed by the off-map operation.

Selective interchange inbound loads are scheduled by default. The car may complete its waybill when it arrives, but Toolshed does not unload or remove it from the map until the parent `Interchange` reaches its normal daily or extra service time.

Common fields:

- `loadId`: load consumed, such as `building-supplies`
- `componentId`: scheduled receiver id
- `displayName`: scheduled receiver name
- `carTypeFilterQuery`: matching car type
- `trackSpanIds`: optional span override
- `createIfMissing`: used only when `useVanillaIndustryUnloader` is true
- `orderLoadedCars`: used only when `useVanillaIndustryUnloader` is true
- `orderAwayEmpties`: order cars away after unloading
- `useVanillaIndustryUnloader`: optional compatibility mode; when true, Toolshed creates/configures a normal Railroader `IndustryUnloader`, which unloads on the normal industry tick instead of waiting for interchange service

Accounting fields:

- `supplyCounterKey`: counter credited when the load is delivered
- `supplyCreditsPerCar`: credits added per delivered loaded car
- `maxSupplyCredits`: cap for the supply pool
- `countOnlyTaggedCars`: only count cars ordered by this inbound rule
- `moveToBardoAfterUnload`: remove the unloaded car from the map instead of ordering it away

For Smokemont, use only `building-supplies` if the mill should occasionally receive supplies.

## Purchase Loads

Use `purchaseLoads` only for goods the railroad can buy/import from the off-map railroad.

Fields:

- `loadId`: buyable load
- `componentId`: existing or generated `InterchangedIndustryLoader` sub-id
- `displayName`: loader display name
- `carTypeFilterQuery`: matching car type
- `trackSpanIds`: optional span override
- `createIfMissing`: create a missing buy/import loader

For Ocona-Lufty, logs can be listed here so Ravensford can buy off-map logs. This should be less profitable than hauling local logs because the load has an import cost and the onward helper route can be set to `noPayment`.

## Outbound Routes

Use `outboundRoutes` for loaded cars that return from the off-map operation and continue to an industry or interchange.

Common fields:

- `loadId`: load to spawn/order
- `carTypeFilterQuery`: car types to use
- `carModelIdentifier`: optional exact car definition/model id, such as `fm-alw40`
- `carsPerService`: fixed output count
- `minCarsPerService` and `maxCarsPerService`: random output count
- `orderTag`: stable route tag
- `noPayment`: suppress payment on this generated route

Accounting/gating fields:

- `equipmentCounterKey`: required equipment pool
- `equipmentCreditsPerCar`: equipment credits consumed per outbound loaded car
- `supplyCounterKey`: required supply pool
- `supplyCreditsPerCar`: supply credits consumed per outbound loaded car

Timing and variation fields:

- `minReturnDelayDays` and `maxReturnDelayDays`: random delay after each batch
- `weekdaysOnly`: skip production on days where `day % 7` is 5 or 6
- `productionDays`: exact allowed day numbers using `day % 7`
- `slowDayChance`: chance to reduce production
- `slowDayMultiplier`: multiplier when a slow day happens; use `0` to cancel the batch

## Destinations

For a specific industry component, use:

```json
{
  "destinationIndustryId": "Ravensford-Sawmill",
  "destinationComponentId": "logs"
}
```

The full component id is `industryId.componentId`, so this resolves to `Ravensford-Sawmill.logs`.

Use `destinationPositionId` only when you already know the full component id.

Use `destinationIndustryId` by itself when the destination is an interchange industry.

Use `destinationIndustryIds` when the same traffic can leave through whichever interchange is currently active. Toolshed checks the list and uses the first enabled interchange it can resolve.

Use `destinationWeights` when several destinations may be active and you want a weighted random choice:

```json
"destinationWeights": [
  { "destinationIndustryId": "sylva-interchange", "weight": 70 },
  { "destinationIndustryId": "ewh-interchange", "weight": 25 },
  { "destinationIndustryId": "andrews-interchange", "weight": 5 }
]
```

Disabled or progression-locked interchanges are skipped when `requireEnabledInterchange` is true.

For sandbox-friendly outbound traffic, set `allowInactiveDestinationFallback` on an `outboundRoutes` entry. Toolshed will still prefer enabled destinations, but if none are usable it can route to the configured inactive interchange destinations.

## Startup Reserves And Caps

Use `initialCounters` to seed a new off-map operation so it can produce a little before the first full supply cycle is complete.

```json
"initialCounters": [
  { "key": "flat-equipment", "value": 5.0, "maxValue": 24.0 },
  { "key": "boxcar-equipment", "value": 2.0, "maxValue": 10.0 },
  { "key": "mill-supplies", "value": 8.0, "maxValue": 24.0 }
]
```

Initial counters are applied once per saved game/counter key. They do not reset every session.

Use `maxEquipmentCredits` and `maxSupplyCredits` to stop an off-map operation from hoarding unlimited empties or supplies.

## Payment Notes

Empty-car requests use the game's normal empty-car ordering behavior, including the game's normal small empty-car payment.

Inbound loaded supplies pay like normal industry delivery unless the game or config suppresses payment.

Outbound production pays like a normal loaded route unless `noPayment` is true.

For bought/imported goods, consider using `noPayment` on any helper onward route to avoid double-paying the same move.

## King Appalachian Railway IDs

The King Appalachian Railway sample uses these ids from the supplied route data:

- Ocona-Lufty interchange industry: `Oconalufty-RR-INT`
- Ocona-Lufty interchange component: `t1`
- Smokemont interchange span: `Smokemont-INT`
- existing Ocona-Lufty log buy/import loader: `logs-e`
- Ravensford sawmill log unloader: `Ravensford-Sawmill.logs`

Base-game interchange ids used for onward traffic:

- `ewh-interchange`
- `sylva-interchange`
- `andrews-interchange`

The active set changes with progression, and disabled interchanges are skipped.
