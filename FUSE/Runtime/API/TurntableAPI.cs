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
        private static readonly FieldInfo NodesField = typeof(Turntable).GetField("nodes", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo BridgeGroupIdField = typeof(Turntable).GetField("bridgeGroupId", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo BridgeSegmentField = typeof(Turntable).GetField("_segment", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo CachedTurntableControllersField = typeof(Graph).GetField("_cachedTurntableControllers", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly ConstructorInfo GaugeConstructor = typeof(Gauge).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            new[] { typeof(float), typeof(float), typeof(float) },
            null);
        private static readonly Type TrackMeshBuilderType = ResolveTrackMeshBuilderType();
        private static readonly MethodInfo BuildStockRailMeshMethod = TrackMeshBuilderType?.GetMethod(
            "BuildStockRailMesh",
            BindingFlags.Static | BindingFlags.Public,
            null,
            new[] { typeof(LineCurve), typeof(Vector3), typeof(Gauge), typeof(Func<int, float>) },
            null);
        private const string CustomVisualRootName = "FUSE Custom Turntable Visuals";
        private const string BridgeTrackRootName = "FUSE Bridge Track";
        private static Material _bridgeRailMaterial;
        private static readonly string[] RuntimeCloneComponentTypeNames =
        {
            "KinematicCharacterController.PhysicsMover",
            "KinematicCharacterController.KinematicCharacterMotor",
            "Track.TurntableTransmitter",
            "Track.TurntableReceiver"
        };

        public static Turntable GetTurntable(string id)
        {
            if (FuseTurntableRuntimeIndex.Instance.TryGetValue(id, out var cached))
            {
                return (Turntable)cached;
            }

            return !string.IsNullOrWhiteSpace(id)
                ? UnityEngine.Object.FindObjectsOfType<Turntable>().FirstOrDefault(turntable =>
                    string.Equals(turntable.id, id, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(turntable.id, ToRuntimeTurntableId(id), StringComparison.OrdinalIgnoreCase))
                : null;
        }

        public static IEnumerable<Turntable> GetAllTurntables()
        {
            return UnityEngine.Object.FindObjectsOfType<Turntable>();
        }

        public static FuseTurntable GetTurntableDefinition(string id)
        {
            return GetDefinition(GetTurntable(id));
        }

        public static FuseTurntable GetDefinition(Turntable turntable)
        {
            if (turntable == null)
            {
                return null;
            }

            var id = GetDefinitionTurntableId(turntable);
            FuseRuntimeDefinitionCache.TryGet(FuseDefinitionKind.Turntable, id, out FuseTurntable definition);
            definition = definition ?? new FuseTurntable();
            definition.Position = turntable.transform.localPosition;
            definition.Rotation = turntable.transform.localEulerAngles;
            definition.Radius = turntable.radius;
            definition.Subdivisions = turntable.subdivisions;
            return definition;
        }

        public static Turntable AddTurntable(string id, FuseTurntable definition)
        {
            RequireId(id, nameof(id));
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (GetTurntable(id) != null)
            {
                throw new InvalidOperationException($"Turntable '{id}' already exists.");
            }

            var graph = RequireGraph();
            var root = new GameObject("Turntable-" + id);
            root.transform.SetParent(graph.transform, false);
            root.transform.localPosition = definition.Position;
            root.transform.localRotation = Quaternion.Euler(definition.Rotation);
            root.SetActive(false);

            var turntable = root.AddComponent<Turntable>();
            turntable.id = ToRuntimeTurntableId(id);
            turntable.radius = definition.Radius;
            turntable.subdivisions = definition.Subdivisions;
            ClearBridgeGroup(turntable);

            List<TrackNode> pitNodes;
            using (FuseApiPersistence.SuppressRecording())
            {
                pitNodes = CreateOrUpdatePitNodes(turntable, definition);
                CreateOrUpdateRoundhouseTracks(turntable, definition);
            }

            NodesField?.SetValue(turntable, pitNodes);

            ConfigureTurntableVisuals(root, turntable, definition);
            ConfigureRoundhouse(root, definition);
            ClearBridgeGroup(turntable);
            turntable.UpdateSegmentIndex(false);
            ClearBridgeGroup(turntable);

            root.SetActive(true);
            RefreshTurntableVisuals(root, definition);
            MapAPI.RefreshAttachedMapMasks(root, $"turntable '{id}' add");
            FusePrefabSanitizer.SanitizeTurntable(root, id, turntable, RequiresVanillaController(definition)).Log($"FUSE turntable '{id}'");
            FuseTurntableRuntimeIndex.Instance.Set(id, turntable);
            FuseApiPersistence.RecordDefinition(FuseDefinitionKind.Turntable, id, definition);
            FusePrefabSanitizer.ValidateTurntablePostBind(root, id, turntable, RequiresVanillaController(definition)).Log($"FUSE turntable '{id}' post-bind");
            FuseNodeRuntimeIndex.Instance.Rebuild();
            FuseSegmentRuntimeIndex.Instance.Rebuild();
            InvalidateTurntableControllerCache();
            if (!TrackAPI.IsBatching)
            {
                TrackAPI.RebuildGraph();
            }

            return turntable;
        }

        public static void UpdateTurntable(string id, FuseTurntable definition)
        {
            var turntable = RequireTurntable(id);
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            FuseRuntimeDefinitionCache.TryGet(FuseDefinitionKind.Turntable, id, out FuseTurntable previousDefinition);

            turntable.transform.localPosition = definition.Position;
            turntable.transform.localRotation = Quaternion.Euler(definition.Rotation);
            if (definition.Radius > 0f)
            {
                turntable.radius = definition.Radius;
            }

            if (definition.Subdivisions > 0)
            {
                turntable.subdivisions = definition.Subdivisions;
            }

            List<TrackNode> pitNodes;
            using (FuseApiPersistence.SuppressRecording())
            {
                RemoveStaleRoundhouseTracks(turntable, previousDefinition, definition);
                pitNodes = CreateOrUpdatePitNodes(turntable, definition);
                CreateOrUpdateRoundhouseTracks(turntable, definition);
            }

            NodesField?.SetValue(turntable, pitNodes);
            ConfigureTurntableVisuals(turntable.gameObject, turntable, definition);
            ConfigureRoundhouse(turntable.gameObject, definition);
            ClearBridgeGroup(turntable);
            turntable.UpdateSegmentIndex(false);
            ClearBridgeGroup(turntable);
            RefreshTurntableVisuals(turntable.gameObject, definition);
            MapAPI.RefreshAttachedMapMasks(turntable.gameObject, $"turntable '{id}' update");
            FusePrefabSanitizer.SanitizeTurntable(turntable.gameObject, id, turntable, RequiresVanillaController(definition)).Log($"FUSE turntable '{id}'");

            FuseTurntableRuntimeIndex.Instance.Set(id, turntable);
            FuseApiPersistence.RecordDefinition(FuseDefinitionKind.Turntable, id, definition);
            FusePrefabSanitizer.ValidateTurntablePostBind(turntable.gameObject, id, turntable, RequiresVanillaController(definition)).Log($"FUSE turntable '{id}' post-bind");
            FuseNodeRuntimeIndex.Instance.Rebuild();
            FuseSegmentRuntimeIndex.Instance.Rebuild();
            InvalidateTurntableControllerCache();
            if (!TrackAPI.IsBatching)
            {
                TrackAPI.RebuildGraph();
            }
        }

        public static void SetAngle(string id, float angle)
        {
            RequireTurntable(id).SetAngle(angle);
        }

        public static void SetStopIndex(string id, int? stopIndex)
        {
            RequireTurntable(id).SetStopIndex(stopIndex);
        }

        public static void RemoveTurntable(string id)
        {
            var turntable = RequireTurntable(id);
            FuseRuntimeDefinitionCache.TryGet(FuseDefinitionKind.Turntable, id, out FuseTurntable previousDefinition);
            TrackAPI.BeginBatch();
            try
            {
                RemoveStaleRoundhouseTracks(turntable, previousDefinition, null);
                foreach (var segment in TrackAPI.GetAllSegments().Where(segment => segment.turntable == turntable).ToArray())
                {
                    TrackAPI.RemoveSegment(segment.id);
                }

                foreach (var node in TrackAPI.GetAllNodes().Where(node => node.turntable == turntable).ToArray())
                {
                    if (TrackAPI.GetNode(node.id) != null)
                    {
                        TrackAPI.RemoveNode(node.id);
                    }
                }

                turntable.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(turntable.gameObject);
                FuseTurntableRuntimeIndex.Instance.Remove(id);
                FuseRuntimeDefinitionCache.Remove(FuseDefinitionKind.Turntable, id);
            }
            finally
            {
                TrackAPI.EndBatch();
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

        private static void InvalidateTurntableControllerCache()
        {
            var graph = Graph.Shared;
            if (graph == null)
            {
                return;
            }

            CachedTurntableControllersField?.SetValue(graph, null);
        }

        private static Turntable RequireTurntable(string id)
        {
            var turntable = GetTurntable(id);
            if (turntable == null)
            {
                throw new InvalidOperationException($"Turntable '{id}' was not found.");
            }

            return turntable;
        }

        private static void RequireId(string id, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("ID is required.", parameterName);
            }
        }
    }
}
