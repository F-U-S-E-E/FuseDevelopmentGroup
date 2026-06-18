using System;

namespace Fuse.Core.Model
{
    public sealed class FuseEditorState
    {
        public string ProjectName { get; set; }
        public DateTime? LastEditedUtc { get; set; }
        public FuseEditorViewport Viewport { get; set; }
        public FuseEditorSelection SelectedObject { get; set; }
    }

    public sealed class FuseEditorViewport
    {
        public FuseVector3 Position { get; set; }
        public FuseVector3 Rotation { get; set; }
    }

    public sealed class FuseEditorSelection
    {
        public string Id { get; set; }
        public string Type { get; set; }
    }
}
