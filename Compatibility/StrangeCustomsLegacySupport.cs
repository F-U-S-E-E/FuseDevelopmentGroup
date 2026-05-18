using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Track;
using UnityEngine;

// Interop declarations matching the StrangeCustoms public-API shape that the
// old-loader plugin ecosystem (DKW, ModularScenery, Alina's Map Mod, etc.) was
// compiled against. FUSE.dll declares these types so the CLR can resolve
// TypeRefs from those legacy DLLs when FuseLegacySupportAssemblyShim redirects
// `StrangeCustoms.dll` requests to this assembly.
//
// Bodies are FUSE-controlled facades — none of the StrangeCustoms implementation
// has been copied. FUSE's loading pipeline owns these types and is free to
// remove them once we stop honoring the old-loader contract.

namespace StrangeCustoms
{
    /// <summary>
    /// Spliney builder contract that legacy mods implement to produce custom
    /// scenery / track geometry from JSON data inside the legacy graph.
    /// </summary>
    public interface ISplineyBuilder
    {
        GameObject BuildSpliney(string id, Transform parentTransform, JObject data);
    }

    /// <summary>
    /// Carries a writable view of the legacy graph state to plugins that
    /// subscribe via Messenger.Default. Plugins mutate State.Tracks /
    /// State.Splineys to inject nodes/segments before FUSE applies them.
    /// </summary>
    public struct GraphWillChangeEvent
    {
        public global::StrangeCustoms.Tracks.TrackState State;
        private readonly Action<string[]> _onMarkChanged;

        internal GraphWillChangeEvent(global::StrangeCustoms.Tracks.TrackState state, Action<string[]> onMarkChanged)
        {
            State = state;
            _onMarkChanged = onMarkChanged;
        }

        public void MarkChanged(params string[] path)
        {
            _onMarkChanged?.Invoke(path);
        }
    }

    /// <summary>
    /// Fired after the legacy graph has been applied to the game.
    /// </summary>
    public struct GraphDidChangeEvent
    {
        public global::StrangeCustoms.Tracks.TrackState State;

        internal GraphDidChangeEvent(global::StrangeCustoms.Tracks.TrackState state)
        {
            State = state;
        }
    }

    /// <summary>
    /// Fired before legacy mixinto JSON is deserialized so plugins can layer
    /// in additional patches. FUSE's converter owns the patch path natively,
    /// so this carries no patch state by default; the ApplyPatch method exists
    /// for interop and is a no-op unless FUSE wires it through.
    /// </summary>
    public struct GraphJsonWillDeserializeEvent
    {
        private readonly IReadOnlyDictionary<string, string> _changedKeys;
        private readonly Action<string, JObject> _onApplyPatch;

        internal GraphJsonWillDeserializeEvent(
            IReadOnlyDictionary<string, string> changedKeys,
            Action<string, JObject> onApplyPatch)
        {
            _changedKeys = changedKeys;
            _onApplyPatch = onApplyPatch;
        }

        public IReadOnlyDictionary<string, string> ChangedKeys =>
            _changedKeys ?? EmptyChangedKeys;

        public void ApplyPatch(string patchSource, JObject patch)
        {
            _onApplyPatch?.Invoke(patchSource, patch);
        }

        private static readonly IReadOnlyDictionary<string, string> EmptyChangedKeys =
            new Dictionary<string, string>(0);
    }
}

namespace StrangeCustoms.Tracks
{
    /// <summary>
    /// Mutable view of the legacy graph that GraphWillChangeEvent hands to
    /// plugins. FUSE populates Tracks and Splineys before firing the event and
    /// reads back any mutations to fold into its own graph.
    /// </summary>
    public class TrackState
    {
        public GraphTracks Tracks { get; internal set; } = new GraphTracks();
        public Dictionary<string, JObject> Splineys { get; internal set; } =
            new Dictionary<string, JObject>();
    }

    public class GraphTracks
    {
        public Dictionary<string, SerializedNode> Nodes { get; internal set; } =
            new Dictionary<string, SerializedNode>();
        public Dictionary<string, SerializedSegment> Segments { get; internal set; } =
            new Dictionary<string, SerializedSegment>();
    }

    public class SerializedNode
    {
        public Vector3 Position { get; set; }
        public Vector3 Rotation { get; set; }
        public bool FlipSwitchStand { get; set; }

        public SerializedNode()
        {
        }

        public SerializedNode(TrackNode trackNode)
        {
            if (trackNode == null)
            {
                return;
            }

            var t = trackNode.transform;
            Position = t.localPosition;
            Rotation = t.eulerAngles;
            FlipSwitchStand = trackNode.flipSwitchStand;
        }
    }

    public class SerializedSegment
    {
        public TrackSegment.Style Style { get; set; }
        public TrackClass TrackClass { get; set; }
        public string StartId { get; set; }
        public string EndId { get; set; }
        public int Priority { get; set; }
        public int SpeedLimit { get; set; } = 45;
        public string GroupId { get; set; }

        public SerializedSegment()
        {
        }

        public SerializedSegment(TrackSegment trackSegment)
        {
            if (trackSegment == null)
            {
                return;
            }

            StartId = trackSegment.a?.id;
            EndId = trackSegment.b?.id;
            Style = trackSegment.style;
            TrackClass = trackSegment.trackClass;
            Priority = trackSegment.priority;
            SpeedLimit = trackSegment.speedLimit;
            GroupId = trackSegment.groupId;
        }
    }

    /// <summary>
    /// Which end of a TrackSegment a SerializedLocation is anchored at.
    /// </summary>
    public enum SerializedSegmentEnd
    {
        Start,
        End
    }

    /// <summary>
    /// Pointer into the track graph at a specific distance from a segment end.
    /// Referenced by SignalsEverywhere's SerializedCTCSignal.Location field.
    /// </summary>
    public class SerializedLocation
    {
        public string SegmentId { get; set; }
        public float Distance { get; set; }
        public SerializedSegmentEnd End { get; set; }

        public SerializedLocation()
        {
        }
    }

    /// <summary>
    /// A track span defined by Upper and Lower SerializedLocations. Referenced
    /// from SignalsEverywhere's SerializedCTCBlock.Spans collection.
    /// </summary>
    public class SerializedSpan
    {
        public SerializedLocation Upper { get; set; }
        public SerializedLocation Lower { get; set; }
        public bool Normalize { get; set; }

        public SerializedSpan()
        {
        }
    }

    /// <summary>
    /// Legacy industry-component definition shape. The original carries many
    /// more fields and SC-internal apply/validate methods that hook the game's
    /// IndustryComponent hierarchy; this facade declares only the public
    /// surface that downstream plugins (InterchangedIndustryUnloader's Harmony
    /// patches on .ctor and ApplyTo) need to bind against. Apply behavior is
    /// FUSE's responsibility — see FuseLegacyDataConverter for how component
    /// definitions are routed through native FUSE types.
    /// </summary>
    public class SerializedComponent
    {
        public string Type { get; set; }
        public string Name { get; set; }
        public string[] TrackSpans { get; set; }
        public string CarTypeFilter { get; set; }
        public bool SharedStorage { get; set; } = true;
        public string LoadId { get; set; }
        public float? StorageChangeRate { get; set; }
        public float? MaxStorage { get; set; }
        public bool? OrderAroundEmpties { get; set; }
        public float? CarTransferRate { get; set; }
        public bool? OrderAroundLoaded { get; set; }
        public float? CarLoadPeriod { get; set; }
        public float? CarLengthFeet { get; set; }
        public bool? CanOverhaul { get; set; }

        public SerializedComponent()
        {
        }
    }
}
