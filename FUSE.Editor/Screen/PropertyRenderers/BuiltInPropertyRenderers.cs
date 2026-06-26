using System;
using System.Collections.Generic;
using UnityEngine;

namespace FUSE.Editor.Screen.PropertyRenderers
{
    /// <summary>
    /// Renders string properties as text input fields.
    /// </summary>
    public class StringPropertyRenderer : IPropertyRenderer
    {
        private const float RowHeight = 22f;
        private const float LabelWidth = 96f;
        private const float Padding = 6f;

        public bool CanRender(Type propertyType) => propertyType == typeof(string);

        public (bool changed, object newValue) RenderProperty(Rect rect, string propertyName, Type propertyType,
                                                               object currentValue, GUIStyle labelStyle, GUIStyle valueStyle)
        {
            var labelRect = new Rect(rect.x, rect.y, LabelWidth, RowHeight);
            var inputRect = new Rect(rect.x + LabelWidth, rect.y, rect.width - LabelWidth - Padding, RowHeight);

            GUI.Label(labelRect, "  " + propertyName, labelStyle);
            string currentStr = (string)currentValue ?? "";
            string newValue = GUI.TextField(inputRect, currentStr, valueStyle);

            bool changed = newValue != currentStr;
            return (changed, newValue);
        }

        public float GetPropertyHeight(Type propertyType) => 22f;
    }

    /// <summary>
    /// Renders integer properties with text input and validation.
    /// </summary>
    public class IntPropertyRenderer : IPropertyRenderer
    {
        private const float RowHeight = 22f;
        private const float LabelWidth = 96f;
        private const float Padding = 6f;

        public bool CanRender(Type propertyType) => propertyType == typeof(int);

        public (bool changed, object newValue) RenderProperty(Rect rect, string propertyName, Type propertyType,
                                                               object currentValue, GUIStyle labelStyle, GUIStyle valueStyle)
        {
            var labelRect = new Rect(rect.x, rect.y, LabelWidth, RowHeight);
            var inputRect = new Rect(rect.x + LabelWidth, rect.y, rect.width - LabelWidth - Padding, RowHeight);

            GUI.Label(labelRect, "  " + propertyName, labelStyle);
            string displayStr = ((int)currentValue).ToString();
            string inputStr = GUI.TextField(inputRect, displayStr, valueStyle);

            if (int.TryParse(inputStr, out int newInt))
            {
                bool changed = newInt != (int)currentValue;
                return (changed, newInt);
            }

            return (false, currentValue);
        }

        public float GetPropertyHeight(Type propertyType) => 22f;
    }

    /// <summary>
    /// Renders float properties with text input and validation.
    /// </summary>
    public class FloatPropertyRenderer : IPropertyRenderer
    {
        private const float RowHeight = 22f;
        private const float LabelWidth = 96f;
        private const float Padding = 6f;

        public bool CanRender(Type propertyType) => propertyType == typeof(float);

        public (bool changed, object newValue) RenderProperty(Rect rect, string propertyName, Type propertyType,
                                                               object currentValue, GUIStyle labelStyle, GUIStyle valueStyle)
        {
            var labelRect = new Rect(rect.x, rect.y, LabelWidth, RowHeight);
            var inputRect = new Rect(rect.x + LabelWidth, rect.y, rect.width - LabelWidth - Padding, RowHeight);

            GUI.Label(labelRect, "  " + propertyName, labelStyle);
            string displayStr = ((float)currentValue).ToString();
            string inputStr = GUI.TextField(inputRect, displayStr, valueStyle);

            if (float.TryParse(inputStr, out float newFloat))
            {
                bool changed = !Mathf.Approximately(newFloat, (float)currentValue);
                return (changed, newFloat);
            }

            return (false, currentValue);
        }

        public float GetPropertyHeight(Type propertyType) => 22f;
    }

    /// <summary>
    /// Renders boolean properties with toggle controls.
    /// </summary>
    public class BoolPropertyRenderer : IPropertyRenderer
    {
        private const float RowHeight = 22f;
        private const float LabelWidth = 96f;
        private const float Padding = 6f;

        public bool CanRender(Type propertyType) => propertyType == typeof(bool);

        public (bool changed, object newValue) RenderProperty(Rect rect, string propertyName, Type propertyType,
                                                               object currentValue, GUIStyle labelStyle, GUIStyle valueStyle)
        {
            var labelRect = new Rect(rect.x, rect.y, LabelWidth, RowHeight);
            var inputRect = new Rect(rect.x + LabelWidth, rect.y, rect.width - LabelWidth - Padding, RowHeight);

            GUI.Label(labelRect, "  " + propertyName, labelStyle);
            bool currentBool = (bool)currentValue;
            bool newBool = GUI.Toggle(inputRect, currentBool, "");

            bool changed = newBool != currentBool;
            return (changed, newBool);
        }

        public float GetPropertyHeight(Type propertyType) => 22f;
    }

    /// <summary>
    /// Renders Vector2 properties with separate X and Y fields.
    /// </summary>
    public class Vector2PropertyRenderer : IPropertyRenderer
    {
        private const float RowHeight = 22f;
        private const float LabelWidth = 96f;
        private const float Padding = 6f;
        private const float AxisLabelWidth = 16f;

        public bool CanRender(Type propertyType) => propertyType == typeof(Vector2);

        public (bool changed, object newValue) RenderProperty(Rect rect, string propertyName, Type propertyType,
                                                               object currentValue, GUIStyle labelStyle, GUIStyle valueStyle)
        {
            var value = (Vector2)currentValue;
            var mainLabelRect = new Rect(rect.x, rect.y, LabelWidth, RowHeight);
            GUI.Label(mainLabelRect, "  " + propertyName, labelStyle);

            float availableWidth = rect.width - LabelWidth - Padding;
            float fieldWidth = (availableWidth - AxisLabelWidth * 2 - Padding) / 2f;

            // X component
            var xLabelRect = new Rect(rect.x + LabelWidth, rect.y, AxisLabelWidth, RowHeight);
            var xFieldRect = new Rect(rect.x + LabelWidth + AxisLabelWidth, rect.y, fieldWidth, RowHeight);

            GUI.Label(xLabelRect, "X", labelStyle);
            string xStr = GUI.TextField(xFieldRect, value.x.ToString(), valueStyle);

            // Y component
            var yLabelRect = new Rect(rect.x + LabelWidth + AxisLabelWidth + fieldWidth + Padding, rect.y, AxisLabelWidth, RowHeight);
            var yFieldRect = new Rect(rect.x + LabelWidth + AxisLabelWidth + fieldWidth + Padding + AxisLabelWidth, rect.y, fieldWidth, RowHeight);

            GUI.Label(yLabelRect, "Y", labelStyle);
            string yStr = GUI.TextField(yFieldRect, value.y.ToString(), valueStyle);

            bool xChanged = float.TryParse(xStr, out float newX) && !Mathf.Approximately(newX, value.x);
            bool yChanged = float.TryParse(yStr, out float newY) && !Mathf.Approximately(newY, value.y);

            if (xChanged || yChanged)
            {
                float finalX = xChanged ? newX : value.x;
                float finalY = yChanged ? newY : value.y;
                return (true, new Vector2(finalX, finalY));
            }

            return (false, currentValue);
        }

        public float GetPropertyHeight(Type propertyType) => 22f;
    }

    /// <summary>
    /// Renders Vector3 properties with separate X, Y, and Z fields.
    /// </summary>
    public class Vector3PropertyRenderer : IPropertyRenderer
    {
        private const float RowHeight = 22f;
        private const float LabelWidth = 96f;
        private const float Padding = 6f;
        private const float AxisLabelWidth = 16f;

        public bool CanRender(Type propertyType) => propertyType == typeof(Vector3);

        public (bool changed, object newValue) RenderProperty(Rect rect, string propertyName, Type propertyType,
                                                               object currentValue, GUIStyle labelStyle, GUIStyle valueStyle)
        {
            var value = (Vector3)currentValue;
            var mainLabelRect = new Rect(rect.x, rect.y, LabelWidth, RowHeight);
            GUI.Label(mainLabelRect, "  " + propertyName, labelStyle);

            float availableWidth = rect.width - LabelWidth - Padding;
            float fieldWidth = (availableWidth - AxisLabelWidth * 3 - Padding * 2) / 3f;

            // X component
            var xLabelRect = new Rect(rect.x + LabelWidth, rect.y, AxisLabelWidth, RowHeight);
            var xFieldRect = new Rect(rect.x + LabelWidth + AxisLabelWidth, rect.y, fieldWidth, RowHeight);

            GUI.Label(xLabelRect, "X", labelStyle);
            string xStr = GUI.TextField(xFieldRect, value.x.ToString(), valueStyle);

            // Y component
            float yOffset = LabelWidth + AxisLabelWidth + fieldWidth + Padding;
            var yLabelRect = new Rect(rect.x + yOffset, rect.y, AxisLabelWidth, RowHeight);
            var yFieldRect = new Rect(rect.x + yOffset + AxisLabelWidth, rect.y, fieldWidth, RowHeight);

            GUI.Label(yLabelRect, "Y", labelStyle);
            string yStr = GUI.TextField(yFieldRect, value.y.ToString(), valueStyle);

            // Z component
            float zOffset = LabelWidth + AxisLabelWidth * 2 + fieldWidth * 2 + Padding * 2;
            var zLabelRect = new Rect(rect.x + zOffset, rect.y, AxisLabelWidth, RowHeight);
            var zFieldRect = new Rect(rect.x + zOffset + AxisLabelWidth, rect.y, fieldWidth, RowHeight);

            GUI.Label(zLabelRect, "Z", labelStyle);
            string zStr = GUI.TextField(zFieldRect, value.z.ToString(), valueStyle);

            bool xChanged = float.TryParse(xStr, out float newX) && !Mathf.Approximately(newX, value.x);
            bool yChanged = float.TryParse(yStr, out float newY) && !Mathf.Approximately(newY, value.y);
            bool zChanged = float.TryParse(zStr, out float newZ) && !Mathf.Approximately(newZ, value.z);

            if (xChanged || yChanged || zChanged)
            {
                float finalX = xChanged ? newX : value.x;
                float finalY = yChanged ? newY : value.y;
                float finalZ = zChanged ? newZ : value.z;
                return (true, new Vector3(finalX, finalY, finalZ));
            }

            return (false, currentValue);
        }

        public float GetPropertyHeight(Type propertyType) => 22f;
    }

    /// <summary>
    /// Renders Quaternion properties by displaying Euler angles.
    /// </summary>
    public class QuaternionPropertyRenderer : IPropertyRenderer
    {
        private const float RowHeight = 22f;
        private const float LabelWidth = 96f;
        private const float Padding = 6f;
        private const float AxisLabelWidth = 16f;

        public bool CanRender(Type propertyType) => propertyType == typeof(Quaternion);

        public (bool changed, object newValue) RenderProperty(Rect rect, string propertyName, Type propertyType,
                                                               object currentValue, GUIStyle labelStyle, GUIStyle valueStyle)
        {
            var quat = (Quaternion)currentValue;
            var eulerAngles = quat.eulerAngles;

            var mainLabelRect = new Rect(rect.x, rect.y, LabelWidth, RowHeight);
            GUI.Label(mainLabelRect, "  " + propertyName, labelStyle);

            float availableWidth = rect.width - LabelWidth - Padding;
            float fieldWidth = (availableWidth - AxisLabelWidth * 3 - Padding * 2) / 3f;

            // X component (Roll)
            var xLabelRect = new Rect(rect.x + LabelWidth, rect.y, AxisLabelWidth, RowHeight);
            var xFieldRect = new Rect(rect.x + LabelWidth + AxisLabelWidth, rect.y, fieldWidth, RowHeight);

            GUI.Label(xLabelRect, "X", labelStyle);
            string xStr = GUI.TextField(xFieldRect, eulerAngles.x.ToString(), valueStyle);

            // Y component (Pitch)
            float yOffset = LabelWidth + AxisLabelWidth + fieldWidth + Padding;
            var yLabelRect = new Rect(rect.x + yOffset, rect.y, AxisLabelWidth, RowHeight);
            var yFieldRect = new Rect(rect.x + yOffset + AxisLabelWidth, rect.y, fieldWidth, RowHeight);

            GUI.Label(yLabelRect, "Y", labelStyle);
            string yStr = GUI.TextField(yFieldRect, eulerAngles.y.ToString(), valueStyle);

            // Z component (Yaw)
            float zOffset = LabelWidth + AxisLabelWidth * 2 + fieldWidth * 2 + Padding * 2;
            var zLabelRect = new Rect(rect.x + zOffset, rect.y, AxisLabelWidth, RowHeight);
            var zFieldRect = new Rect(rect.x + zOffset + AxisLabelWidth, rect.y, fieldWidth, RowHeight);

            GUI.Label(zLabelRect, "Z", labelStyle);
            string zStr = GUI.TextField(zFieldRect, eulerAngles.z.ToString(), valueStyle);

            bool xChanged = float.TryParse(xStr, out float newX) && !Mathf.Approximately(newX, eulerAngles.x);
            bool yChanged = float.TryParse(yStr, out float newY) && !Mathf.Approximately(newY, eulerAngles.y);
            bool zChanged = float.TryParse(zStr, out float newZ) && !Mathf.Approximately(newZ, eulerAngles.z);

            if (xChanged || yChanged || zChanged)
            {
                float finalX = xChanged ? newX : eulerAngles.x;
                float finalY = yChanged ? newY : eulerAngles.y;
                float finalZ = zChanged ? newZ : eulerAngles.z;
                return (true, Quaternion.Euler(finalX, finalY, finalZ));
            }

            return (false, currentValue);
        }

        public float GetPropertyHeight(Type propertyType) => 22f;
    }

    /// <summary>
    /// Renders enum properties with a dropdown menu selector using IMGUI.
    /// Displays all enum values as a clickable dropdown. The renderer renders the dropdown
    /// inline, taking up space vertically when open so other properties don't overlap.
    /// </summary>
    public class EnumPropertyRenderer : IPropertyRenderer
    {
        private const float RowHeight = 22f;
        private const float ItemHeight = 20f;
        private const float LabelWidth = 96f;
        private const float Padding = 6f;

        private static Dictionary<int, EnumDropdownState> _dropdownStates = new Dictionary<int, EnumDropdownState>();

        private class EnumDropdownState
        {
            public bool IsOpen { get; set; }
            public int SelectedIndex { get; set; } = -1;
            public Type EnumType { get; set; }
            public string[] EnumNames { get; set; }
            public Array EnumValues { get; set; }
            public int CurrentIndex { get; set; }
        }

        public bool CanRender(Type propertyType) => propertyType != null && propertyType.IsEnum;

        public (bool changed, object newValue) RenderProperty(Rect rect, string propertyName, Type propertyType,
                                                               object currentValue, GUIStyle labelStyle, GUIStyle valueStyle)
        {
            var labelRect = new Rect(rect.x, rect.y, LabelWidth, RowHeight);
            var dropdownRect = new Rect(rect.x + LabelWidth, rect.y, rect.width - LabelWidth - Padding, RowHeight);

            GUI.Label(labelRect, "  " + propertyName, labelStyle);

            // Get all enum names and values
            string[] enumNames = Enum.GetNames(propertyType);
            Array enumValues = Enum.GetValues(propertyType);

            // Find current index
            int currentIndex = -1;
            for (int i = 0; i < enumValues.Length; i++)
            {
                if (enumValues.GetValue(i).Equals(currentValue))
                {
                    currentIndex = i;
                    break;
                }
            }

            if (currentIndex == -1)
                currentIndex = 0;

            // Get or create dropdown state
            int stateKey = propertyType.GetHashCode() ^ propertyName.GetHashCode();
            EnumDropdownState state;

            if (!_dropdownStates.TryGetValue(stateKey, out state))
            {
                state = new EnumDropdownState
                {
                    IsOpen = false,
                    EnumType = propertyType,
                    EnumNames = enumNames,
                    EnumValues = enumValues,
                    CurrentIndex = currentIndex
                };
                _dropdownStates[stateKey] = state;
            }

            state.CurrentIndex = currentIndex;
            state.EnumNames = enumNames;
            state.EnumValues = enumValues;

            // Display dropdown button
            if (GUI.Button(dropdownRect, enumNames[currentIndex], valueStyle))
            {
                state.IsOpen = !state.IsOpen;
            }

            // Render dropdown menu if open
            bool changed = false;
            object newValue = currentValue;

            if (state.IsOpen)
            {
                float menuStartY = rect.y + RowHeight + Padding;
                RenderDropdownMenu(new Rect(rect.x + LabelWidth, menuStartY, rect.width - LabelWidth - Padding, enumNames.Length * ItemHeight),
                    state, out int selectedIndex);

                if (selectedIndex >= 0 && selectedIndex != currentIndex)
                {
                    newValue = enumValues.GetValue(selectedIndex);
                    changed = true;
                    state.IsOpen = false; // Close dropdown after selection
                }
            }

            return (changed, newValue);
        }

        private void RenderDropdownMenu(Rect menuRect, EnumDropdownState state, out int selectedIndex)
        {
            selectedIndex = -1;

            // Draw menu background
            GUI.Box(menuRect, "", "box");

            // Draw each enum option
            for (int i = 0; i < state.EnumNames.Length; i++)
            {
                var itemRect = new Rect(menuRect.x, menuRect.y + (i * ItemHeight), menuRect.width, ItemHeight);

                // Highlight current selection
                if (i == state.CurrentIndex)
                {
                    GUI.backgroundColor = new Color(0.5f, 0.5f, 0.5f, 1f); // Gray
                    GUI.Box(itemRect, "", "box");
                    GUI.backgroundColor = Color.white;
                }

                if (GUI.Button(itemRect, state.EnumNames[i], "label"))
                {
                    selectedIndex = i;
                }
            }
        }

        public float GetPropertyHeight(Type propertyType)
        {
            // For now, just return button height. The properties panel should be updated
            // to query this renderer's state to get the full height when dropdown is open.
            return RowHeight;
        }

        /// <summary>
        /// Gets the total height needed for an enum property, including dropdown if open.
        /// Called by the properties panel to properly calculate scrollable area.
        /// </summary>
        public static float GetEnumPropertyHeightWithDropdown(int stateKey, Type enumType)
        {
            if (_dropdownStates.TryGetValue(stateKey, out var state) && state.IsOpen)
            {
                int itemCount = state.EnumNames?.Length ?? 0;
                return RowHeight + Padding + (itemCount * ItemHeight);
            }
            return RowHeight;
        }

        /// <summary>
        /// Gets the state key for an enum property (used with GetEnumPropertyHeightWithDropdown).
        /// </summary>
        public static int GetStateKey(Type propertyType, string propertyName)
        {
            return propertyType.GetHashCode() ^ propertyName.GetHashCode();
        }
    }

    /// <summary>
    /// Default fallback renderer that displays properties as read-only labels.
    /// Used for types that don't have a specific renderer.
    /// </summary>
    public class DefaultPropertyRenderer : IPropertyRenderer
    {
        private const float RowHeight = 22f;
        private const float LabelWidth = 96f;
        private const float Padding = 6f;

        public bool CanRender(Type propertyType) => true; // Can render anything as fallback

        public (bool changed, object newValue) RenderProperty(Rect rect, string propertyName, Type propertyType,
                                                               object currentValue, GUIStyle labelStyle, GUIStyle valueStyle)
        {
            var labelRect = new Rect(rect.x, rect.y, LabelWidth, RowHeight);
            var valueRect = new Rect(rect.x + LabelWidth, rect.y, rect.width - LabelWidth - Padding, RowHeight);

            GUI.Label(labelRect, "  " + propertyName, labelStyle);
            GUI.Label(valueRect, currentValue?.ToString() ?? "<null>", labelStyle);

            return (false, currentValue);
        }

        public float GetPropertyHeight(Type propertyType) => 22f;
    }
}
