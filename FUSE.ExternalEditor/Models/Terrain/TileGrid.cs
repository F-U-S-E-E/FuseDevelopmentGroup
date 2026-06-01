using System.Collections.Generic;

namespace Fuse.ExternalEditor.Models.Terrain;

/// <summary>
/// The loaded set of terrain tiles keyed by (x, y), with the grid bounds the
/// viewport uses to lay tiles out on screen (matches the Python editor's
/// <c>min_x</c>/<c>max_x</c>/<c>min_y</c>/<c>max_y</c>).
/// </summary>
public sealed class TileGrid
{
    private readonly Dictionary<(int X, int Y), TerrainTile> _tiles = new();

    public IReadOnlyDictionary<(int X, int Y), TerrainTile> Tiles => _tiles;

    public int Count => _tiles.Count;

    public int MinX { get; private set; }
    public int MaxX { get; private set; }
    public int MinY { get; private set; }
    public int MaxY { get; private set; }

    public bool TryGet(int x, int y, out TerrainTile tile) => _tiles.TryGetValue((x, y), out tile!);

    public void Add(TerrainTile tile)
    {
        _tiles[(tile.X, tile.Y)] = tile;
        Recompute();
    }

    public void AddRange(IEnumerable<TerrainTile> tiles)
    {
        foreach (var tile in tiles)
        {
            _tiles[(tile.X, tile.Y)] = tile;
        }

        Recompute();
    }

    public void Clear()
    {
        _tiles.Clear();
        MinX = MaxX = MinY = MaxY = 0;
    }

    private void Recompute()
    {
        if (_tiles.Count == 0)
        {
            MinX = MaxX = MinY = MaxY = 0;
            return;
        }

        MinX = int.MaxValue;
        MaxX = int.MinValue;
        MinY = int.MaxValue;
        MaxY = int.MinValue;

        foreach (var (x, y) in _tiles.Keys)
        {
            if (x < MinX) MinX = x;
            if (x > MaxX) MaxX = x;
            if (y < MinY) MinY = y;
            if (y > MaxY) MaxY = y;
        }
    }
}
