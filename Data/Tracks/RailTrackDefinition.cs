using System;
using System.Collections.Generic;
using RAIL.Data.Common;
using UnityEngine;

namespace RAIL.Data
{
    public sealed class RailTrackDefinition
    {
        public Dictionary<string, RailNode> Nodes { get; set; } = new Dictionary<string, RailNode>();
        public Dictionary<string, RailSegment> Segments { get; set; } = new Dictionary<string, RailSegment>();
        public Dictionary<string, RailSpan> Spans { get; set; } = new Dictionary<string, RailSpan>();
        public Dictionary<string, RailArea> Areas { get; set; } = new Dictionary<string, RailArea>();
        public RailTrackRemovals Removals { get; set; } = new RailTrackRemovals();
    }

    public sealed class RailTrackRemovals
    {
        public string[] Nodes { get; set; } = Array.Empty<string>();
        public string[] Segments { get; set; } = Array.Empty<string>();
        public string[] Spans { get; set; } = Array.Empty<string>();
    }

    public sealed class RailNode
    {
        public Vector3 Position { get; set; }
        public Vector3 Rotation { get; set; }
        public bool FlipSwitchStand { get; set; }
        public string GroupId { get; set; }
        public string[] Tags { get; set; }
    }

    public sealed class RailSegment
    {
        public string StartNodeId { get; set; }
        public string EndNodeId { get; set; }
        public string Style { get; set; } = "standard";
        public string TrackClass { get; set; } = "main";
        public int SpeedLimit { get; set; } = 45;
        public int Priority { get; set; }
        public string GroupId { get; set; }
        public string[] Tags { get; set; }
    }

    public sealed class RailSpan
    {
        public RailTrackLocation Upper { get; set; }
        public RailTrackLocation Lower { get; set; }
        public bool Normalize { get; set; } = true;
        public string GroupId { get; set; }
    }

    public sealed class RailArea
    {
        public string Name { get; set; }
        public Vector3? Position { get; set; }
        public float? Radius { get; set; }
        public float[] TagColor { get; set; }
        public int? Order { get; set; }
        public string[] SpanIds { get; set; }
        public string GroupId { get; set; }
    }
}
