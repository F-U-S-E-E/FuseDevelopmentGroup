using System;
using System.IO;
using Fuse.ExternalEditor.Logic;
using Fuse.ExternalEditor.Models.Terrain;
using Fuse.ExternalEditor.Rendering;
using Fuse.ExternalEditor.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Fuse.ExternalEditor.UiTests;

/// <summary>
/// Pure-logic tests for the Phase 1 terrain core (transform, tile stats, decode,
/// renderer). No Avalonia UI thread needed, so these are plain [Fact]s.
/// </summary>
public class TerrainTests
{
    // ---- ViewTransform (pan/zoom, tile placement) ----

    [Fact]
    public void ZoomAt_Anchors_On_Cursor_And_Clamps()
    {
        var vt = new ViewTransform { Zoom = 1.0, PanX = 0, PanY = 0 };

        vt.ZoomAt(100, 100, 2.0);
        Assert.Equal(2.0, vt.Zoom, 6);
        Assert.Equal(-100.0, vt.PanX, 6); // 100 - (100-0)*(2/1)
        Assert.Equal(-100.0, vt.PanY, 6);

        var hi = new ViewTransform { Zoom = 40 };
        hi.ZoomAt(0, 0, 2.0);
        Assert.Equal(ViewTransform.MaxZoom, hi.Zoom, 6); // clamp 80 -> 50

        var lo = new ViewTransform { Zoom = 0.06 };
        lo.ZoomAt(0, 0, 0.5);
        Assert.Equal(ViewTransform.MinZoom, lo.Zoom, 6); // clamp 0.03 -> 0.05
    }

    [Fact]
    public void TileTopLeft_And_ScreenToTile_Are_Consistent()
    {
        var vt = new ViewTransform { Zoom = 1.0, PanX = 0, PanY = 0, MinX = 2, MaxY = 5 };

        Assert.Equal((0.0, 0.0), vt.TileTopLeft(2, 5));
        Assert.Equal((64.0, 0.0), vt.TileTopLeft(3, 5)); // ts = 64 at zoom 1
        Assert.Equal((0.0, 64.0), vt.TileTopLeft(2, 4)); // tile-y down = screen-y up

        Assert.Equal((2, 5), vt.ScreenToTile(10, 10));
        Assert.Equal((3, 5), vt.ScreenToTile(70, 10));
        Assert.Equal((2, 4), vt.ScreenToTile(10, 70));
    }

    [Fact]
    public void WorldToScreen_RoundTrips_Through_ScreenToWorld()
    {
        var vt = new ViewTransform { Zoom = 2.0, PanX = 30, PanY = -15, MinX = 1, MaxY = 4 };

        var (sx, sy) = vt.WorldToScreen(1234.5, -678.25);
        var (wx, wz) = vt.ScreenToWorld(sx, sy);

        Assert.True(System.Math.Abs(wx - 1234.5) < 1e-6);
        Assert.True(System.Math.Abs(wz - (-678.25)) < 1e-6);
    }

    // ---- TerrainTile (height decode + stats) ----

    [Fact]
    public void TerrainTile_Computes_Height_And_Preset_Stats()
    {
        // 2x2: heights 0, 65280, 255, 65535; water on idx1+idx3; presets 0,0,2,3
        var r = new byte[] { 0, 255, 0, 255 };
        var g = new byte[] { 0, 0, 255, 255 };
        var a = new byte[] { 0, 1 << 7, 2 << 4, (1 << 7) | (3 << 4) };

        var tile = new TerrainTile(7, 9, 2, r, g, a);

        Assert.Equal(0, tile.Height16(0));
        Assert.Equal(65535, tile.Height16(3));
        Assert.True(Math.Abs(tile.MinM - 500f) < 0.01f);
        Assert.True(Math.Abs(tile.MaxM - 1500f) < 0.01f);
        Assert.Equal(2, tile.Presets[0]); // idx0 + idx1
        Assert.Equal(1, tile.Presets[2]);
        Assert.Equal(1, tile.Presets[3]);
        Assert.Equal(0, tile.DomPreset);
        Assert.True(Math.Abs(tile.WaterPct - 50f) < 0.01f);
    }

    // ---- TerrainRenderer (mode math, no hillshade = deterministic) ----

    [Fact]
    public void Render_Height_Mode_Passes_Through_Without_Hillshade()
    {
        var tile = new TerrainTile(0, 0, 1, new byte[] { 100 }, new byte[] { 50 }, new byte[] { 0 });
        var px = TerrainRenderer.Render(tile, TerrainMode.Height, hillshade: false);
        Assert.Equal(new byte[] { 100, 50, 0, 255 }, px);
    }

    [Fact]
    public void Render_Water_And_Veg_Modes()
    {
        var water = new TerrainTile(0, 0, 1, new byte[] { 0 }, new byte[] { 0 }, new byte[] { 1 << 7 });
        Assert.Equal(new byte[] { 0, 100, 220, 255 }, TerrainRenderer.Render(water, TerrainMode.Water, false));

        var land = new TerrainTile(0, 0, 1, new byte[] { 0 }, new byte[] { 0 }, new byte[] { 0 });
        Assert.Equal(new byte[] { 18, 22, 30, 255 }, TerrainRenderer.Render(land, TerrainMode.Water, false));

        var veg2 = new TerrainTile(0, 0, 1, new byte[] { 0 }, new byte[] { 0 }, new byte[] { 2 << 4 });
        var (vr, vg, vb) = TerrainRenderer.VegColors[2];
        Assert.Equal(new byte[] { vr, vg, vb, 255 }, TerrainRenderer.Render(veg2, TerrainMode.Veg, false));
    }

    [Fact]
    public void Hillshade_Darkens_Flat_Tile_Uniformly()
    {
        // Flat tile: every pixel same height -> uniform shade < 1 (light not straight down).
        var n = 4 * 4;
        var r = new byte[n];
        var g = new byte[n];
        var a = new byte[n];
        for (var i = 0; i < n; i++) { r[i] = 100; g[i] = 50; }
        var tile = new TerrainTile(0, 0, 4, r, g, a);

        var px = TerrainRenderer.Render(tile, TerrainMode.Height, hillshade: true);

        // All pixels identical, and shaded below the un-shaded value (100).
        for (var i = 0; i < n; i++)
        {
            Assert.Equal(px[0], px[i * 4]);
            Assert.True(px[i * 4] > 0 && px[i * 4] < 100);
        }
    }

    // ---- TerrainTileService (ImageSharp decode of tile_X_Y.data) ----

    [Fact]
    public void LoadTile_Parses_Coords_And_Channels()
    {
        var dir = Path.Combine(Path.GetTempPath(), "fuse-tiletest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "tile_001_002.data");
            using (var img = new Image<Rgba32>(4, 4))
            {
                img[0, 0] = new Rgba32(10, 20, 0, 200);
                img[3, 3] = new Rgba32(40, 50, 0, 128);
                img.SaveAsPng(path);
            }

            var service = new TerrainTileService();
            var tile = service.LoadTile(path);

            Assert.NotNull(tile);
            Assert.Equal(1, tile!.X);
            Assert.Equal(2, tile.Y);
            Assert.Equal(4, tile.Res);
            Assert.Equal((byte)10, tile.R[0]);
            Assert.Equal((byte)20, tile.G[0]);
            Assert.Equal((byte)200, tile.A[0]);
            Assert.Equal((byte)40, tile.R[(3 * 4) + 3]);
            Assert.Equal((byte)128, tile.A[(3 * 4) + 3]);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void LoadTile_Parses_Negative_Padded_Coordinates()
    {
        var dir = Path.Combine(Path.GetTempPath(), "fuse-tiletest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "tile_-066_-098.data");
            using (var img = new Image<Rgba32>(2, 2))
            {
                img.SaveAsPng(path);
            }

            var tile = new TerrainTileService().LoadTile(path);

            Assert.NotNull(tile);
            Assert.Equal(-66, tile!.X);
            Assert.Equal(-98, tile.Y);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
