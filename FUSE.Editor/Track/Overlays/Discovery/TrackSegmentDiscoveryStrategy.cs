using FUSE.Editor.Overlays.Discovery;
using Helpers;
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
    public class TrackSegmentDiscoveryStrategy : IOverlayDiscoveryStrategy
    {
        /// <summary>
        /// Unique identifier for this discovery strategy.
        /// </summary>
        public string StrategyName => "TrackSegments";

        /// <summary>
        /// Execution order for this strategy.
        /// Lower values execute first. Adjust to place this discovery before/after other strategies.
        /// </summary>
        public int ExecutionOrder => 21;

        /// <summary>
        /// Discovers TrackSegment objects that should have overlays.
        /// 
        /// TODO: Implement your discovery logic here.
        /// 
        /// Example implementation patterns:
        /// 
        /// 1. Find all TrackSegments in scene:
        ///    var segments = Object.FindObjectsOfType<TrackSegment>();
        /// 
        /// 2. Find nearby TrackSegments:
        ///    var colliders = Physics.OverlapSphere(focalPoint, searchRadius);
        ///    Extract TrackSegments from colliders
        /// 
        /// 3. Find TrackSegments in a specific mod:
        ///    Query your mod system for TrackSegments
        /// 
        /// 4. Find TrackSegments by property:
        ///    Filter by state, type, flags, etc.
        /// 
        /// For each TrackSegment you want to discover, yield:
        /// 
        ///     yield return new DiscoveredOverlayObject
        ///     {
        ///         Entity = trackSegment,
        ///         HasPendingEdits = CheckIfHasPendingEdits(trackSegment),  // Optional
        ///         PreviewData = GetPendingEditData(trackSegment),           // Optional
        ///         ObjectId = GenerateUniqueId(trackSegment),               // Must be unique
        ///         Priority = CalculatePriority(trackSegment),             // 0-1, higher = render first
        ///         Distance = CalculateDistance(trackSegment),             // Distance from focal point
        ///         SourceStrategy = StrategyName
        ///     };
        /// </summary>
        /// <returns>Collection of discovered TrackSegment objects wrapped with overlay metadata.</returns>

        public IEnumerable<DiscoveredOverlayObject> DiscoverObjects()
        {
            var focalPoint = Camera.main.transform.position;
            var trackSegments = GameObject.FindObjectsOfType<TrackSegment>();
            const float searchRadius = 500f;

            foreach (var trackSegment in trackSegments)
            {
                if (trackSegment == null) continue;

                var distance = Vector3.Distance(WorldTransformer.GameToWorld(trackSegment.Curve.GetPoint(0.5f)), focalPoint);
                if (distance > searchRadius) continue;  // Skip too far away

                yield return new DiscoveredOverlayObject
                {
                    Entity = trackSegment,
                    HasPendingEdits = true,
                    PreviewData = FUSE.Runtime.API.TrackAPI.GetDefinition(trackSegment),
                    ObjectId = trackSegment.id,
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
