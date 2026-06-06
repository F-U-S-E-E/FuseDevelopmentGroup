using System;
using Fuse.ExternalEditor.Models.Terrain;

namespace Fuse.ExternalEditor.Rendering;

/// <summary>
/// 2D top-down map transform, ported from the Python editor's pan/zoom model
/// (<c>app.py</c> <c>_zoom_at</c> + the tile placement in <c>renderer._draw_terrain</c>).
/// <see cref="Zoom"/> is screen pixels per world pixel (clamped [0.05, 50]); a
/// tile spans <see cref="TerrainConstants.TileStride"/> world pixels, so its
/// on-screen size is <see cref="TileScreenSize"/>. <see cref="MinX"/>/<see cref="MaxY"/>
/// are the grid origin (screen row grows downward, world tile-y grows upward).
/// </summary>
public sealed class ViewTransform
{
    public const double MinZoom = 0.05;
    public const double MaxZoom = 50.0;

    public double Zoom { get; set; } = 1.0;
    public double PanX { get; set; }
    public double PanY { get; set; }
    public int MinX { get; set; }
    public int MaxY { get; set; }

    public double TileScreenSize => Zoom * TerrainConstants.TileScreenBase;

    /// <summary>Screen-space top-left of tile (tx, ty).</summary>
    public (double X, double Y) TileTopLeft(int tx, int ty)
    {
        var ts = TileScreenSize;
        return ((tx - MinX) * ts + PanX, (MaxY - ty) * ts + PanY);
    }

    /// <summary>
    /// World metres (x east, z north) → screen, consistent with tile placement
    /// (Python <c>unity_to_screen</c>). World-z grows north = screen-y up, hence
    /// the +1 row offset relative to a tile's top edge.
    /// </summary>
    public (double X, double Y) WorldToScreen(double worldX, double worldZ)
    {
        var ts = TileScreenSize;
        var tx = worldX / TerrainConstants.UnityTileMeters;
        var tz = worldZ / TerrainConstants.UnityTileMeters;
        return ((tx - MinX) * ts + PanX, (MaxY - tz + 1) * ts + PanY);
    }

    /// <summary>Inverse of <see cref="WorldToScreen"/>.</summary>
    public (double WorldX, double WorldZ) ScreenToWorld(double sx, double sy)
    {
        var ts = TileScreenSize;
        var tx = ((sx - PanX) / ts) + MinX;
        var tz = MaxY + 1 - ((sy - PanY) / ts);
        return (tx * TerrainConstants.UnityTileMeters, tz * TerrainConstants.UnityTileMeters);
    }

    /// <summary>Cursor-anchored zoom — the world point under (sx, sy) stays put.</summary>
    public void ZoomAt(double sx, double sy, double factor)
    {
        var newZoom = Math.Clamp(Zoom * factor, MinZoom, MaxZoom);
        if (newZoom == Zoom)
        {
            return;
        }

        PanX = sx - (sx - PanX) * (newZoom / Zoom);
        PanY = sy - (sy - PanY) * (newZoom / Zoom);
        Zoom = newZoom;
    }

    public void PanBy(double dx, double dy)
    {
        PanX += dx;
        PanY += dy;
    }

    /// <summary>Which tile coordinate the screen point (sx, sy) falls in.</summary>
    public (int Tx, int Ty) ScreenToTile(double sx, double sy)
    {
        var ts = TileScreenSize;
        var tx = MinX + (int)Math.Floor((sx - PanX) / ts);
        var ty = MaxY - (int)Math.Floor((sy - PanY) / ts);
        return (tx, ty);
    }
}
