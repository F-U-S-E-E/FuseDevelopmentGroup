using System;
using System.Collections.Generic;

namespace Fuse.Core.Model
{
    public sealed class FuseTrackDefinition
    {
        public Dictionary<string, FuseNode> Nodes { get; set; } = new Dictionary<string, FuseNode>();
        public Dictionary<string, FuseSegment> Segments { get; set; } = new Dictionary<string, FuseSegment>();
        public Dictionary<string, FuseSpan> Spans { get; set; } = new Dictionary<string, FuseSpan>();
        public Dictionary<string, FuseArea> Areas { get; set; } = new Dictionary<string, FuseArea>();
        public FuseTrackRemovals Removals { get; set; } = new FuseTrackRemovals();
    }

    public sealed class FuseTrackRemovals
    {
        public string[] Nodes { get; set; } = Array.Empty<string>();
        public string[] Segments { get; set; } = Array.Empty<string>();
        public string[] Spans { get; set; } = Array.Empty<string>();
    }

    public sealed class FuseNode
    {
        public FuseVector3 Position { get; set; }
        public FuseVector3 Rotation { get; set; }
        public bool FlipSwitchStand { get; set; }
        public string GroupId { get; set; }
        public string[] Tags { get; set; }
    }

    public sealed class FuseSegment
    {
        public string StartNodeId { get; set; }
        public string EndNodeId { get; set; }
        public string Style { get; set; } = "standard";
        public string TrackClass { get; set; } = "main";
        public int SpeedLimit { get; set; } = 45;
        public int Priority { get; set; }
        public string GroupId { get; set; }
        public string[] Tags { get; set; }
        public string Gauge { get; set; }
        public bool Partial { get; set; }
        public bool PreserveStyle { get; set; }
        public bool PreserveTrackClass { get; set; }
        public bool PreserveSpeedLimit { get; set; }
        public bool PreservePriority { get; set; }
        public bool PreserveGroupId { get; set; }
    }

    public sealed class FuseSpan
    {
        public FuseTrackLocation Upper { get; set; }
        public FuseTrackLocation Lower { get; set; }
        public bool Normalize { get; set; } = true;
        public string GroupId { get; set; }
    }

    public sealed class FuseArea
    {
        public string Name { get; set; }
        public FuseVector3? Position { get; set; }
        public float? Radius { get; set; }
        public float[] TagColor { get; set; }
        public int? Order { get; set; }
        public string[] SpanIds { get; set; }
        public string GroupId { get; set; }
    }
}
