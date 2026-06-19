using FUSE.Infrastructure;
using FUSE.Loading;
using Fuse.Core.Model;
using FUSE.Editor.Screen.UI;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace FUSE.Editor.Screen
{
    /// <summary>
    /// Standalone properties panel for the FuseEditorScreen. Automatically generates
    /// property editors based on the selected entity's type using the FuseEditor.EntityTypeMap.
    /// Supports single-selection with type-specific property editing, and displays a
    /// "multi-type editing not supported" message when multiple different entity types
    /// are selected.
    /// </summary>
    internal sealed class FuseEditorPropertiesPanel
    {
        private const float RowHeight = 22f;
        private const float LabelWidth = 96f;
        private const float Padding = 6f;

        private Vector2 _scrollPosition;
        private string _lastBufferedEntityId;

        // Per-property IMGUI buffers for editable fields
        private Dictionary<string, string> _propertyBuffers = new Dictionary<string, string>();

        // GUI styles (provided by caller)
        private GUIStyle _propertyLabelStyle;
        private GUIStyle _propertyValueStyle;
        private GUIStyle _toolButtonStyle;

        public FuseEditorPropertiesPanel()
        {
        }

        /// <summary>
        /// Draws the properties panel into the given rect.
        /// </summary>
        public void Draw(Rect panelRect, List<string> selectedEntityKinds, List<string> selectedEntityIds,
                        GUIStyle propertyLabelStyle, GUIStyle propertyValueStyle, GUIStyle toolButtonStyle)
        {
            _propertyLabelStyle = propertyLabelStyle;
            _propertyValueStyle = propertyValueStyle;
            _toolButtonStyle = toolButtonStyle;

            var contentRect = new Rect(panelRect.x + Padding,
                                       panelRect.y + Padding,
                                       panelRect.width - Padding * 2,
                                       panelRect.height - Padding * 2);

            // Handle empty selection
            if (selectedEntityIds.Count == 0)
            {
                GUI.Label(contentRect, "  " + FuseEditorStrings.Get("fuse.editor.properties.empty_hint"), propertyLabelStyle);
                return;
            }

            // Handle multi-selection with multiple types
            if (selectedEntityIds.Count > 1)
            {
                DrawMultiSelectionState(contentRect, selectedEntityKinds, selectedEntityIds);
                return;
            }

            // Single selection: draw type-specific properties
            DrawSingleSelectionProperties(contentRect, selectedEntityKinds[0], selectedEntityIds[0]);
        }

        private void DrawMultiSelectionState(Rect contentRect, List<string> selectedEntityKinds, List<string> selectedEntityIds)
        {
            var y = contentRect.y;

            // Check if we have multiple different types
            var uniqueTypes = new HashSet<string>(selectedEntityKinds);
            if (uniqueTypes.Count > 1)
            {
                // Multi-type selection: show unsupported message
                GUI.Label(new Rect(contentRect.x, y, contentRect.width, RowHeight),
                          $"  Multiple Selection ({selectedEntityIds.Count} entities)",
                          _propertyLabelStyle);
                y += RowHeight + Padding;

                GUI.Label(new Rect(contentRect.x + 8, y, contentRect.width - 16, RowHeight * 2),
                          "  Multi-type editing is not supported.\n  Select entities of a single type to edit properties.",
                          _propertyValueStyle);
                y += RowHeight * 2 + Padding;

                // Group selected entities by kind for display
                var groupedByKind = new Dictionary<string, int>();
                foreach (var kind in selectedEntityKinds)
                {
                    if (!groupedByKind.ContainsKey(kind))
                    {
                        groupedByKind[kind] = 0;
                    }
                    groupedByKind[kind]++;
                }

                foreach (var kvp in groupedByKind)
                {
                    GUI.Label(new Rect(contentRect.x + 8, y, contentRect.width - 16, RowHeight),
                              $"  {kvp.Key}: {kvp.Value}",
                              _propertyValueStyle);
                    y += RowHeight;
                }
            }
            else
            {
                // Single-type multi-selection: show summary only (no editing)
                GUI.Label(new Rect(contentRect.x, y, contentRect.width, RowHeight),
                          $"  Multiple Selection ({selectedEntityIds.Count} {selectedEntityKinds[0]})",
                          _propertyLabelStyle);
                y += RowHeight + Padding;

                GUI.Label(new Rect(contentRect.x + 8, y, contentRect.width - 16, RowHeight),
                          "  Bulk editing coming soon.",
                          _propertyValueStyle);
            }
        }

        private void DrawSingleSelectionProperties(Rect contentRect, string entityKind, string entityId)
        {
            // Get the entity type from the map
            if (!FuseEditor.TryGetEntityType(entityKind, out Type entityType))
            {
                GUI.Label(contentRect, $"  Unknown entity type: {entityKind}", _propertyLabelStyle);
                return;
            }

            // Get the entity instance from the active mod
            var mod = FuseEditor.Instance?.ActiveMod;
            if (mod == null)
            {
                GUI.Label(contentRect, "  No active mod", _propertyLabelStyle);
                return;
            }

            var entity = GetEntityInstance(mod, entityKind, entityId);
            if (entity == null)
            {
                GUI.Label(contentRect, $"  Entity not found: {entityKind}/{entityId}", _propertyLabelStyle);
                return;
            }

            // Reseed buffers on selection change
            if (!string.Equals(_lastBufferedEntityId, entityId, StringComparison.Ordinal))
            {
                SeedBuffersFromEntity(entityType, entity);
                _lastBufferedEntityId = entityId;
            }

            // Draw scrollable properties
            var viewRect = new Rect(0f, 0f, contentRect.width - 16f, CalculateViewHeight(entityType));
            _scrollPosition = GUI.BeginScrollView(contentRect, _scrollPosition, viewRect);

            DrawEntityProperties(viewRect, entityType, entity, mod, entityKind, entityId);

            GUI.EndScrollView();
        }

        private float CalculateViewHeight(Type entityType)
        {
            // Estimate: 2 header rows + property count * row height
            var properties = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            return (2 + properties.Length) * RowHeight + Padding * 2;
        }

        private void DrawEntityProperties(Rect viewRect, Type entityType, object entity, FuseLoadedMod mod,
                                         string entityKind, string entityId)
        {
            var y = viewRect.y;

            // Header: Kind
            DrawPropertyLabelRow(y, LabelWidth, viewRect.width,
                                 FuseEditorStrings.Get("fuse.editor.properties.kind"), entityKind);
            y += RowHeight;

            // Header: Id
            DrawPropertyLabelRow(y, LabelWidth, viewRect.width,
                                 FuseEditorStrings.Get("fuse.editor.properties.id"), entityId);
            y += RowHeight;

            // Dynamic properties from the entity type
            var properties = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var property in properties)
            {
                // Skip properties that are too complex to edit simply
                if (!IsEditableProperty(property))
                {
                    continue;
                }

                DrawPropertyField(new Rect(viewRect.x, y, viewRect.width, RowHeight),
                                  property, entity, mod, entityKind, entityId);
                y += RowHeight;
            }
        }

        private bool IsEditableProperty(PropertyInfo property)
        {
            // Only support simple types for now
            var propType = property.PropertyType;
            return propType == typeof(string) ||
                   propType == typeof(int) || 
                   propType == typeof(float) ||
                   propType == typeof(bool) ||
                   propType == typeof(Vector3) ||
                   !property.CanWrite; // Read-only properties are ok (we'll show labels)
        }

        private void DrawPropertyField(Rect rect, PropertyInfo property, object entity, FuseLoadedMod mod,
                                       string entityKind, string entityId)
        {
            var propName = property.Name;
            var value = property.GetValue(entity);
            var valueStr = value?.ToString() ?? "<null>";

            if (!property.CanWrite)
            {
                // Read-only property: just show as label
                DrawPropertyLabelRow(rect.y, LabelWidth, rect.width, propName, valueStr);
            }
            else
            {
                // Editable property: show as edited field (simplified)
                GUI.Label(new Rect(rect.x, rect.y, LabelWidth, RowHeight),
                          "  " + propName, _propertyLabelStyle);
                GUI.Label(new Rect(rect.x + LabelWidth, rect.y, rect.width - LabelWidth, RowHeight),
                          valueStr, _propertyValueStyle);
            }
        }

        private void DrawPropertyLabelRow(float y, float labelWidth, float totalWidth, string label, string value)
        {
            GUI.Label(new Rect(0f, y, labelWidth, RowHeight), "  " + label, _propertyLabelStyle);
            GUI.Label(new Rect(labelWidth, y, totalWidth - labelWidth, RowHeight), value, _propertyValueStyle);
        }

        private void SeedBuffersFromEntity(Type entityType, object entity)
        {
            _propertyBuffers.Clear();

            if (entity == null)
            {
                return;
            }

            var properties = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var property in properties)
            {
                if (!IsEditableProperty(property) || !property.CanWrite)
                {
                    continue;
                }

                var value = property.GetValue(entity);
                _propertyBuffers[property.Name] = value?.ToString() ?? string.Empty;
            }
        }

        private object GetEntityInstance(FuseLoadedMod mod, string entityKind, string entityId)
        {
            if (mod?.Definition == null)
            {
                return null;
            }

            // Get the appropriate collection from the definition based on entity kind
            var definition = mod.Definition;

            switch (entityKind)
            {
                // Track entities
                case "Node":
                    if (definition.Tracks?.Nodes != null && definition.Tracks.Nodes.TryGetValue(entityId, out var node))
                        return node;
                    break;
                case "Segment":
                    if (definition.Tracks?.Segments != null && definition.Tracks.Segments.TryGetValue(entityId, out var segment))
                        return segment;
                    break;
                case "Span":
                    if (definition.Tracks?.Spans != null && definition.Tracks.Spans.TryGetValue(entityId, out var span))
                        return span;
                    break;
                case "Area":
                    if (definition.Tracks?.Areas != null && definition.Tracks.Areas.TryGetValue(entityId, out var area))
                        return area;
                    break;

                // World entities
                case "Scenery":
                    if (definition.World?.Scenery != null && definition.World.Scenery.TryGetValue(entityId, out var scenery))
                        return scenery;
                    break;
                case "Spliney":
                    if (definition.World?.Splineys != null && definition.World.Splineys.TryGetValue(entityId, out var spliney))
                        return spliney;
                    break;
                case "MapLabel":
                    if (definition.World?.MapLabels != null && definition.World.MapLabels.TryGetValue(entityId, out var mapLabel))
                        return mapLabel;
                    break;
                case "Telegraph":
                    if (definition.World?.TelegraphPoles != null && definition.World.TelegraphPoles.TryGetValue(entityId, out var telegraph))
                        return telegraph;
                    break;

                // Operations entities
                case "Industry":
                    if (definition.Operations?.Industries != null && definition.Operations.Industries.TryGetValue(entityId, out var industry))
                        return industry;
                    break;
                case "Load":
                    if (definition.Operations?.Loads != null && definition.Operations.Loads.TryGetValue(entityId, out var load))
                        return load;
                    break;
                case "Station":
                    if (definition.Operations?.Stations != null && definition.Operations.Stations.TryGetValue(entityId, out var station))
                        return station;
                    break;
                case "Turntable":
                    if (definition.Operations?.Turntables != null && definition.Operations.Turntables.TryGetValue(entityId, out var turntable))
                        return turntable;
                    break;
                case "Loader":
                    if (definition.Operations?.Loaders != null && definition.Operations.Loaders.TryGetValue(entityId, out var loader))
                        return loader;
                    break;
            }

            return null;
        }

        public void Clear()
        {
            _scrollPosition = Vector2.zero;
            _lastBufferedEntityId = null;
            _propertyBuffers.Clear();
        }
    }
}
