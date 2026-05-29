using System.Globalization;
using UnityEngine;

namespace FUSE.Editor.Screen.UI
{
    /// <summary>
    /// Centered overlay dialog for editor-wide settings. Today
    /// hosts a single control: a UI scale slider with a live
    /// percentage display. Future settings (hotkey overrides,
    /// panel positions, color theme variants) can append below.
    /// </summary>
    /// <remarks>
    /// Apply-on-change pattern: dragging the slider takes effect
    /// instantly, no separate Apply button. Close (✕) just
    /// dismisses the panel — the value's already saved by the
    /// time you click it.
    /// </remarks>
    internal static class FuseEditorSettingsPanel
    {
        // Centered dialog dimensions in logical pixels (post-scale).
        // The dialog ALSO scales along with the rest of the editor
        // because the OnGUI matrix transform is in effect when
        // this draw runs.
        private const float PanelWidth = 460f;
        private const float PanelHeight = 240f;
        private const float TitleBarHeight = 28f;
        private const float RowHeight = 28f;
        private const float Padding = 12f;

        public sealed class Options
        {
            /// <summary>Called when the user clicks the title bar ✕.</summary>
            public System.Action OnClose { get; set; }
        }

        /// <summary>
        /// The centered panel rect for the given LOGICAL screen bounds
        /// (post-UI-scale). Exposed so the screen's front-of-frame modal
        /// input gate can hit-test against the same rectangle this draws.
        /// </summary>
        public static Rect GetPanelRect(Rect screen)
        {
            return new Rect(
                (screen.width - PanelWidth) * 0.5f,
                (screen.height - PanelHeight) * 0.5f,
                PanelWidth, PanelHeight);
        }

        public static void Draw(Rect screen, Options options)
        {
            options ??= new Options();

            var panelRect = GetPanelRect(screen);

            // Backdrop: subtle dim so the panel reads as modal-ish
            // without fully blocking the world. Outside-click dismissal
            // and blocking the chrome beneath are handled by the
            // screen's front-of-frame input gate — it must run BEFORE
            // the chrome draws to consume the event, so it can't live
            // here. This method only paints the dim.
            DrawBackdrop(screen);

            // Frame + body
            FuseEditorTheme.DrawSolid(panelRect, FuseEditorTheme.Palette.BorderStrong);
            var inner = new Rect(panelRect.x + 1, panelRect.y + 1,
                                  panelRect.width - 2, panelRect.height - 2);
            FuseEditorTheme.DrawSolid(inner, FuseEditorTheme.Palette.BackgroundSecondary);

            DrawTitleBar(inner, options);
            DrawBody(inner);
        }

        private static void DrawBackdrop(Rect screen)
        {
            // Half-transparent black covering the whole logical
            // screen; muted enough that the world stays visible.
            var prevColor = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.35f);
            GUI.DrawTexture(screen, Texture2D.whiteTexture);
            GUI.color = prevColor;
        }

        private static void DrawTitleBar(Rect inner, Options options)
        {
            var titleRect = new Rect(inner.x, inner.y, inner.width, TitleBarHeight);
            FuseEditorTheme.DrawSolid(titleRect, FuseEditorTheme.Palette.BackgroundDeep);

            var titleLabel = FuseEditorUiHelper.TranslateLabel("fuse.editor.settings.title");
            GUI.Label(new Rect(titleRect.x + Padding, titleRect.y,
                                titleRect.width - Padding - 32f, titleRect.height),
                      titleLabel.Title, FuseEditorTheme.CategoryHeader);

            // Close button on the far right of the title bar.
            var closeRect = new Rect(titleRect.xMax - 28f, titleRect.y + 2f, 24f, 24f);
            var closeLabel = FuseEditorUiHelper.TranslateLabel("fuse.editor.settings.close");
            if (GUI.Button(closeRect, new GUIContent("✕", closeLabel.Description),
                            FuseEditorTheme.ToolbarButton))
            {
                options.OnClose?.Invoke();
            }
        }

        private static void DrawBody(Rect inner)
        {
            var y = inner.y + TitleBarHeight + Padding;
            var contentX = inner.x + Padding;
            var contentW = inner.width - Padding * 2;

            // UI scale row: label | slider | live percentage.
            var rowRect = new Rect(contentX, y, contentW, RowHeight);
            DrawUiScaleRow(rowRect);
            y += RowHeight + Padding;

            // Hint text below the row.
            var hintRect = new Rect(contentX, y, contentW, RowHeight * 2);
            var hint = FuseEditorUiHelper.TranslateLabel("fuse.editor.settings.ui_scale.hint");
            GUI.Label(hintRect, hint.Title, FuseEditorTheme.PropertyLabel);
        }

        private static void DrawUiScaleRow(Rect rect)
        {
            var labelLabel = FuseEditorUiHelper.TranslateLabel("fuse.editor.settings.ui_scale");
            const float LabelWidth = 100f;
            const float ValueWidth = 60f;

            // Label
            GUI.Label(new Rect(rect.x, rect.y, LabelWidth, rect.height),
                      labelLabel.Title, FuseEditorTheme.PropertyLabel);

            // Slider (between label and value)
            var sliderRect = new Rect(rect.x + LabelWidth + 6f,
                                       rect.y + (rect.height - 18f) * 0.5f,
                                       rect.width - LabelWidth - ValueWidth - 18f, 18f);
            var current = FuseEditorSettings.UiScale;
            var next = GUI.HorizontalSlider(sliderRect, current,
                                              FuseEditorSettings.MinUiScale,
                                              FuseEditorSettings.MaxUiScale);
            if (Mathf.Abs(next - current) > 0.001f)
            {
                FuseEditorSettings.UiScale = next;
            }

            // Live percentage display (e.g. "120%").
            var percent = Mathf.RoundToInt(FuseEditorSettings.UiScale * 100f);
            var valueRect = new Rect(rect.xMax - ValueWidth, rect.y, ValueWidth, rect.height);
            GUI.Label(valueRect,
                      percent.ToString(CultureInfo.InvariantCulture) + "%",
                      FuseEditorTheme.PropertyValue);
        }
    }
}
