using Fuse.Core.Model;
using FUSE.Editor.Screen.UI;
using FUSE.Infrastructure;
using FUSE.Loading;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using static Core.SpatialHashLinear;

namespace FUSE.Editor.Screen
{
    /// <summary>
    /// Standalone properties panel for the FuseEditorScreen. Automatically generates
    /// property editors based on the selected entity's type using the FuseEditor.EntityTypeMap.
    /// Supports single-selection with type-specific property editing, and displays a
    /// "multi-type editing not supported" message when multiple different entity types
    /// are selected.
    /// 
    /// Entity resolution is handled via a pluggable resolver system, allowing external
    /// mods to register custom entity resolvers for their own entity types.
    /// </summary>
    internal sealed class FuseEditorPropertiesPanel
    {
        /// <summary>
        /// List of entity resolvers used to look up entities by kind.
        /// The default resolver is always registered first and handles all built-in FUSE types.
        /// External mods can register additional resolvers via <see cref="RegisterEntityResolver"/>.
        /// </summary>

        /// <summary>
        /// Registers a custom entity resolver for handling entity types beyond the built-in FUSE types.
        /// Resolvers are tried in registration order; the first resolver that returns true for
        /// <see cref="IEntityResolver.CanResolve(string)"/> will handle the entity lookup.
        /// </summary>
        /// <param name="resolver">The entity resolver to register</param>
        /// <exception cref="ArgumentNullException">Thrown if resolver is null</exception>

        private const float RowHeight = 22f;
        private const float LabelWidth = 96f;
        private const float Padding = 6f;

        private Vector2 _scrollPosition;
        private string _lastBufferedEntityId;
        private object _currentEntityObject;
        private FuseLoadedMod _currentMod;
        private object _currentEntity;
        private PropertyInfo[] _currentProperties;

        // Per-property IMGUI buffers for editable fields
        private Dictionary<string, object> _propertyBuffers = new Dictionary<string, object>();

        // Buffer for vector components (key format: "propertyName.X", "propertyName.Y", etc.)
        private Dictionary<string, float> _axisBuffers = new Dictionary<string, float>();

        // Queue for deferred property changes to avoid lock contention
        private Dictionary<string, object> _pendingPropertyChanges = new Dictionary<string, object>();
        private string _pendingEntityId;

        // GUI styles (provided by caller)
        private GUIStyle _propertyLabelStyle;
        private GUIStyle _propertyValueStyle;


        public FuseEditorPropertiesPanel()
        {
        }

        /// <summary>
        /// Draws the properties panel into the given rect.
        /// </summary>
        public void Draw(Rect panelRect, List<object> selectedEntityObjects, List<string> selectedEntityIds,
                        GUIStyle propertyLabelStyle, GUIStyle propertyValueStyle, GUIStyle toolButtonStyle)
        {
            _propertyLabelStyle = propertyLabelStyle;
            _propertyValueStyle = propertyValueStyle;

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
                DrawMultiSelectionState(contentRect, selectedEntityObjects, selectedEntityIds);
                return;
            }

            // Single selection: draw type-specific properties
            DrawSingleSelectionProperties(contentRect, selectedEntityObjects[0], selectedEntityIds[0]);
        }

        private void DrawMultiSelectionState(Rect contentRect, List<object> selectedEntityObjects, List<string> selectedEntityIds)
        {
            var y = contentRect.y;

            // Check if we have multiple different types
            var uniqueTypes = new HashSet<string>(selectedEntityObjects.Select(x => x.GetType().Name));
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
                foreach (var kind in selectedEntityObjects.Select(x => x.GetType().Name))
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
                          $"  Multiple Selection ({selectedEntityIds.Count} {selectedEntityObjects[0]})",
                          _propertyLabelStyle);
                y += RowHeight + Padding;

                GUI.Label(new Rect(contentRect.x + 8, y, contentRect.width - 16, RowHeight),
                          "  Bulk editing coming soon.",
                          _propertyValueStyle);
            }
        }

        private void DrawSingleSelectionProperties(Rect contentRect, object entityObject, string entityId)
        {
            // Get the entity instance from the active mod
            var mod = FuseEditor.Instance?.ActiveMod;
            if (mod == null)
            {
                GUI.Label(contentRect, "  No active mod", _propertyLabelStyle);
                return;
            }

            // Store current mod and entity kind for property change application
            _currentMod = mod;
            _currentEntityObject = entityObject;

            // Use the actual runtime type of the entity, not the mapped type
            // This prevents InvalidCastException when the entity's actual type differs from the mapped type
            var actualEntityType = entityObject.GetType();

            // Reseed buffers on selection change
            if (!string.Equals(_lastBufferedEntityId, entityId, StringComparison.Ordinal))
            {
                SeedBuffersFromEntity(actualEntityType, entityObject);
                _lastBufferedEntityId = entityId;
            }

            // Draw scrollable properties
            var viewRect = new Rect(0f, 0f, contentRect.width - 16f, CalculateViewHeight(actualEntityType));
            _scrollPosition = GUI.BeginScrollView(contentRect, _scrollPosition, viewRect);

            DrawEntityProperties(viewRect, actualEntityType, entityObject, mod, entityId);

            GUI.EndScrollView();
        }

        private float CalculateViewHeight(Type entityType)
        {
            // Estimate: 2 header rows + property count * row height
            var properties = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            return (2 + properties.Length) * RowHeight + Padding * 2;
        }

        private void DrawEntityProperties(Rect viewRect, Type entityType, object entity, FuseLoadedMod mod, string entityId)
        {
            var y = viewRect.y;

            // Header: Kind
            DrawPropertyLabelRow(y, LabelWidth, viewRect.width,
                                 FuseEditorStrings.Get("fuse.editor.properties.kind"), entityType.Name);
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

                var propType = property.PropertyType;

                // Vector types take one row with multi-column layout
                if (propType == typeof(Vector2))
                {
                    DrawVectorField(new Rect(viewRect.x, y, viewRect.width, RowHeight),
                                  property, entity, entityId, property.Name, propType, y);
                    y += RowHeight;
                }
                else if (propType == typeof(Vector3) || propType == typeof(FuseVector3))
                {
                    DrawVectorField(new Rect(viewRect.x, y, viewRect.width, RowHeight),
                                  property, entity, entityId, property.Name, propType, y);
                    y += RowHeight;
                }
                else
                {
                    DrawPropertyField(new Rect(viewRect.x, y, viewRect.width, RowHeight),
                                      property, entity, mod, entityId);
                    y += RowHeight;
                }
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
                   propType == typeof(FuseVector3) ||
                   propType == typeof(Vector3) ||
                   propType == typeof(Vector2) ||
                   !property.CanWrite; // Read-only properties are ok (we'll show labels)
        }

        private void DrawPropertyField(Rect rect, PropertyInfo property, object entity, FuseLoadedMod mod, string entityId)
        {
            var propName = property.Name;
            var propType = property.PropertyType;
            string valueStr = "<error>";

            try
            {
                object value = property.GetValue(entity);
                valueStr = value?.ToString() ?? "<null>";
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"Failed to read property '{propName}' from {entity?.GetType().Name}", ex);
                valueStr = "<error>";
            }

            if (!property.CanWrite)
            {
                // Read-only property: just show as label
                DrawPropertyLabelRow(rect.y, LabelWidth, rect.width, propName, valueStr);
            }
            else
            {
                // Editable property: show type-specific input field
                DrawEditablePropertyField(rect, property, entity, entityId, propName, propType);
            }
        }

        private void DrawEditablePropertyField(Rect rect, PropertyInfo property, object entity, string entityId, string propName, Type propType)
        {
            // For vectors, we need to handle specially. Return early to let the parent handle layout
            if (propType == typeof(Vector2) || propType == typeof(Vector3) || propType == typeof(FuseVector3))
            {
                // Vectors are handled in DrawEntityProperties with multi-row layout
                return;
            }

            // Label
            GUI.Label(new Rect(rect.x, rect.y, LabelWidth, RowHeight),
                      "  " + propName, _propertyLabelStyle);

            // Input field area stretches to fill remaining width
            var inputRect = new Rect(rect.x + LabelWidth, rect.y, rect.width - LabelWidth - Padding, RowHeight);

            // Get or create buffer from current entity value if not present
            if (!_propertyBuffers.TryGetValue(propName, out object bufferValue))
            {
                bufferValue = property.GetValue(entity);
                _propertyBuffers[propName] = bufferValue;
            }

            object newValue = bufferValue;
            bool changed = false;

            // Type-specific input fields using IMGUI
            if (propType == typeof(string))
            {
                newValue = GUI.TextField(inputRect, (string)bufferValue ?? "", _propertyValueStyle);
                changed = (string)newValue != (string)bufferValue;
            }
            else if (propType == typeof(int))
            {
                string displayStr = ((int)bufferValue).ToString();
                string inputStr = GUI.TextField(inputRect, displayStr, _propertyValueStyle);

                if (int.TryParse(inputStr, out int newInt))
                {
                    newValue = newInt;
                    changed = newInt != (int)bufferValue;
                }
                else if (inputStr != displayStr)
                {
                    // User typed something that doesn't parse, but don't revert immediately
                    // Keep the display as-is and don't apply
                    newValue = bufferValue;
                    changed = false;
                }
            }
            else if (propType == typeof(float))
            {
                string displayStr = ((float)bufferValue).ToString();
                string inputStr = GUI.TextField(inputRect, displayStr, _propertyValueStyle);

                if (float.TryParse(inputStr, out float newFloat))
                {
                    newValue = newFloat;
                    changed = !Mathf.Approximately(newFloat, (float)bufferValue);
                }
                else if (inputStr != displayStr)
                {
                    // User typed something that doesn't parse, but don't revert immediately
                    newValue = bufferValue;
                    changed = false;
                }
            }
            else if (propType == typeof(bool))
            {
                // For bools, provide a toggle
                bool newBool = GUI.Toggle(inputRect, (bool)bufferValue, "");
                newValue = newBool;
                changed = newBool != (bool)bufferValue;
            }
            else
            {
                // Fallback: show as label
                GUI.Label(inputRect, bufferValue?.ToString() ?? "<null>", _propertyValueStyle);
            }

            // Update buffer and apply changes only if actually changed
            if (changed)
            {
                _propertyBuffers[propName] = newValue;
                ApplyPropertyChange(property, entity, entityId, propType, newValue);
            }
        }

        private void DrawVectorField(Rect rect, PropertyInfo property, object entity, string entityId, string propName, Type propType, float y)
        {
            // Get current value
            try
            {
                object value = property.GetValue(entity);

                if (propType == typeof(Vector2))
                {
                    Vector2 vec2 = (Vector2)value;
                    DrawVector2Field(rect, propName, property, entity, entityId, vec2, y);
                }
                else if (propType == typeof(Vector3))
                {
                    Vector3 vec3 = (Vector3)value;
                    DrawVector3Field(rect, propName, property, entity, entityId, vec3, y);
                }
                else if (propType == typeof(FuseVector3))
                {
                    FuseVector3 fuseVec3 = (FuseVector3)value;
                    Vector3 vec3 = new Vector3(fuseVec3.x, fuseVec3.y, fuseVec3.z);
                    DrawVector3Field(rect, propName, property, entity, entityId, vec3, y, isFuseVector: true);
                }
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"Failed to read vector property '{propName}'", ex);
            }
        }

        private void DrawVector2Field(Rect rect, string propName, PropertyInfo property, object entity, string entityId, Vector2 value, float y)
        {
            const float axisLabelWidth = 16f;
            const float spacing = Padding;

            // Main label
            GUI.Label(new Rect(rect.x, y, LabelWidth, RowHeight), "  " + propName, _propertyLabelStyle);

            // Calculate available width for input fields
            float availableWidth = rect.width - LabelWidth - spacing;
            // Divide equally between 2 axes, accounting for axis labels and spacing
            float fieldWidth = (availableWidth - axisLabelWidth * 2 - spacing) / 2f;

            // X component
            var xLabelRect = new Rect(rect.x + LabelWidth, y, axisLabelWidth, RowHeight);
            var xFieldRect = new Rect(rect.x + LabelWidth + axisLabelWidth, y, fieldWidth, RowHeight);

            GUI.Label(xLabelRect, "X", _propertyLabelStyle);
            var xValue = DrawAxisInput(xFieldRect, propName, "X", value.x);

            bool xChanged = false;
            float newX = value.x;
            if (float.TryParse(xValue, out float parsedX))
            {
                xChanged = !Mathf.Approximately(parsedX, value.x);
                newX = parsedX;
            }

            // Y component  
            var yLabelRect = new Rect(rect.x + LabelWidth + axisLabelWidth + fieldWidth + spacing, y, axisLabelWidth, RowHeight);
            var yFieldRect = new Rect(rect.x + LabelWidth + axisLabelWidth + fieldWidth + spacing + axisLabelWidth, y, fieldWidth, RowHeight);

            GUI.Label(yLabelRect, "Y", _propertyLabelStyle);
            var yValue = DrawAxisInput(yFieldRect, propName, "Y", value.y);

            bool yChanged = false;
            float newY = value.y;
            if (float.TryParse(yValue, out float parsedY))
            {
                yChanged = !Mathf.Approximately(parsedY, value.y);
                newY = parsedY;
            }

            // Apply only if something actually changed
            if (xChanged || yChanged)
            {
                Vector2 newVec = new Vector2(newX, newY);
                ApplyPropertyChange(property, entity, entityId, typeof(Vector2), newVec);
            }
        }

        private void DrawVector3Field(Rect rect, string propName, PropertyInfo property, object entity, string entityId, Vector3 value, float y, bool isFuseVector = false)
        {
            const float axisLabelWidth = 16f;
            const float spacing = Padding;

            // Main label
            GUI.Label(new Rect(rect.x, y, LabelWidth, RowHeight), "  " + propName, _propertyLabelStyle);

            // Calculate available width for input fields
            float availableWidth = rect.width - LabelWidth - spacing;
            // Divide equally between 3 axes, accounting for axis labels and spacing
            float fieldWidth = (availableWidth - axisLabelWidth * 3 - spacing * 2) / 3f;

            // X component
            var xLabelRect = new Rect(rect.x + LabelWidth, y, axisLabelWidth, RowHeight);
            var xFieldRect = new Rect(rect.x + LabelWidth + axisLabelWidth, y, fieldWidth, RowHeight);

            GUI.Label(xLabelRect, "X", _propertyLabelStyle);
            var xValue = DrawAxisInput(xFieldRect, propName, "X", value.x);

            bool xChanged = false;
            float newX = value.x;
            if (float.TryParse(xValue, out float parsedX))
            {
                xChanged = !Mathf.Approximately(parsedX, value.x);
                newX = parsedX;
            }

            // Y component
            float yOffset = LabelWidth + axisLabelWidth + fieldWidth + spacing;
            var yLabelRect = new Rect(rect.x + yOffset, y, axisLabelWidth, RowHeight);
            var yFieldRect = new Rect(rect.x + yOffset + axisLabelWidth, y, fieldWidth, RowHeight);

            GUI.Label(yLabelRect, "Y", _propertyLabelStyle);
            var yValue = DrawAxisInput(yFieldRect, propName, "Y", value.y);

            bool yChanged = false;
            float newY = value.y;
            if (float.TryParse(yValue, out float parsedY))
            {
                yChanged = !Mathf.Approximately(parsedY, value.y);
                newY = parsedY;
            }

            // Z component
            float zOffset = LabelWidth + axisLabelWidth * 2 + fieldWidth * 2 + spacing * 2;
            var zLabelRect = new Rect(rect.x + zOffset, y, axisLabelWidth, RowHeight);
            var zFieldRect = new Rect(rect.x + zOffset + axisLabelWidth, y, fieldWidth, RowHeight);

            GUI.Label(zLabelRect, "Z", _propertyLabelStyle);
            var zValue = DrawAxisInput(zFieldRect, propName, "Z", value.z);

            bool zChanged = false;
            float newZ = value.z;
            if (float.TryParse(zValue, out float parsedZ))
            {
                zChanged = !Mathf.Approximately(parsedZ, value.z);
                newZ = parsedZ;
            }

            // Apply only if something actually changed
            if (xChanged || yChanged || zChanged)
            {
                object newVec = isFuseVector 
                    ? (object)new FuseVector3(newX, newY, newZ) 
                    : (object)new Vector3(newX, newY, newZ);
                ApplyPropertyChange(property, entity, entityId, isFuseVector ? typeof(FuseVector3) : typeof(Vector3), newVec);
            }
        }

        private string DrawAxisInput(Rect fieldRect, string propName, string axis, float currentValue)
        {
            string bufferKey = $"{propName}.{axis}";

            // Get or initialize buffer with current value if needed
            if (!_axisBuffers.TryGetValue(bufferKey, out float bufferValue))
            {
                bufferValue = currentValue;
                _axisBuffers[bufferKey] = bufferValue;
            }

            // Draw input field
            string newValueStr = GUI.TextField(fieldRect, bufferValue.ToString(), _propertyValueStyle);

            // Update buffer if parsing succeeds
            if (float.TryParse(newValueStr, out float newFloat))
            {
                _axisBuffers[bufferKey] = newFloat;
            }

            return newValueStr;
        }

        private bool TryParseVector2(string value, out Vector2 result)
        {
            result = Vector2.zero;
            if (string.IsNullOrEmpty(value))
                return false;

            // Try parsing formats like "(1.5, 2.3)" or "1.5, 2.3"
            var trimmed = value.Trim().Trim('(', ')');
            var parts = trimmed.Split(',');

            if (parts.Length == 2 &&
                float.TryParse(parts[0].Trim(), out float x) &&
                float.TryParse(parts[1].Trim(), out float y))
            {
                result = new Vector2(x, y);
                return true;
            }

            return false;
        }

        private bool TryParseVector3(string value, out Vector3 result)
        {
            result = Vector3.zero;
            if (string.IsNullOrEmpty(value))
                return false;

            // Try parsing formats like "(1.5, 2.3, 3.1)" or "1.5, 2.3, 3.1"
            var trimmed = value.Trim().Trim('(', ')');
            var parts = trimmed.Split(',');

            if (parts.Length == 3 &&
                float.TryParse(parts[0].Trim(), out float x) &&
                float.TryParse(parts[1].Trim(), out float y) &&
                float.TryParse(parts[2].Trim(), out float z))
            {
                result = new Vector3(x, y, z);
                return true;
            }

            return false;
        }

        private void ApplyPropertyChange(PropertyInfo property, object entity, string entityId, Type propType, object newValue)
        {
            // Queue the change instead of applying immediately to avoid lock contention
            // during the draw cycle. Changes will be applied by the caller after drawing is complete.
            _pendingPropertyChanges[property.Name] = newValue;
            _pendingEntityId = entityId;
        }

        /// <summary>
        /// Flushes any pending property changes. This should be called after the Draw cycle completes
        /// to avoid ReaderWriterLockSlim promotion errors. Returns true if changes were applied.
        /// </summary>
        public bool FlushPendingChanges()
        {
            if (_pendingPropertyChanges.Count == 0)
            {
                return false;
            }

            try
            {
                foreach (var kvp in _pendingPropertyChanges)
                {
                    string propName = kvp.Key;
                    object newValue = kvp.Value;

                    // Find the property info
                    var property = _currentEntityObject?.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
                    if (property != null && _currentEntityObject != null)
                    {
                        property.SetValue(_currentEntityObject, newValue);
                        FuseEditor.Instance.EntitySelection.EntityHandler.ApplyEntity(_pendingEntityId, _currentEntityObject);
                        FuseLog.Info($"Applied queued change to '{propName}': {newValue}. Entity ID: {_pendingEntityId}");
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                FuseLog.Warning($"Failed to flush pending property changes: {ex.Message}");
                return false;
            }
            finally
            {
                _pendingPropertyChanges.Clear();
                _pendingEntityId = null;
            }
        }

        private void DrawPropertyLabelRow(float y, float labelWidth, float totalWidth, string label, string value)
        {
            GUI.Label(new Rect(0f, y, labelWidth, RowHeight), "  " + label, _propertyLabelStyle);
            GUI.Label(new Rect(labelWidth, y, totalWidth - labelWidth, RowHeight), value, _propertyLabelStyle);
        }

        private void SeedBuffersFromEntity(Type entityType, object entity)
        {
            _propertyBuffers.Clear();
            _axisBuffers.Clear();

            if (entity == null)
            {
                return;
            }

            var properties = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var property in properties)
            {
                // Include all editable properties, writable or not
                if (!IsEditableProperty(property))
                {
                    continue;
                }

                try
                {
                    object value = property.GetValue(entity);
                    var propType = property.PropertyType;

                    // Store scalar value in property buffers
                    _propertyBuffers[property.Name] = value;

                    // Initialize axis buffers for vector types
                    if (propType == typeof(Vector2) && value is Vector2 vec2)
                    {
                        _axisBuffers[$"{property.Name}.X"] = vec2.x;
                        _axisBuffers[$"{property.Name}.Y"] = vec2.y;
                    }
                    else if (propType == typeof(Vector3) && value is Vector3 vec3)
                    {
                        _axisBuffers[$"{property.Name}.X"] = vec3.x;
                        _axisBuffers[$"{property.Name}.Y"] = vec3.y;
                        _axisBuffers[$"{property.Name}.Z"] = vec3.z;
                    }
                    else if (propType == typeof(FuseVector3) && value is FuseVector3 fuseVec3)
                    {
                        _axisBuffers[$"{property.Name}.X"] = fuseVec3.x;
                        _axisBuffers[$"{property.Name}.Y"] = fuseVec3.y;
                        _axisBuffers[$"{property.Name}.Z"] = fuseVec3.z;
                    }
                }
                catch (Exception ex)
                {
                    // Log and skip properties that fail to read
                    FuseLog.Exception($"Failed to read property '{property.Name}' from {entity?.GetType().Name}", ex);
                    _propertyBuffers[property.Name] = "<error>";
                }
            }
        }

        public void Clear()
        {
            _scrollPosition = Vector2.zero;
            _lastBufferedEntityId = null;
            _currentMod = null;
            _currentEntityObject = null;
            _propertyBuffers.Clear();
            _axisBuffers.Clear();
            _pendingPropertyChanges.Clear();
            _pendingEntityId = null;
        }
    }
}
