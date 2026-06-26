using FUSE.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using EditorHandlerBase = FUSE.Editor.EditorHandler.EditorHandlerBase;

namespace FUSE.Editor.Overlays
{
    /// <summary>
    /// Renders preview overlays for objects with uncommitted edits using EditorHandler instances.
    /// Manages a collection of EditorHandler objects and renders them sorted by distance from the camera.
    /// Each EditorHandler is responsible for its own rendering via the Render() method.
    /// </summary>
    public class FuseOverlayRenderer : IDisposable
    {
        private readonly Dictionary<string, EditorHandlerBase> _activePreviews = new();
        private readonly OverlaySelectionSystem _selectionSystem;
        private bool _disposed;

        /// <summary>
        /// Called when a preview is added.
        /// </summary>
        public event Action<string> OnPreviewAdded;

        /// <summary>
        /// Called when a preview is removed.
        /// </summary>
        public event Action<string> OnPreviewRemoved;

        /// <summary>
        /// Called when a preview is updated.
        /// </summary>
        public event Action<string> OnPreviewUpdated;

        public FuseOverlayRenderer()
        {
            try
            {
                _selectionSystem = new OverlaySelectionSystem(_activePreviews);
            }
            catch (System.Exception ex)
            {
                FuseLog.Error($"FUSE overlay renderer: Error during initialization: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// Gets the selection system for handling overlay clicks and selection.
        /// </summary>
        public OverlaySelectionSystem SelectionSystem => _selectionSystem;

        /// <summary>
        /// Sets the camera used for selection raycasting.
        /// </summary>
        public void SetSelectionCamera(Camera camera)
        {
            _selectionSystem?.SetCamera(camera);
        }

        /// <summary>
        /// Registers a preview handler.
        /// </summary>
        /// <param name="handlerId">Unique identifier for the preview.</param>
        /// <param name="handler">The EditorHandler managing this preview.</param>
        /// <returns>The registered handler, or null if failed.</returns>
        public EditorHandlerBase RegisterPreview(string handlerId, EditorHandlerBase handler)
        {
            if (string.IsNullOrWhiteSpace(handlerId))
            {
                FuseLog.Error("Overlay renderer: Cannot register preview with null or empty ID.");
                return null;
            }

            if (handler == null)
            {
                FuseLog.Error($"Overlay renderer: Cannot register preview '{handlerId}' with null handler.");
                return null;
            }

            // Replace existing preview if it exists
            if (_activePreviews.ContainsKey(handlerId))
            {
                UnregisterPreview(handlerId);
            }

            _activePreviews[handlerId] = handler;
            OnPreviewAdded?.Invoke(handlerId);

            return handler;
        }

        /// <summary>
        /// Unregisters and stops rendering a preview.
        /// </summary>
        public void UnregisterPreview(string handlerId)
        {
            if (_activePreviews.Remove(handlerId))
            {
                OnPreviewRemoved?.Invoke(handlerId);
            }
        }

        /// <summary>
        /// Gets a registered preview handler by ID.
        /// </summary>
        public EditorHandlerBase GetPreview(string handlerId)
        {
            _activePreviews.TryGetValue(handlerId, out var handler);
            return handler;
        }

        /// <summary>
        /// Checks whether a preview is registered.
        /// </summary>
        public bool HasPreview(string handlerId)
        {
            return _activePreviews.ContainsKey(handlerId);
        }

        /// <summary>
        /// Clears all registered previews.
        /// </summary>
        public void ClearAllPreviews()
        {
            var ids = new List<string>(_activePreviews.Keys);
            foreach (var id in ids)
            {
                UnregisterPreview(id);
            }
        }

        /// <summary>
        /// Renders all active previews sorted by distance from the camera.
        /// Each EditorHandler is responsible for rendering itself.
        /// Call this from an editor update or OnPostRender hook.
        /// </summary>
        public void RenderPreviews(Camera camera)
        {
            if (_disposed || camera == null)
            {
                return;
            }

            // Sort previews by distance from camera (closest first)
            var cameraPos = camera.transform.position;
            var sortedPreviews = _activePreviews.Values
                .OrderBy(h => Vector3.Distance(h.GetPosition(), cameraPos))
                .ToList();

            // Render each preview
            foreach (var handler in sortedPreviews)
            {
                try
                {
                    handler.Render(camera);
                }
                catch (System.Exception ex)
                {
                    FuseLog.Error($"FUSE overlay renderer: Error rendering preview: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Gets the count of active previews.
        /// </summary>
        public int GetActivePreviewCount() => _activePreviews.Count;

        /// <summary>
        /// Gets all active preview IDs.
        /// </summary>
        public IEnumerable<string> GetActivePreviewIds() => _activePreviews.Keys;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _activePreviews.Clear();
            _disposed = true;
        }
    }
}
