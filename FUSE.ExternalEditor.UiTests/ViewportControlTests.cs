using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Fuse.ExternalEditor.Controls;
using Fuse.ExternalEditor.Models.Terrain;
using Fuse.ExternalEditor.Rendering;
using Xunit;

namespace Fuse.ExternalEditor.UiTests;

/// <summary>
/// Headless tests for the viewport control + bitmap cache. These need the
/// Avalonia platform (WriteableBitmap, control hosting), hence [AvaloniaFact].
/// </summary>
public class ViewportControlTests
{
    private static TerrainTile MakeTile(int x, int y, int res)
    {
        var n = res * res;
        var r = new byte[n];
        var g = new byte[n];
        var a = new byte[n];
        for (var i = 0; i < n; i++)
        {
            r[i] = (byte)(i % 256);
            g[i] = (byte)((i * 7) % 256);
            a[i] = (byte)((i % 2) << 7); // alternate water
        }

        return new TerrainTile(x, y, res, r, g, a);
    }

    [AvaloniaFact]
    public void TileBitmapCache_Builds_Correctly_Sized_Bitmap_And_Caches()
    {
        var cache = new TileBitmapCache();
        var tile = MakeTile(0, 0, 8);

        var first = cache.GetOrCreate(tile, TerrainMode.Height, hillshade: false);
        var second = cache.GetOrCreate(tile, TerrainMode.Height, hillshade: false);

        Assert.Equal(new PixelSize(8, 8), first.PixelSize);
        Assert.Same(first, second); // cached, not rebuilt

        var shaded = cache.GetOrCreate(tile, TerrainMode.Height, hillshade: true);
        Assert.NotSame(first, shaded); // different key
    }

    [AvaloniaFact]
    public void MapViewport_Hosts_Grid_And_Reacts_To_Property_Changes()
    {
        var grid = new TileGrid();
        grid.Add(MakeTile(0, 0, 16));

        var viewport = new MapViewport { TileGrid = grid };
        var window = new Window { Width = 400, Height = 300, Content = viewport };

        window.Show();

        Assert.True(viewport.Bounds.Width > 0, "viewport should be laid out once shown");

        // Property changes must not throw (they invalidate render / rebuild cache).
        viewport.Mode = TerrainMode.Veg;
        viewport.Hillshade = true;

        var grid2 = new TileGrid();
        grid2.Add(MakeTile(0, 0, 16));
        grid2.Add(MakeTile(1, 0, 16));
        viewport.TileGrid = grid2;

        Assert.Equal(2, viewport.TileGrid!.Count);
    }
}
