using FUSE.Editor.Screen.UI;

namespace FUSE.Editor.Track.Tools
{
    /// <summary>
    /// Rotation tool. Same shape as <see cref="FuseMoveTool"/> but engages
    /// the RLD rotation gizmo instead of the translate gizmo.
    /// </summary>
    internal sealed class FuseRotateTool : IFuseEditorTool
    {
        public const string ToolId = "fuse.editor.tool.rotate";

        public string Id => ToolId;
        public string LabelKey => "fuse.editor.tool.rotate";
        public string IconGlyph => "↻";
        public bool IsAvailable => true;
        public string UnavailableReason => null;

        public void OnActivate()
        {
            
        }

        public void OnDeactivate()
        {
            
        }

        public void Tick() { }
    }
}
