using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Fuse.ExternalEditor.Models.Terrain;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Fuse.ExternalEditor.Services;

/// <summary>
/// Decodes terrain tiles with ImageSharp (the files are RGBA PNGs despite the
/// <c>.data</c> extension). Mirrors <c>edit_tiles/terrain.py</c> <c>load_tile</c>:
/// parses (x, y) from the filename and reads the R/G/A channels (B is unused).
/// </summary>
public sealed partial class TerrainTileService : ITerrainTileService
{
    [GeneratedRegex(@"^tile_(-?\d+)_(-?\d+)\.data$", RegexOptions.IgnoreCase)]
    private static partial Regex TileNameRegex();

    public TerrainTile? LoadTile(string path)
    {
        var name = System.IO.Path.GetFileName(path);
        var match = TileNameRegex().Match(name);
        if (!match.Success)
        {
            return null;
        }

        var tx = int.Parse(match.Groups[1].Value);
        var ty = int.Parse(match.Groups[2].Value);

        using var image = Image.Load<Rgba32>(path);
        var res = System.Math.Min(image.Width, image.Height);
        var count = res * res;
        var r = new byte[count];
        var g = new byte[count];
        var a = new byte[count];

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < res; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < res; x++)
                {
                    var i = (y * res) + x;
                    var px = row[x];
                    r[i] = px.R;
                    g[i] = px.G;
                    a[i] = px.A;
                }
            }
        });

        return new TerrainTile(tx, ty, res, r, g, a, path);
    }

    public IReadOnlyList<TerrainTile> LoadFolder(string directory)
    {
        var tiles = new List<TerrainTile>();
        if (!Directory.Exists(directory))
        {
            return tiles;
        }

        foreach (var file in Directory.EnumerateFiles(directory, "tile_*.data"))
        {
            var tile = LoadTile(file);
            if (tile is not null)
            {
                tiles.Add(tile);
            }
        }

        return tiles;
    }

    public void SaveTile(TerrainTile tile, string? path = null)
    {
        var dest = path ?? tile.Path;
        if (string.IsNullOrEmpty(dest))
        {
            throw new InvalidOperationException("Tile has no path to save to.");
        }

        var res = tile.Res;
        using var image = new Image<Rgba32>(res, res);
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < res; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < res; x++)
                {
                    var i = (y * res) + x;
                    row[x] = new Rgba32(tile.R[i], tile.G[i], 0, tile.A[i]);
                }
            }
        });

        image.SaveAsPng(dest);
        tile.Dirty = false;
    }
}
