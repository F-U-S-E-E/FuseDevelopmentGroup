using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Model.Ops;
using FUSE.Cache;
using FUSE.Data;
using FUSE.Data.Common;
using FUSE.Events;
using FUSE.Infrastructure;
using Track;
using UnityEngine;

namespace FUSE.API
{
    public static class TrackAPI
    {
        private const float SpanDistanceTolerance = 0.001f;

        private static int _batchDepth;
        private static bool _rebuildRequested;
        private static Transform _fallbackAreaRoot;
        private static readonly Dictionary<string, int> AreaOrders = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, FuseNode> BaseNodeDefinitions = new Dictionary<string, FuseNode>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, FuseSegment> BaseSegmentDefinitions = new Dictionary<string, FuseSegment>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, FuseSpan> BaseSpanDefinitions = new Dictionary<string, FuseSpan>(StringComparer.OrdinalIgnoreCase);
        private static readonly MethodInfo GraphAddSpanMethod =
            typeof(Graph).GetMethod("AddSpan", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo GraphSpansField =
            typeof(Graph).GetField("spans", BindingFlags.Instance | BindingFlags.NonPublic);
        private static bool _warnedMissingGraphAddSpan;
        private static bool _baseGraphSnapshotCaptured;

        public static bool IsBatching => _batchDepth > 0;

        public static TrackNode AddNode(string id, Vector3 position, Vector3 rotation, bool flipStand = false, string groupId = null)
        {
            RequireId(id, nameof(id));
            var graph = RequireGraph();
            if (graph.GetNode(id) != null)
            {
                throw new InvalidOperationException($"Track node '{id}' already exists.");
            }

            var node = CreateGraphChild<TrackNode>(graph, "Node-" + id);
            node.id = id;
            node.transform.localPosition = position;
            node.transform.localRotation = Quaternion.Euler(rotation);
            node.flipSwitchStand = flipStand;

            graph.AddNode(node);
            FuseNodeRuntimeIndex.Instance.Set(id, node);
            FuseEvents.RaiseNodeAdded(node);
            RequestRebuild();
            FuseApiPersistence.RecordDefinition(FuseDefinitionKind.TrackNode, id, GetDefinition(node));
            return node;
        }

        public static TrackNode AddNode(string id, FuseNode definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            var node = AddNode(id, definition.Position, definition.Rotation, definition.FlipSwitchStand, definition.GroupId);
            FuseApiPersistence.RecordDefinition(FuseDefinitionKind.TrackNode, id, definition);
            return node;
        }

        public static void UpdateNode(string id, Vector3 position, Vector3 rotation, bool? flipStand = null)
        {
            var node = RequireNode(id);
            node.transform.localPosition = position;
            node.transform.localRotation = Quaternion.Euler(rotation);
            if (flipStand != null)
            {
                node.flipSwitchStand = flipStand.Value;
            }

            Graph.Shared.OnNodeDidChange(node);
            FuseNodeRuntimeIndex.Instance.Set(id, node);
            FuseEvents.RaiseNodeUpdated(node);
            RequestRebuild();
            FuseApiPersistence.RecordDefinition(FuseDefinitionKind.TrackNode, id, GetDefinition(node));
        }

        public static void UpdateNode(string id, FuseNode definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            UpdateNode(id, definition.Position, definition.Rotation, definition.FlipSwitchStand);
            FuseApiPersistence.RecordDefinition(FuseDefinitionKind.TrackNode, id, definition);
        }

        public static void RemoveNode(string id)
        {
            var node = RequireNode(id);
            foreach (var segment in Graph.Shared.SegmentsConnectedTo(node).ToArray())
            {
                RemoveSegment(segment.id);
            }

            RemoveRuntimeObject(node);
            FuseNodeRuntimeIndex.Instance.Remove(id);
            FuseRuntimeDefinitionCache.Remove(FuseDefinitionKind.TrackNode, id);
            FuseEvents.RaiseNodeRemoved(id);
            RequestRebuild();
        }

        public static TrackNode GetNode(string id)
        {
            var graph = Graph.Shared;
            return graph != null && !string.IsNullOrWhiteSpace(id) ? graph.GetNode(id) : null;
        }

        public static IEnumerable<TrackNode> GetAllNodes()
        {
            var graph = Graph.Shared;
            return graph != null ? graph.Nodes : Enumerable.Empty<TrackNode>();
        }

        public static FuseNode GetNodeDefinition(string id)
        {
            return GetDefinition(GetNode(id));
        }

        public static FuseNode GetDefinition(TrackNode node)
        {
            if (node == null)
            {
                return null;
            }

            FuseRuntimeDefinitionCache.TryGet(FuseDefinitionKind.TrackNode, node.id, out FuseNode definition);
            definition = definition ?? new FuseNode();
            definition.Position = node.transform.localPosition;
            definition.Rotation = node.transform.localEulerAngles;
            definition.FlipSwitchStand = node.flipSwitchStand;
            return definition;
        }

        public static TrackSegment AddSegment(string id, string startNodeId, string endNodeId, TrackSegment.Style style = TrackSegment.Style.Standard, int speedLimit = 45, string groupId = null, TrackClass trackClass = TrackClass.Mainline, int priority = 0)
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

            var segment = AddSegment(
                id,
                definition.StartNodeId,
                definition.EndNodeId,
                ParseSegmentStyle(definition.Style),
                definition.SpeedLimit,
                definition.GroupId,
                ParseTrackClass(definition.TrackClass),
                definition.Priority);
            FuseApiPersistence.RecordDefinition(FuseDefinitionKind.TrackSegment, id, definition);
            return segment;
        }

        public static void UpdateSegment(string id, TrackSegment.Style style, int speedLimit, TrackClass? trackClass = null, int? priority = null, string groupId = null)
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

            UpdateSegment(
                id,
                ParseSegmentStyle(definition.Style),
                definition.SpeedLimit,
                ParseTrackClass(definition.TrackClass),
                definition.Priority,
                definition.GroupId);
            FuseApiPersistence.RecordDefinition(FuseDefinitionKind.TrackSegment, id, definition);
        }

        public static void RemoveSegment(string id)
        {
            var segment = RequireSegment(id);
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
            definition.Style = segment.style.ToString();
            definition.TrackClass = segment.trackClass == TrackClass.Mainline ? "main" : segment.trackClass.ToString();
            definition.SpeedLimit = segment.speedLimit;
            definition.Priority = segment.priority;
            definition.GroupId = segment.groupId;
            return definition;
        }

        public static TrackSpan AddSpan(string id, FuseTrackLocation upper, FuseTrackLocation lower, bool normalize = true)
        {
            RequireId(id, nameof(id));
            var graph = RequireGraph();
            if (graph.SpanForId(id) != null)
            {
                throw new InvalidOperationException($"Track span '{id}' already exists.");
            }

            var upperLocation = MakeLocation(graph, upper);
            var lowerLocation = MakeLocation(graph, lower);
            ValidateSpanEndpointPair(id, ref upperLocation, ref lowerLocation);

            var span = CreateGraphChild<TrackSpan>(graph, "Span-" + id);
            try
            {
                span.id = id;
                span.upper = upperLocation;
                span.lower = lowerLocation;
                if (normalize)
                {
                    span.NormalizeUpperLower();
                }

                ValidateSpanRoute(id, span);
            }
            catch
            {
                RemoveRuntimeObject(span);
                throw;
            }

            FuseSpanRuntimeIndex.Instance.Set(id, span);
            RegisterSpanWithGraph(graph, span);
            FuseEvents.RaiseSpanAdded(span);
            RequestRebuild();
            FuseApiPersistence.RecordDefinition(FuseDefinitionKind.TrackSpan, id, GetDefinition(span));
            return span;
        }

        public static TrackSpan AddSpan(string id, FuseSpan definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            var span = AddSpan(id, definition.Upper, definition.Lower, definition.Normalize);
            FuseApiPersistence.RecordDefinition(FuseDefinitionKind.TrackSpan, id, GetDefinition(span));
            return span;
        }

        public static void UpdateSpan(string id, FuseTrackLocation upper, FuseTrackLocation lower, bool normalize = true)
        {
            var span = RequireSpan(id);
            var graph = RequireGraph();
            EnsureTrackSpanGraphChild(graph, span);
            var upperLocation = MakeLocation(graph, upper);
            var lowerLocation = MakeLocation(graph, lower);
            ValidateSpanEndpointPair(id, ref upperLocation, ref lowerLocation);

            var originalUpper = span.upper;
            var originalLower = span.lower;
            try
            {
                span.upper = upperLocation;
                span.lower = lowerLocation;
                if (normalize)
                {
                    span.NormalizeUpperLower();
                }

                ValidateSpanRoute(id, span);
            }
            catch
            {
                span.upper = originalUpper;
                span.lower = originalLower;
                throw;
            }

            FuseSpanRuntimeIndex.Instance.Set(id, span);
            RegisterSpanWithGraph(graph, span);
            FuseEvents.RaiseSpanUpdated(span);
            RequestRebuild();
            FuseApiPersistence.RecordDefinition(FuseDefinitionKind.TrackSpan, id, GetDefinition(span));
        }

        public static void UpdateSpan(string id, FuseSpan definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            UpdateSpan(id, definition.Upper, definition.Lower, definition.Normalize);
            FuseApiPersistence.RecordDefinition(FuseDefinitionKind.TrackSpan, id, GetSpanDefinition(id));
        }

        public static void RemoveSpan(string id)
        {
            var span = RequireSpan(id);
            RemoveRuntimeObject(span);
            FuseSpanRuntimeIndex.Instance.Remove(id);
            FuseRuntimeDefinitionCache.Remove(FuseDefinitionKind.TrackSpan, id);
            FuseEvents.RaiseSpanRemoved(id);
            RequestRebuild();
        }

        public static TrackSpan GetSpan(string id)
        {
            if (FuseSpanRuntimeIndex.Instance.TryGetValue(id, out var cached))
            {
                var cachedSpan = (TrackSpan)cached;
                if (cachedSpan != null)
                {
                    return cachedSpan;
                }

                FuseSpanRuntimeIndex.Instance.Remove(id);
            }

            var graph = Graph.Shared;
            if (graph != null && !string.IsNullOrWhiteSpace(id))
            {
                var graphSpan = graph.SpanForId(id);
                if (graphSpan != null)
                {
                    FuseSpanRuntimeIndex.Instance.Set(id, graphSpan);
                    return graphSpan;
                }
            }

            var sceneSpan = FindSpanInScene(id);
            if (sceneSpan != null)
            {
                FuseSpanRuntimeIndex.Instance.Set(id, sceneSpan);
                if (graph != null)
                {
                    RegisterSpanWithGraph(graph, sceneSpan);
                }
            }

            return sceneSpan;
        }

        public static IEnumerable<TrackSpan> GetAllSpans()
        {
            return UnityEngine.Object.FindObjectsOfType<TrackSpan>(true)
                .Where(span => span != null && !string.IsNullOrWhiteSpace(span.id));
        }

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
                FuseLog.Warning($"FUSE base graph snapshot failed operation='capture' reason='{reason ?? string.Empty}' error='{ex.Message}'.");
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

        public static TrackSpan TryEnsureBaseGraphSpan(string id, string reason)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            var existing = GetSpan(id);
            if (existing != null)
            {
                return existing;
            }

            if (!_baseGraphSnapshotCaptured || !BaseSpanDefinitions.TryGetValue(id, out var definition))
            {
                return null;
            }

            try
            {
                var upperSegmentId = definition.Upper?.SegmentId;
                var lowerSegmentId = definition.Lower?.SegmentId;
                if (GetSegment(upperSegmentId) == null || GetSegment(lowerSegmentId) == null)
                {
                    FuseLog.Warning(
                        $"FUSE base graph span restore skipped operation='resolve-base-span' id='{id}' reason='{reason ?? string.Empty}' " +
                        $"upperSegment='{upperSegmentId ?? string.Empty}' lowerSegment='{lowerSegmentId ?? string.Empty}' detail='endpoint segment missing at runtime'.");
                    return null;
                }

                BeginBatch();
                try
                {
                    var span = AddSpan(id, CloneSpanDefinition(definition));
                    FuseLog.Info($"FUSE restored base graph span id='{id}' reason='{reason ?? string.Empty}' from captured Railroader graph snapshot.");
                    return span;
                }
                finally
                {
                    EndBatch(false);
                }
            }
            catch (Exception ex)
            {
                FuseLog.Warning($"FUSE base graph span restore failed operation='resolve-base-span' id='{id}' reason='{reason ?? string.Empty}' error='{ex.Message}'.");
                return null;
            }
        }

        public static FuseSpan GetSpanDefinition(string id)
        {
            return GetDefinition(GetSpan(id));
        }

        public static FuseSpan GetDefinition(TrackSpan span)
        {
            if (span == null)
            {
                return null;
            }

            FuseRuntimeDefinitionCache.TryGet(FuseDefinitionKind.TrackSpan, span.id, out FuseSpan definition);
            definition = definition ?? new FuseSpan();
            definition.Upper = span.upper.HasValue ? ToDefinition(span.upper.Value) : null;
            definition.Lower = span.lower.HasValue ? ToDefinition(span.lower.Value) : null;
            return definition;
        }

        public static Area AddArea(string id, FuseArea definition)
        {
            RequireId(id, nameof(id));
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (GetArea(id) != null)
            {
                throw new InvalidOperationException($"Area '{id}' already exists.");
            }

            var displayName = string.IsNullOrWhiteSpace(definition.Name) ? id : definition.Name;
            var gameObject = new GameObject(displayName);
            gameObject.transform.SetParent(GetAreaRoot(), false);
            var area = gameObject.AddComponent<Area>();
            area.identifier = id;
            ApplyAreaDefinition(area, definition);
            RememberAreaOrder(id, definition.Order);
            FuseAreaRuntimeIndex.Instance.Set(id, area);
            FuseLog.Info($"FUSE created area '{id}' name='{displayName}' parent='{DescribeAreaParent(area.transform.parent)}' position={area.transform.localPosition} radius={area.radius}.");
            FuseApiPersistence.RecordDefinition(FuseDefinitionKind.TrackArea, id, definition);
            return area;
        }

        public static void UpdateArea(string id, FuseArea definition)
        {
            var area = RequireArea(id);
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            ApplyAreaDefinition(area, definition);
            RememberAreaOrder(id, definition.Order);
            FuseAreaRuntimeIndex.Instance.Set(id, area);
            FuseLog.Info($"FUSE updated area '{id}' name='{area.name}' position={area.transform.localPosition} radius={area.radius}.");
            FuseApiPersistence.RecordDefinition(FuseDefinitionKind.TrackArea, id, definition);
        }

        public static Area GetArea(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            if (FuseAreaRuntimeIndex.Instance.TryGetValue(id, out var cached))
            {
                return (Area)cached;
            }

            var controller = OpsController.Shared;
            if (controller != null)
            {
                var area = controller.Areas.FirstOrDefault(candidate =>
                    candidate != null &&
                    (string.Equals(candidate.identifier, id, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(candidate.name, id, StringComparison.OrdinalIgnoreCase)));
                if (area != null)
                {
                    FuseAreaRuntimeIndex.Instance.Set(area.identifier, area);
                    return area;
                }
            }

            return UnityEngine.Object.FindObjectsOfType<Area>(true).FirstOrDefault(candidate =>
                candidate != null &&
                (string.Equals(candidate.identifier, id, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(candidate.name, id, StringComparison.OrdinalIgnoreCase)));
        }

        public static IEnumerable<Area> GetAllAreas()
        {
            return UnityEngine.Object.FindObjectsOfType<Area>(true).Where(area => area != null);
        }

        public static FuseArea GetAreaDefinition(string id)
        {
            return GetDefinition(GetArea(id));
        }

        public static FuseArea GetDefinition(Area area)
        {
            if (area == null)
            {
                return null;
            }

            FuseRuntimeDefinitionCache.TryGet(FuseDefinitionKind.TrackArea, area.identifier, out FuseArea definition);
            definition = definition ?? new FuseArea();
            definition.Name = area.name;
            definition.Position = area.transform.localPosition;
            definition.Radius = area.radius;
            definition.TagColor = new[] { area.tagColor.r, area.tagColor.g, area.tagColor.b, area.tagColor.a };
            return definition;
        }

        public static void ApplyAreaOrdering()
        {
            var orderedAreas = GetAllAreas()
                .Where(area => area != null && !string.IsNullOrWhiteSpace(area.identifier) && AreaOrders.ContainsKey(area.identifier))
                .OrderBy(area => AreaOrders[area.identifier])
                .ThenBy(area => area.name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (orderedAreas.Length == 0)
            {
                return;
            }

            var firstIndex = orderedAreas.Min(area => area.transform.GetSiblingIndex());
            for (var index = 0; index < orderedAreas.Length; index++)
            {
                orderedAreas[index].transform.SetSiblingIndex(firstIndex + index);
            }

            FuseLog.Info($"FUSE applied area ordering for {orderedAreas.Length} area(s).");
        }

        public static void SetGroupEnabled(string groupId, bool enabled)
        {
            RequireGraph().SetGroupEnabled(groupId, enabled);
            RequestRebuild();
        }

        public static void BeginBatch()
        {
            _batchDepth++;
        }

        public static void EndBatch()
        {
            EndBatch(true);
        }

        public static void EndBatch(bool rebuild)
        {
            if (_batchDepth == 0)
            {
                return;
            }

            _batchDepth--;
            if (_batchDepth == 0 && _rebuildRequested && rebuild)
            {
                RebuildGraph();
            }
        }

        public static void RebuildGraph()
        {
            _rebuildRequested = false;
            var manager = TrackObjectManager.Instance;
            if (manager != null)
            {
                manager.Rebuild();
            }
            else
            {
                RequireGraph().RebuildCollections();
            }

            FuseEvents.RaiseGraphRebuilt();
        }

        public static int DisableInvalidTrackMarkers(string reason)
        {
            var disabled = 0;
            var markers = UnityEngine.Object.FindObjectsOfType<TrackMarker>(true);
            for (var index = 0; index < markers.Length; index++)
            {
                var marker = markers[index];
                if (marker == null || !marker.enabled)
                {
                    continue;
                }

                Location? location;
                try
                {
                    location = marker.Location;
                }
                catch
                {
                    location = null;
                }

                if (location != null && location.Value.IsValid)
                {
                    continue;
                }

                marker.enabled = false;
                disabled++;
                FuseLog.Info(
                    $"FUSE disabled invalid TrackMarker operation='track marker cleanup' " +
                    $"id='{marker.id ?? string.Empty}' name='{marker.name ?? string.Empty}' " +
                    $"reason='{reason ?? "unspecified"}'.");
            }

            if (disabled > 0)
            {
                FuseLog.Info($"FUSE disabled {disabled} invalid TrackMarker component(s) reason='{reason ?? "unspecified"}'.");
            }

            return disabled;
        }

        private static void RequestRebuild()
        {
            _rebuildRequested = true;
            if (!IsBatching)
            {
                RebuildGraph();
            }
        }

        private static T CreateGraphChild<T>(Graph graph, string name)
            where T : Component
        {
            var gameObject = new GameObject(name);
            gameObject.transform.SetParent(graph.transform, false);
            return gameObject.AddComponent<T>();
        }

        private static Location MakeLocation(Graph graph, FuseTrackLocation definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            var segment = graph.GetSegment(definition.SegmentId);
            if (segment == null)
            {
                throw new InvalidOperationException($"Track segment '{definition.SegmentId}' was not found.");
            }

            var segmentLength = segment.GetLength();
            var distance = definition.Distance ?? ((definition.Normalized ?? 0f) * segmentLength);
            distance += definition.Offset;
            if (distance < -SpanDistanceTolerance || distance > segmentLength + SpanDistanceTolerance)
            {
                throw new InvalidOperationException(
                    $"Track location on segment '{definition.SegmentId}' is outside the segment length. distance={distance:0.###}, segmentLength={segmentLength:0.###}, end='{definition.End ?? "A"}'.");
            }

            return new Location(
                segment,
                Mathf.Clamp(distance, 0f, segmentLength),
                ParseLocationEnd(definition.End));
        }

        private static void ValidateSpanEndpointPair(string spanId, ref Location upper, ref Location lower)
        {
            if (upper.segment == null || lower.segment == null)
            {
                throw new InvalidOperationException($"Track span '{spanId}' has a null endpoint segment.");
            }

            if (upper.segment != lower.segment)
            {
                return;
            }

            // Resolve both endpoints to a common reference (distance-from-A on
            // this segment) so we can detect and repair crossed/same-direction
            // patterns in one place. Legacy AlinasMapMod tolerated both
            // categories; FUSE used to throw, killing the whole apply phase.
            var length = upper.segment.GetLength();
            var upperFromA = DistanceFromSegmentA(upper);
            var lowerFromA = DistanceFromSegmentA(lower);
            var minPos = Mathf.Min(upperFromA, lowerFromA);
            var maxPos = Mathf.Max(upperFromA, lowerFromA);
            var spanLength = maxPos - minPos;

            if (spanLength <= SpanDistanceTolerance)
            {
                throw new InvalidOperationException(
                    $"Track span '{spanId}' has zero length on segment '{upper.segment.id}'. " +
                    $"upper=({DescribeLocation(upper)}) lower=({DescribeLocation(lower)}).");
            }

            var sameDirection = upper.EndIsA == lower.EndIsA;
            var startSide = upper.EndIsA ? upperFromA : lowerFromA;
            var endSide = upper.EndIsA ? lowerFromA : upperFromA;
            var crossed = !sameDirection && startSide > endSide + SpanDistanceTolerance;

            if (!sameDirection && !crossed)
            {
                return;
            }

            // Re-anchor: A side takes the smaller physical position, B side
            // takes the larger. Preserves both endpoints' physical positions
            // exactly while making the validator's startSide<=endSide invariant
            // hold by construction. Upper/lower roles are reassigned so the
            // larger-position endpoint is "upper" (matches NormalizeUpperLower's
            // expectation downstream).
            var newUpper = new Location(upper.segment, length - maxPos, TrackSegment.End.B);
            var newLower = new Location(upper.segment, minPos, TrackSegment.End.A);
            upper = newUpper;
            lower = newLower;

            var reason = crossed ? "crossed endpoints" : "same-direction endpoints";
            FuseLog.Info(
                $"FUSE auto-repaired track span '{spanId}' on segment '{upper.segment.id}': " +
                $"normalized {reason} to A({minPos:0.###}) <-> B({(length - maxPos):0.###}). " +
                "Legacy AlinasMapMod tolerated this pattern; FUSE re-anchored to keep the package loadable.");
        }

        private static float DistanceFromSegmentA(Location location)
        {
            var length = location.segment.GetLength();
            return location.EndIsA ? location.distance : length - location.distance;
        }

        private static void ValidateSpanRoute(string spanId, TrackSpan span)
        {
            if (span == null || !span.IsValid)
            {
                throw new InvalidOperationException($"Track span '{spanId}' has invalid endpoints.");
            }

            var points = span.GetPoints();
            if (points == null || points.Count < 2 || span.Length <= SpanDistanceTolerance)
            {
                var upper = span.upper.HasValue ? DescribeLocation(span.upper.Value) : "missing";
                var lower = span.lower.HasValue ? DescribeLocation(span.lower.Value) : "missing";
                throw new InvalidOperationException(
                    $"Track span '{spanId}' did not resolve to a valid route. " +
                    $"upper=({upper}) lower=({lower}). Check that upper/lower arrows face each other, " +
                    "distances are inside segment length, and endpoint segments are connected.");
            }
        }

        private static string DescribeLocation(Location location)
        {
            var segment = location.segment;
            if (segment == null)
            {
                return "segment='<null>'";
            }

            var end = location.EndIsA ? "A/Start" : "B/End";
            return $"segment='{segment.id}' end='{end}' distance={location.distance:0.###} segmentLength={segment.GetLength():0.###}";
        }

        private static void RegisterSpanWithGraph(Graph graph, TrackSpan span)
        {
            if (graph == null || span == null || string.IsNullOrWhiteSpace(span.id))
            {
                return;
            }

            try
            {
                var registered = graph.SpanForId(span.id);
                if (registered == span)
                {
                    return;
                }

                var spans = GraphSpansField?.GetValue(graph) as Dictionary<string, TrackSpan>;
                if (spans != null)
                {
                    spans[span.id] = span;
                    return;
                }

                if (GraphAddSpanMethod == null)
                {
                    if (!_warnedMissingGraphAddSpan)
                    {
                        _warnedMissingGraphAddSpan = true;
                        FuseLog.Warning(
                            "FUSE could not register newly applied track spans with the Railroader graph cache: " +
                            "private Graph.AddSpan method was not found. Spans remain in FUSE runtime index until the next graph rebuild.");
                    }

                    return;
                }

                GraphAddSpanMethod.Invoke(graph, new object[] { span });
            }
            catch (TargetInvocationException ex)
            {
                FuseLog.Warning($"FUSE could not register track span '{span.id}' with Graph cache: {ex.InnerException?.Message ?? ex.Message}");
            }
            catch (Exception ex)
            {
                FuseLog.Warning($"FUSE could not register track span '{span.id}' with Graph cache: {ex.Message}");
            }
        }

        private static TrackSpan FindSpanInScene(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            return UnityEngine.Object.FindObjectsOfType<TrackSpan>(true)
                .FirstOrDefault(span => span != null && string.Equals(span.id, id, StringComparison.OrdinalIgnoreCase));
        }

        private static void EnsureTrackSpanGraphChild(Graph graph, TrackSpan span)
        {
            if (graph == null || span == null || span.transform == null || graph.transform == null)
            {
                return;
            }

            try
            {
                if (span.transform.parent != graph.transform)
                {
                    span.transform.SetParent(graph.transform, true);
                }
            }
            catch (Exception ex)
            {
                FuseLog.Warning($"FUSE could not move track span '{span.id}' under Graph root: {ex.Message}");
            }
        }

        private static FuseSpan CloneSpanDefinition(FuseSpan definition)
        {
            if (definition == null)
            {
                return null;
            }

            return new FuseSpan
            {
                Upper = CloneTrackLocation(definition.Upper),
                Lower = CloneTrackLocation(definition.Lower),
                Normalize = definition.Normalize,
                GroupId = definition.GroupId
            };
        }

        private static FuseNode CloneNodeDefinition(FuseNode definition)
        {
            if (definition == null)
            {
                return null;
            }

            return new FuseNode
            {
                Position = definition.Position,
                Rotation = definition.Rotation,
                FlipSwitchStand = definition.FlipSwitchStand,
                GroupId = definition.GroupId,
                Tags = definition.Tags?.ToArray()
            };
        }

        private static FuseSegment CloneSegmentDefinition(FuseSegment definition)
        {
            if (definition == null)
            {
                return null;
            }

            return new FuseSegment
            {
                StartNodeId = definition.StartNodeId,
                EndNodeId = definition.EndNodeId,
                Style = definition.Style,
                TrackClass = definition.TrackClass,
                SpeedLimit = definition.SpeedLimit,
                Priority = definition.Priority,
                GroupId = definition.GroupId,
                Tags = definition.Tags?.ToArray()
            };
        }

        private static FuseTrackLocation CloneTrackLocation(FuseTrackLocation location)
        {
            if (location == null)
            {
                return null;
            }

            return new FuseTrackLocation
            {
                SegmentId = location.SegmentId,
                Normalized = location.Normalized,
                Distance = location.Distance,
                End = location.End,
                Offset = location.Offset
            };
        }

        private static FuseTrackLocation ToDefinition(Location location)
        {
            if (location.segment == null)
            {
                return null;
            }

            return new FuseTrackLocation
            {
                SegmentId = location.segment.id,
                End = location.end == TrackSegment.End.B ? "B" : "A",
                Distance = location.distance
            };
        }

        private static TrackSegment.End ParseLocationEnd(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return TrackSegment.End.A;
            }

            switch (value.Trim().ToUpperInvariant())
            {
                case "A":
                case "START":
                    return TrackSegment.End.A;
                case "B":
                case "END":
                    return TrackSegment.End.B;
                default:
                    throw new ArgumentException($"Unsupported track location end '{value}'. Expected A/B or Start/End.", nameof(value));
            }
        }

        private static Graph RequireGraph()
        {
            var graph = Graph.Shared;
            if (graph == null)
            {
                throw new InvalidOperationException("Railroader Graph.Shared is not available.");
            }

            return graph;
        }

        private static TrackNode RequireNode(string id)
        {
            var node = GetNode(id);
            if (node == null)
            {
                throw new InvalidOperationException($"Track node '{id}' was not found.");
            }

            return node;
        }

        private static TrackSegment RequireSegment(string id)
        {
            var segment = GetSegment(id);
            if (segment == null)
            {
                throw new InvalidOperationException($"Track segment '{id}' was not found.");
            }

            return segment;
        }

        private static TrackSpan RequireSpan(string id)
        {
            var span = GetSpan(id);
            if (span == null)
            {
                throw new InvalidOperationException($"Track span '{id}' was not found.");
            }

            return span;
        }

        private static Area RequireArea(string id)
        {
            var area = GetArea(id);
            if (area == null)
            {
                throw new InvalidOperationException($"Area '{id}' was not found.");
            }

            return area;
        }

        private static void ApplyAreaDefinition(Area area, FuseArea definition)
        {
            var displayName = string.IsNullOrWhiteSpace(definition.Name) ? area.identifier : definition.Name;
            area.name = displayName;
            area.gameObject.name = displayName;
            if (definition.Position.HasValue)
            {
                area.transform.localPosition = definition.Position.Value;
            }

            if (definition.Radius.HasValue)
            {
                area.radius = definition.Radius.Value;
            }

            if (definition.TagColor != null && definition.TagColor.Length >= 3)
            {
                area.tagColor = ParseAreaColor(definition.TagColor);
            }
        }

        private static void RememberAreaOrder(string id, int? order)
        {
            if (order.HasValue)
            {
                AreaOrders[id] = order.Value;
                return;
            }

            AreaOrders.Remove(id);
        }

        private static Color ParseAreaColor(float[] values)
        {
            return new Color(
                Mathf.Clamp01(values[0]),
                Mathf.Clamp01(values[1]),
                Mathf.Clamp01(values[2]),
                values.Length > 3 ? Mathf.Clamp01(values[3]) : 1f);
        }

        private static Transform GetAreaRoot()
        {
            if (OpsController.Shared != null)
            {
                return OpsController.Shared.transform;
            }

            if (_fallbackAreaRoot == null)
            {
                _fallbackAreaRoot = new GameObject("FUSE Areas").transform;
                UnityEngine.Object.DontDestroyOnLoad(_fallbackAreaRoot.gameObject);
            }

            return _fallbackAreaRoot;
        }

        private static string DescribeAreaParent(Transform parent)
        {
            if (parent == null)
            {
                return "<none>";
            }

            var ops = parent.GetComponent<OpsController>();
            return ops != null ? $"{parent.name} (OpsController)" : parent.name;
        }

        private static void RequireId(string id, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("ID is required.", parameterName);
            }
        }

        private static void RemoveRuntimeObject(Component component)
        {
            if (component == null)
            {
                return;
            }

            var gameObject = component.gameObject;
            PreserveChildTrackSpansBeforeDestroy(component, gameObject);
            gameObject.SetActive(false);
            UnityEngine.Object.Destroy(gameObject);
        }

        private static void PreserveChildTrackSpansBeforeDestroy(Component component, GameObject gameObject)
        {
            if (component is TrackSpan || gameObject == null)
            {
                return;
            }

            var graph = Graph.Shared;
            if (graph == null)
            {
                return;
            }

            foreach (var span in gameObject.GetComponentsInChildren<TrackSpan>(true))
            {
                if (span == null || string.IsNullOrWhiteSpace(span.id))
                {
                    continue;
                }

                try
                {
                    if (span.gameObject == gameObject)
                    {
                        var upper = span.upper;
                        var lower = span.lower;
                        if (!upper.HasValue || !lower.HasValue)
                        {
                            continue;
                        }

                        var clone = CreateGraphChild<TrackSpan>(graph, "Span-" + span.id);
                        clone.id = span.id;
                        clone.upper = upper;
                        clone.lower = lower;
                        FuseSpanRuntimeIndex.Instance.Set(clone.id, clone);
                        RegisterSpanWithGraph(graph, clone);
                    }
                    else
                    {
                        span.transform.SetParent(graph.transform, true);
                        FuseSpanRuntimeIndex.Instance.Set(span.id, span);
                        RegisterSpanWithGraph(graph, span);
                    }
                }
                catch (Exception ex)
                {
                    FuseLog.Warning($"FUSE could not preserve child track span '{span.id}' before destroying '{gameObject.name}': {ex.Message}");
                }
            }
        }

        private static TrackSegment.Style ParseSegmentStyle(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return TrackSegment.Style.Standard;
            }

            return (TrackSegment.Style)Enum.Parse(typeof(TrackSegment.Style), value, true);
        }

        private static TrackClass ParseTrackClass(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return TrackClass.Mainline;
            }

            if (string.Equals(value, "main", StringComparison.OrdinalIgnoreCase))
            {
                return TrackClass.Mainline;
            }

            return (TrackClass)Enum.Parse(typeof(TrackClass), value, true);
        }
    }
}
