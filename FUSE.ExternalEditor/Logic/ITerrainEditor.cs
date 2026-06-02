using Fuse.ExternalEditor.Models.Terrain;

namespace Fuse.ExternalEditor.Logic;

/// <summary>
/// The terrain-editing surface the viewport drives during a brush stroke. Kept
/// minimal and Avalonia-free (the view model implements it): the viewport owns the
/// pan/zoom transform and computes per-tile pixel centres, then feeds dabs here.
/// </summary>
public interface ITerrainEditor
{
    /// <summary>True when a terrain brush is selected (viewport routes pointer input to brushing).</summary>
    bool Active { get; }

    /// <summary>Brush radius in screen pixels (zoom-independent), for the HUD ring + tile-pixel conversion.</summary>
    int BrushScreenRadius { get; }

    void BeginStroke();

    /// <summary>Stamp one dab onto a tile at the given tile-pixel centre/radius.</summary>
    void Dab(TerrainTile tile, double centreRow, double centreCol, int radiusTilePx);

    void EndStroke();
}
