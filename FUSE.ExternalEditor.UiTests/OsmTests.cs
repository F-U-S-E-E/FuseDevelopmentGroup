using Fuse.ExternalEditor.Logic;
using Xunit;

namespace Fuse.ExternalEditor.UiTests;

/// <summary>Pure OSM slippy-map math (no network).</summary>
public class OsmTests
{
    [Fact]
    public void Deg2Tile_And_Tile2Deg_Known_Center()
    {
        // At zoom 1 the world is 2x2 tiles; (0,0) lat/lon sits at the (1,1) corner.
        Assert.Equal((1, 1), OsmTileMath.Deg2Tile(0.0, 0.0, 1));

        var (lat, lon) = OsmTileMath.Tile2Deg(1, 1, 1);
        Assert.True(System.Math.Abs(lat) < 1e-9);
        Assert.True(System.Math.Abs(lon) < 1e-9);
    }

    [Fact]
    public void Tile_Brackets_The_Source_Point()
    {
        // The tile a point falls in must have its NW corner north-west of the
        // point and its SE corner (next tile's NW) south-east of it.
        const double lat = 35.382614;
        const double lon = -83.49541;
        const int zoom = 15;

        var (tx, ty) = OsmTileMath.Deg2Tile(lat, lon, zoom);
        var nw = OsmTileMath.Tile2Deg(tx, ty, zoom);
        var se = OsmTileMath.Tile2Deg(tx + 1, ty + 1, zoom);

        Assert.True(nw.Lat >= lat && lat >= se.Lat, "latitude must fall within the tile");
        Assert.True(nw.Lon <= lon && lon <= se.Lon, "longitude must fall within the tile");
    }
}
