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
using Track;
using Track.Signals;
using UnityEngine;

namespace FUSE.Runtime.API
{
    public static partial class TrackAPI
    {

        public static void CaptureBaseGraphSnapshot(string reason)
        {
            if (_baseGraphSnapshotCaptured)
            {
                return;
            }

            var graph = Graph.Shared;
            if (graph == null)
            {
                FuseLog.Warning($"FUSE base graph snapshot skipped operation='capture' reason='{reason ?? string.Empty}' detail='Graph.Shared is null'.");
                return;
            }

            try
            {
                BaseNodeDefinitions.Clear();
                BaseSegmentDefinitions.Clear();
                BaseSpanDefinitions.Clear();

                foreach (var node in GetAllNodes())
                {
                    if (node == null || string.IsNullOrWhiteSpace(node.id))
                    {
                        continue;
                    }

                    BaseNodeDefinitions[node.id] = GetDefinition(node);
                }

                foreach (var segment in GetAllSegments())
                {
                    if (segment == null || string.IsNullOrWhiteSpace(segment.id))
                    {
                        continue;
                    }

                    BaseSegmentDefinitions[segment.id] = GetDefinition(segment);
                }

                foreach (var span in GetAllSpans())
                {
                    if (span == null || string.IsNullOrWhiteSpace(span.id))
                    {
                        continue;
                    }

                    var definition = GetDefinition(span);
                    if (definition?.Upper?.SegmentId == null || definition.Lower?.SegmentId == null)
                    {
                        continue;
                    }

                    BaseSpanDefinitions[span.id] = CloneSpanDefinition(definition);
                }

                _baseGraphSnapshotCaptured = true;
                FuseLog.Info(
                    $"FUSE captured base graph snapshot reason='{reason ?? string.Empty}' " +
                    $"nodes={BaseNodeDefinitions.Count} segments={BaseSegmentDefinitions.Count} spans={BaseSpanDefinitions.Count}.");
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE base graph snapshot failed operation='capture' reason='{reason ?? string.Empty}' error=''.", ex);
            }
        }

        public static void ClearBaseGraphSnapshot()
        {
            BaseNodeDefinitions.Clear();
            BaseSegmentDefinitions.Clear();
            BaseSpanDefinitions.Clear();
            _baseGraphSnapshotCaptured = false;
        }

        public static bool HasBaseGraphSnapshot => _baseGraphSnapshotCaptured;

        public static FuseTrackDefinition GetBaseGraphSnapshotDefinition()
        {
            if (!_baseGraphSnapshotCaptured)
            {
                return null;
            }

            return new FuseTrackDefinition
            {
                Nodes = BaseNodeDefinitions.ToDictionary(
                    item => item.Key,
                    item => CloneNodeDefinition(item.Value),
                    StringComparer.OrdinalIgnoreCase),
                Segments = BaseSegmentDefinitions.ToDictionary(
                    item => item.Key,
                    item => CloneSegmentDefinition(item.Value),
                    StringComparer.OrdinalIgnoreCase),
                Spans = BaseSpanDefinitions.ToDictionary(
                    item => item.Key,
                    item => CloneSpanDefinition(item.Value),
                    StringComparer.OrdinalIgnoreCase),
                Areas = new Dictionary<string, FuseArea>(StringComparer.OrdinalIgnoreCase),
                Removals = new FuseTrackRemovals()
            };
        }

        public static FuseTrackDefinition GetRuntimeGraphDefinition()
        {
            return new FuseTrackDefinition
            {
                Nodes = GetAllNodes()
                    .Where(node => node != null && !string.IsNullOrWhiteSpace(node.id))
                    .GroupBy(node => node.id, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        group => group.Key,
                        group => CloneNodeDefinition(GetDefinition(group.First())),
                        StringComparer.OrdinalIgnoreCase),
                Segments = GetAllSegments()
                    .Where(segment => segment != null && !string.IsNullOrWhiteSpace(segment.id))
                    .GroupBy(segment => segment.id, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        group => group.Key,
                        group => CloneSegmentDefinition(GetDefinition(group.First())),
                        StringComparer.OrdinalIgnoreCase),
                Spans = GetRegisteredGraphSpans()
                    .GroupBy(span => span.id, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        group => group.Key,
                        group => CloneSpanDefinition(GetDefinition(group.First())),
                        StringComparer.OrdinalIgnoreCase),
                Areas = GetAllAreas()
                    .Where(area => area != null && !string.IsNullOrWhiteSpace(area.identifier))
                    .GroupBy(area => area.identifier, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        group => group.Key,
                        group => GetDefinition(group.First()),
                        StringComparer.OrdinalIgnoreCase),
                Removals = new FuseTrackRemovals()
            };
        }

        public static void ClearRuntimeMetadata()
        {
            AreaOrders.Clear();
            AreaFallbackOrders.Clear();
            AreaAliases.Clear();
        }
    }
}
