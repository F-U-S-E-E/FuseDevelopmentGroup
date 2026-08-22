using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Model.Ops;
using FUSE.Runtime.Cache;
using FUSE.Authoring.Data;
using FUSE.Authoring.Data.Common;
using FUSE.Runtime.Events;
using FUSE.Infrastructure;
using FUSE.Compatibility;
using Track;
using Track.Signals;
using UnityEngine;

namespace FUSE.Runtime.API
{
    public static partial class TrackAPI
    {

        public static TrackSegment AddSegment(string id, string startNodeId, string endNodeId, TrackSegment.Style style = TrackSegment.Style.Standard, int speedLimit = 45, string groupId = null, TrackClass trackClass = TrackClass.Mainline, int priority = 0)
        {
            return AddSegmentCore(
                id,
                startNodeId,
                endNodeId,
                style,
                speedLimit,
                groupId,
                trackClass,
                priority,
                null);
        }

        private static TrackSegment AddSegmentCore(
            string id,
            string startNodeId,
            string endNodeId,
            TrackSegment.Style style,
            int speedLimit,
            string groupId,
            TrackClass trackClass,
            int priority,
            Action<TrackSegment> beforeNotification)
        {
            RequireId(id, nameof(id));
            var graph = RequireGraph();
            if (graph.GetSegment(id) != null)
            {
                throw new InvalidOperationException($"Track segment '{id}' already exists.");
            }

            var start = RequireNode(startNodeId);
            var end = RequireNode(endNodeId);
            var segment = CreateGraphChild<TrackSegment>(graph, "Segment-" + id);
            segment.id = id;
            segment.a = start;
            segment.b = end;
            segment.style = style;
            segment.trackClass = trackClass;
            segment.speedLimit = speedLimit;
            segment.priority = priority;
            segment.groupId = groupId;

            beforeNotification?.Invoke(segment);
            graph.AddSegment(segment, true);
            FuseSegmentRuntimeIndex.Instance.Set(id, segment);
            FuseEvents.RaiseSegmentAdded(segment);
            RequestRebuild();
            FuseApiPersistence.RecordDefinition(FuseDefinitionKind.TrackSegment, id, GetDefinition(segment));
            return segment;
        }

        public static TrackSegment AddSegment(string id, FuseSegment definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            var segment = AddSegmentCore(
                id,
                definition.StartNodeId,
                definition.EndNodeId,
                ParseSegmentStyle(definition.Style),
                definition.SpeedLimit,
                definition.GroupId,
                ParseTrackClass(definition.TrackClass),
                definition.Priority,
                value => RailroaderTrackContract.ApplyStructure(
                    value,
                    definition.Style,
                    definition.BridgeSupportsSteel,
                    definition.Yard));
            FuseApiPersistence.RecordDefinition(FuseDefinitionKind.TrackSegment, id, definition);
            return segment;
        }

        public static void UpdateSegment(string id, TrackSegment.Style style, int speedLimit, TrackClass? trackClass = null, int? priority = null, string groupId = null)
        {
            UpdateSegmentCore(id, style, speedLimit, trackClass, priority, groupId, null);
        }

        private static void UpdateSegmentCore(
            string id,
            TrackSegment.Style style,
            int speedLimit,
            TrackClass? trackClass,
            int? priority,
            string groupId,
            Action<TrackSegment> beforeNotification)
        {
            var segment = RequireSegment(id);
            segment.style = style;
            segment.speedLimit = speedLimit;
            if (trackClass != null)
            {
                segment.trackClass = trackClass.Value;
            }

            if (priority != null)
            {
                segment.priority = priority.Value;
            }

            if (groupId != null)
            {
                segment.groupId = groupId;
            }

            segment.InvalidateCurve();
            Graph.Shared.InvalidateNode(segment.a);
            Graph.Shared.InvalidateNode(segment.b);
            beforeNotification?.Invoke(segment);
            FuseSegmentRuntimeIndex.Instance.Set(id, segment);
            FuseEvents.RaiseSegmentUpdated(segment);
            RequestRebuild();
            FuseApiPersistence.RecordDefinition(FuseDefinitionKind.TrackSegment, id, GetDefinition(segment));
        }

        public static void UpdateSegment(string id, FuseSegment definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            UpdateSegmentCore(
                id,
                ParseSegmentStyle(definition.Style),
                definition.SpeedLimit,
                ParseTrackClass(definition.TrackClass),
                definition.Priority,
                definition.GroupId,
                value => RailroaderTrackContract.ApplyStructure(
                    value,
                    definition.Style,
                    definition.BridgeSupportsSteel,
                    definition.Yard));
            FuseApiPersistence.RecordDefinition(FuseDefinitionKind.TrackSegment, id, definition);
        }

        public static void RemoveSegment(string id)
        {
            var segment = RequireSegment(id);
            //DisableTrackMarkersReferencingSegment(segment, "track segment removal");
            //RemoveSpansReferencingSegment(segment, "track segment removal");
            UnregisterSegmentWithGraph(Graph.Shared, id);
            RemoveRuntimeObject(segment);
            FuseSegmentRuntimeIndex.Instance.Remove(id);
            FuseRuntimeDefinitionCache.Remove(FuseDefinitionKind.TrackSegment, id);
            FuseEvents.RaiseSegmentRemoved(id);
            RequestRebuild();
        }

        public static TrackSegment GetSegment(string id)
        {
            var graph = Graph.Shared;
            return graph != null && !string.IsNullOrWhiteSpace(id) ? graph.GetSegment(id) : null;
        }

        public static IEnumerable<TrackSegment> GetAllSegments()
        {
            var graph = Graph.Shared;
            return graph != null ? graph.Segments : Enumerable.Empty<TrackSegment>();
        }

        public static FuseSegment GetSegmentDefinition(string id)
        {
            return GetDefinition(GetSegment(id));
        }

        public static FuseSegment GetDefinition(TrackSegment segment)
        {
            if (segment == null)
            {
                return null;
            }

            FuseRuntimeDefinitionCache.TryGet(FuseDefinitionKind.TrackSegment, segment.id, out FuseSegment definition);
            definition = definition ?? new FuseSegment();
            definition.StartNodeId = segment.a != null ? segment.a.id : null;
            definition.EndNodeId = segment.b != null ? segment.b.id : null;
            definition.Style = RailroaderTrackContract.GetStyleName(segment);
            definition.BridgeSupportsSteel = RailroaderTrackContract.GetBridgeSupportsSteel(segment);
            definition.Yard = RailroaderTrackContract.GetYard(segment);
            definition.TrackClass = segment.trackClass == TrackClass.Mainline ? "main" : segment.trackClass.ToString();
            definition.SpeedLimit = segment.speedLimit;
            definition.Priority = segment.priority;
            definition.GroupId = segment.groupId;
            return definition;
        }
    }
}
