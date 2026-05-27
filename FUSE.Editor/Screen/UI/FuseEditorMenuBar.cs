using System;
using System.Collections.Generic;
using UnityEngine;

namespace FUSE.Editor.Screen.UI
{
    /// <summary>
    /// Top menu strip (Scenario / Edit / View / Attributes / Tools /
    /// Settings / Play / Help). Hovering a top-level menu opens its
    /// submenu underneath; clicking a submenu item dispatches the
    /// item's <see cref="MenuItem.Action"/> (when available) and
    /// closes the menu. Items can be marked unavailable with a
    /// reason key so the user sees a tooltip explaining why a stub
    /// is grayed out.
    /// </summary>
    /// <remarks>
    /// Hover behavior matches EDEN: once a menu is open, sliding
    /// the cursor across the top-level row swaps the active menu
    /// instantly (no re-click). Moving the cursor off both the
    /// menu name AND the open submenu starts a 200ms close timer
    /// so the menu doesn't snap shut on accidental cursor drift.
    /// </remarks>
    internal sealed class FuseEditorMenuBar
    {
        public sealed class MenuItem
        {
            public MenuItem(string labelKey, Action action = null,
                            string unavailableReasonKey = null,
                            params MenuItem[] children)
            {
                LabelKey = labelKey;
                Action = action;
                UnavailableReasonKey = unavailableReasonKey;
                Children = children ?? Array.Empty<MenuItem>();
            }

            /// <summary>Localization key for the visible label.</summary>
            public string LabelKey { get; }

            /// <summary>
            /// Click handler. <c>null</c> means the item is a
            /// header / placeholder; if <see cref="UnavailableReasonKey"/>
            /// is also set, the item draws disabled with the reason
            /// as the hover tooltip (matches Axiom's pattern).
            /// </summary>
            public Action Action { get; }

            public string UnavailableReasonKey { get; }
            public MenuItem[] Children { get; }

            public bool HasChildren => Children != null && Children.Length > 0;
            public bool IsSeparator => string.IsNullOrEmpty(LabelKey);
            public bool IsAvailable => Action != null || HasChildren;

            /// <summary>
            /// Convenience factory for an in-menu horizontal divider.
            /// Use sparingly; the EDEN style nests separators inside
            /// long submenus to break related items into groups.
            /// </summary>
            public static MenuItem Separator() => new MenuItem(labelKey: "");
        }

        private readonly List<MenuItem> _topLevel;
        private int _openTopLevelIndex = -1;
        private float _lastHoverTime;
        private const float SubmenuCloseGraceSeconds = 0.2f;
        private const float TopLevelMinWidth = 76f;
        private const float SubmenuItemHeight = 22f;
        private const float SubmenuMinWidth = 200f;

        public FuseEditorMenuBar(IEnumerable<MenuItem> topLevel)
        {
            _topLevel = new List<MenuItem>(topLevel ?? Array.Empty<MenuItem>());
        }

        public bool IsOpen => _openTopLevelIndex >= 0;

        /// <summary>
        /// Paints the menu bar into <paramref name="rect"/>. Submenu
        /// popup (when open) is rendered on top of subsequent draws —
        /// call <see cref="DrawOpenSubmenu"/> AFTER drawing the rest
        /// of the screen so it lands above other panels.
        /// </summary>
        public void DrawBar(Rect rect)
        {
            GUI.Box(rect, GUIContent.none, FuseEditorTheme.MenuBar);

            float cursorX = rect.x + 4f;
            int hoveredIndex = -1;
            for (int i = 0; i < _topLevel.Count; i++)
            {
                var item = _topLevel[i];
                var label = FuseEditorUiHelper.TranslateLabel(item.LabelKey);
                var content = new GUIContent(label.Title, label.Description);
                var width = Mathf.Max(TopLevelMinWidth, FuseEditorTheme.MenuItem.CalcSize(content).x);
                var menuRect = new Rect(cursorX, rect.y, width, rect.height);

                var style = (i == _openTopLevelIndex) ? FuseEditorTheme.MenuItemActive : FuseEditorTheme.MenuItem;
                if (GUI.Button(menuRect, content, style))
                {
                    // Click toggles open/close on the same menu;
                    // opens a different menu when clicked.
                    _openTopLevelIndex = (_openTopLevelIndex == i) ? -1 : i;
                    _lastHoverTime = Time.realtimeSinceStartup;
                }

                if (menuRect.Contains(Event.current?.mousePosition ?? Vector2.zero))
                {
                    hoveredIndex = i;
                }

                cursorX += width;
            }

            // EDEN-style hover-swap: once any menu is open, drifting
            // the cursor over a different top-level item swaps the
            // open one instantly without requiring another click.
            if (_openTopLevelIndex >= 0 && hoveredIndex >= 0 && hoveredIndex != _openTopLevelIndex)
            {
                _openTopLevelIndex = hoveredIndex;
                _lastHoverTime = Time.realtimeSinceStartup;
            }

            if (hoveredIndex >= 0)
            {
                _lastHoverTime = Time.realtimeSinceStartup;
            }
        }

        /// <summary>
        /// Paints the open submenu popup (if any) above the rest of
        /// the screen content. Call from the screen's draw flow after
        /// every other region so it stacks on top.
        /// </summary>
        public void DrawOpenSubmenu(float menuBarBottomY)
        {
            if (_openTopLevelIndex < 0) return;
            if (_openTopLevelIndex >= _topLevel.Count)
            {
                _openTopLevelIndex = -1;
                return;
            }

            var topLevel = _topLevel[_openTopLevelIndex];
            if (!topLevel.HasChildren)
            {
                // No-children menu: invoke the action immediately on
                // open-click (we get here when the user clicks a
                // top-level entry that's actually a leaf — e.g. a
                // hypothetical top-level "About" item).
                topLevel.Action?.Invoke();
                _openTopLevelIndex = -1;
                return;
            }

            // Position the submenu under the corresponding top-level
            // label. Calculate by re-running the cursorX walk so we
            // don't need to cache geometry per frame.
            float anchorX = 4f;
            for (int i = 0; i < _openTopLevelIndex; i++)
            {
                var earlier = _topLevel[i];
                var earlierContent = new GUIContent(FuseEditorUiHelper.TranslateLabel(earlier.LabelKey).Title);
                anchorX += Mathf.Max(TopLevelMinWidth, FuseEditorTheme.MenuItem.CalcSize(earlierContent).x);
            }

            var popupRect = new Rect(anchorX, menuBarBottomY,
                                     SubmenuMinWidth, topLevel.Children.Length * SubmenuItemHeight + 4f);
            // Border + fill: paint a 1px frame in the divider color,
            // then the secondary background inside.
            FuseEditorTheme.DrawSolid(popupRect, FuseEditorTheme.Palette.BorderStrong);
            var inner = new Rect(popupRect.x + 1, popupRect.y + 1, popupRect.width - 2, popupRect.height - 2);
            FuseEditorTheme.DrawSolid(inner, FuseEditorTheme.Palette.BackgroundSecondary);

            float itemY = inner.y + 2f;
            bool anyHover = false;
            foreach (var child in topLevel.Children)
            {
                var itemRect = new Rect(inner.x + 2, itemY, inner.width - 4, SubmenuItemHeight);
                DrawSubmenuItem(itemRect, child);
                if (itemRect.Contains(Event.current?.mousePosition ?? Vector2.zero)) anyHover = true;
                itemY += SubmenuItemHeight;
            }

            // Close on outside-click. Use the layout repaint so we
            // don't fire during the same event the user clicked the
            // top-level menu with.
            var mouse = Event.current?.mousePosition ?? Vector2.zero;
            var inMenuBar = mouse.y < menuBarBottomY;
            if (Event.current != null && Event.current.type == EventType.MouseDown
                && !popupRect.Contains(mouse) && !inMenuBar)
            {
                _openTopLevelIndex = -1;
                return;
            }

            // Drift-close: if no top-level hover AND no popup hover
            // for the grace window, close.
            if (!anyHover && !inMenuBar
                && Time.realtimeSinceStartup - _lastHoverTime > SubmenuCloseGraceSeconds)
            {
                _openTopLevelIndex = -1;
            }
        }

        private void DrawSubmenuItem(Rect rect, MenuItem item)
        {
            if (item.IsSeparator)
            {
                FuseEditorTheme.DrawHorizontalDivider(
                    new Rect(rect.x + 6, rect.y + rect.height / 2f, rect.width - 12, 1));
                return;
            }

            var label = FuseEditorUiHelper.TranslateLabel(item.LabelKey);
            if (item.IsAvailable)
            {
                var content = new GUIContent(label.Title, label.Description);
                if (GUI.Button(rect, content, FuseEditorTheme.MenuItem))
                {
                    item.Action?.Invoke();
                    _openTopLevelIndex = -1;
                }
            }
            else
            {
                // Stubbed: gray button + reason tooltip via the
                // same DisabledButton helper the rest of the editor
                // uses. The reason key falls back to the item's own
                // description if no explicit reason was given.
                var reason = !string.IsNullOrEmpty(item.UnavailableReasonKey)
                    ? FuseEditorUiHelper.TranslateLabel(item.UnavailableReasonKey).Title
                    : label.Description;
                FuseEditorUiHelper.DisabledButton(rect, item.LabelKey, reason, FuseEditorTheme.MenuItem);
            }

            if (rect.Contains(Event.current?.mousePosition ?? Vector2.zero))
            {
                _lastHoverTime = Time.realtimeSinceStartup;
            }
        }

        /// <summary>
        /// Immediately closes any open submenu. Use when the editor
        /// dispatches an action externally (e.g. ESC handler).
        /// </summary>
        public void Close()
        {
            _openTopLevelIndex = -1;
        }
    }
}
