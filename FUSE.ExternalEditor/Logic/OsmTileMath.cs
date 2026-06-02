using System;

namespace Fuse.ExternalEditor.Logic;

/// <summary>
/// OpenStreetMap slippy-map tile math (Web Mercator), ported from
/// <c>edit_tiles/osm.py</c> <c>osm_deg2tile</c>/<c>osm_tile2deg</c>. Pure and
/// offline; the networked overlay fetch/stitch (which needs the terrain's
/// lat/lon projection) lands with the rest of the geo work in Phase 6.
/// </summary>
public static class OsmTileMath
{
    /// <summary>lat/lon → slippy tile (x, y) at the given zoom.</summary>
    public static (int X, int Y) Deg2Tile(double latDeg, double lonDeg, int zoom)
    {
        var latR = latDeg * Math.PI / 180.0;
        var n = Math.Pow(2, zoom);
        var x = (int)((lonDeg + 180.0) / 360.0 * n);
        var y = (int)((1.0 - (Math.Log(Math.Tan(latR) + (1.0 / Math.Cos(latR))) / Math.PI)) / 2.0 * n);
        return (x, y);
    }

    /// <summary>slippy tile (x, y) → its NW-corner lat/lon at the given zoom.</summary>
    public static (double Lat, double Lon) Tile2Deg(int x, int y, int zoom)
    {
        var n = Math.Pow(2, zoom);
        var lon = (x / n * 360.0) - 180.0;
        var latR = Math.Atan(Math.Sinh(Math.PI * (1 - (2.0 * y / n))));
        var lat = latR * 180.0 / Math.PI;
        return (lat, lon);
    }
}
