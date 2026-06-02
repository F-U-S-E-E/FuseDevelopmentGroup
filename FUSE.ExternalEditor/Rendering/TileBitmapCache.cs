using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Fuse.ExternalEditor.Logic;
using Fuse.ExternalEditor.Models.Terrain;

namespace Fuse.ExternalEditor.Rendering;

/// <summary>
/// Caches a rendered <see cref="WriteableBitmap"/> per (tile, mode, hillshade),
/// mirroring the Python editor's per-tile surface cache. The renderer produces
/// row-major RGBA; this swaps to the BGRA the bitmap expects.
/// </summary>
public sealed class TileBitmapCache
{
    private readonly Dictionary<(int X, int Y, TerrainMode Mode, bool Hillshade), WriteableBitmap> _cache = new();

    public WriteableBitmap GetOrCreate(TerrainTile tile, TerrainMode mode, bool hillshade)
    {
        var key = (tile.X, tile.Y, mode, hillshade);
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var bitmap = Build(tile, mode, hillshade);
        _cache[key] = bitmap;
        return bitmap;
    }

    public void Clear()
    {
        foreach (var bitmap in _cache.Values)
        {
            bitmap.Dispose();
        }

        _cache.Clear();
    }

    /// <summary>Drop cached bitmaps for one tile (all modes) after its pixels were edited.</summary>
    public void Invalidate(TerrainTile tile)
    {
        var stale = new List<(int X, int Y, TerrainMode Mode, bool Hillshade)>();
        foreach (var key in _cache.Keys)
        {
            if (key.X == tile.X && key.Y == tile.Y)
            {
                stale.Add(key);
            }
        }

        foreach (var key in stale)
        {
            _cache[key].Dispose();
            _cache.Remove(key);
        }
    }

    private static WriteableBitmap Build(TerrainTile tile, TerrainMode mode, bool hillshade)
    {
        var rgba = TerrainRenderer.Render(tile, mode, hillshade);
        var res = tile.Res;
        var bitmap = new WriteableBitmap(
            new PixelSize(res, res),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Opaque);

        using var fb = bitmap.Lock();
        var rowLen = res * 4;
        var row = new byte[rowLen];
        for (var y = 0; y < res; y++)
        {
            var srcBase = y * rowLen;
            for (var x = 0; x < res; x++)
            {
                var s = srcBase + (x * 4);
                var d = x * 4;
                row[d] = rgba[s + 2];     // B
                row[d + 1] = rgba[s + 1]; // G
                row[d + 2] = rgba[s];     // R
                row[d + 3] = rgba[s + 3]; // A
            }

            Marshal.Copy(row, 0, fb.Address + (y * fb.RowBytes), rowLen);
        }

        return bitmap;
    }
}
