using System;
using UnityEngine;

namespace RAIL.Data
{
    public sealed class RailEditorState
    {
        public string ProjectName { get; set; }
        public DateTime? LastEditedUtc { get; set; }
        public RailEditorViewport Viewport { get; set; }
        public RailEditorSelection SelectedObject { get; set; }
    }

    public sealed class RailEditorViewport
    {
        public Vector3 Position { get; set; }
        public Vector3 Rotation { get; set; }
    }

    public sealed class RailEditorSelection
    {
        public string Id { get; set; }
        public string Type { get; set; }
    }
}
