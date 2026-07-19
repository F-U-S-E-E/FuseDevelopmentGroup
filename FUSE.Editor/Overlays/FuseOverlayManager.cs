using FUSE.Editor.EditorHandler;
using FUSE.Editor.Overlays.Discovery;
using FUSE.Editor.Track.Overlays.Discovery;
using FUSE.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using static Core.SpatialHashLinear;

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
            private OverlayTooltipManager _tooltipManager;
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
                _discoverySystem.RegisterStrategy(new TrackSegmentDiscoveryStrategy());
                _discoverySystem.RegisterStrategy(new TrackSpanDiscoveryStrategy());

                // Initialize tooltip manager
                _tooltipManager = gameObject.AddComponent<OverlayTooltipManager>();
                _tooltipManager.Initialize(_renderer.SelectionSystem);

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
            /*
            Vector2 mousePos = Mouse.current.position.ReadValue();

            SelectionSystem.UpdateHoverFromMouse(mousePos);

            // Cleanup stale previews
            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                TrySelectPreviewAtMouse(mousePos);
            }
            */
            if (!_isEnabled || _renderer == null)
            {
                return;
            }

            var camera = Camera.main ?? GetComponent<Camera>();
            if (camera != null)
            {
                _renderer.RenderPreviews(camera);
            }
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
                if (FuseEditorChangeHandler.Instance.TryGetQueuedChange(discovered.ObjectId, discovered.Entity.GetType(), out var handler))
                {
                    RegisterPreview(discovered.ObjectId, handler);
                }
                else
                {
                    RegisterPreview(discovered.ObjectId, EditorHandlerRegistry.CreateHandler(discovered.Entity));
                }
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
        /// Cleans up stale previews that shouldn't be rendered anymore.
        /// </summary>
        private void CleanupStalePreviews()
        {
            // This is called during Update to clean up old previews
            // For now, this is handled by the discovery system
            // Additional cleanup logic can be added here as needed
        }

        #endregion

        // Handler registry has been removed - EditorHandler instances are now managed directly by FuseOverlayRenderer
        // Use FuseOverlayRenderer.RegisterPreview() to add previews directly

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

        // Old generic ApplyPreview<TEntity, TPreviewData> methods have been replaced
        // Use EditorHandler concrete implementations (TrackNodeEditorHandler, TrackSegmentEditorHandler, etc.)
        // and call FuseOverlayRenderer.RegisterPreview() instead
        //
        // Example:
        //   var handler = new TrackNodeEditorHandler(id, trackNode, fuseNode);
        //   _renderer.RegisterPreview(id, handler);

        /// <summary>
        /// Registers a preview for an object with pending edits.
        /// </summary>
        /// <param name="handlerId">Unique identifier for the handler.</param>
        /// <param name="handler">The EditorHandler instance managing the overlay rendering.</param>
        public void RegisterPreview(string handlerId, EditorHandler.EditorHandlerBase handler)
        {
            if (!_initialized)
            {
                Initialize();
            }

            _renderer.RegisterPreview(handlerId, handler);
            _trackedOverlayIds.Add(handlerId);
            OnPreviewAdded?.Invoke(handlerId);
        }

        /// <summary>
        /// Gets a registered preview handler by ID.
        /// </summary>
        public EditorHandler.EditorHandlerBase GetPreview(string handlerId)
        {
            if (!_initialized)
            {
                return null;
            }

            return _renderer.GetPreview(handlerId);
        }

        /// <summary>
        /// Checks whether a preview is registered.
        /// </summary>
        public bool HasPreview(string handlerId)
        {
            if (!_initialized)
            {
                return false;
            }

            return _renderer.HasPreview(handlerId);
        }

        /// <summary>
        /// Unregisters and stops rendering a preview.
        /// </summary>
        public void UnregisterPreview(string handlerId)
        {
            if (!_initialized)
            {
                return;
            }

            _renderer.UnregisterPreview(handlerId);
            _trackedOverlayIds.Remove(handlerId);
            OnPreviewRemoved?.Invoke(handlerId);
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
                var handler = _renderer.GetPreview(previewId);

                FuseLog.Info($"Preview selected: {handler.ID}");

                FuseEditor.Instance.EntitySelection.OnSelected(handler);

                /*
                if (Keyboard.current.shiftKey.isPressed)
                {
                    FuseEditor.Instance.EntitySelection.ToggleSelection(handler);
                }
                else
                {
                    FuseEditor.Instance.EntitySelection.ClearSelection();
                    FuseEditor.Instance.EntitySelection.AddToSelection(handler);
                }
                */
            }

            return false;
        }

        /// <summary>
        /// Attempts to select all overlay previews within a rectangular area on the screen.
        /// Supports modifier keys for selection behavior:
        /// - No modifier: Replace current selection with previews in rectangle
        /// - Shift: Add previews in rectangle to current selection
        /// - Ctrl: Toggle selection of previews in rectangle
        /// </summary>
        /// <param name="screenRect">The rectangular selection area in screen space (x, y, width, height)</param>
        /// <returns>True if at least one preview was selected/modified, false otherwise</returns>
        public bool TrySelectPreviewsInRectangle(Rect screenRect)
        {
            SelectionSystem.SetCamera(Camera.main);
            if (!_initialized || SelectionSystem == null)
            {
                FuseLog.Warning("FuseOverlayManager: Not initialized or selection system is null.");
                return false;
            }

            if (!SelectionSystem.TrySelectPreviewsInRectangle(screenRect, out var selectedPreviews))
            {
                FuseLog.Info("FuseOverlayManager: No previews found in rectangular selection area.");
                return false;
            }

            // Convert to handlers list
            var handlers = new List<EditorHandler.EditorHandlerBase>();
            foreach (var (previewId, handler) in selectedPreviews)
            {
                handlers.Add(handler);
                FuseLog.Info($"Preview in rectangle: {handler.ID}");
            }

            // Handle modifier keys for selection behavior
            bool isShiftPressed = Keyboard.current.shiftKey.isPressed;
            bool isCtrlPressed = Keyboard.current.ctrlKey.isPressed;

            if (isCtrlPressed)
            {
                // Toggle selection of each preview in rectangle
                foreach (var handler in handlers)
                {
                    FuseEditor.Instance.EntitySelection.ToggleSelection(handler);
                }
            }
            else if (isShiftPressed)
            {
                // Add previews to current selection
                FuseEditor.Instance.EntitySelection.AddToSelection(handlers);
            }
            else
            {
                // Replace selection with previews in rectangle
                FuseEditor.Instance.EntitySelection.SetSelectedHandlers(handlers);
            }

            FuseLog.Info($"FuseOverlayManager: Selected {handlers.Count} preview(s) in rectangle.");
            return true;
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
