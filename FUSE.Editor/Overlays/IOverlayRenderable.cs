using UnityEngine;

namespace FUSE.Editor.Overlays
{
    /// <summary>
    /// Interface for objects that can be rendered in the editor overlay.
    /// Implementations provide the mesh and material data needed to display a preview.
    /// </summary>
    public interface IOverlayRenderable
    {
        /// <summary>
        /// Gets the mesh to render for this object.
        /// </summary>
        /// <returns>A shared or generated mesh, or null if the object has no mesh.</returns>
        Mesh GetOverlayMesh(object Entity, object FuseData);

        /// <summary>
        /// Gets the material to use when rendering the overlay preview.
        /// This should typically be a wireframe, ghost, or outline material.
        /// </summary>
        /// <returns>A material suitable for overlay rendering.</returns>
        Material GetOverlayMaterial(object Entity, object FuseData);

        /// <summary>
        /// Gets the bounds of the object for culling and size estimation.
        /// </summary>
        Bounds GetObjectBounds(object Entity, object FuseData);
    }
}
