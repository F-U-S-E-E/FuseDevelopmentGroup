#!/usr/bin/env python3
"""Generate the FUSE test map pack: a small flat-terrain map used to verify
FUSE map-package loading end to end (registry -> launch -> MapStore redirect).

Tile format (reverse-engineered from Map.Runtime.TileData.Save):
  - each tile_XXX_YYY.data is a 513x513 RGBA PNG
  - height is a 16-bit value split across R (high byte) and G (low byte),
    encoded as (heightMeters - 500) * 65.535, clamped to [0, 65535]
    (the game's terrain band is 500..1500 m)
  - B is unused (0); A packs vegetation/water mask bits (0 = defaults)

Map.json mirrors StreamingAssets/Maps/BushnellWhittier/Map.json:
  {"origin": {"latitude": ..., "longitude": ...}, "tileDimension": 500,
   "tiles": [{"x": ..., "y": ...}, ...]}

Usage:
  python scripts/generate-test-map-pack.py [output-folder]

Default output folder is the pack folder inside the Railroader Mods
directory: C:/Steam/steamapps/common/Railroader/Mods/FuseTestMap
"""

import json
import math
import os
import sys

from PIL import Image

RESOLUTION = 513
TILE_DIMENSION = 500
BASE_HEIGHT_M = 550.0
HILL_HEIGHT_M = 40.0

# PRR Middle Division project center (Tuscarora Valley) so the test pack also
# exercises an origin far away from the stock map's Bushnell, NC origin.
ORIGIN_LAT = 40.43
ORIGIN_LON = -77.72

# Tile grid: generous coverage around the world origin so the stock spawn
# point (somewhere in the Whittier area of game-coordinate space) sits over
# real terrain instead of a hole.
TILE_RANGE_X = range(-2, 15)
TILE_RANGE_Y = range(-2, 15)

PACK_ID = "fuse-test-map"
DEFAULT_OUTPUT = r"C:\Steam\steamapps\common\Railroader\Mods\FuseTestMap"


def height_at(world_x: float, world_z: float) -> float:
    """Flat plain with one broad sinusoidal hill so terrain is visibly ours."""
    hill = (
        math.sin(world_x / 700.0 * math.pi)
        * math.sin(world_z / 700.0 * math.pi)
    )
    return BASE_HEIGHT_M + HILL_HEIGHT_M * max(0.0, hill)


def encode_height(height_m: float) -> int:
    value = int((height_m - 500.0) * 65.535)
    return max(0, min(65535, value))


def build_tile(tile_x: int, tile_y: int) -> Image.Image:
    image = Image.new("RGBA", (RESOLUTION, RESOLUTION))
    pixels = image.load()
    step = TILE_DIMENSION / (RESOLUTION - 1)
    for row in range(RESOLUTION):
        world_z = (tile_y * TILE_DIMENSION) + row * step
        for col in range(RESOLUTION):
            world_x = (tile_x * TILE_DIMENSION) + col * step
            encoded = encode_height(height_at(world_x, world_z))
            pixels[col, row] = ((encoded >> 8) & 0xFF, encoded & 0xFF, 0, 0)
    return image


def tile_coord(value: int) -> str:
    """Match C# value.ToString("000"): minimum three digits, sign extra."""
    return f"-{abs(value):03d}" if value < 0 else f"{value:03d}"


def main() -> None:
    output = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_OUTPUT
    map_folder = os.path.join(output, "Map")
    os.makedirs(map_folder, exist_ok=True)

    tiles = []
    for tile_x in TILE_RANGE_X:
        for tile_y in TILE_RANGE_Y:
            tiles.append({"x": tile_x, "y": tile_y})
            tile_path = os.path.join(
                map_folder, f"tile_{tile_coord(tile_x)}_{tile_coord(tile_y)}.data"
            )
            build_tile(tile_x, tile_y).save(tile_path, format="PNG")

    map_json = {
        "origin": {"latitude": ORIGIN_LAT, "longitude": ORIGIN_LON},
        "tileDimension": TILE_DIMENSION,
        "tiles": tiles,
    }
    with open(os.path.join(map_folder, "Map.json"), "w", encoding="utf-8") as f:
        json.dump(map_json, f)

    definition = {
        "schemaVersion": "1.0",
        "id": PACK_ID,
        "name": "FUSE Test Map",
        "author": "FUSE",
        "description": (
            "Flat test terrain used to verify FUSE map-package loading. "
            "Launch from the FUSE console (/fuse.map.launch fuse-test-map) "
            "or the FUSE menu Tools page."
        ),
        "map": {
            "displayName": "FUSE Test Map (Flat Plain)",
            "description": "A flat 550 m plain with one broad hill.",
            "mapFolder": "Map",
        },
    }
    definition_path = os.path.join(output, f"{PACK_ID}.fuse.json")
    with open(definition_path, "w", encoding="utf-8") as f:
        json.dump(definition, f, indent=2)

    # FUSE package discovery only treats a Mods folder as a data package when
    # Info.json declares FuseDataFile(s) (or a FUSE requirement plus a root
    # definition). Without this, the pack is silently ignored.
    info = {
        "Id": PACK_ID,
        "DisplayName": "FUSE Test Map",
        "Author": "FUSE",
        "Version": "0.1.0",
        "ManagerVersion": "0.27.10",
        "GameVersion": "2025.1",
        "Requirements": ["FUSE"],
        "LoadAfter": ["FUSE"],
        "FuseDataFiles": [f"{PACK_ID}.fuse.json"],
    }
    with open(os.path.join(output, "Info.json"), "w", encoding="utf-8") as f:
        json.dump(info, f, indent=2)

    print(f"Wrote {len(tiles)} tiles, Info.json, and {definition_path}")


if __name__ == "__main__":
    main()
