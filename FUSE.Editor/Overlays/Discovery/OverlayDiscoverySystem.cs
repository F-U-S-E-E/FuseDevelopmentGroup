using FUSE.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FUSE.Editor.Overlays.Discovery
{
    /// <summary>
    /// System for managing overlay discovery strategies with culling, LOD, and performance optimization.
    /// 
    /// Features:
    /// - Strategy-based object discovery (pluggable discovery logic)
    /// - Throttled discovery updates (configurable interval)
    /// - Distance-based culling (max overlays, max distance)
    /// - Frustum culling (camera view filtering)
    /// - LOD (Level of Detail) support via priority system
    /// - Staleness detection and cleanup
    /// - Performance monitoring
    /// </summary>
    public class OverlayDiscoverySystem
    {
        private readonly List<IOverlayDiscoveryStrategy> _strategies = new List<IOverlayDiscoveryStrategy>();
        private readonly Dictionary<string, DiscoveredOverlayObject> _discoveredObjects = 
            new Dictionary<string, DiscoveredOverlayObject>();
        private readonly Dictionary<string, DateTime> _previewCreationTimes = 
            new Dictionary<string, DateTime>();

        private OverlayDiscoveryCullingConfig _cullingConfig;
        private float _lastDiscoveryTime = 0f;
        private int _lastDiscoveryCount = 0;
        private float _lastFrameDiscoveryTime = 0f;

        public OverlayDiscoverySystem(OverlayDiscoveryCullingConfig cullingConfig = null)
        {
            _cullingConfig = cullingConfig ?? new OverlayDiscoveryCullingConfig();
        }

        /// <summary>
        /// Gets the current culling configuration.
        /// </summary>
        public OverlayDiscoveryCullingConfig CullingConfig => _cullingConfig;

        /// <summary>
        /// Sets a new culling configuration.
        /// </summary>
        public void SetCullingConfig(OverlayDiscoveryCullingConfig config)
        {
            _cullingConfig = config ?? new OverlayDiscoveryCullingConfig();
        }

        /// <summary>
        /// Registers a discovery strategy.
        /// </summary>
        public void RegisterStrategy(IOverlayDiscoveryStrategy strategy)
        {
            if (strategy == null)
            {
                FuseLog.Error("OverlayDiscoverySystem: Cannot register null strategy.");
                return;
            }

            if (_strategies.Any(s => s.StrategyName == strategy.StrategyName))
            {
                FuseLog.Warning($"OverlayDiscoverySystem: Strategy '{strategy.StrategyName}' already registered. Replacing.");
                UnregisterStrategy(strategy.StrategyName);
            }

            _strategies.Add(strategy);
            _strategies.Sort((a, b) => a.ExecutionOrder.CompareTo(b.ExecutionOrder));
            strategy.OnEnable();

            FuseLog.Info($"OverlayDiscoverySystem: Registered strategy '{strategy.StrategyName}' (order: {strategy.ExecutionOrder})");
        }

        /// <summary>
        /// Unregisters a discovery strategy by name.
        /// </summary>
        public void UnregisterStrategy(string strategyName)
        {
            var strategy = _strategies.FirstOrDefault(s => s.StrategyName == strategyName);
            if (strategy != null)
            {
                strategy.OnDisable();
                _strategies.Remove(strategy);
                FuseLog.Info($"OverlayDiscoverySystem: Unregistered strategy '{strategyName}'");
            }
        }

        /// <summary>
        /// Gets a registered strategy by name.
        /// </summary>
        public IOverlayDiscoveryStrategy GetStrategy(string strategyName)
        {
            return _strategies.FirstOrDefault(s => s.StrategyName == strategyName);
        }

        /// <summary>
        /// Gets all registered strategies (read-only).
        /// </summary>
        public IReadOnlyList<IOverlayDiscoveryStrategy> GetStrategies()
        {
            return _strategies.AsReadOnly();
        }

        /// <summary>
        /// Performs object discovery with throttling and culling.
        /// Call this periodically (e.g., once per frame) - actual discovery
        /// only happens at the configured interval.
        /// </summary>
        /// <returns>Collection of objects to render overlays for, sorted by priority.</returns>
        public IEnumerable<DiscoveredOverlayObject> DiscoverObjects(float deltaTime)
        {
            _lastDiscoveryTime += deltaTime;

            // Early exit if not enough time has passed
            if (_lastDiscoveryTime < _cullingConfig.DiscoveryUpdateInterval)
            {
                // Return previously discovered objects during throttle period
                return GetCulledAndFilteredObjects();
            }

            // Reset timer and perform discovery
            _lastDiscoveryTime = 0f;
            var startTime = Time.realtimeSinceStartup;

            _discoveredObjects.Clear();

            // Run discovery strategies in order
            foreach (var strategy in _strategies)
            {
                try
                {
                    var discovered = strategy.DiscoverObjects();
                    if (discovered == null) continue;

                    foreach (var obj in discovered)
                    {
                        if (obj == null || string.IsNullOrEmpty(obj.ObjectId)) continue;

                        obj.SourceStrategy = strategy.StrategyName;
                        _discoveredObjects[obj.ObjectId] = obj;
                    }
                }
                catch (Exception ex)
                {
                    FuseLog.Error($"OverlayDiscoverySystem: Strategy '{strategy.StrategyName}' failed: {ex.Message}");
                }
            }

            _lastFrameDiscoveryTime = Time.realtimeSinceStartup - startTime;
            _lastDiscoveryCount = _discoveredObjects.Count;

            // Apply culling and return filtered results
            return GetCulledAndFilteredObjects();
        }

        /// <summary>
        /// Applies culling and filtering to discovered objects.
        /// </summary>
        private IEnumerable<DiscoveredOverlayObject> GetCulledAndFilteredObjects()
        {
            var filtered = _discoveredObjects.Values
                .Where(obj =>
                {
                    // Distance culling
                    if (obj.Distance > _cullingConfig.MaxDiscoveryDistance)
                    {
                        return false;
                    }

                    // Frustum culling
                    if (_cullingConfig.EnableFrustumCulling && _cullingConfig.ReferenceCamera != null)
                    {
                        if (!IsInCameraFrustum(_cullingConfig.ReferenceCamera, obj))
                        {
                            return false;
                        }
                    }

                    return true;
                })
                .ToList();

            // Sort by priority if enabled
            if (_cullingConfig.SortByPriority)
            {
                filtered.Sort((a, b) =>
                {
                    // Higher priority first, then closer distance
                    int priorityCompare = b.Priority.CompareTo(a.Priority);
                    if (priorityCompare != 0) return priorityCompare;
                    return a.Distance.CompareTo(b.Distance);
                });
            }

            // Apply max count culling
            if (filtered.Count > _cullingConfig.MaxOverlayCount)
            {
                filtered = filtered.Take(_cullingConfig.MaxOverlayCount).ToList();
            }

            return filtered;
        }

        /// <summary>
        /// Tracks when a preview was created for a discovered object.
        /// Used for staleness detection and cleanup.
        /// </summary>
        public void TrackPreviewCreation(string objectId)
        {
            _previewCreationTimes[objectId] = DateTime.UtcNow;
        }

        /// <summary>
        /// Removes tracking for a preview (call when preview is removed).
        /// </summary>
        public void UntrackPreview(string objectId)
        {
            _previewCreationTimes.Remove(objectId);
        }

        /// <summary>
        /// Gets IDs of previews that are stale and should be removed.
        /// Returns empty if staleness checking is disabled.
        /// </summary>
        public IEnumerable<string> GetStalePreviews()
        {
            if (_cullingConfig.MaxPreviewStaleness <= 0)
            {
                return Enumerable.Empty<string>();
            }

            var now = DateTime.UtcNow;
            var threshold = TimeSpan.FromSeconds(_cullingConfig.MaxPreviewStaleness);

            return _previewCreationTimes
                .Where(kvp => now - kvp.Value > threshold)
                .Select(kvp => kvp.Key)
                .ToList();
        }

        /// <summary>
        /// Checks if a discovered object should use lower LOD rendering.
        /// </summary>
        public bool ShouldUseLOD(DiscoveredOverlayObject discoveredObject)
        {
            if (_cullingConfig.LODPriorityThreshold <= 0)
            {
                return false; // LOD disabled
            }

            // Low priority = lower LOD
            if (discoveredObject.Priority < _cullingConfig.LODPriorityThreshold)
            {
                return true;
            }

            // Far away = lower LOD
            if (discoveredObject.Distance > _cullingConfig.LODDistanceThreshold)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Gets performance metrics from the last discovery update.
        /// </summary>
        public DiscoveryPerformanceMetrics GetPerformanceMetrics()
        {
            return new DiscoveryPerformanceMetrics
            {
                LastDiscoveryTime = _lastFrameDiscoveryTime,
                ObjectsDiscovered = _lastDiscoveryCount,
                UpdateInterval = _cullingConfig.DiscoveryUpdateInterval,
                StrategiesActive = _strategies.Count,
                PreviewsTracked = _previewCreationTimes.Count,
                ThrottledUntilNext = Mathf.Max(0, _cullingConfig.DiscoveryUpdateInterval - _lastDiscoveryTime)
            };
        }

        /// <summary>
        /// Clears all discovered objects and preview tracking.
        /// </summary>
        public void Clear()
        {
            _discoveredObjects.Clear();
            _previewCreationTimes.Clear();
            _lastDiscoveryTime = 0f;
        }

        /// <summary>
        /// Disposes the discovery system and cleanup strategies.
        /// </summary>
        public void Dispose()
        {
            foreach (var strategy in _strategies)
            {
                strategy.OnDisable();
            }
            _strategies.Clear();
            Clear();
        }

        /// <summary>
        /// Checks if a point is within the camera frustum.
        /// </summary>
        private bool IsInCameraFrustum(Camera camera, DiscoveredOverlayObject obj)
        {
            Vector3 pos = Vector3.zero;
            if (obj.Entity is GameObject go)
            {
                pos = go.transform.position;
            }
            else return true;

            // Simple plane distance check for each frustum plane
            foreach (Plane plane in GeometryUtility.CalculateFrustumPlanes(camera))
            {
                if (plane.GetDistanceToPoint(pos) < 0)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// Performance metrics from discovery system.
    /// </summary>
    public struct DiscoveryPerformanceMetrics
    {
        /// <summary>
        /// Time taken by the last discovery update, in seconds.
        /// </summary>
        public float LastDiscoveryTime { get; set; }

        /// <summary>
        /// Number of objects discovered in last update.
        /// </summary>
        public int ObjectsDiscovered { get; set; }

        /// <summary>
        /// Configured update interval in seconds.
        /// </summary>
        public float UpdateInterval { get; set; }

        /// <summary>
        /// Number of active discovery strategies.
        /// </summary>
        public int StrategiesActive { get; set; }

        /// <summary>
        /// Number of previews currently being tracked for staleness.
        /// </summary>
        public int PreviewsTracked { get; set; }

        /// <summary>
        /// Seconds remaining until next discovery update (during throttle period).
        /// </summary>
        public float ThrottledUntilNext { get; set; }
    }
}
