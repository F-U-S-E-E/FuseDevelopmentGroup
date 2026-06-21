using FUSE.Editor.Overlays.Discovery;
using System;
using System.Collections.Generic;
using Track;
using UnityEngine;

namespace FUSE.Editor.Track.Overlays.Discovery
{
    /// <summary>
    /// Discovery strategy for TrackNode objects.
    /// 
    /// Implement the <see cref="DiscoverObjects"/> method to define how TrackNodes
    /// are discovered and made available for overlay rendering.
    /// 
    /// Example scenarios:
    /// - Find TrackNodes in a scene
    /// - Find TrackNodes in a specific area/radius
    /// - Find TrackNodes with specific properties
    /// - Find TrackNodes belonging to an active mod
    /// </summary>
    public class TrackNodeDiscoveryStrategy : IOverlayDiscoveryStrategy
    {
        /// <summary>
        /// Unique identifier for this discovery strategy.
        /// </summary>
        public string StrategyName => "TrackNodes";

        /// <summary>
        /// Execution order for this strategy.
        /// Lower values execute first. Adjust to place this discovery before/after other strategies.
        /// </summary>
        public int ExecutionOrder => 20;

        /// <summary>
        /// Discovers TrackNode objects that should have overlays.
        /// 
        /// TODO: Implement your discovery logic here.
        /// 
        /// Example implementation patterns:
        /// 
        /// 1. Find all TrackNodes in scene:
        ///    var nodes = Object.FindObjectsOfType<TrackNode>();
        /// 
        /// 2. Find nearby TrackNodes:
        ///    var colliders = Physics.OverlapSphere(focalPoint, searchRadius);
        ///    Extract TrackNodes from colliders
        /// 
        /// 3. Find TrackNodes in a specific mod:
        ///    Query your mod system for TrackNodes
        /// 
        /// 4. Find TrackNodes by property:
        ///    Filter by state, type, flags, etc.
        /// 
        /// For each TrackNode you want to discover, yield:
        /// 
        ///     yield return new DiscoveredOverlayObject
        ///     {
        ///         Entity = trackNode,
        ///         HasPendingEdits = CheckIfHasPendingEdits(trackNode),  // Optional
        ///         PreviewData = GetPendingEditData(trackNode),           // Optional
        ///         ObjectId = GenerateUniqueId(trackNode),               // Must be unique
        ///         Priority = CalculatePriority(trackNode),             // 0-1, higher = render first
        ///         Distance = CalculateDistance(trackNode),             // Distance from focal point
        ///         SourceStrategy = StrategyName
        ///     };
        /// </summary>
        /// <returns>Collection of discovered TrackNode objects wrapped with overlay metadata.</returns>

        public IEnumerable<DiscoveredOverlayObject> DiscoverObjects()
        {
            var focalPoint = Camera.main.transform.position;
            var trackNodes = GameObject.FindObjectsOfType<TrackNode>();
            const float searchRadius = 500f;

            foreach (var trackNode in trackNodes)
            {
                if (trackNode == null) continue;

                var distance = Vector3.Distance(trackNode.transform.position, focalPoint);
                if (distance > searchRadius) continue;  // Skip too far away

                yield return new DiscoveredOverlayObject
                {
                    Entity = trackNode,
                    HasPendingEdits = true,
                    PreviewData = FUSE.Runtime.API.TrackAPI.GetDefinition(trackNode),
                    ObjectId = trackNode.id,
                    Priority = 1.0f - (distance / searchRadius),  // Closer = higher priority
                    Distance = distance,
                    SourceStrategy = StrategyName
                };
            }
        }

        /// <summary>
        /// Called when this discovery strategy is registered and enabled.
        /// Use this for initialization (caching, subscribing to events, etc).
        /// </summary>
        public void OnEnable()
        {
            // TODO: Optional - Add initialization logic if needed
        }

        /// <summary>
        /// Called when this discovery strategy is unregistered or disabled.
        /// Use this for cleanup (unsubscribing from events, clearing caches, etc).
        /// </summary>
        public void OnDisable()
        {
            // TODO: Optional - Add cleanup logic if needed
        }
    }
}
