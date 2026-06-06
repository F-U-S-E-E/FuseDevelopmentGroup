using Fuse.Core.Model;
using Fuse.ExternalEditor.Rendering;

namespace Fuse.ExternalEditor.Logic;

/// <summary>Picks the track node nearest a screen point (within a pixel radius).</summary>
public static class TrackHitTest
{
    public static string? NearestNode(ViewTransform view, FuseTrackDefinition tracks, double screenX, double screenY, double radiusPx)
    {
        string? best = null;
        var bestDistSq = radiusPx * radiusPx;

        foreach (var (id, node) in tracks.Nodes)
        {
            if (node is null)
            {
                continue;
            }

            var (px, py) = view.WorldToScreen(node.Position.x, node.Position.z);
            var dx = px - screenX;
            var dy = py - screenY;
            var distSq = (dx * dx) + (dy * dy);
            if (distSq <= bestDistSq)
            {
                bestDistSq = distSq;
                best = id;
            }
        }

        return best;
    }
}
