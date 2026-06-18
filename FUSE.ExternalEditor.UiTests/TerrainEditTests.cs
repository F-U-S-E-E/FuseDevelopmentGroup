using System;
using System.IO;
using Fuse.ExternalEditor.Logic;
using Fuse.ExternalEditor.Models.Terrain;
using Fuse.ExternalEditor.Services;
using Xunit;

namespace Fuse.ExternalEditor.UiTests;

/// <summary>fBm noise determinism, tile save round-trip, and terrain-stroke undo/redo.</summary>
public class TerrainEditTests
{
    [Fact]
    public void Fbm_Is_Bounded_Seed_Deterministic_And_Seed_Sensitive()
    {
        const int res = 48;
        var a = FbmNoise.Build(res, 32, seed: 42);
        var b = FbmNoise.Build(res, 32, seed: 42);
        var c = FbmNoise.Build(res, 32, seed: 7);

        var anyNonZero = false;
        var differsFromOtherSeed = false;
        for (var r = 0; r < res; r++)
        {
            for (var col = 0; col < res; col++)
            {
                var va = a.Sample(r, col);
                Assert.True(Math.Abs(va) <= 2.0);          // ~[-1,1], allow gradient overshoot
                Assert.Equal(va, b.Sample(r, col));         // same seed → identical field
                if (va != 0f)
                {
                    anyNonZero = true;
                }

                if (va != c.Sample(r, col))
                {
                    differsFromOtherSeed = true;
                }
            }
        }

        Assert.True(anyNonZero);
        Assert.True(differsFromOtherSeed);
    }

    [Fact]
    public void SaveTile_RoundTrips_Channels()
    {
        const int res = 16;
        var tile = TerrainBrushTests.Flat(res, 12345, a: 0xC0);
        tile.R[5] = 0xAB;
        tile.G[5] = 0xCD;
        tile.A[5] = 0x9F;
        tile.RecalcStats();

        var dir = Path.Combine(Path.GetTempPath(), "fuse-tile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "tile_0_0.data");
            var svc = new TerrainTileService();
            svc.SaveTile(tile, path);
            var loaded = svc.LoadTile(path);

            Assert.NotNull(loaded);
            Assert.False(tile.Dirty); // save clears the dirty flag
            for (var i = 0; i < res * res; i++)
            {
                Assert.Equal(tile.R[i], loaded!.R[i]);
                Assert.Equal(tile.G[i], loaded.G[i]);
                Assert.Equal(tile.A[i], loaded.A[i]);
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void TerrainStroke_Undo_Redo_Restores_Exactly()
    {
        const int res = 64;
        var tile = TerrainBrushTests.Flat(res, 20000);
        var stroke = new TerrainStroke();
        var s = new BrushSettings { Kind = TerrainBrushKind.Raise, Strength = 0.1f };

        // Two dabs in one stroke, recording originals before each write.
        TerrainBrush.Apply(tile, 32, 32, 6, s, stroke: stroke);
        TerrainBrush.Apply(tile, 44, 44, 6, s, stroke: stroke);

        var raisedA = tile.Height16((32 * res) + 32);
        var raisedB = tile.Height16((44 * res) + 44);
        Assert.True(raisedA > 20000);
        Assert.True(raisedB > 20000);

        var action = stroke.Commit();
        Assert.NotNull(action);

        action!.Undo();
        Assert.Equal(20000, tile.Height16((32 * res) + 32));
        Assert.Equal(20000, tile.Height16((44 * res) + 44));

        action.Redo();
        Assert.Equal(raisedA, tile.Height16((32 * res) + 32));
        Assert.Equal(raisedB, tile.Height16((44 * res) + 44));
    }

    [Fact]
    public void TerrainStroke_With_No_Change_Commits_Null()
    {
        var tile = TerrainBrushTests.Flat(32, 10000);
        var stroke = new TerrainStroke();
        stroke.RecordBefore(tile, new[] { 0, 1, 2 }); // recorded but nothing edited
        Assert.Null(stroke.Commit());
    }
}
