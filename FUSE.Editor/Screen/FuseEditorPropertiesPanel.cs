using FUSE.Editor.EditorHandler;
using FUSE.Editor.Screen.PropertyRenderers;
using FUSE.Infrastructure;
using FUSE.Loading;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FUSE.Editor.Screen
{
    /// <summary>
    /// Standalone properties panel for the FuseEditorScreen. Works directly with EditorHandler
    /// instances to display and edit properties using an extensible renderer system.
    /// 
    /// The panel supports:
    /// - Single and multi-selection of handlers
    /// - Type-specific property editing via extensible IPropertyRenderer system
    /// - Custom renderers for complex types (dropdowns, object selectors, etc.)
    /// - Built-in renderers for common types (Vector3, Quaternion, bool, int, float, string)
    /// - Per-property IMGUI buffers for smooth editing
    /// </summary>
    internal sealed class FuseEditorPropertiesPanel
    {
        private const float RowHeight = 22f;
        private const float LabelWidth = 96f;
        private const float Padding = 6f;

        private Vector2 _scrollPosition;
        private string _lastBufferedHandlerId;
        private EditorHandlerBase _currentHandler;

        // Per-property edit buffers for smoother IMGUI interaction
        private Dictionary<string, object> _propertyBuffers = new Dictionary<string, object>();

        // GUI styles (provided by caller)
        private GUIStyle _propertyLabelStyle;
        private GUIStyle _propertyValueStyle;

        private bool bufferedValuesChanged = false;

        public FuseEditorPropertiesPanel()
        {
        }

        /// <summary>
        /// Draws the properties panel into the given rect.
        /// </summary>
        /// <param name="panelRect">The rectangle to draw into</param>
        /// <param name="selectedHandlers">The list of selected EditorHandler instances</param>
        /// <param name="propertyLabelStyle">Style for property labels</param>
        /// <param name="propertyValueStyle">Style for property input values</param>
        /// <param name="toolButtonStyle">Style for tool buttons (unused for now)</param>
        public void Draw(Rect panelRect, List<EditorHandlerBase> selectedHandlers,
                        GUIStyle propertyLabelStyle, GUIStyle propertyValueStyle, GUIStyle toolButtonStyle)
        {
            _propertyLabelStyle = propertyLabelStyle;
            _propertyValueStyle = propertyValueStyle;

            var contentRect = new Rect(panelRect.x + Padding,
                                       panelRect.y + Padding,
                                       panelRect.width - Padding * 2,
                                       panelRect.height - Padding * 2);

            // Handle empty selection
            if (selectedHandlers.Count == 0)
            {
                GUI.Label(contentRect, "  No selection", _propertyLabelStyle);
                return;
            }

            // Handle multi-selection with different types
            if (selectedHandlers.Count > 1)
            {
                DrawMultiSelectionState(contentRect, selectedHandlers);
                return;
            }

            // Single selection: draw handler properties
            DrawHandlerProperties(contentRect, selectedHandlers[0]);
        }

        private void DrawMultiSelectionState(Rect contentRect, List<EditorHandlerBase> handlers)
        {
            var y = contentRect.y;

            // Check if we have multiple different entity types
            var uniqueTypes = new HashSet<string>(handlers.Select(h => h.Entity.GetType().Name));
            if (uniqueTypes.Count > 1)
            {
                // Multi-type selection: show unsupported message
                GUI.Label(new Rect(contentRect.x, y, contentRect.width, RowHeight),
                          $"  Multiple Selection ({handlers.Count} entities)",
                          _propertyLabelStyle);
                y += RowHeight + Padding;

                GUI.Label(new Rect(contentRect.x + 8, y, contentRect.width - 16, RowHeight * 2),
                          "  Multi-type editing is not supported.\n  Select entities of a single type to edit properties.",
                          _propertyValueStyle);
                y += RowHeight * 2 + Padding;

                // Group selected handlers by entity type for display
                var groupedByType = new Dictionary<string, int>();
                foreach (var type in handlers.Select(h => h.Entity.GetType().Name))
                {
                    if (!groupedByType.ContainsKey(type))
                    {
                        groupedByType[type] = 0;
                    }
                    groupedByType[type]++;
                }

                foreach (var kvp in groupedByType)
                {
                    GUI.Label(new Rect(contentRect.x + 8, y, contentRect.width - 16, RowHeight),
                              $"  {kvp.Key}: {kvp.Value}",
                              _propertyValueStyle);
                    y += RowHeight;
                }
            }
            else
            {
                // Single-type multi-selection: show summary only (no editing for now)
                GUI.Label(new Rect(contentRect.x, y, contentRect.width, RowHeight),
                          $"  Multiple Selection ({handlers.Count} {handlers[0].Entity.GetType().Name})",
                          _propertyLabelStyle);
                y += RowHeight + Padding;

                GUI.Label(new Rect(contentRect.x + 8, y, contentRect.width - 16, RowHeight),
                          "  Bulk editing coming soon.",
                          _propertyValueStyle);
            }
        }

        private void DrawHandlerProperties(Rect contentRect, EditorHandlerBase handler)
        {
            // Reseed buffers on handler change
            if (!string.Equals(_lastBufferedHandlerId, handler.ID, StringComparison.Ordinal))
            {
                SeedBuffersFromHandler(handler);
                _lastBufferedHandlerId = handler.ID;
            }
            else
            {
                // Update buffers from handler in case properties changed externally
                UpdatedBuffersFromHandler(handler);
            }

            _currentHandler = handler;

            // Calculate view height based on properties
            float viewHeight = CalculateViewHeight(handler);
            var viewRect = new Rect(0f, 0f, contentRect.width - 16f, viewHeight);

            _scrollPosition = GUI.BeginScrollView(contentRect, _scrollPosition, viewRect);

            DrawHandlerPropertyRows(viewRect, handler);

            GUI.EndScrollView();
        }

        private float CalculateViewHeight(EditorHandlerBase handler)
        {
            try
            {
                var properties = handler.GetProperties();
                float height = 2 * RowHeight + Padding * 2; // Header rows (Kind, ID)

                var registry = PropertyRendererRegistry.Instance;
                foreach (var kvp in properties)
                {
                    Type propType = kvp.Value.type;
                    float propHeight = registry.GetPropertyHeight(propType);

                    // Special handling for enum properties that might have open dropdowns
                    if (propType.IsEnum)
                    {
                        int stateKey = propType.GetHashCode() ^ kvp.Key.GetHashCode();
                        propHeight = EnumPropertyRenderer.GetEnumPropertyHeightWithDropdown(stateKey, propType);
                    }

                    height += propHeight + Padding;
                }

                return height;
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FuseEditorPropertiesPanel: Error calculating view height", ex);
                return 5 * RowHeight; // Fallback
            }
        }

        private void DrawHandlerPropertyRows(Rect viewRect, EditorHandlerBase handler)
        {
            var y = viewRect.y;

            try
            {
                // Header: Entity Type
                DrawPropertyLabelRow(y, LabelWidth, viewRect.width,
                                     "Type", handler.Entity?.GetType().Name ?? "<unknown>");
                y += RowHeight + Padding;

                // Header: ID
                DrawPropertyLabelRow(y, LabelWidth, viewRect.width,
                                     "ID", handler.ID ?? "<unknown>");
                y += RowHeight + Padding;

                // Get properties from handler
                var properties = handler.GetProperties();
                var registry = PropertyRendererRegistry.Instance;

                // Draw each property using the appropriate renderer
                foreach (var kvp in properties)
                {
                    string propName = kvp.Key;
                    Type propType = kvp.Value.type;
                    object propValue = kvp.Value.value;

                    try
                    {
                        // Get buffered value if available
                        object currentValue = propValue;

                        // Calculate height for this property (including dropdown if enum is open)
                        float propHeight = registry.GetPropertyHeight(propType);
                        if (propType.IsEnum)
                        {
                            int stateKey = propType.GetHashCode() ^ propName.GetHashCode();
                            propHeight = EnumPropertyRenderer.GetEnumPropertyHeightWithDropdown(stateKey, propType);
                        }

                        // Render the property
                        var (changed, newValue) = registry.RenderProperty(
                            new Rect(viewRect.x, y, viewRect.width, propHeight),
                            propName, propType, currentValue, _propertyLabelStyle, _propertyValueStyle);

                        // Update buffer and apply changes
                        if (changed && !bufferedValuesChanged)
                        {
                            //handler.UpdateProperty(propName, newValue);
                        }

                        y += propHeight + Padding;
                    }
                    catch (Exception ex)
                    {
                        FuseLog.Exception($"FuseEditorPropertiesPanel: Error rendering property '{propName}'", ex);
                        y += RowHeight + Padding;
                    }
                }
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FuseEditorPropertiesPanel: Error drawing properties", ex);
            }
        }

        private void SeedBuffersFromHandler(EditorHandlerBase handler)
        {
            _propertyBuffers.Clear();

            try
            {
                var properties = handler.GetProperties();
                foreach (var kvp in properties)
                {
                    _propertyBuffers[kvp.Key] = kvp.Value.value;
                }
            }
            catch (Exception ex)
            {
                FuseLog.Error($"FuseEditorPropertiesPanel: Error seeding buffers from handler: {ex.Message}");
            }
        }

        private void UpdatedBuffersFromHandler(EditorHandlerBase handler)
        {
            try
            {
                bufferedValuesChanged = false;
                var properties = handler.GetProperties();
                foreach (var kvp in properties)
                {
                    if (_propertyBuffers.ContainsKey(kvp.Key))
                    {
                        if (!_propertyBuffers[kvp.Key].Equals(kvp.Value.value))
                        {
                            _propertyBuffers[kvp.Key] = kvp.Value.value;
                            bufferedValuesChanged = true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FuseEditorPropertiesPanel: Error updating buffers from handler", ex);
            }
        }

        private void DrawPropertyLabelRow(float y, float labelWidth, float totalWidth, string label, string value)
        {
            GUI.Label(new Rect(0f, y, labelWidth, RowHeight), "  " + label, _propertyLabelStyle);
            GUI.Label(new Rect(labelWidth, y, totalWidth - labelWidth, RowHeight), value, _propertyLabelStyle);
        }
    }
}
