using FUSE.Editor.Screen.UI;

namespace FUSE.Editor.Track.Tools
{
    /// <summary>
    /// Scale tool — reserved for entity kinds that have a meaningful
    /// extent (scenery instances, splineys). Track nodes are points so
    /// scaling them is meaningless, and that's the only entity kind the
    /// editor currently supports. Reports unavailable with an
    /// explanatory reason so the button shows up in the strip as a
    /// labeled placeholder rather than vanishing.
    /// </summary>
    internal sealed class FuseScaleTool : IFuseEditorTool
    {
        public const string ToolId = "fuse.editor.tool.scale";

        public string Id => ToolId;
        public string LabelKey => "fuse.editor.tool.scale";
        public string IconGlyph => "⬌";
        public bool IsAvailable => false;
        public string UnavailableReason =>
            "Scale will activate once non-point entity kinds (scenery, splineys) are wired into the editor. Track nodes are points and have no extent to scale.";

        public void OnActivate() { }
        public void OnDeactivate() { }
        public void Tick() { }
    }
}
