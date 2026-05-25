using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Core;
using Helpers;
using KeyValue.Runtime;
using FUSE.Runtime.Cache;
using FUSE.Authoring.Data;
using FUSE.Infrastructure;
using RollingStock.Controls;
using Track;
using UnityEngine;

namespace FUSE.Runtime.API
{
    public static partial class TurntableAPI
    {

        public static string GetPitNodeId(string turntableId, int index)
        {
            return FuseTurntableIds.GetPitNodeId(turntableId, index);
        }

        private static List<TrackNode> CreateOrUpdatePitNodes(Turntable turntable, FuseTurntable definition)
        {
            var nodes = new List<TrackNode>(turntable.subdivisions);
            var rootRotation = turntable.transform.localRotation;
            var turntableId = GetDefinitionTurntableId(turntable);
            TrackAPI.BeginBatch();
            try
            {
                for (var index = 0; index < turntable.subdivisions; index++)
                {
                    var angle = (360f / turntable.subdivisions) * index;
                    var rotation = rootRotation * Quaternion.Euler(0f, angle, 0f);
                    var position = turntable.transform.localPosition + rotation * Vector3.forward * turntable.radius;
                    var nodeId = GetPitNodeId(turntableId, index, definition);
                    var node = TrackAPI.GetNode(nodeId);
                    if (node == null)
                    {
                        node = TrackAPI.AddNode(nodeId, position, rotation.eulerAngles);
                    }
                    else
                    {
                        TrackAPI.UpdateNode(nodeId, position, rotation.eulerAngles);
                    }

                    node.turntable = turntable;
                    nodes.Add(node);
                }
            }
            finally
            {
                TrackAPI.EndBatch();
            }

            return nodes;
        }

        private static void RemoveStaleRoundhouseTracks(Turntable turntable, FuseTurntable previousDefinition, FuseTurntable nextDefinition)
        {
            if (turntable == null)
            {
                return;
            }

            var turntableId = GetDefinitionTurntableId(turntable);
            var desiredSegments = GetDesiredRoundhouseSegmentIds(turntableId, nextDefinition);
            var desiredNodes = GetDesiredRoundhouseNodeIds(turntableId, nextDefinition);

            var generatedSegmentIds = TrackAPI.GetAllSegments()
                .Where(segment => segment != null &&
                                  !string.IsNullOrWhiteSpace(segment.id) &&
                                  IsGeneratedRoundhouseSegmentId(segment.id, turntableId, previousDefinition, nextDefinition) &&
                                  !desiredSegments.Contains(segment.id))
                .Select(segment => segment.id)
                .ToArray();

            var generatedNodeIds = TrackAPI.GetAllNodes()
                .Where(node => node != null &&
                               !string.IsNullOrWhiteSpace(node.id) &&
                               IsGeneratedRoundhouseNodeId(node.id, turntableId, previousDefinition, nextDefinition) &&
                               !desiredNodes.Contains(node.id))
                .Select(node => node.id)
                .ToArray();

            if (generatedSegmentIds.Length == 0 && generatedNodeIds.Length == 0)
            {
                return;
            }

            TrackAPI.BeginBatch();
            try
            {
                foreach (var segmentId in generatedSegmentIds)
                {
                    if (TrackAPI.GetSegment(segmentId) != null)
                    {
                        TrackAPI.RemoveSegment(segmentId);
                    }
                }

                foreach (var nodeId in generatedNodeIds)
                {
                    var node = TrackAPI.GetNode(nodeId);
                    if (node == null)
                    {
                        continue;
                    }

                    var remainingConnections = Graph.Shared != null
                        ? Graph.Shared.SegmentsConnectedTo(node)
                            .Where(segment => segment != null &&
                                              !IsGeneratedRoundhouseSegmentId(segment.id, turntableId, previousDefinition, nextDefinition))
                            .ToArray()
                        : Array.Empty<TrackSegment>();
                    if (remainingConnections.Length > 0)
                    {
                        FuseLog.Warning(
                            $"FUSE kept generated roundhouse node '{nodeId}' for turntable '{turntableId}' " +
                            $"because {remainingConnections.Length} non-generated segment(s) still reference it.");
                        continue;
                    }

                    TrackAPI.RemoveNode(nodeId);
                }

                FuseLog.Info(
                    $"FUSE removed stale generated roundhouse graph for turntable '{turntableId}' " +
                    $"segments={generatedSegmentIds.Length} nodes={generatedNodeIds.Length}.");
            }
            finally
            {
                TrackAPI.EndBatch();
            }
        }

        private static HashSet<string> GetDesiredRoundhouseSegmentIds(string turntableId, FuseTurntable definition)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var stalls = definition?.Roundhouse?.Stalls ?? 0;
            if (stalls <= 0)
            {
                return result;
            }

            for (var index = 1; index <= stalls; index++)
            {
                result.Add(GetRoundhouseSegmentId(turntableId, index, definition));
            }

            return result;
        }

        private static HashSet<string> GetDesiredRoundhouseNodeIds(string turntableId, FuseTurntable definition)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var stalls = definition?.Roundhouse?.Stalls ?? 0;
            if (stalls <= 0)
            {
                return result;
            }

            for (var index = 1; index <= stalls; index++)
            {
                result.Add(GetRoundhouseNodeId(turntableId, index, definition));
            }

            return result;
        }

        private static bool IsGeneratedRoundhouseSegmentId(string id, string turntableId, params FuseTurntable[] definitions)
        {
            return RoundhouseSegmentPrefixes(turntableId, definitions)
                .Any(prefix => id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsGeneratedRoundhouseNodeId(string id, string turntableId, params FuseTurntable[] definitions)
        {
            return RoundhouseNodePrefixes(turntableId, definitions)
                .Any(prefix => id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }

        private static IEnumerable<string> RoundhouseSegmentPrefixes(string turntableId, params FuseTurntable[] definitions)
        {
            foreach (var legacyIdentifier in LegacyIdentifiers(definitions))
            {
                yield return $"S{legacyIdentifier}RoundhouseSegment";
            }

            if (!string.IsNullOrWhiteSpace(turntableId))
            {
                yield return $"{turntableId}.roundhouse.segment.";
            }
        }

        private static IEnumerable<string> RoundhouseNodePrefixes(string turntableId, params FuseTurntable[] definitions)
        {
            foreach (var legacyIdentifier in LegacyIdentifiers(definitions))
            {
                yield return $"N{legacyIdentifier}RoundhouseNode";
            }

            if (!string.IsNullOrWhiteSpace(turntableId))
            {
                yield return $"{turntableId}.roundhouse.node.";
            }
        }

        private static IEnumerable<string> LegacyIdentifiers(IEnumerable<FuseTurntable> definitions)
        {
            return (definitions ?? Enumerable.Empty<FuseTurntable>())
                .Where(definition => definition != null && !string.IsNullOrWhiteSpace(definition.LegacyIdentifier))
                .Select(definition => definition.LegacyIdentifier)
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private static void CreateOrUpdateRoundhouseTracks(Turntable turntable, FuseTurntable definition)
        {
            var roundhouse = definition.Roundhouse;
            if (roundhouse == null || roundhouse.Stalls <= 0)
            {
                return;
            }

            var angleStep = 360f / Mathf.Max(turntable.subdivisions, 1);
            var rootRotation = turntable.transform.localRotation;
            var trackLength = roundhouse.TrackLength > 0f ? roundhouse.TrackLength : 46f;
            var turntableId = GetDefinitionTurntableId(turntable);

            TrackAPI.BeginBatch();
            try
            {
                for (var index = 1; index <= roundhouse.Stalls; index++)
                {
                    var angle = angleStep * index;
                    var rotation = rootRotation * Quaternion.Euler(0f, angle, 0f);
                    var nodePosition = turntable.transform.localPosition + rotation * Vector3.forward * (turntable.radius + trackLength);

                    var roundhouseNodeId = GetRoundhouseNodeId(turntableId, index, definition);
                    var roundhouseNode = TrackAPI.GetNode(roundhouseNodeId);
                    if (roundhouseNode == null)
                    {
                        roundhouseNode = TrackAPI.AddNode(roundhouseNodeId, nodePosition, rotation.eulerAngles);
                    }
                    else
                    {
                        TrackAPI.UpdateNode(roundhouseNodeId, nodePosition, rotation.eulerAngles);
                    }

                    var roundhouseSegmentId = GetRoundhouseSegmentId(turntableId, index, definition);
                    var pitNodeId = GetPitNodeId(turntableId, index, definition);
                    var segment = TrackAPI.GetSegment(roundhouseSegmentId);
                    if (segment == null)
                    {
                        TrackAPI.AddSegment(roundhouseSegmentId, pitNodeId, roundhouseNodeId, TrackSegment.Style.Yard, 10);
                    }
                    else if (segment.a.id != pitNodeId || segment.b.id != roundhouseNodeId)
                    {
                        TrackAPI.RemoveSegment(roundhouseSegmentId);
                        TrackAPI.AddSegment(roundhouseSegmentId, pitNodeId, roundhouseNodeId, TrackSegment.Style.Yard, 10);
                    }
                    else
                    {
                        TrackAPI.UpdateSegment(roundhouseSegmentId, TrackSegment.Style.Yard, 10);
                    }
                }
            }
            finally
            {
                TrackAPI.EndBatch();
            }
        }

        internal static string GetPitNodeId(string turntableId, int index, FuseTurntable definition)
        {
            return FuseTurntableIds.GetPitNodeId(turntableId, index, definition);
        }

        internal static string GetRoundhouseNodeId(string turntableId, int index, FuseTurntable definition)
        {
            return FuseTurntableIds.GetRoundhouseNodeId(turntableId, index, definition);
        }

        internal static string GetRoundhouseSegmentId(string turntableId, int index, FuseTurntable definition)
        {
            return FuseTurntableIds.GetRoundhouseSegmentId(turntableId, index, definition);
        }
    }
}
