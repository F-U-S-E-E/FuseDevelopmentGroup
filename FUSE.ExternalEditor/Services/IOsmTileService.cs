using System.Threading;
using System.Threading.Tasks;

namespace Fuse.ExternalEditor.Services;

/// <summary>A stitched OSM raster mosaic plus the geo bounds it covers (for alignment).</summary>
public sealed class OsmMosaic
{
    public OsmMosaic(byte[] rgba, int width, int height, int tileCount, double northLat, double westLon, double southLat, double eastLon)
    {
        Rgba = rgba;
        Width = width;
        Height = height;
        TileCount = tileCount;
        NorthLat = northLat;
        WestLon = westLon;
        SouthLat = southLat;
        EastLon = eastLon;
    }

    public byte[] Rgba { get; }
    public int Width { get; }
    public int Height { get; }
    public int TileCount { get; }
    public double NorthLat { get; }
    public double WestLon { get; }
    public double SouthLat { get; }
    public double EastLon { get; }
}

/// <summary>Fetches + stitches OpenStreetMap raster tiles covering a lat/lon bounding box.</summary>
public interface IOsmTileService
{
    Task<OsmMosaic> FetchAsync(double minLat, double minLon, double maxLat, double maxLon, int zoom, CancellationToken ct = default);
}
