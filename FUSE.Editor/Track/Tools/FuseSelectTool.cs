using FUSE.Editor.Screen.UI;

namespace FUSE.Editor.Track.Tools
{
    /// <summary>
    /// The idle / inspection tool. Turns on the FUSE entity markers for
    /// the active mod and lets the user click them to surface their
    /// metadata in the Properties panel. No gizmo, no scene mutation.
    /// </summary>
    internal sealed class FuseSelectTool : IFuseEditorTool
    {
        public const string ToolId = "fuse.editor.tool.select";

        public string Id => ToolId;
        public string LabelKey => "fuse.editor.tool.select";
        public string IconGlyph => "▢";
        public bool IsAvailable => true;
        public string UnavailableReason => null;

        public void OnActivate()
        {
            FuseNodeEditorController.ShowMarkersForActiveMod();
        }

        public void OnDeactivate()
        {
            FuseNodeEditorController.DeselectCurrent();
            FuseNodeEditorController.ClearMarkers();
        }

        public void OnNodeSelected(FuseNodeMarker marker)
        {
            // Pure inspection — selection state is already managed by
            // FuseNodeEditorController.SelectMarker; nothing extra to do.
        }

        public void Tick() { }
    }
}
