namespace Fuse.ExternalEditor.Models.Terrain;

/// <summary>
/// Terrain constants ported from the Python editor's <c>edit_tiles/constants.py</c>.
/// Height is stored as a 16-bit value (R*256+G) mapped linearly onto
/// [<see cref="HeightMinM"/>, <see cref="HeightMaxM"/>] metres. Tiles are
/// <see cref="OverviewRes"/>² but tile up at <see cref="TileStride"/> world
/// pixels (the overlap pixel is shared).
/// </summary>
public static class TerrainConstants
{
    public const float HeightMinM = 500.0f;
    public const float HeightMaxM = 1500.0f;
    public const int OverviewRes = 513;

    /// <summary>World-pixel stride per tile (terrain bitmap is 513², overlap pixel shared).</summary>
    public const int TileStride = 512;

    /// <summary>On-screen pixels per tile at zoom 1 (Python <c>tile_size</c>); screen tile size = zoom * this.</summary>
    public const int TileScreenBase = 64;

    /// <summary>World metres per tile (Python <c>UNITY_TILE</c>) — used to map world coords to the tile grid.</summary>
    public const double UnityTileMeters = 500.0;

    /// <summary>Convert a 16-bit height sample (0..65535) to metres.</summary>
    public static float ToMeters(float h16) =>
        h16 / 65535f * (HeightMaxM - HeightMinM) + HeightMinM;
}
