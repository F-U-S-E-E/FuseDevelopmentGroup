using FUSE.Editor.Gizmos;
using FUSE.Editor.Screen.UI;

namespace FUSE.Editor.Track.Tools
{
    /// <summary>
    /// Rotation tool. Same shape as <see cref="FuseMoveTool"/> but engages
    /// the RLD rotation gizmo instead of the translate gizmo.
    /// </summary>
    internal sealed class FuseRotateTool : IFuseEditorTool
    {
        FuseGizmoManager GizmoManager => FuseEditor.Instance.GizmoManager;

        public const string ToolId = "fuse.editor.tool.rotate";

        public string Id => ToolId;
        public string LabelKey => "fuse.editor.tool.rotate";
        public string IconGlyph => "↻";
        public bool IsAvailable => true;
        public string UnavailableReason => null;

        public void OnActivate()
        {
            if (GizmoManager.HasActiveGizmo)
            {
                GizmoManager.EndCurrentGizmo();
            }
            if (FuseEditor.Instance.EntitySelection.SelectionCount == 1)
            {
                GizmoManager.BeginRotate(FuseEditor.Instance.EntitySelection.PrimaryHandler);
            }
            else if (FuseEditor.Instance.EntitySelection.SelectionCount > 1)
            {
                GizmoManager.BeginRotateMultiple(FuseEditor.Instance.EntitySelection.SelectedHandlers);
            }
        }

        public void OnDeactivate()
        {
            GizmoManager.EndCurrentGizmo();
        }

        public void Tick() { }

        public void Draw() { }
    }
}
