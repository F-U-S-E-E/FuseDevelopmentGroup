using UnityEngine;

namespace FUSE.Editor.Overlays
{
    /// <summary>
    /// Represents a selectable area within an overlay preview.
    /// Provides hit detection and bounds information for mouse interaction.
    /// </summary>
    public class OverlaySelectionArea
    {
        /// <summary>
        /// Unique identifier for this selection area within its preview.
        /// </summary>
        public string AreaId { get; set; }

        /// <summary>
        /// The preview this area belongs to.
        /// </summary>
        public string PreviewId { get; set; }

        /// <summary>
        /// Bounds of this selection area (in world space).
        /// </summary>
        public Bounds Bounds { get; set; }

        /// <summary>
        /// Optional mesh for more precise hit detection.
        /// If null, spherical bounds will be used.
        /// </summary>
        public Mesh SelectionMesh { get; set; }

        /// <summary>
        /// Transform matrix for the selection area (includes position, rotation, scale).
        /// </summary>
        public Matrix4x4 Transform { get; set; }

        /// <summary>
        /// Whether this area is currently selectable.
        /// </summary>
        public bool IsSelectable { get; set; } = true;

        /// <summary>
        /// Custom data associated with this selection area (handler-specific).
        /// Can be used to store any additional context needed for selection handling.
        /// </summary>
        public object SelectionData { get; set; }

        /// <summary>
        /// Optional color override for this selection area when highlighted.
        /// If null, default highlight color will be used.
        /// </summary>
        public Color? HighlightColor { get; set; }

        public OverlaySelectionArea()
        {
            AreaId = System.Guid.NewGuid().ToString().Substring(0, 8);
        }

        /// <summary>
        /// Checks if a world space point is within this selection area.
        /// </summary>
        public bool ContainsPoint(Vector3 worldPoint)
        {
            // Transform point to local space for bounds check
            var localPoint = Transform.inverse.MultiplyPoint(worldPoint);
            return Bounds.Contains(localPoint);
        }

        /// <summary>
        /// Performs a raycast against this selection area.
        /// </summary>
        public bool Raycast(Ray ray, out float distance)
        {
            distance = 0f;

            // Transform ray to local space
            var origin = Transform.inverse.MultiplyPoint(ray.origin);
            var direction = Transform.inverse.MultiplyVector(ray.direction).normalized;
            var localRay = new Ray(origin, direction);

            if (SelectionMesh != null)
            {
                // Use mesh-based collision if available
                return Physics.Raycast(localRay, out var hit, 1000f) && 
                       Bounds.Contains(hit.point);
            }

            // Use sphere bounds intersection
            return Bounds.IntersectRay(localRay, out distance);
        }
    }
}
