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
            RailroaderTrackContract.SetNodeIsDiamond(node, definition.IsDiamond);
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
            RailroaderTrackContract.SetNodeIsDiamond(RequireNode(id), definition.IsDiamond);
            FuseApiPersistence.RecordDefinition(FuseDefinitionKind.TrackNode, id, definition);
        }

        public static void RemoveNode(string id)
        {
            var node = RequireNode(id);
            foreach (var segment in Graph.Shared.SegmentsConnectedTo(node).ToArray())
            {
                RemoveSegment(segment.id);
            }

            UnregisterNodeWithGraph(Graph.Shared, id);
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
            definition.IsDiamond = RailroaderTrackContract.GetNodeIsDiamond(node);
            return definition;
        }
    }
}
