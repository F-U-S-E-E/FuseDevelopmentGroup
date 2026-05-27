using System;
using System.Collections.Generic;
using UnityEngine;

namespace FUSE.Editor.Screen.UI
{
    /// <summary>
    /// Horizontal icon toolbar painted under the menu bar. Groups
    /// related actions with thin vertical dividers between them:
    /// File | History | Gizmo | View. Each button is a 24×24 icon
    /// from <see cref="FuseEditorIcons"/> with the standard
    /// label/description pulled from
    /// <see cref="FuseEditorUiHelper.TranslateLabel"/>.
    /// </summary>
    /// <remarks>
    /// The toolbar replaces the old bottom-of-viewport "tool strip"
    /// as the primary surface for gizmo (Select / Move / Rotate /
    /// Scale / Place) selection. The bottom strip is still
    /// registered in <see cref="FuseEditorWindowRegistry"/> as an
    /// off-by-default fallback for users who prefer it.
    /// </remarks>
    internal sealed class FuseEditorIconToolbar
    {
        public sealed class Button
        {
            public Button(FuseEditorIconKind icon, string labelKey,
                          Action onClick = null,
                          Func<bool> isAvailable = null,
                          Func<bool> isActive = null,
                          string unavailableReasonKey = null)
            {
                Icon = icon;
                LabelKey = labelKey;
                OnClick = onClick;
                IsAvailable = isAvailable ?? AlwaysAvailable;
                IsActive = isActive ?? NeverActive;
                UnavailableReasonKey = unavailableReasonKey;
            }

            public FuseEditorIconKind Icon { get; }
            public string LabelKey { get; }
            public Action OnClick { get; }
            public Func<bool> IsAvailable { get; }
            public Func<bool> IsActive { get; }
            public string UnavailableReasonKey { get; }

            private static readonly Func<bool> AlwaysAvailable = () => true;
            private static readonly Func<bool> NeverActive = () => false;
        }

        public sealed class Group
        {
            public Group(string id, params Button[] buttons)
            {
                Id = id;
                Buttons = buttons ?? Array.Empty<Button>();
            }

            public string Id { get; }
            public Button[] Buttons { get; }
        }

        private readonly List<Group> _groups;

        public FuseEditorIconToolbar(IEnumerable<Group> groups)
        {
            _groups = new List<Group>(groups ?? Array.Empty<Group>());
        }

        /// <summary>
        /// Paints the toolbar across <paramref name="rect"/>. Returns
        /// the X coordinate where rendering ended, in case the caller
        /// wants to lay further content (e.g. a mode-picker dropdown
        /// on the right) after the last group.
        /// </summary>
        public float Draw(Rect rect)
        {
            GUI.Box(rect, GUIContent.none, FuseEditorTheme.Toolbar);

            var buttonSize = FuseEditorTheme.Metrics.ToolbarButtonSize;
            var groupGap = FuseEditorTheme.Metrics.ToolbarGroupGap;
            var yPadding = (rect.height - buttonSize) / 2f;

            float cursorX = rect.x + FuseEditorTheme.Metrics.Padding;
            for (int g = 0; g < _groups.Count; g++)
            {
                var group = _groups[g];
                foreach (var button in group.Buttons)
                {
                    var buttonRect = new Rect(cursorX, rect.y + yPadding, buttonSize, buttonSize);
                    DrawButton(buttonRect, button);
                    cursorX += buttonSize + 2f;
                }

                // Group separator (skip after the last group).
                if (g < _groups.Count - 1)
                {
                    cursorX += groupGap;
                    FuseEditorTheme.DrawVerticalDivider(
                        new Rect(cursorX, rect.y + 4f, 1f, rect.height - 8f));
                    cursorX += groupGap;
                }
            }

            return cursorX;
        }

        private static void DrawButton(Rect rect, Button button)
        {
            var label = FuseEditorUiHelper.TranslateLabel(button.LabelKey);
            var available = button.IsAvailable();
            var active = available && button.IsActive();
            var style = active ? FuseEditorTheme.ToolbarButtonActive : FuseEditorTheme.ToolbarButton;

            if (!available)
            {
                var reason = !string.IsNullOrEmpty(button.UnavailableReasonKey)
                    ? FuseEditorUiHelper.TranslateLabel(button.UnavailableReasonKey).Title
                    : label.Description;
                var prev = GUI.enabled;
                GUI.enabled = false;
                // Paint the same button cell + draw the icon dim so
                // the affordance is clear: gray slot, gray glyph,
                // tooltip explains why.
                GUI.Box(rect, new GUIContent(string.Empty, reason ?? label.Description), style);
                FuseEditorIcons.Draw(rect, button.Icon, style, FuseEditorTheme.Palette.TextDisabled);
                GUI.enabled = prev;
                return;
            }

            // Backdrop button + tooltip first (so hover shows the
            // tooltip), then overlay the icon. Two-pass keeps the
            // tooltip wiring identical to the rest of the editor.
            var tooltipContent = new GUIContent(string.Empty, label.Description);
            if (GUI.Button(rect, tooltipContent, style))
            {
                button.OnClick?.Invoke();
            }
            FuseEditorIcons.Draw(rect, button.Icon, style,
                                  active ? FuseEditorTheme.Palette.TextAccent : FuseEditorTheme.Palette.TextPrimary);
        }
    }
}
