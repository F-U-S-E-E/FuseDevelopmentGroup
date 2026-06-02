using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Fuse.ExternalEditor.Logic.Generation;
using Xunit;

namespace Fuse.ExternalEditor.UiTests;

/// <summary>
/// Golden tests: the pure terrain-generation math must match the Python reference
/// (edit_tiles/generate.py) — Mapbox decode, 16-bit pack, the float32 geo offset,
/// web-mercator projection, NLCD colour mapping, bilinear resample, tile naming.
/// Fixture: Fixtures/gen-golden.json (regenerate with tmp_gen6_golden.py).
/// </summary>
public class GenerationGoldenTests
{
    private static JsonElement Root() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "gen-golden.json"))).RootElement;

    [Fact]
    public void Decode_Matches_Python()
    {
        foreach (var e in Root().GetProperty("decode").EnumerateArray())
        {
            var m = MapboxTerrain.DecodeElevation((byte)e.GetProperty("r").GetInt32(), (byte)e.GetProperty("g").GetInt32(), (byte)e.GetProperty("b").GetInt32());
            Assert.True(Math.Abs(m - e.GetProperty("m").GetDouble()) < 1e-6);
        }
    }

    [Fact]
    public void Pack16_Matches_Python()
    {
        foreach (var e in Root().GetProperty("pack").EnumerateArray())
        {
            Assert.Equal(e.GetProperty("u16").GetInt32(), (int)MapboxTerrain.PackHeight16(e.GetProperty("m").GetDouble()));
        }
    }

    [Fact]
    public void AddMeters_Matches_Python()
    {
        foreach (var e in Root().GetProperty("addMeters").EnumerateArray())
        {
            var (lat, lon) = MapboxTerrain.AddMeters(
                e.GetProperty("lat").GetDouble(), e.GetProperty("lon").GetDouble(),
                e.GetProperty("north").GetDouble(), e.GetProperty("east").GetDouble());
            Assert.True(Math.Abs(lat - e.GetProperty("outLat").GetDouble()) < 1e-4);
            Assert.True(Math.Abs(lon - e.GetProperty("outLon").GetDouble()) < 1e-4);
        }
    }

    [Fact]
    public void TileBounds_Matches_Python()
    {
        foreach (var e in Root().GetProperty("tileBounds").EnumerateArray())
        {
            var (mn, mx) = MapboxTerrain.TileBounds(e.GetProperty("gx").GetInt32(), e.GetProperty("gy").GetInt32());
            Assert.True(Math.Abs(mn.Lat - e.GetProperty("minLat").GetDouble()) < 1e-4);
            Assert.True(Math.Abs(mn.Lon - e.GetProperty("minLon").GetDouble()) < 1e-4);
            Assert.True(Math.Abs(mx.Lat - e.GetProperty("maxLat").GetDouble()) < 1e-4);
            Assert.True(Math.Abs(mx.Lon - e.GetProperty("maxLon").GetDouble()) < 1e-4);
        }
    }

    [Fact]
    public void Pixel_Projection_Matches_Python()
    {
        foreach (var e in Root().GetProperty("lonToPx").EnumerateArray())
        {
            Assert.True(Math.Abs(MapboxTerrain.LonToPixelX(e.GetProperty("lon").GetDouble(), 15) - e.GetProperty("px").GetDouble()) < 1e-6);
        }

        foreach (var e in Root().GetProperty("latToPy").EnumerateArray())
        {
            Assert.True(Math.Abs(MapboxTerrain.LatToPixelY(e.GetProperty("lat").GetDouble(), 15) - e.GetProperty("py").GetDouble()) < 1e-6);
        }
    }

    [Fact]
    public void ColorToPreset_Matches_Python()
    {
        foreach (var e in Root().GetProperty("color").EnumerateArray())
        {
            var (preset, water) = NlcdLandcover.ColorToPreset(
                (byte)e.GetProperty("r").GetInt32(), (byte)e.GetProperty("g").GetInt32(), (byte)e.GetProperty("b").GetInt32());
            Assert.Equal(e.GetProperty("preset").GetInt32(), preset);
            Assert.Equal(e.GetProperty("water").GetBoolean(), water);
        }
    }

    [Fact]
    public void Bilinear_Matches_Python()
    {
        var b = Root().GetProperty("bilinear");
        var src = b.GetProperty("src").EnumerateArray().Select(x => (float)x.GetDouble()).ToArray();
        int h = b.GetProperty("h").GetInt32(), w = b.GetProperty("w").GetInt32();
        foreach (var s in b.GetProperty("samples").EnumerateArray())
        {
            var v = Resample.Bilinear(src, h, w, s.GetProperty("y").GetDouble(), s.GetProperty("x").GetDouble());
            Assert.True(Math.Abs(v - s.GetProperty("v").GetDouble()) < 1e-9);
        }
    }

    [Fact]
    public void Filename_Matches_Python()
    {
        foreach (var e in Root().GetProperty("filename").EnumerateArray())
        {
            Assert.Equal(e.GetProperty("name").GetString(), MapboxTerrain.TileDataFilename(e.GetProperty("gx").GetInt32(), e.GetProperty("gy").GetInt32()));
        }
    }
}
