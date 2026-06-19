using FUSE.Infrastructure;
using FUSE.Loading;
using Fuse.Core.Model;
using FUSE.Editor.Screen.UI;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEditor;

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
        private static readonly List<IEntityResolver> _entityResolvers = new List<IEntityResolver>
        {
            new DefaultEntityResolver()
        };

        /// <summary>
        /// Registers a custom entity resolver for handling entity types beyond the built-in FUSE types.
        /// Resolvers are tried in registration order; the first resolver that returns true for
        /// <see cref="IEntityResolver.CanResolve(string)"/> will handle the entity lookup.
        /// </summary>
        /// <param name="resolver">The entity resolver to register</param>
        /// <exception cref="ArgumentNullException">Thrown if resolver is null</exception>
        public static void RegisterEntityResolver(IEntityResolver resolver)
        {
            if (resolver == null)
            {
                throw new ArgumentNullException(nameof(resolver));
            }

            _entityResolvers.Add(resolver);
        }
        private const float RowHeight = 22f;
        private const float LabelWidth = 96f;
        private const float Padding = 6f;

        private Vector2 _scrollPosition;
        private string _lastBufferedEntityId;
        private string _currentEntityKind;
        private FuseLoadedMod _currentMod;
        private object _currentEntity;
        private PropertyInfo[] _currentProperties;

        // Per-property IMGUI buffers for editable fields
        private Dictionary<string, string> _propertyBuffers = new Dictionary<string, string>();

        // Separate buffers for vector components
        private Dictionary<string, string> _vectorXBuffers = new Dictionary<string, string>();
        private Dictionary<string, string> _vectorYBuffers = new Dictionary<string, string>();
        private Dictionary<string, string> _vectorZBuffers = new Dictionary<string, string>();

        // GUI styles (provided by caller)
        private GUIStyle _propertyLabelStyle;
        private GUIStyle _propertyValueStyle;


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

            // Store current mod and entity kind for property change application
            _currentMod = mod;
            _currentEntityKind = entityKind;

            // Use the actual runtime type of the entity, not the mapped type
            // This prevents InvalidCastException when the entity's actual type differs from the mapped type
            var actualEntityType = entity.GetType();

            // Reseed buffers on selection change
            if (!string.Equals(_lastBufferedEntityId, entityId, StringComparison.Ordinal))
            {
                SeedBuffersFromEntity(actualEntityType, entity);
                _lastBufferedEntityId = entityId;
            }

            // Draw scrollable properties
            var viewRect = new Rect(0f, 0f, contentRect.width - 16f, CalculateViewHeight(actualEntityType));
            _scrollPosition = GUI.BeginScrollView(contentRect, _scrollPosition, viewRect);

            DrawEntityProperties(viewRect, actualEntityType, entity, mod, entityKind, entityId);

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

                var propType = property.PropertyType;

                // Vector types take one row with multi-column layout
                if (propType == typeof(Vector2))
                {
                    DrawVectorField(new Rect(viewRect.x, y, viewRect.width, RowHeight),
                                  property, entity, property.Name, propType, y);
                    y += RowHeight;
                }
                else if (propType == typeof(Vector3) || propType == typeof(FuseVector3))
                {
                    DrawVectorField(new Rect(viewRect.x, y, viewRect.width, RowHeight),
                                  property, entity, property.Name, propType, y);
                    y += RowHeight;
                }
                else
                {
                    DrawPropertyField(new Rect(viewRect.x, y, viewRect.width, RowHeight),
                                      property, entity, mod, entityKind, entityId);
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

        private void DrawPropertyField(Rect rect, PropertyInfo property, object entity, FuseLoadedMod mod,
                                       string entityKind, string entityId)
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
                DrawEditablePropertyField(rect, property, entity, propName, propType);
            }
        }

        private void DrawEditablePropertyField(Rect rect, PropertyInfo property, object entity, string propName, Type propType)
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

            // Get or create buffer
            if (!_propertyBuffers.TryGetValue(propName, out string bufferValue))
            {
                bufferValue = "";
            }

            string newValue = bufferValue;
            bool changed = false;

            // Type-specific input fields using IMGUI
            if (propType == typeof(string))
            {
                newValue = GUI.TextField(inputRect, bufferValue ?? "", _propertyValueStyle);
                changed = newValue != bufferValue;
            }
            else if (propType == typeof(int))
            {
                newValue = GUI.TextField(inputRect, bufferValue ?? "", _propertyValueStyle);
                if (int.TryParse(newValue, out int _))
                {
                    changed = newValue != bufferValue;
                }
                else if (!string.IsNullOrEmpty(newValue))
                {
                    // Invalid int input, don't accept it
                    newValue = bufferValue;
                }
            }
            else if (propType == typeof(float))
            {
                newValue = GUI.TextField(inputRect, bufferValue ?? "", _propertyValueStyle);
                if (float.TryParse(newValue, out float _))
                {
                    changed = newValue != bufferValue;
                }
                else if (!string.IsNullOrEmpty(newValue))
                {
                    // Invalid float input, don't accept it
                    newValue = bufferValue;
                }
            }
            else if (propType == typeof(bool))
            {
                // For bools, provide a toggle
                if (bool.TryParse(bufferValue, out bool boolValue))
                {
                    bool newBool = GUI.Toggle(inputRect, boolValue, "");
                    newValue = newBool.ToString();
                    changed = newValue != bufferValue;
                }
                else
                {
                    GUI.TextField(inputRect, bufferValue ?? "", _propertyValueStyle);
                }
            }
            else
            {
                // Fallback: show as label
                GUI.Label(inputRect, bufferValue, _propertyValueStyle);
            }

            // Update buffer
            if (changed || !_propertyBuffers.ContainsKey(propName))
            {
                _propertyBuffers[propName] = newValue;
            }

            // Apply changes to entity
            if (changed)
            {
                ApplyPropertyChange(property, entity, propType, newValue);
            }
        }

        private void DrawVectorField(Rect rect, PropertyInfo property, object entity, string propName, Type propType, float y)
        {
            // Get current value
            try
            {
                object value = property.GetValue(entity);

                if (propType == typeof(Vector2))
                {
                    Vector2 vec2 = (Vector2)value;
                    DrawVector2Field(rect, propName, property, entity, vec2, y);
                }
                else if (propType == typeof(Vector3))
                {
                    Vector3 vec3 = (Vector3)value;
                    DrawVector3Field(rect, propName, property, entity, vec3, y);
                }
                else if (propType == typeof(FuseVector3))
                {
                    FuseVector3 fuseVec3 = (FuseVector3)value;
                    Vector3 vec3 = new Vector3(fuseVec3.x, fuseVec3.y, fuseVec3.z);
                    DrawVector3Field(rect, propName, property, entity, vec3, y, isFuseVector: true);
                }
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"Failed to read vector property '{propName}'", ex);
            }
        }

        private void DrawVector2Field(Rect rect, string propName, PropertyInfo property, object entity, Vector2 value, float y)
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

            if (float.TryParse(xValue, out float newX) && newX != value.x)
            {
                value.x = newX;
                ApplyPropertyChange(property, entity, typeof(Vector2), value);
            }

            // Y component  
            var yLabelRect = new Rect(rect.x + LabelWidth + axisLabelWidth + fieldWidth + spacing, y, axisLabelWidth, RowHeight);
            var yFieldRect = new Rect(rect.x + LabelWidth + axisLabelWidth + fieldWidth + spacing + axisLabelWidth, y, fieldWidth, RowHeight);

            GUI.Label(yLabelRect, "Y", _propertyLabelStyle);
            var yValue = DrawAxisInput(yFieldRect, propName, "Y", value.y);

            if (float.TryParse(yValue, out float newY) && newY != value.y)
            {
                value.y = newY;
                ApplyPropertyChange(property, entity, typeof(Vector2), value);
            }
        }

        private void DrawVector3Field(Rect rect, string propName, PropertyInfo property, object entity, Vector3 value, float y, bool isFuseVector = false)
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

            if (float.TryParse(xValue, out float newX) && newX != value.x)
            {
                value.x = newX;
                var newVec = isFuseVector ? (object)new FuseVector3(value.x, value.y, value.z) : (object)value;
                ApplyPropertyChange(property, entity, isFuseVector ? typeof(FuseVector3) : typeof(Vector3), newVec);
            }

            // Y component
            float yOffset = LabelWidth + axisLabelWidth + fieldWidth + spacing;
            var yLabelRect = new Rect(rect.x + yOffset, y, axisLabelWidth, RowHeight);
            var yFieldRect = new Rect(rect.x + yOffset + axisLabelWidth, y, fieldWidth, RowHeight);

            GUI.Label(yLabelRect, "Y", _propertyLabelStyle);
            var yValue = DrawAxisInput(yFieldRect, propName, "Y", value.y);

            if (float.TryParse(yValue, out float newY) && newY != value.y)
            {
                value.y = newY;
                var newVec = isFuseVector ? (object)new FuseVector3(value.x, value.y, value.z) : (object)value;
                ApplyPropertyChange(property, entity, isFuseVector ? typeof(FuseVector3) : typeof(Vector3), newVec);
            }

            // Z component
            float zOffset = LabelWidth + axisLabelWidth * 2 + fieldWidth * 2 + spacing * 2;
            var zLabelRect = new Rect(rect.x + zOffset, y, axisLabelWidth, RowHeight);
            var zFieldRect = new Rect(rect.x + zOffset + axisLabelWidth, y, fieldWidth, RowHeight);

            GUI.Label(zLabelRect, "Z", _propertyLabelStyle);
            var zValue = DrawAxisInput(zFieldRect, propName, "Z", value.z);

            if (float.TryParse(zValue, out float newZ) && newZ != value.z)
            {
                value.z = newZ;
                var newVec = isFuseVector ? (object)new FuseVector3(value.x, value.y, value.z) : (object)value;
                ApplyPropertyChange(property, entity, isFuseVector ? typeof(FuseVector3) : typeof(Vector3), newVec);
            }
        }

        private string DrawAxisInput(Rect fieldRect, string propName, string axis, float currentValue)
        {
            string bufferKey = $"{propName}.{axis}";
            if (!_vectorXBuffers.TryGetValue(bufferKey, out string bufferValue))
            {
                bufferValue = currentValue.ToString();
            }

            string newValue = GUI.TextField(fieldRect, bufferValue, _propertyValueStyle);

            if (!_vectorXBuffers.ContainsKey(bufferKey) || _vectorXBuffers[bufferKey] != newValue)
            {
                _vectorXBuffers[bufferKey] = newValue;
            }

            return newValue;
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

        private void ApplyPropertyChange(PropertyInfo property, object entity, Type propType, string newValue)
        {
            ApplyPropertyChangeInternal(property, entity, propType, newValue);
        }

        private void ApplyPropertyChange(PropertyInfo property, object entity, Type propType, object newValue)
        {
            try
            {
                property.SetValue(entity, newValue);
            }
            catch (Exception ex)
            {
                FuseLog.Warning($"Failed to apply property change '{property.Name}': {ex.Message}");
            }
        }

        private void ApplyPropertyChangeInternal(PropertyInfo property, object entity, Type propType, string newValue)
        {
            try
            {
                object convertedValue = null;

                if (propType == typeof(string))
                {
                    convertedValue = newValue;
                }
                else if (propType == typeof(int))
                {
                    if (int.TryParse(newValue, out int intVal))
                        convertedValue = intVal;
                    else
                        return; // Invalid format, don't apply
                }
                else if (propType == typeof(float))
                {
                    if (float.TryParse(newValue, out float floatVal))
                        convertedValue = floatVal;
                    else
                        return;
                }
                else if (propType == typeof(bool))
                {
                    if (bool.TryParse(newValue, out bool boolVal))
                        convertedValue = boolVal;
                    else
                        return;
                }
                else if (propType == typeof(Vector2))
                {
                    if (TryParseVector2(newValue, out Vector2 vec2Val))
                        convertedValue = vec2Val;
                    else
                        return;
                }
                else if (propType == typeof(Vector3))
                {
                    if (TryParseVector3(newValue, out Vector3 vec3Val))
                        convertedValue = vec3Val;
                    else
                        return;
                }
                else if (propType == typeof(FuseVector3))
                {
                    if (TryParseVector3(newValue, out Vector3 vec3Val))
                        convertedValue = new FuseVector3(vec3Val.x, vec3Val.y, vec3Val.z);
                    else
                        return;
                }
                else
                {
                    return; // Unsupported type
                }

                property.SetValue(entity, convertedValue);
            }
            catch (Exception ex)
            {
                FuseLog.Warning($"Failed to apply property change '{property.Name}': {ex.Message}");
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
                    // Format vector types nicely for parsing
                    string strValue = value?.ToString() ?? string.Empty;
                    if (value is Vector2 vec2)
                        strValue = $"({vec2.x}, {vec2.y})";
                    else if (value is Vector3 vec3)
                        strValue = $"({vec3.x}, {vec3.y}, {vec3.z})";

                    _propertyBuffers[property.Name] = strValue;
                }
                catch (Exception ex)
                {
                    // Log and skip properties that fail to read
                    FuseLog.Exception($"Failed to read property '{property.Name}' from {entity?.GetType().Name}", ex);
                    _propertyBuffers[property.Name] = "<error>";
                }
            }
        }

        private object GetEntityInstance(FuseLoadedMod mod, string entityKind, string entityId)
        {
            if (mod?.Definition == null)
            {
                return null;
            }

            // Try each resolver in order. The first resolver that can handle this entity kind
            // will be used to look it up. This allows built-in types to be resolved first,
            // and custom types to be handled by registered resolvers.
            foreach (var resolver in _entityResolvers)
            {
                if (resolver.CanResolve(entityKind))
                {
                    return resolver.TryResolveEntity(mod, entityKind, entityId);
                }
            }

            return null;
        }

        public void Clear()
        {
            _scrollPosition = Vector2.zero;
            _lastBufferedEntityId = null;
            _currentMod = null;
            _currentEntityKind = null;
            _propertyBuffers.Clear();
            _vectorXBuffers.Clear();
            _vectorYBuffers.Clear();
            _vectorZBuffers.Clear();
        }
    }
}
