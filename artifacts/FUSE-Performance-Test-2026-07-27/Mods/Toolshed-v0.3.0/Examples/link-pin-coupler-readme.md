# Link And Pin Coupler Definition Setup

Toolshed's link-and-pin support is definition-driven. It only changes an end of a vehicle when that vehicle has a `DetailModel` component using the `LinkAndPinCoupler/link-pin-coupler` asset.

Use this when a locomotive, tender, or car modeler wants a visual link-and-pin setup without replacing every coupler in the game.

## What Toolshed Does

- Hides the vanilla knuckle coupler only on the matching end.
- Shows the link-and-pin asset as a loose link visual by default.
- Hides the imported coupler pocket and pin from the asset, so the model can use its own pocket or drawbar.
- When two link-and-pin ends couple together, only one loose link stays visible between them.
- Adds a small `0.01f` slack adjustment to marked link-and-pin ends.
- Adds equipment customization checkboxes for every marked end.

## Player Customization

If a vehicle definition has one marked link-and-pin end, the customization window shows:

- `Loose Link`
- `Pin`
- `Pocket`

If both ends are marked, it shows:

- `A-End Loose Link`
- `A-End Pin`
- `A-End Pocket`
- `B-End Loose Link`
- `B-End Pin`
- `B-End Pocket`

The loose link defaults on so existing setups keep working. The imported prefab pin and pocket default off because many locomotive and tender models already have their own pin or pocket detail. These choices save on the car.

## End Naming

The component name tells Toolshed which end it belongs to:

- `Link And Pin Coupler F` applies to the front/A end.
- `Link And Pin Coupler R` applies to the rear/B end.

Names ending in `Front` or `Rear` also work.

## Definition Component

Paste one of the objects from `link-pin-coupler-detail-model.json` into the vehicle's `components` array, then tune:

- `parent.path`
- `transform.position`
- `transform.rotation`
- `transform.scale`

For locomotives with a fixed front drawbar, usually put the visual link only on the tender or rear pocket end. For freight cars, add one component per end only where the car should have link-and-pin visuals.

## Asset Ids

The model entry must stay:

```json
"model": {
  "assetPackIdentifier": "LinkAndPinCoupler",
  "assetIdentifier": "link-pin-coupler"
}
```

If those ids change, Toolshed will treat it as a normal detail model and will not hide the vanilla coupler for that end.
