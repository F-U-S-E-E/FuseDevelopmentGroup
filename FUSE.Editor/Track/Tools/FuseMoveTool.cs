using FUSE.Editor.Screen.UI;

namespace FUSE.Editor.Track.Tools
{
    /// <summary>
    /// Translation tool. Behaves like <see cref="FuseSelectTool"/> for
    /// marker visibility, but engages the RLD move gizmo on the active
    /// selection — both an already-selected marker at activation time
    /// and any marker the user clicks afterwards.
    /// </summary>
    internal sealed class FuseMoveTool : IFuseEditorTool
    {
        public const string ToolId = "fuse.editor.tool.move";

        public string Id => ToolId;
        public string LabelKey => "fuse.editor.tool.move";
        public string IconGlyph => "✥";
        public bool IsAvailable => true;
        public string UnavailableReason => null;

        public void OnActivate()
        {
            
        }

        public void OnDeactivate()
        {
            // Selected.Deselect() tears down whatever gizmo is attached
            // (move OR rotate), so we don't need to distinguish here.
            
        }

        public void Tick() { }
    }
}
