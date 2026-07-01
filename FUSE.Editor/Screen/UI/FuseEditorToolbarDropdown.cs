using System;
using System.Collections.Generic;
using UnityEngine;

namespace FUSE.Editor.Screen.UI
{
    /// <summary>
    /// A dropdown menu control for the toolbar. Allows selecting from a set of options
    /// and displays the currently selected value.
    /// </summary>
    internal sealed class FuseEditorToolbarDropdown
    {
        public sealed class Option
        {
            public Option(string id, string labelKey, Action onSelected = null)
            {
                Id = id;
                LabelKey = labelKey;
                OnSelected = onSelected;
            }

            public string Id { get; }
            public string LabelKey { get; }
            public Action OnSelected { get; }
        }

        private readonly string _id;
        private readonly string _labelKey;
        private readonly List<Option> _options;
        private string _selectedOptionId;
        private bool _isOpen;
        private Vector2 _openPosition;

        public FuseEditorToolbarDropdown(string id, string labelKey, IEnumerable<Option> options, string initialSelectedId = null)
        {
            _id = id;
            _labelKey = labelKey;
            _options = new List<Option>(options ?? Array.Empty<Option>());
            _selectedOptionId = initialSelectedId ?? (_options.Count > 0 ? _options[0].Id : null);
            _isOpen = false;
        }

        public string SelectedOptionId
        {
            get => _selectedOptionId;
            set
            {
                if (value != _selectedOptionId)
                {
                    _selectedOptionId = value;
                    _isOpen = false;
                }
            }
        }

        public bool IsOpen => _isOpen;

        /// <summary>
        /// Draw the dropdown button and handle interactions. Returns the X coordinate
        /// where rendering ended.
        /// </summary>
        public float Draw(Rect rect)
        {
            var buttonRect = new Rect(rect.x, rect.y, GetWidth(), rect.height);
            DrawButton(buttonRect);

            if (_isOpen)
            {
                DrawDropdownMenu(buttonRect);
            }

            return buttonRect.xMax;
        }

        private float GetWidth()
        {
            // Base width: label + padding + indicator
            const float basePadding = 12f;
            const float indicatorWidth = 12f;
            float labelWidth = GetMaxOptionLabelWidth() + basePadding + indicatorWidth;
            return Mathf.Max(80f, labelWidth); // Minimum width
        }

        private float GetMaxOptionLabelWidth()
        {
            float maxWidth = 0f;
            var guiSkin = GUI.skin;
            var buttonStyle = guiSkin.button;

            foreach (var option in _options)
            {
                var label = FuseEditorUiHelper.TranslateLabel(option.LabelKey).Title;
                var size = buttonStyle.CalcSize(new GUIContent(label));
                maxWidth = Mathf.Max(maxWidth, size.x);
            }

            return maxWidth;
        }

        private void DrawButton(Rect rect)
        {
            var selectedLabel = GetSelectedLabel();
            var displayText = selectedLabel ?? "---";

            // Draw background box
            GUI.Box(rect, GUIContent.none, FuseEditorTheme.ToolbarButton);

            // Draw text with dropdown indicator
            var textRect = new Rect(rect.x + 6f, rect.y, rect.width - 18f, rect.height);
            GUI.Label(textRect, displayText, FuseEditorTheme.ToolbarDropdownLabel);

            // Draw dropdown indicator (triangle)
            var indicatorRect = new Rect(rect.xMax - 12f, rect.y + rect.height / 2f - 4f, 8f, 8f);
            DrawDropdownIndicator(indicatorRect);

            // Button interaction area (includes entire rect)
            if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
            {
                _isOpen = !_isOpen;
                _openPosition = new Vector2(rect.x, rect.yMax);
            }
        }

        private string GetSelectedLabel()
        {
            foreach (var option in _options)
            {
                if (option.Id == _selectedOptionId)
                {
                    return FuseEditorUiHelper.TranslateLabel(option.LabelKey).Title;
                }
            }

            return null;
        }

        private void DrawDropdownIndicator(Rect rect)
        {
            var prevColor = GUI.color;
            GUI.color = FuseEditorTheme.Palette.TextPrimary;

            // Draw a simple downward-pointing triangle using vertices
            var p1 = new Vector2(rect.x, rect.y);
            var p2 = new Vector2(rect.xMax, rect.y);
            var p3 = new Vector2(rect.center.x, rect.yMax);

            // Draw lines using Debug.DrawLine won't work in IMGUI context,
            // so we'll use GL instead - draw as solid lines on the GUI
            const float lineWidth = 1f;
            DrawLine(p1, p2, lineWidth);
            DrawLine(p2, p3, lineWidth);
            DrawLine(p3, p1, lineWidth);

            GUI.color = prevColor;
        }

        private void DrawLine(Vector2 from, Vector2 to, float width)
        {
            var prevColor = GUI.color;
            var angle = Mathf.Atan2(to.y - from.y, to.x - from.x) * Mathf.Rad2Deg;
            var distance = Vector2.Distance(from, to);

            var matrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(angle, from);

            GUI.DrawTexture(new Rect(from.x, from.y - width / 2f, distance, width), Texture2D.whiteTexture);

            GUI.matrix = matrix;
            GUI.color = prevColor;
        }

        private void DrawDropdownMenu(Rect buttonRect)
        {
            // Calculate menu dimensions
            const float itemHeight = 22f;
            const float padding = 4f;
            float menuWidth = GetWidth();
            float menuHeight = (_options.Count * itemHeight) + (padding * 2f);

            var menuRect = new Rect(_openPosition.x, _openPosition.y, menuWidth, menuHeight);

            // Clamp to screen bounds
            var screenRect = new Rect(0f, 0f, UnityEngine.Screen.width, UnityEngine.Screen.height);
            if (menuRect.yMax > screenRect.height)
            {
                menuRect.y = buttonRect.y - menuHeight;
            }
            if (menuRect.xMax > screenRect.width)
            {
                menuRect.x = screenRect.width - menuRect.width;
            }

            // Draw menu background
            GUI.Box(menuRect, GUIContent.none, FuseEditorTheme.Panel);

            // Draw menu items
            var itemRect = new Rect(menuRect.x + padding, menuRect.y + padding, menuWidth - (padding * 2f), itemHeight);
            foreach (var option in _options)
            {
                DrawMenuItem(itemRect, option);
                itemRect.y += itemHeight;
            }

            // Close dropdown if clicked outside
            if (Event.current.type == EventType.MouseDown && !menuRect.Contains(Event.current.mousePosition))
            {
                _isOpen = false;
            }
        }

        private void DrawMenuItem(Rect rect, Option option)
        {
            var isSelected = option.Id == _selectedOptionId;
            var label = FuseEditorUiHelper.TranslateLabel(option.LabelKey).Title;
            var style = isSelected ? FuseEditorTheme.ToolbarDropdownItemActive : FuseEditorTheme.ToolbarDropdownItem;

            if (GUI.Button(rect, label, style))
            {
                _selectedOptionId = option.Id;
                _isOpen = false;
                option.OnSelected?.Invoke();
            }
        }
    }
}
