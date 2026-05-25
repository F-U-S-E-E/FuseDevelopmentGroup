using System;
using UnityEngine;

namespace FUSE.Authoring.Data
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
        public Vector3 Position { get; set; }
        public Vector3 Rotation { get; set; }
    }

    public sealed class FuseEditorSelection
    {
        public string Id { get; set; }
        public string Type { get; set; }
    }
}
