using UnityEngine;
using FUSE.Infrastructure;

namespace FUSE.Editor.Screen.UI
{
    /// <summary>
    /// A simple panel for displaying buttons and tools.
    /// Used at the bottom of the properties panel for apply/cancel actions.
    /// </summary>
    internal sealed class FuseEditorButtonToolPanel
    {
        private const float RowHeight = 32f;
        private const float Padding = 8f;
        private const float ButtonHeight = 28f;

        public void Draw(Rect panelRect)
        {
            if (panelRect.height < ButtonHeight + Padding * 2)
            {
                return;
            }

            // Background
            GUI.Box(panelRect, GUIContent.none, "box");

            var contentRect = new Rect(panelRect.x + Padding, panelRect.y + Padding,
                                       panelRect.width - Padding * 2, panelRect.height - Padding * 2);

            var y = contentRect.y;

            // "Apply Changes" button
            var applyButtonRect = new Rect(contentRect.x, y, contentRect.width, ButtonHeight);
            if (GUI.Button(applyButtonRect, "Apply Changes"))
            {
                ApplyChanges();
            }

            y += ButtonHeight + Padding;
        }

        private void ApplyChanges()
        {
            try
            {
                if (FuseEditorChangeHandler.Instance != null)
                {
                    FuseEditorChangeHandler.Instance.ApplyChanges();
                }
                else
                {
                    FuseLog.Warning("FuseEditorButtonToolPanel: FuseEditorChangeHandler.Instance is null");
                }
            }
            catch (System.Exception ex)
            {
                FuseLog.Exception($"FuseEditorButtonToolPanel: Error applying changes", ex);
            }
        }
    }
}
