namespace Fuse.ExternalEditor.Rendering;

/// <summary>
/// A fetched OSM raster mosaic ready to draw as a viewport guide overlay: row-major
/// RGBA pixels plus the world-metre rectangle it covers (derived from its geo bounds).
/// The bitmap conversion is done lazily by the viewport that renders it.
/// </summary>
public sealed class OsmOverlay
{
    public OsmOverlay(byte[] rgba, int width, int height, double worldMinX, double worldMaxX, double worldMinZ, double worldMaxZ)
    {
        Rgba = rgba;
        Width = width;
        Height = height;
        WorldMinX = worldMinX;
        WorldMaxX = worldMaxX;
        WorldMinZ = worldMinZ;
        WorldMaxZ = worldMaxZ;
    }

    public byte[] Rgba { get; }
    public int Width { get; }
    public int Height { get; }
    public double WorldMinX { get; }
    public double WorldMaxX { get; }
    public double WorldMinZ { get; }
    public double WorldMaxZ { get; }
}
