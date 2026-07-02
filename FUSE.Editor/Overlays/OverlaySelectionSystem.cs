using FUSE.Infrastructure;
using System;
using System.Collections.Generic;
using UnityEngine;
using EditorHandlerBase = FUSE.Editor.EditorHandler.EditorHandlerBase;

namespace FUSE.Editor.Overlays
{
    /// <summary>
    /// Manages selection interactions with overlay previews using EditorHandler instances.
    /// Handles raycasting, hit detection, and selection callbacks.
    /// </summary>
    public class OverlaySelectionSystem
    {
        private readonly Dictionary<string, EditorHandlerBase> _previews;
        private Camera _editorCamera;
        private OverlaySelectionArea _currentHoveredArea;
        private string _currentHoveredPreviewId;

        /// <summary>
        /// Called when a preview is selected via rendering.
        /// Parameters: (previewId, selectionArea)
        /// </summary>
        public event Action<string, OverlaySelectionArea> OnPreviewSelectionChanged;

        /// <summary>
        /// Called when mouse hovers over a selection area.
        /// Parameters: (previewId, selectionArea)
        /// </summary>
        public event Action<string, OverlaySelectionArea> OnPreviewHovered;

        /// <summary>
        /// Called when mouse leaves a selection area.
        /// </summary>
        public event Action OnPreviewUnhovered;

        public OverlaySelectionSystem(Dictionary<string, EditorHandlerBase> previews)
        {
            _previews = previews ?? throw new ArgumentNullException(nameof(previews));
        }

        /// <summary>
        /// Sets the camera used for raycasting during selection.
        /// </summary>
        public void SetCamera(Camera camera)
        {
            _editorCamera = camera;
        }

        /// <summary>
        /// Perform selection from mouse position.
        /// Returns true if something was selected.
        /// </summary>
        public bool TrySelect(Vector2 mousePosition)
        {
            if (_editorCamera == null)
            {
                FuseLog.Warning("OverlaySelectionSystem: No editor camera set for raycasting.");
                return false;
            }

            var ray = _editorCamera.ScreenPointToRay(mousePosition);
            return TrySelectFromRay(ray, out var hitPreviewId, out var hitArea);
        }

        /// <summary>
        /// Perform selection from a ray (useful for non-mouse input).
        /// </summary>
        public bool TrySelectFromRay(Ray ray, out string hitPreviewId, out OverlaySelectionArea hitArea)
        {
            hitPreviewId = null;
            hitArea = null;

            var closestDistance = float.MaxValue;
            OverlaySelectionArea closestArea = null;
            string closestPreviewId = null;

            // Raycast against all previews
            foreach (var kvp in _previews)
            {
                var previewId = kvp.Key;
                var preview = kvp.Value;

                if (!preview.IsVisible || preview.SelectionAreas == null || preview.SelectionAreas.Length == 0)
                {
                    continue;
                }

                // Check each selection area
                foreach (var area in preview.SelectionAreas)
                {
                    if (!area.IsSelectable)
                    {
                        continue;
                    }

                    if (area.Raycast(ray, out var distance))
                    {
                        if (distance < closestDistance)
                        {
                            closestDistance = distance;
                            closestArea = area;
                            closestPreviewId = previewId;
                        }
                    }
                }
            }

            if (closestArea != null)
            {
                hitPreviewId = closestPreviewId;
                hitArea = closestArea;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Update hover state from mouse position (for UI feedback).
        /// </summary>
        public void UpdateHoverFromMouse(Vector2 mousePosition)
        {
            if (_editorCamera == null)
            {
                return;
            }

            var ray = _editorCamera.ScreenPointToRay(mousePosition);
            UpdateHoverFromRay(ray);
        }

        /// <summary>
        /// Update hover state from ray.
        /// </summary>
        public void UpdateHoverFromRay(Ray ray)
        {
            if (TrySelectFromRay(ray, out var previewId, out var area))
            {
                if (_currentHoveredArea != area)
                {
                    _currentHoveredArea = area;
                    _currentHoveredPreviewId = previewId;
                    OnPreviewHovered?.Invoke(previewId, area);
                }
            }
            else
            {
                if (_currentHoveredArea != null)
                {
                    _currentHoveredArea = null;
                    _currentHoveredPreviewId = null;
                    OnPreviewUnhovered?.Invoke();
                }
            }
        }

        /// <summary>
        /// Clear all hover state.
        /// </summary>
        public void ClearHover()
        {
            if (_currentHoveredArea != null)
            {
                _currentHoveredArea = null;
                _currentHoveredPreviewId = null;
                OnPreviewUnhovered?.Invoke();
            }
        }

        /// <summary>
        /// Get the currently hovered selection area.
        /// </summary>
        public OverlaySelectionArea GetHoveredArea() => _currentHoveredArea;

        /// <summary>
        /// Get the handler for the currently hovered preview.
        /// </summary>
        public EditorHandlerBase GetHoveredHandler()
        {
            if (string.IsNullOrEmpty(_currentHoveredPreviewId) || !_previews.TryGetValue(_currentHoveredPreviewId, out var handler))
            {
                return null;
            }
            return handler;
        }

        /// <summary>
        /// Get all selection areas for a specific preview.
        /// </summary>
        public OverlaySelectionArea[] GetSelectionAreas(string previewId)
        {
            if (_previews.TryGetValue(previewId, out var preview))
            {
                return preview.SelectionAreas;
            }

            return null;
        }

        /// <summary>
        /// Selects all previews whose bounds intersect with a rectangular area on the screen.
        /// Returns a list of (previewId, handler) pairs for all previews that fall within the screen-space rectangle.
        /// </summary>
        /// <param name="screenRect">The rectangular selection area in screen space (x, y, width, height)</param>
        /// <param name="selectedPreviews">Output list of (previewId, handler) tuples for all selected previews</param>
        /// <returns>True if at least one preview was selected, false otherwise</returns>
        public bool TrySelectPreviewsInRectangle(Rect screenRect, out List<(string previewId, EditorHandlerBase handler)> selectedPreviews)
        {
            selectedPreviews = new List<(string, EditorHandlerBase)>();

            if (_editorCamera == null)
            {
                FuseLog.Warning("OverlaySelectionSystem: No editor camera set for raycasting.");
                return false;
            }

            // Iterate through all previews and check if their bounds intersect with the screen rectangle
            foreach (var kvp in _previews)
            {
                var previewId = kvp.Key;
                var preview = kvp.Value;

                if (!preview.IsVisible || preview.SelectionAreas == null || preview.SelectionAreas.Length == 0)
                {
                    continue;
                }

                // Check if any selection area of this preview intersects with the screen rectangle
                bool previewIntersects = false;
                foreach (var area in preview.SelectionAreas)
                {
                    if (!area.IsSelectable)
                    {
                        continue;
                    }

                    if (IsSelectionAreaInScreenRectangle(area, screenRect))
                    {
                        previewIntersects = true;
                        break;
                    }
                }

                if (previewIntersects)
                {
                    selectedPreviews.Add((previewId, preview));
                }
            }

            return selectedPreviews.Count > 0;
        }

        /// <summary>
        /// Checks if a selection area's bounds intersect with a screen-space rectangle.
        /// Uses the camera to project world space bounds to screen space.
        /// </summary>
        private bool IsSelectionAreaInScreenRectangle(OverlaySelectionArea area, Rect screenRect)
        {
            if (area == null || _editorCamera == null)
            {
                return false;
            }

            // Get the world space bounds
            var worldBounds = area.Bounds;
            var boundsCenter = area.Transform.MultiplyPoint(worldBounds.center);
            var boundsExtents = worldBounds.extents;

            // Project the bounds center and extents to screen space
            var screenCenter = _editorCamera.WorldToScreenPoint(boundsCenter);

            // Project the eight corners of the bounding box to screen space
            var corners = new Vector3[8];
            var extents = boundsExtents;

            corners[0] = area.Transform.MultiplyPoint(worldBounds.center + new Vector3(-extents.x, -extents.y, -extents.z));
            corners[1] = area.Transform.MultiplyPoint(worldBounds.center + new Vector3(extents.x, -extents.y, -extents.z));
            corners[2] = area.Transform.MultiplyPoint(worldBounds.center + new Vector3(-extents.x, extents.y, -extents.z));
            corners[3] = area.Transform.MultiplyPoint(worldBounds.center + new Vector3(extents.x, extents.y, -extents.z));
            corners[4] = area.Transform.MultiplyPoint(worldBounds.center + new Vector3(-extents.x, -extents.y, extents.z));
            corners[5] = area.Transform.MultiplyPoint(worldBounds.center + new Vector3(extents.x, -extents.y, extents.z));
            corners[6] = area.Transform.MultiplyPoint(worldBounds.center + new Vector3(-extents.x, extents.y, extents.z));
            corners[7] = area.Transform.MultiplyPoint(worldBounds.center + new Vector3(extents.x, extents.y, extents.z));

            // Check if any corner is within the screen rectangle
            foreach (var corner in corners)
            {
                var screenCorner = _editorCamera.WorldToScreenPoint(corner);

                // Check if the corner is within the screen rectangle
                if (screenRect.Contains(screenCorner))
                {
                    return true;
                }
            }

            // Also check if any corner of the screen rectangle is within the projected bounds
            var screenMin = screenRect.min;
            var screenMax = screenRect.max;

            var corners2D = new Vector2[] 
            {
                new Vector2(screenMin.x, screenMin.y),
                new Vector2(screenMax.x, screenMin.y),
                new Vector2(screenMin.x, screenMax.y),
                new Vector2(screenMax.x, screenMax.y)
            };

            foreach (var corner2D in corners2D)
            {
                var ray = _editorCamera.ScreenPointToRay(corner2D);
                if (TrySelectFromRay(ray, out var _, out var selectedArea))
                {
                    if (selectedArea == area)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
