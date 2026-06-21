using System;
using System.Collections.Generic;

namespace FUSE.Editor.Overlays.Discovery
{
    /// <summary>
    /// Interface for strategies that discover objects (GameObjects or Components)
    /// in the scene that should have overlays rendered.
    /// 
    /// Implementations can discover objects with pending edits, nearby objects,
    /// specific component types, or any other criteria.
    /// </summary>
    public interface IOverlayDiscoveryStrategy
    {
        /// <summary>
        /// Unique identifier for this discovery strategy.
        /// Used for logging, debugging, and strategy management.
        /// </summary>
        string StrategyName { get; }

        /// <summary>
        /// Execution order for applying this discovery strategy.
        /// Lower values execute first. Allows prioritization of strategies.
        /// </summary>
        int ExecutionOrder { get; }

        /// <summary>
        /// Discovers objects that should have overlays.
        /// </summary>
        /// <returns>Collection of discovered objects with their metadata.</returns>
        IEnumerable<DiscoveredOverlayObject> DiscoverObjects();

        /// <summary>
        /// Optional: Called when discovery should clean up resources.
        /// </summary>
        void OnDisable();

        /// <summary>
        /// Optional: Called when discovery is re-enabled.
        /// </summary>
        void OnEnable();
    }

    /// <summary>
    /// Metadata and entity wrapper for a discovered object that should have an overlay.
    /// Supports both GameObjects and Components.
    /// </summary>
    public class DiscoveredOverlayObject
    {
        /// <summary>
        /// The entity being discovered (GameObject or Component).
        /// </summary>
        public object Entity { get; set; }

        /// <summary>
        /// Whether this object has pending edits that need preview visualization.
        /// If false, overlay is for selection/interaction only.
        /// </summary>
        public bool HasPendingEdits { get; set; }

        /// <summary>
        /// Preview/pending-edit data if HasPendingEdits is true.
        /// Null for selection-only overlays.
        /// </summary>
        public object PreviewData { get; set; }

        /// <summary>
        /// Unique identifier for this object in the overlay system.
        /// </summary>
        public string ObjectId { get; set; }

        /// <summary>
        /// Priority/distance metric for culling and LOD decisions.
        /// Higher values = higher priority (will be rendered first).
        /// Used for distance-based culling: objects closer to the focal point get higher priority.
        /// </summary>
        public float Priority { get; set; } = 1.0f;

        /// <summary>
        /// Optional: Distance from focal point (camera, player, etc).
        /// Used for culling and LOD calculations.
        /// </summary>
        public float Distance { get; set; } = 0f;

        /// <summary>
        /// Strategy that discovered this object.
        /// </summary>
        public string SourceStrategy { get; set; }

        /// <summary>
        /// Timestamp when this object was discovered.
        /// Used for staleness detection.
        /// </summary>
        public DateTime DiscoveredAt { get; set; } = DateTime.UtcNow;
    }
}
