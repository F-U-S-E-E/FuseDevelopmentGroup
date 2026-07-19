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
    public class TrackSpanDiscoveryStrategy : IOverlayDiscoveryStrategy
    {
        /// <summary>
        /// Unique identifier for this discovery strategy.
        /// </summary>
        public string StrategyName => "TrackSpans";

        /// <summary>
        /// Execution order for this strategy.
        /// Lower values execute first. Adjust to place this discovery before/after other strategies.
        /// </summary>
        public int ExecutionOrder => 20;

        /// <summary>
        /// Discovers TrackSpan objects that should have overlays.
        /// 
        /// TODO: Implement your discovery logic here.
        /// 
        /// Example implementation patterns:
        /// 
        /// 1. Find all TrackSpans in scene:
        ///    var spans = Object.FindObjectsOfType<TrackSpan>();
        /// 
        /// 2. Find nearby TrackSpans:
        ///    var colliders = Physics.OverlapSphere(focalPoint, searchRadius);
        ///    Extract TrackSpans from colliders
        /// 
        /// 3. Find TrackSpans in a specific mod:
        ///    Query your mod system for TrackSpans
        /// 
        /// 4. Find TrackSpans by property:
        ///    Filter by state, type, flags, etc.
        /// 
        /// For each TrackSpan you want to discover, yield:
        /// 
        ///     yield return new DiscoveredOverlayObject
        ///     {
        ///         Entity = trackSpan,
        ///         HasPendingEdits = CheckIfHasPendingEdits(trackSpan),  // Optional
        ///         PreviewData = GetPendingEditData(trackSpan),           // Optional
        ///         ObjectId = GenerateUniqueId(trackSpan),               // Must be unique
        ///         Priority = CalculatePriority(trackSpan),             // 0-1, higher = render first
        ///         Distance = CalculateDistance(trackSpan),             // Distance from focal point
        ///         SourceStrategy = StrategyName
        ///     };
        /// </summary>
        /// <returns>Collection of discovered TrackSpan objects wrapped with overlay metadata.</returns>

        public IEnumerable<DiscoveredOverlayObject> DiscoverObjects()
        {
            var focalPoint = Camera.main.transform.position;
            var trackSpans = GameObject.FindObjectsOfType<TrackSpan>();
            const float searchRadius = 1500f;

            foreach (var trackSpan in trackSpans)
            {
                if (trackSpan == null) continue;

                var distance = Vector3.Distance(WorldTransformer.GameToWorld(trackSpan.GetCenterPoint()), focalPoint);
                if (distance > searchRadius) continue;  // Skip too far away

                yield return new DiscoveredOverlayObject
                {
                    Entity = trackSpan,
                    HasPendingEdits = true,
                    PreviewData = FUSE.Runtime.API.TrackAPI.GetDefinition(trackSpan),
                    ObjectId = trackSpan.id,
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
