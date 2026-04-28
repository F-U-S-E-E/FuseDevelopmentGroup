using System;
using System.Collections.Generic;
using System.Linq;
using Model.Ops;
using RAIL.Cache;
using RAIL.Data;
using RAIL.Data.Common;
using RAIL.Events;
using RAIL.Infrastructure;
using Track;
using UnityEngine;

namespace RAIL.API
{
    public static class TrackAPI
    {
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
            TrackNodeCache.Instance.Set(id, node);
            RailEvents.RaiseNodeAdded(node);
            RequestRebuild();
            return node;
        }

        public static TrackNode AddNode(string id, RailNode definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            return AddNode(id, definition.Position, definition.Rotation, definition.FlipSwitchStand, definition.GroupId);
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
            TrackNodeCache.Instance.Set(id, node);
            RailEvents.RaiseNodeUpdated(node);
            RequestRebuild();
        }

        public static void UpdateNode(string id, RailNode definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            UpdateNode(id, definition.Position, definition.Rotation, definition.FlipSwitchStand);
        }

        public static void RemoveNode(string id)
        {
            var node = RequireNode(id);
            foreach (var segment in Graph.Shared.SegmentsConnectedTo(node).ToArray())
            {
                RemoveSegment(segment.id);
            }

            RemoveRuntimeObject(node);
            TrackNodeCache.Instance.Remove(id);
            RailEvents.RaiseNodeRemoved(id);
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
            TrackSegmentCache.Instance.Set(id, segment);
            RailEvents.RaiseSegmentAdded(segment);
            RequestRebuild();
            return segment;
        }

        public static TrackSegment AddSegment(string id, RailSegment definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            return AddSegment(
                id,
                definition.StartNodeId,
                definition.EndNodeId,
                ParseSegmentStyle(definition.Style),
                definition.SpeedLimit,
                definition.GroupId,
                ParseTrackClass(definition.TrackClass),
                definition.Priority);
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
            TrackSegmentCache.Instance.Set(id, segment);
            RailEvents.RaiseSegmentUpdated(segment);
            RequestRebuild();
        }

        public static void UpdateSegment(string id, RailSegment definition)
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
        }

        public static void RemoveSegment(string id)
        {
            var segment = RequireSegment(id);
            RemoveRuntimeObject(segment);
            TrackSegmentCache.Instance.Remove(id);
            RailEvents.RaiseSegmentRemoved(id);
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

        public static TrackSpan AddSpan(string id, RailTrackLocation upper, RailTrackLocation lower, bool normalize = true)
        {
            RequireId(id, nameof(id));
            var graph = RequireGraph();
            if (graph.SpanForId(id) != null)
            {
                throw new InvalidOperationException($"Track span '{id}' already exists.");
            }

            var span = CreateGraphChild<TrackSpan>(graph, "Span-" + id);
            span.id = id;
            span.upper = MakeLocation(graph, upper);
            span.lower = MakeLocation(graph, lower);
            if (normalize)
            {
                span.NormalizeUpperLower();
            }

            TrackSpanCache.Instance.Set(id, span);
            RailEvents.RaiseSpanAdded(span);
            RequestRebuild();
            return span;
        }

        public static TrackSpan AddSpan(string id, RailSpan definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            return AddSpan(id, definition.Upper, definition.Lower, definition.Normalize);
        }

        public static void UpdateSpan(string id, RailTrackLocation upper, RailTrackLocation lower, bool normalize = true)
        {
            var span = RequireSpan(id);
            var graph = RequireGraph();
            span.upper = MakeLocation(graph, upper);
            span.lower = MakeLocation(graph, lower);
            if (normalize)
            {
                span.NormalizeUpperLower();
            }

            TrackSpanCache.Instance.Set(id, span);
            RailEvents.RaiseSpanUpdated(span);
            RequestRebuild();
        }

        public static void UpdateSpan(string id, RailSpan definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            UpdateSpan(id, definition.Upper, definition.Lower, definition.Normalize);
        }

        public static void RemoveSpan(string id)
        {
            var span = RequireSpan(id);
            RemoveRuntimeObject(span);
            TrackSpanCache.Instance.Remove(id);
            RailEvents.RaiseSpanRemoved(id);
            RequestRebuild();
        }

        public static TrackSpan GetSpan(string id)
        {
            if (TrackSpanCache.Instance.TryGetValue(id, out var cached))
            {
                return (TrackSpan)cached;
            }

            var graph = Graph.Shared;
            return graph != null && !string.IsNullOrWhiteSpace(id) ? graph.SpanForId(id) : null;
        }

        public static Area AddArea(string id, RailArea definition)
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
            AreaCache.Instance.Set(id, area);
            RailLog.Info($"RAIL created area '{id}' name='{displayName}' parent='{DescribeAreaParent(area.transform.parent)}' position={area.transform.localPosition} radius={area.radius}.");
            return area;
        }

        public static void UpdateArea(string id, RailArea definition)
        {
            var area = RequireArea(id);
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            ApplyAreaDefinition(area, definition);
            RememberAreaOrder(id, definition.Order);
            AreaCache.Instance.Set(id, area);
            RailLog.Info($"RAIL updated area '{id}' name='{area.name}' position={area.transform.localPosition} radius={area.radius}.");
        }

        public static Area GetArea(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            if (AreaCache.Instance.TryGetValue(id, out var cached))
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
                    AreaCache.Instance.Set(area.identifier, area);
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

            RailLog.Info($"RAIL applied area ordering for {orderedAreas.Length} area(s).");
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
            if (_batchDepth == 0)
            {
                return;
            }

            _batchDepth--;
            if (_batchDepth == 0 && _rebuildRequested)
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

            RailEvents.RaiseGraphRebuilt();
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

        private static Location MakeLocation(Graph graph, RailTrackLocation definition)
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

            var distance = definition.Distance ?? ((definition.Normalized ?? 0f) * segment.GetLength());
            distance += definition.Offset;
            return new Location(
                segment,
                Mathf.Clamp(distance, 0f, segment.GetLength()),
                ParseLocationEnd(definition.End));
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

        private static void ApplyAreaDefinition(Area area, RailArea definition)
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
                _fallbackAreaRoot = new GameObject("RAIL Areas").transform;
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
