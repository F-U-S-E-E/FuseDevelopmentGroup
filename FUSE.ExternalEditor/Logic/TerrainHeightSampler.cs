using System;
using Fuse.ExternalEditor.Models.Terrain;

namespace Fuse.ExternalEditor.Logic;

/// <summary>
/// Samples terrain elevation (metres) at a world XZ position from a loaded
/// <see cref="TileGrid"/>. Inverts the viewport's world→tile mapping: a tile (X,Y)
/// covers worldX in [X·500,(X+1)·500] and worldZ in [Y·500,(Y+1)·500], with higher
/// worldZ (north) at pixel row 0. Returns null if no tile covers the point.
/// </summary>
public static class TerrainHeightSampler
{
    public static double? Sample(TileGrid grid, double worldX, double worldZ)
    {
        if (grid is null || grid.Count == 0)
        {
            return null;
        }

        var txf = worldX / TerrainConstants.UnityTileMeters;
        var tzf = worldZ / TerrainConstants.UnityTileMeters;
        var tx = (int)Math.Floor(txf);
        var ty = (int)Math.Floor(tzf);
        if (!grid.TryGet(tx, ty, out var tile))
        {
            return null;
        }

        var res = tile.Res;
        var col = Math.Clamp((int)((txf - tx) * res), 0, res - 1);
        var row = Math.Clamp((int)((1.0 - (tzf - ty)) * res), 0, res - 1);
        return TerrainConstants.ToMeters(tile.Height16((row * res) + col));
    }
}
