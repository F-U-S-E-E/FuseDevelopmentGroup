using UnityEngine;

namespace FUSE.Editor.Overlays.Discovery
{
    /// <summary>
    /// Configuration for overlay discovery culling and LOD behavior.
    /// </summary>
    public class OverlayDiscoveryCullingConfig
    {
        /// <summary>
        /// Maximum number of overlays to render simultaneously.
        /// Objects will be culled in distance order if exceeded.
        /// </summary>
        public int MaxOverlayCount { get; set; } = 100;

        /// <summary>
        /// Maximum distance from focal point for overlay discovery.
        /// Objects beyond this distance are ignored.
        /// </summary>
        public float MaxDiscoveryDistance { get; set; } = 500f;

        /// <summary>
        /// How frequently to run discovery updates, in seconds.
        /// Lower values = more responsive but more CPU cost.
        /// </summary>
        public float DiscoveryUpdateInterval { get; set; } = 1.0f;

        /// <summary>
        /// Once a preview is registered, this is the maximum staleness before removal.
        /// Prevents memory leaks from dead overlays.
        /// Set to 0 to disable staleness checking.
        /// </summary>
        public float MaxPreviewStaleness { get; set; } = 10.0f;

        /// <summary>
        /// Priority threshold for LOD: overlays below this priority are rendered at lower detail.
        /// Set to 0 to disable LOD (render all at full detail).
        /// </summary>
        public float LODPriorityThreshold { get; set; } = 1.0f;

        /// <summary>
        /// Distance-based LOD: if object is farther than focal point by this amount,
        /// use lower LOD rendering.
        /// </summary>
        public float LODDistanceThreshold { get; set; } = 100f;

        /// <summary>
        /// Whether to sort discovered objects by priority (distance) before culling.
        /// </summary>
        public bool SortByPriority { get; set; } = true;

        /// <summary>
        /// Whether to enable frustum culling for overlays.
        /// Overlays outside camera view are skipped.
        /// </summary>
        public bool EnableFrustumCulling { get; set; } = true;

        /// <summary>
        /// Focal point for distance calculations (usually player/camera position).
        /// </summary>
        public Vector3 FocalPoint { get; set; } = Vector3.zero;

        /// <summary>
        /// Optional camera for frustum culling.
        /// </summary>
        public Camera ReferenceCamera { get; set; }
    }
}
