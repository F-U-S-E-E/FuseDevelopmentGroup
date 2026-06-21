using FUSE.Editor.Overlays.Discovery;
using FUSE.Editor.Track.Overlays.Discovery;
using FUSE.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FUSE.Editor.Overlays
{
    /// <summary>
    /// Manages the overlay system lifecycle and provides a convenient API for
    /// registering/updating previews of objects with uncommitted edits.
    /// 
    /// Integrates a discovery system for automatic overlay creation from nearby objects
    /// and pending edits, with built-in culling, LOD, and performance optimization.
    /// </summary>
    public class FuseOverlayManager : MonoBehaviour
    {
        private FuseOverlayRenderer _renderer;
        private OverlayDiscoverySystem _discoverySystem;
        private bool _initialized;
        private bool _isEnabled = true;
        private bool _discoveryEnabled = false;
        private readonly HashSet<string> _trackedOverlayIds = new HashSet<string>();

        /// <summary>
        /// Singleton instance of the overlay manager.
        /// </summary>
        public static FuseOverlayManager Instance { get; private set; }

        /// <summary>
        /// Called when a preview is registered.
        /// </summary>
        public event Action<string> OnPreviewAdded;

        /// <summary>
        /// Called when a preview is unregistered.
        /// </summary>
        public event Action<string> OnPreviewRemoved;

        /// <summary>
        /// Called when a preview is updated.
        /// </summary>
        public event Action<string> OnPreviewUpdated;

        /// <summary>
        /// Whether the overlay system is currently enabled and rendering previews.
        /// </summary>
        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_isEnabled == value)
                {
                    return;
                }
                _isEnabled = value;
                if (_renderer != null)
                {
                    enabled = _isEnabled;
                }
            }
        }

        private void Awake()
        {
            // Enforce singleton pattern
            if (Instance != null && Instance != this)
            {
                FuseLog.Warning("FuseOverlayManager: Multiple instances detected. Destroying duplicate.");
                Destroy(this);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            Initialize();
        }

        /// <summary>
        /// Initializes the overlay renderer and subscribes to events.
        /// </summary>
        private void Initialize()
        {
            try
            {
                if (_initialized)
                {
                    return;
                }

                _renderer = new FuseOverlayRenderer();
                _renderer.OnPreviewAdded += id => OnPreviewAdded?.Invoke(id);
                _renderer.OnPreviewRemoved += id => OnPreviewRemoved?.Invoke(id);
                _renderer.OnPreviewUpdated += id => OnPreviewUpdated?.Invoke(id);

                // Initialize discovery system
                _discoverySystem = new OverlayDiscoverySystem();

                _discoverySystem.RegisterStrategy(new TrackNodeDiscoveryStrategy());

                _discoveryEnabled = true;

                _initialized = true;
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FuseOverlayManager: Initialization failed", ex);
                _initialized = false;
            }
        }

        private void Update()
        {
            // Process discovery system if enabled
            if (_discoveryEnabled && _discoverySystem != null)
            {
                ProcessDiscovery();
            }

            // Cleanup stale previews
            CleanupStalePreviews();
            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                TrySelectPreviewAtMouse(Mouse.current.position.ReadValue());
            }

            if (!_isEnabled || _renderer == null)
            {
                return;
            }

            _renderer.RenderPreviews();
        }

        #region Discovery System API

        /// <summary>
        /// Gets the discovery system for registering strategies and configuring behavior.
        /// </summary>
        public OverlayDiscoverySystem DiscoverySystem
        {
            get
            {
                if (!_initialized)
                {
                    Initialize();
                }
                return _discoverySystem;
            }
        }

        /// <summary>
        /// Enables automatic discovery of nearby objects and objects with pending edits.
        /// </summary>
        public void EnableDiscovery()
        {
            if (!_initialized)
            {
                Initialize();
            }

            _discoveryEnabled = true;
            FuseLog.Info("FuseOverlayManager: Discovery enabled");
        }

        /// <summary>
        /// Disables automatic discovery.
        /// </summary>
        public void DisableDiscovery()
        {
            _discoveryEnabled = false;
            FuseLog.Info("FuseOverlayManager: Discovery disabled");
        }

        /// <summary>
        /// Checks if discovery is currently enabled.
        /// </summary>
        public bool IsDiscoveryEnabled => _discoveryEnabled;

        /// <summary>
        /// Registers a discovery strategy.
        /// </summary>
        public void RegisterDiscoveryStrategy(IOverlayDiscoveryStrategy strategy)
        {
            if (!_initialized)
            {
                Initialize();
            }

            _discoverySystem.RegisterStrategy(strategy);
        }

        /// <summary>
        /// Unregisters a discovery strategy.
        /// </summary>
        public void UnregisterDiscoveryStrategy(string strategyName)
        {
            if (!_initialized) return;
            _discoverySystem.UnregisterStrategy(strategyName);
        }

        /// <summary>
        /// Configures the discovery culling behavior.
        /// </summary>
        public void ConfigureDiscoveryCulling(OverlayDiscoveryCullingConfig config)
        {
            if (!_initialized)
            {
                Initialize();
            }

            _discoverySystem.SetCullingConfig(config);
        }

        /// <summary>
        /// Gets the current discovery culling configuration.
        /// </summary>
        public OverlayDiscoveryCullingConfig GetDiscoveryCullingConfig()
        {
            if (!_initialized)
            {
                Initialize();
            }

            return _discoverySystem.CullingConfig;
        }

        /// <summary>
        /// Gets performance metrics from the discovery system.
        /// </summary>
        public DiscoveryPerformanceMetrics GetDiscoveryMetrics()
        {
            if (!_initialized)
            {
                return default;
            }

            return _discoverySystem.GetPerformanceMetrics();
        }

        /// <summary>
        /// Manually triggers discovery update (normally happens automatically).
        /// Only needed if discovery is disabled.
        /// </summary>
        public void ManuallyUpdateDiscovery()
        {
            if (!_initialized)
            {
                Initialize();
            }

            ProcessDiscovery();
        }

        /// <summary>
        /// Internal: Processes discovery and updates overlays.
        /// </summary>
        private void ProcessDiscovery()
        {
            // Update focal point to camera position
            if (Camera.main != null)
            {
                _discoverySystem.CullingConfig.FocalPoint = Camera.main.transform.position;
                _discoverySystem.CullingConfig.ReferenceCamera = Camera.main;
            }

            // Discover objects
            var discovered = _discoverySystem.DiscoverObjects(Time.deltaTime);

            // Create/update overlays for discovered objects
            var newIds = new HashSet<string>();
            foreach (var obj in discovered)
            {
                newIds.Add(obj.ObjectId);

                if (!_trackedOverlayIds.Contains(obj.ObjectId))
                {
                    // New overlay - create it
                    CreateOverlayForDiscoveredObject(obj);
                    _trackedOverlayIds.Add(obj.ObjectId);
                    _discoverySystem.TrackPreviewCreation(obj.ObjectId);
                }
                else
                {
                    // Existing overlay - update if needed
                    UpdateOverlayForDiscoveredObject(obj);
                }
            }

            // Remove overlays for objects no longer discovered
            var toRemove = _trackedOverlayIds.Except(newIds).ToList();
            foreach (var id in toRemove)
            {
                RemoveOverlayForDiscoveredObject(id);
                _trackedOverlayIds.Remove(id);
                _discoverySystem.UntrackPreview(id);
            }
        }

        /// <summary>
        /// Creates an overlay for a newly discovered object.
        /// </summary>
        private void CreateOverlayForDiscoveredObject(DiscoveredOverlayObject discovered)
        {
            try
            {
                    CreateHandlerBasedOverlay(discovered);
            }
            catch (Exception ex)
            {
                FuseLog.Error($"FuseOverlayManager: Failed to create overlay for '{discovered.ObjectId}': {ex.Message}");
            }
        }

        /// <summary>
        /// Updates an overlay for a discovered object.
        /// </summary>
        private void UpdateOverlayForDiscoveredObject(DiscoveredOverlayObject discovered)
        {
            // Check LOD status
            bool shouldUseLOD = _discoverySystem.ShouldUseLOD(discovered);

            // For now, LOD is tracked via priority tinting
            // In future, could adjust mesh complexity or material
        }

        /// <summary>
        /// Removes an overlay for a discovered object.
        /// </summary>
        private void RemoveOverlayForDiscoveredObject(string objectId)
        {
            if (_renderer != null && _renderer.HasPreview(objectId))
            {
                _renderer.UnregisterPreview(objectId);
            }
        }

        /// <summary>
        /// Creates an overlay using the handler system for objects with preview data.
        /// </summary>
        private void CreateHandlerBasedOverlay(DiscoveredOverlayObject discovered)
        {
            if (discovered.Entity == null)
            {
                return;
            }

            var entityType = discovered.Entity.GetType();
            var previewDataType = discovered.PreviewData.GetType();

            // Use reflection to call the generic ApplyPreview method
            try
            {
                var method = typeof(OverlayHandlerRegistry)
                    .GetMethods()
                    .FirstOrDefault(m =>
                        m.Name == "ApplyPreview" &&
                        m.IsGenericMethodDefinition &&
                        m.GetGenericArguments().Length == 2 &&
                        m.GetParameters().Length == 3);

                if (method != null)
                {
                    var genericMethod = method.MakeGenericMethod(entityType, previewDataType);
                    var result = genericMethod.Invoke(
                        HandlerRegistry,
                        new object[] { discovered.Entity, discovered.PreviewData, discovered.ObjectId });

                    if (result is OverlayPreviewData previewData && previewData != null)
                    {
                        _renderer.RegisterPreview(previewData);
                        FuseLog.Info($"FuseOverlayManager: Created handler-based overlay for '{discovered.ObjectId}'");
                    }
                }
            }
            catch (Exception ex)
            {
                FuseLog.Warning($"FuseOverlayManager: Handler-based overlay creation failed: {ex.Message}");
                // Fallback to simple overlay
                CreateSelectableOverlay(discovered);
            }
        }

        /// <summary>
        /// Creates a simple selectable overlay for objects without pending edits.
        /// </summary>
        private void CreateSelectableOverlay(DiscoveredOverlayObject discovered)
        {
            if (discovered.Entity == null)
            {
                return;
            }

            GameObject targetGo = null;
            if (discovered.Entity is GameObject go)
            {
                targetGo = go;
            }
            else if (discovered.Entity is Component comp)
            {
                targetGo = comp.gameObject;
            }

            if (targetGo == null)
            {
                return;
            }

            // Register simple preview without pending edits
            var previewData = _renderer.RegisterPreview(
                discovered.ObjectId,
                targetGo,
                discovered.Entity,
                null);

            if (previewData != null)
            {
                // Set priority-based tinting to indicate LOD status
                bool useLOD = _discoverySystem.ShouldUseLOD(discovered);
                previewData.Tint = useLOD ? new Color(0.6f, 0.6f, 0.6f, 1f) : new Color(1f, 1f, 1f, 1f);

                FuseLog.Info($"FuseOverlayManager: Created selectable overlay for '{discovered.ObjectId}' (LOD: {useLOD})");
            }
        }

        /// <summary>
        /// Removes stale previews that haven't been discovered recently.
        /// </summary>
        private void CleanupStalePreviews()
        {
            if (!_initialized || _discoverySystem == null)
            {
                return;
            }

            var stalePreviews = _discoverySystem.GetStalePreviews();
            foreach (var previewId in stalePreviews)
            {
                if (_renderer != null && _renderer.HasPreview(previewId))
                {
                    _renderer.UnregisterPreview(previewId);
                    _trackedOverlayIds.Remove(previewId);
                    FuseLog.Info($"FuseOverlayManager: Removed stale preview '{previewId}'");
                }
            }
        }

        #endregion        /// <summary>
        /// Gets the handler registry for registering entity-specific overlay handlers.
        /// Use this to register handlers for custom entity types.
        /// </summary>
        public OverlayHandlerRegistry HandlerRegistry
        {
            get
            {
                if (!_initialized)
                {
                    Initialize();
                }
                return _renderer.HandlerRegistry;
            }
        }

        /// <summary>
        /// Gets the selection system for handling overlay selection interactions.
        /// </summary>
        public OverlaySelectionSystem SelectionSystem
        {
            get
            {
                if (!_initialized)
                {
                    Initialize();
                }
                return _renderer.SelectionSystem;
            }
        }

        /// <summary>
        /// Applies a preview for an entity using its registered dual-type handler.
        /// Generic API that works with any entity type that has a registered handler.
        /// </summary>
        /// <typeparam name="TEntity">The entity type (e.g., TrackNode, Building).</typeparam>
        /// <typeparam name="TPreviewData">The preview data type.</typeparam>
        /// <param name="entity">The entity to create a preview for.</param>
        /// <param name="previewData">The preview/pending-edit data.</param>
        /// <returns>The preview data, or null if failed.</returns>
        public OverlayPreviewData ApplyPreview<TEntity, TPreviewData>(TEntity entity, TPreviewData previewData)
        {
            if (!_initialized)
            {
                Initialize();
            }

            return _renderer.ApplyPreview(entity, previewData);
        }

        /// <summary>
        /// Applies a preview for an entity and returns its preview ID.
        /// Convenience overload that returns the ID for further reference.
        /// </summary>
        /// <typeparam name="TEntity">The entity type.</typeparam>
        /// <typeparam name="TPreviewData">The preview data type.</typeparam>
        /// <param name="entity">The entity to create a preview for.</param>
        /// <param name="previewData">The preview/pending-edit data.</param>
        /// <param name="previewId">Output: the ID of the created preview.</param>
        /// <returns>The preview data, or null if failed.</returns>
        public OverlayPreviewData ApplyPreview<TEntity, TPreviewData>(TEntity entity, TPreviewData previewData, out string previewId)
        {
            if (!_initialized)
            {
                Initialize();
            }

            return _renderer.ApplyPreview(entity, previewData, out previewId);
        }

        /// <summary>
        /// Updates an existing preview from entity and preview data using its handler.
        /// </summary>
        /// <typeparam name="TEntity">The entity type.</typeparam>
        /// <typeparam name="TPreviewData">The preview data type.</typeparam>
        /// <param name="objectId">The ID of the preview to update.</param>
        /// <param name="entity">The entity with updated values.</param>
        /// <param name="previewData">The updated preview/pending-edit data.</param>
        public void UpdatePreviewFromEntity<TEntity, TPreviewData>(string objectId, TEntity entity, TPreviewData previewData)
        {
            if (!_initialized)
            {
                return;
            }

            _renderer.UpdatePreviewFromEntity(objectId, entity, previewData);
        }

        /// <summary>
        /// Registers a preview for an object with pending edits.
        /// </summary>
        /// <param name="objectId">Unique identifier for the object.</param>
        /// <param name="originalObject">The original game object (not modified by the overlay).</param>
        /// <param name="fuseData">The preview/pending-edit data (e.g., FuseNode).</param>
        /// <param name="renderable">Optional IOverlayRenderable for custom rendering.</param>
        /// <returns>The preview data object.</returns>
        public OverlayPreviewData RegisterPreview(
            string objectId,
            GameObject originalObject,
            object fuseData,
            IOverlayRenderable renderable = null)
        {
            if (!_initialized)
            {
                Initialize();
            }

            return _renderer.RegisterPreview(objectId, originalObject, fuseData, renderable);
        }

        /// <summary>
        /// Updates an existing preview's transform.
        /// </summary>
        public void UpdatePreview(
            string objectId,
            Vector3 position,
            Quaternion rotation,
            Vector3 scale)
        {
            if (!_initialized)
            {
                return;
            }

            _renderer.UpdatePreview(objectId, position, rotation, scale);
        }

        /// <summary>
        /// Gets a registered preview by ID.
        /// </summary>
        public OverlayPreviewData GetPreview(string objectId)
        {
            if (!_initialized)
            {
                return null;
            }

            return _renderer.GetPreview(objectId);
        }

        /// <summary>
        /// Checks whether a preview is registered.
        /// </summary>
        public bool HasPreview(string objectId)
        {
            if (!_initialized)
            {
                return false;
            }

            return _renderer.HasPreview(objectId);
        }

        /// <summary>
        /// Unregisters and stops rendering a preview.
        /// </summary>
        public void UnregisterPreview(string objectId)
        {
            if (!_initialized)
            {
                return;
            }

            _renderer.UnregisterPreview(objectId);
        }

        /// <summary>
        /// Clears all registered previews.
        /// </summary>
        public void ClearAllPreviews()
        {
            if (!_initialized)
            {
                return;
            }

            _renderer.ClearAllPreviews();
        }

        /// <summary>
        /// Gets the count of active previews.
        /// </summary>
        public int GetActivePreviewCount()
        {
            return _initialized ? _renderer.GetActivePreviewCount() : 0;
        }

        /// <summary>
        /// Gets all active preview IDs.
        /// </summary>
        public IEnumerable<string> GetActivePreviewIds()
        {
            return _initialized ? _renderer.GetActivePreviewIds() : new List<string>();
        }

        /// <summary>
        /// Gets the underlying renderer (for advanced usage).
        /// </summary>
        public FuseOverlayRenderer GetRenderer()
        {
            if (!_initialized)
            {
                Initialize();
            }

            return _renderer;
        }

        /// <summary>
        /// Sets the camera used for overlay selection raycasting.
        /// Call this with the editor/scene camera.
        /// </summary>
        public void SetSelectionCamera(Camera camera)
        {
            if (!_initialized)
            {
                Initialize();
            }

            _renderer.SetSelectionCamera(camera);
        }

        /// <summary>
        /// Attempts to select an overlay preview from a mouse position.
        /// Will invoke the handler's OnPreviewSelected callback for the selected area.
        /// </summary>
        public bool TrySelectPreviewAtMouse(Vector2 mousePosition)
        {
            SelectionSystem.SetCamera(Camera.main);
            if (!_initialized || SelectionSystem == null)
            {
                return false;
            }

            if (!SelectionSystem.TrySelect(mousePosition))
            {
                return false;
            }

            // The selection system found a hit; now dispatch to handler
            if (SelectionSystem.TrySelectFromRay(
                Camera.main.ScreenPointToRay(mousePosition),
                out var previewId,
                out var selectionArea))
            {
                var preview = _renderer.GetPreview(previewId);
                if (preview != null && preview.Entity != null)
                {
                    // Directly invoke via dynamic dispatch on handler registry
                    InvokeSelectionCallback(preview, selectionArea);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Invokes the handler's selection callback for a preview using generic dispatch.
        /// </summary>
        private void InvokeSelectionCallback(OverlayPreviewData preview, OverlaySelectionArea selectionArea)
        {
            if (preview?.Entity == null)
            {
                return;
            }

            // Get the entity type and preview data type
            var entityType = preview.Entity.GetType();
            var previewDataType = preview.FuseData?.GetType() ?? typeof(object);

            try
            {
                // Use reflection to invoke the generic handler method
                // InvokeSelectionCallback<TEntity, TPreviewData> requires both type parameters
                var method = typeof(OverlayHandlerRegistry)
                    .GetMethod(nameof(OverlayHandlerRegistry.InvokeSelectionCallback))
                    .MakeGenericMethod(entityType, previewDataType);

                method.Invoke(HandlerRegistry, new object[] { preview.Entity, preview.FuseData, selectionArea });
            }
            catch (System.Exception ex)
            {
                FuseLog.Exception($"FuseOverlayManager: Failed to invoke selection callback", ex);
            }
        }

        private void OnDestroy()
        {
            if (_discoverySystem != null)
            {
                _discoverySystem.Dispose();
                _discoverySystem = null;
            }

            if (_renderer != null)
            {
                _renderer.Dispose();
                _renderer = null;
            }

            if (Instance == this)
            {
                Instance = null;
            }
        }
    }
}
