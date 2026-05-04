using System;
using System.Collections.Generic;
using System.Linq;
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
                return (TrackSpan)cached;
            }

            var graph = Graph.Shared;
            return graph != null && !string.IsNullOrWhiteSpace(id) ? graph.SpanForId(id) : null;
        }

        public static IEnumerable<TrackSpan> GetAllSpans()
        {
            return UnityEngine.Object.FindObjectsOfType<TrackSpan>(true)
                .Where(span => span != null && !string.IsNullOrWhiteSpace(span.id));
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
                throw new InvalidOperationException($"Track span '{spanId}' has zero length on segment '{upper.segment.id}'.");
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
            FuseLog.Warning(
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
                throw new InvalidOperationException($"Track span '{spanId}' did not resolve to a valid route. Check that the endpoint arrows face each other and the segments are connected.");
            }
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
            gameObject.SetActive(false);
            UnityEngine.Object.Destroy(gameObject);
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
