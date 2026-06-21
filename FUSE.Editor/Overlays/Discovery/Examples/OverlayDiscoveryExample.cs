using FUSE.Editor.Overlays.Discovery;
using FUSE.Editor.Overlays.Discovery.Strategies;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace FUSE.Editor.Overlays.Examples
{
    /// <summary>
    /// Example demonstrating how to use the overlay discovery system.
    /// Shows:
    /// - Enabling discovery
    /// - Registering multiple discovery strategies
    /// - Configuring culling and performance
    /// - Monitoring performance metrics
    /// </summary>
    public class OverlayDiscoveryExample : MonoBehaviour
    {
        [SerializeField] private bool _autoInitialize = true;
        [SerializeField] private float _discoveryUpdateInterval = 1.0f;
        [SerializeField] private float _searchRadius = 500f;
        [SerializeField] private int _maxOverlayCount = 100;
        [SerializeField] private float _maxDiscoveryDistance = 500f;

        private void Start()
        {
            if (_autoInitialize)
            {
                SetupDiscoverySystem();
            }
        }

        /// <summary>
        /// Sets up the discovery system with default strategies and configuration.
        /// </summary>
        public void SetupDiscoverySystem()
        {
            var manager = FuseOverlayManager.Instance;
            if (manager == null)
            {
                Debug.LogError("FuseOverlayManager instance not found");
                return;
            }

            // Configure culling behavior
            var cullingConfig = new OverlayDiscoveryCullingConfig
            {
                DiscoveryUpdateInterval = _discoveryUpdateInterval,  // Update every 1 second
                MaxOverlayCount = _maxOverlayCount,                  // Max 100 overlays
                MaxDiscoveryDistance = _maxDiscoveryDistance,         // Search up to 500 units
                MaxPreviewStaleness = 10.0f,                         // Remove after 10 seconds of no updates
                LODPriorityThreshold = 0.5f,                         // Objects below 0.5 priority use LOD
                LODDistanceThreshold = 200f,                         // Objects beyond 200 units use LOD
                SortByPriority = true,                               // Sort by distance for culling
                EnableFrustumCulling = true,                         // Don't render overlays outside camera view
            };

            manager.ConfigureDiscoveryCulling(cullingConfig);

            // Register discovery strategies
            // Find nearby GameObjects (colliders)
            var gameObjectStrategy = new NearbyGameObjectDiscoveryStrategy(
                getFocalPoint: () => Camera.main?.transform.position ?? Vector3.zero,
                searchRadius: _searchRadius,
                layerMask: ~0);  // All layers
            manager.RegisterDiscoveryStrategy(gameObjectStrategy);

            // Find nearby components of a specific type (example: Renderer)
            var rendererStrategy = new NearbyComponentDiscoveryStrategy<Renderer>(
                getFocalPoint: () => Camera.main?.transform.position ?? Vector3.zero,
                searchRadius: _searchRadius);
            manager.RegisterDiscoveryStrategy(rendererStrategy);

            // Find objects by tag (if you want tag-based selection)
            try
            {
                var tagStrategy = new TagBasedDiscoveryStrategy(
                    tag: "EditorSelectable",
                    getFocalPoint: () => Camera.main?.transform.position ?? Vector3.zero,
                    maxDistance: _searchRadius);
                manager.RegisterDiscoveryStrategy(tagStrategy);
            }
            catch { /* tag may not exist */ }

            // Enable the discovery system
            manager.EnableDiscovery();

            Debug.Log("Overlay discovery system initialized and enabled");
        }

        private void Update()
        {
#if UNITY_EDITOR
            // Optional: Display performance metrics in editor log periodically
            if (Input.GetKeyDown(KeyCode.F1))
            {
                ShowMetrics();
            }

            if (Input.GetKeyDown(KeyCode.F2))
            {
                FuseOverlayManager.Instance?.DisableDiscovery();
                Debug.Log("Discovery disabled");
            }

            if (Input.GetKeyDown(KeyCode.F3))
            {
                FuseOverlayManager.Instance?.EnableDiscovery();
                Debug.Log("Discovery enabled");
            }
#endif
        }

        /// <summary>
        /// Displays current performance metrics to the console.
        /// </summary>
        public void ShowMetrics()
        {
            var manager = FuseOverlayManager.Instance;
            if (manager == null) return;

            var metrics = manager.GetDiscoveryMetrics();
            var config = manager.GetDiscoveryCullingConfig();

            Debug.Log($@"
=== Overlay Discovery Metrics ===
Discovery Update Interval: {config.DiscoveryUpdateInterval}s
Last Discovery Time: {metrics.LastDiscoveryTime * 1000:F2}ms
Objects Discovered: {metrics.ObjectsDiscovered}
Active Strategies: {metrics.StrategiesActive}
Previews Tracked: {metrics.PreviewsTracked}
Active Previews: {manager.GetActivePreviewCount()}
Throttled Until Next: {metrics.ThrottledUntilNext:F2}s

Max Settings:
  Max Overlays: {config.MaxOverlayCount}
  Max Distance: {config.MaxDiscoveryDistance}
  Max Staleness: {config.MaxPreviewStaleness}s

LOD Settings:
  Priority Threshold: {config.LODPriorityThreshold}
  Distance Threshold: {config.LODDistanceThreshold}

Culling Enabled: {config.EnableFrustumCulling}
");
        }

        /// <summary>
        /// Example of dynamically updating the search configuration.
        /// </summary>
        public void UpdateSearchRadius(float newRadius)
        {
            var manager = FuseOverlayManager.Instance;
            if (manager == null) return;

            var config = manager.GetDiscoveryCullingConfig();
            config.MaxDiscoveryDistance = newRadius;

            Debug.Log($"Search radius updated to {newRadius} units");
        }

        /// <summary>
        /// Example of temporarily disabling LOD.
        /// </summary>
        public void DisableLOD()
        {
            var manager = FuseOverlayManager.Instance;
            if (manager == null) return;

            var config = manager.GetDiscoveryCullingConfig();
            config.LODPriorityThreshold = 0; // 0 = disabled

            Debug.Log("LOD disabled - all overlays render at full detail");
        }
    }
}
