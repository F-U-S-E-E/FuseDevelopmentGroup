using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Fuse.ExternalEditor.Logic;
using Fuse.ExternalEditor.Models.Terrain;
using Xunit;

namespace Fuse.ExternalEditor.UiTests;

/// <summary>Pure terrain-brush tests: quintic falloff golden vs Python + per-brush effects.</summary>
public class TerrainBrushTests
{
    internal static TerrainTile Flat(int res, int h16, byte a = 0)
    {
        var r = new byte[res * res];
        var g = new byte[res * res];
        var aa = new byte[res * res];
        for (var i = 0; i < res * res; i++)
        {
            r[i] = (byte)((h16 >> 8) & 0xFF);
            g[i] = (byte)(h16 & 0xFF);
            aa[i] = a;
        }

        return new TerrainTile(0, 0, res, r, g, aa);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(12)]
    public void Falloff_Matches_Python(int radius)
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "falloff-golden.json"));
        using var doc = JsonDocument.Parse(json);
        var golden = doc.RootElement.GetProperty("falloff").GetProperty(radius.ToString())
            .EnumerateArray().Select(e => e.GetDouble()).ToArray();

        var d = (2 * radius) + 1;
        for (var yy = 0; yy < d; yy++)
        {
            for (var xx = 0; xx < d; xx++)
            {
                var dist = Math.Sqrt(Math.Pow(yy - radius, 2) + Math.Pow(xx - radius, 2));
                var actual = TerrainBrush.Falloff(dist, radius);
                Assert.True(Math.Abs(golden[(yy * d) + xx] - actual) < 1e-4, $"r{radius}[{yy},{xx}] expected {golden[(yy * d) + xx]}, got {actual}");
            }
        }
    }

    [Fact]
    public void Raise_Lifts_Centre_Most_And_Leaves_Outside_Untouched()
    {
        const int res = 64;
        var tile = Flat(res, 20000);
        var s = new BrushSettings { Kind = TerrainBrushKind.Raise, Strength = 0.05f };

        var changed = TerrainBrush.Apply(tile, 32, 32, 6, s);

        Assert.NotEmpty(changed);
        var centre = tile.Height16((32 * res) + 32);
        var near = tile.Height16((32 * res) + 36);
        Assert.True(centre > near);            // quintic falloff: centre rises most
        Assert.True(near > 20000);             // still lifted within radius
        Assert.Equal(20000, tile.Height16(0)); // outside the radius: unchanged
    }

    [Fact]
    public void Lower_Is_Raise_With_Erase()
    {
        const int res = 64;
        var tile = Flat(res, 30000);
        TerrainBrush.Apply(tile, 32, 32, 6, new BrushSettings { Kind = TerrainBrushKind.Raise, Strength = 0.05f, Erase = true });
        Assert.True(tile.Height16((32 * res) + 32) < 30000);
    }

    [Fact]
    public void Flatten_Moves_Toward_Target()
    {
        const int res = 64;
        var tile = Flat(res, 10000);
        TerrainBrush.Apply(tile, 32, 32, 6, new BrushSettings { Kind = TerrainBrushKind.Flatten, Strength = 0.05f, HeightTarget = 40000 });
        var h = tile.Height16((32 * res) + 32);
        Assert.True(h > 10000 && h <= 40000);
    }

    [Fact]
    public void Paint_Moves_Toward_Target_Independent_Of_Strength()
    {
        const int res = 64;
        var weak = Flat(res, 10000);
        var strong = Flat(res, 10000);
        TerrainBrush.Apply(weak, 32, 32, 6, new BrushSettings { Kind = TerrainBrushKind.Paint, Strength = 0.001f, HeightTarget = 50000 });
        TerrainBrush.Apply(strong, 32, 32, 6, new BrushSettings { Kind = TerrainBrushKind.Paint, Strength = 0.5f, HeightTarget = 50000 });

        // Paint blends by falloff only, so strength makes no difference.
        Assert.Equal(weak.Height16((32 * res) + 32), strong.Height16((32 * res) + 32));
        Assert.True(weak.Height16((32 * res) + 32) > 10000);
    }

    [Fact]
    public void Smooth_Reduces_A_Spike()
    {
        const int res = 64;
        var tile = Flat(res, 10000);
        var idx = (32 * res) + 32;
        tile.R[idx] = 60000 >> 8;
        tile.G[idx] = 60000 & 0xFF;
        tile.RecalcStats();

        TerrainBrush.Apply(tile, 32, 32, 6, new BrushSettings { Kind = TerrainBrushKind.Smooth, Strength = 0.5f });
        Assert.True(tile.Height16(idx) < 60000); // blended toward the lower neighbours
    }

    [Fact]
    public void Noise_Perturbs_Pixels_Within_Radius()
    {
        const int res = 64;
        var tile = Flat(res, 30000);
        var noise = FbmNoise.Build(res, 32, seed: 1);
        var changed = TerrainBrush.Apply(tile, 32, 32, 8, new BrushSettings { Kind = TerrainBrushKind.Noise, Strength = 0.3f, NoiseScale = 32 }, noise);

        Assert.NotEmpty(changed);
        Assert.Contains(changed, i => tile.Height16(i) != 30000);
    }

    [Fact]
    public void Veg_And_Water_Set_Alpha_Bits()
    {
        const int res = 32;
        var tile = Flat(res, 0);
        var idx = (16 * res) + 16;

        TerrainBrush.Apply(tile, 16, 16, 4, new BrushSettings { Mode = TerrainEditMode.Veg, VegPreset = 5 });
        Assert.Equal(5, (tile.A[idx] >> 4) & 0x7);

        TerrainBrush.Apply(tile, 16, 16, 4, new BrushSettings { Mode = TerrainEditMode.Water });
        Assert.Equal(1, (tile.A[idx] >> 7) & 1);

        TerrainBrush.Apply(tile, 16, 16, 4, new BrushSettings { Mode = TerrainEditMode.Water, Erase = true });
        Assert.Equal(0, (tile.A[idx] >> 7) & 1);
    }

    [Fact]
    public void ErodeThermal_Lowers_A_Peak()
    {
        const int res = 64;
        var tile = Flat(res, 10000);
        var idx = (32 * res) + 32;
        const int tall = 40000;
        tile.R[idx] = tall >> 8;
        tile.G[idx] = tall & 0xFF;
        tile.RecalcStats();

        TerrainBrush.Apply(tile, 32, 32, 6, new BrushSettings { Kind = TerrainBrushKind.ErodeThermal, Strength = 0.05f });
        Assert.True(tile.Height16(idx) < tall); // talus transfer moved material off the peak
    }
}
