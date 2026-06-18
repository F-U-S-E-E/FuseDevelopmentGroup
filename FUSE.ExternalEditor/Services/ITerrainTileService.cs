using System.Collections.Generic;
using Fuse.ExternalEditor.Models.Terrain;

namespace Fuse.ExternalEditor.Services;

/// <summary>Loads terrain <c>tile_X_Y.data</c> files (RGBA PNGs) into <see cref="TerrainTile"/>s.</summary>
public interface ITerrainTileService
{
    /// <summary>Load a single <c>tile_X_Y.data</c>; returns null if the name doesn't parse.</summary>
    TerrainTile? LoadTile(string path);

    /// <summary>Load every <c>tile_*.data</c> in a folder.</summary>
    IReadOnlyList<TerrainTile> LoadFolder(string directory);

    /// <summary>Encode a (possibly edited) tile back to an RGBA <c>.data</c> PNG (B=0); defaults to the tile's own path.</summary>
    void SaveTile(TerrainTile tile, string? path = null);
}
