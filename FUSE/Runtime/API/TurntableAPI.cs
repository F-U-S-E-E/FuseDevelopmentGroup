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
    public static class TurntableAPI
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

        private static void ConfigureTurntableVisuals(GameObject root, Turntable turntable, FuseTurntable definition)
        {
            if (UsesCustomVisuals(definition))
            {
                RemoveVanillaTurntableTemplate(root);
                ConfigureCustomVisuals(root, turntable, definition);
                return;
            }

            RemoveCustomVisuals(root);
            RemoveCustomControllers(root, null);
            var binding = root.GetComponent<FuseTurntableVisualBinding>();
            if (binding != null)
            {
                UnityEngine.Object.Destroy(binding);
            }

            AttachTurntableTemplate(root, turntable);
        }

        private static bool UsesCustomVisuals(FuseTurntable definition)
        {
            var visuals = definition?.Visuals;
            return visuals != null &&
                   (!string.IsNullOrWhiteSpace(visuals.PitAssetIdentifier) ||
                    !string.IsNullOrWhiteSpace(visuals.BridgeAssetIdentifier) ||
                    !string.IsNullOrWhiteSpace(visuals.ControllerType));
        }

        private static bool RequiresVanillaController(FuseTurntable definition)
        {
            return !UsesCustomVisuals(definition);
        }

        private static void ConfigureCustomVisuals(GameObject root, Turntable turntable, FuseTurntable definition)
        {
            RemoveCustomVisuals(root);

            var visuals = definition.Visuals;
            var visualRoot = new GameObject(CustomVisualRootName);
            visualRoot.transform.SetParent(root.transform, false);
            visualRoot.transform.localPosition = Vector3.zero;
            visualRoot.transform.localEulerAngles = Vector3.zero;
            visualRoot.transform.localScale = Vector3.one;

            CreateSceneryAssetChild(
                visualRoot.transform,
                "Pit",
                visuals.PitAssetIdentifier,
                visuals.PitPosition,
                visuals.PitRotation,
                visuals.PitScale);

            var bridgeRoot = new GameObject("Bridge").transform;
            bridgeRoot.SetParent(visualRoot.transform, false);
            bridgeRoot.localPosition = Vector3.zero;
            bridgeRoot.localEulerAngles = Vector3.zero;
            bridgeRoot.localScale = Vector3.one;

            CreateSceneryAssetChild(
                bridgeRoot,
                "Bridge Asset",
                visuals.BridgeAssetIdentifier,
                visuals.BridgePosition,
                visuals.BridgeRotation,
                visuals.BridgeScale);
            CreateBridgeTrackVisual(bridgeRoot, turntable, definition);

            var binding = root.GetComponent<FuseTurntableVisualBinding>() ?? root.AddComponent<FuseTurntableVisualBinding>();
            binding.Turntable = turntable;
            binding.BridgeRoot = bridgeRoot;
            binding.Sync();

            ConfigureExternalController(root, turntable, bridgeRoot, definition);
            visualRoot.SetActive(true);
        }

        private static void CreateSceneryAssetChild(
            Transform parent,
            string name,
            string assetIdentifier,
            Vector3 localPosition,
            Vector3 localRotation,
            Vector3 localScale)
        {
            if (string.IsNullOrWhiteSpace(assetIdentifier))
            {
                return;
            }

            var sceneryDefinition = new FuseScenery
            {
                AssetIdentifier = assetIdentifier,
                Model = assetIdentifier
            };

            if (!SceneryAPI.TryResolveAssetIdentifier(name, sceneryDefinition, out var resolvedAssetIdentifier))
            {
                FuseLog.Warning(
                    $"FUSE skipped turntable visual '{name}' because scenery asset '{assetIdentifier}' " +
                    "could not be resolved.");
                return;
            }

            var child = new GameObject(name);
            child.SetActive(false);
            child.transform.SetParent(parent, false);
            child.transform.localPosition = localPosition;
            child.transform.localEulerAngles = localRotation;
            child.transform.localScale = localScale == default ? Vector3.one : localScale;

            var scenery = child.AddComponent<SceneryAssetInstance>();
            scenery.identifier = resolvedAssetIdentifier;
            scenery.OnDidLoadModels += loadedRoot =>
            {
                if (loadedRoot != null)
                {
                    EnableRenderers(loadedRoot.gameObject);
                }

                MapAPI.RefreshAttachedMapMasks(child, $"turntable visual '{name}' load");
            };
            child.SetActive(true);
            FuseLog.Info($"FUSE turntable visual '{name}' created from scenery asset '{resolvedAssetIdentifier}'.");
        }

        private static void CreateBridgeTrackVisual(Transform bridgeRoot, Turntable turntable, FuseTurntable definition)
        {
            var visuals = definition?.Visuals;
            if (bridgeRoot == null || turntable == null || visuals == null || !visuals.BridgeTrackEnabled)
            {
                return;
            }

            var existing = bridgeRoot.Find(BridgeTrackRootName);
            if (existing != null)
            {
                UnityEngine.Object.Destroy(existing.gameObject);
            }

            var trackRoot = new GameObject(BridgeTrackRootName);
            trackRoot.transform.SetParent(bridgeRoot, false);
            trackRoot.transform.localPosition = Vector3.zero;
            trackRoot.transform.localRotation = Quaternion.identity;
            trackRoot.transform.localScale = Vector3.one;

            var gaugeInside = visuals.BridgeTrackGauge > 0f ? visuals.BridgeTrackGauge : Gauge.Standard.Inside;
            var length = visuals.BridgeTrackLength > 0f
                ? visuals.BridgeTrackLength
                : Mathf.Max(turntable.radius * 2f, 1f);
            var y = visuals.BridgeTrackYOffset;

            try
            {
                var gauge = CreateGauge(gaugeInside);
                var half = length * 0.5f;
                var handle = Mathf.Max(length / 3f, 0.1f);
                var curve = new BezierCurve(
                    new Vector3(0f, y, -half),
                    new Vector3(0f, y, -half + handle),
                    new Vector3(0f, y, half - handle),
                    new Vector3(0f, y, half),
                    Vector3.up,
                    Vector3.up);
                var rails = SwitchGeometry.MakeTrackLineSegments(curve, gauge);
                CreateBridgeRailObject(trackRoot.transform, "L", rails.left, gauge);
                CreateBridgeRailObject(trackRoot.transform, "R", rails.right, gauge);
                trackRoot.SetActive(true);
                FuseLog.Info(
                    $"FUSE turntable bridge track visual created length={length:F3}m gauge={gaugeInside:F3}m y={y:F3}.");
            }
            catch (Exception ex)
            {
                FuseLog.Warning($"FUSE bridge rail mesh creation failed, using simple rail boxes: {ex.Message}");
                CreateFallbackBridgeRails(trackRoot.transform, gaugeInside, length, y);
                trackRoot.SetActive(true);
            }
        }

        private static void CreateBridgeRailObject(Transform parent, string name, LineCurve curve, Gauge gauge)
        {
            if (BuildStockRailMeshMethod == null)
            {
                throw new MissingMethodException("TrackMeshBuilder.BuildStockRailMesh was not found.");
            }

            var mesh = (Mesh)BuildStockRailMeshMethod.Invoke(
                null,
                new object[] { curve, Vector3.zero, gauge, new Func<int, float>(_ => 1f) });
            mesh.name = "FUSE Turntable Bridge Rail " + name;

            var rail = new GameObject(name);
            rail.transform.SetParent(parent, false);
            rail.transform.localPosition = Vector3.zero;
            rail.transform.localRotation = Quaternion.identity;
            rail.transform.localScale = Vector3.one;
            rail.AddComponent<MeshFilter>().sharedMesh = mesh;
            ConfigureBridgeRailRenderer(rail.AddComponent<MeshRenderer>());
            rail.SetActive(true);
        }

        private static void CreateFallbackBridgeRails(Transform parent, float gaugeInside, float length, float y)
        {
            var xOffset = gaugeInside * 0.5f;
            CreateFallbackBridgeRail(parent, "L", -xOffset, length, y);
            CreateFallbackBridgeRail(parent, "R", xOffset, length, y);
        }

        private static void CreateFallbackBridgeRail(Transform parent, string name, float x, float length, float y)
        {
            var rail = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rail.name = name;
            rail.transform.SetParent(parent, false);
            rail.transform.localPosition = new Vector3(x, y - 0.06f, 0f);
            rail.transform.localRotation = Quaternion.identity;
            rail.transform.localScale = new Vector3(0.08f, 0.12f, length);
            var collider = rail.GetComponent("BoxCollider");
            if (collider != null)
            {
                UnityEngine.Object.Destroy(collider);
            }

            var renderer = rail.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                ConfigureBridgeRailRenderer(renderer);
            }
        }

        private static void ConfigureBridgeRailRenderer(MeshRenderer renderer)
        {
            if (renderer == null)
            {
                return;
            }

            var material = GetBridgeRailMaterial();
            if (material != null)
            {
                renderer.material = material;
            }

            renderer.enabled = true;
            renderer.forceRenderingOff = false;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            renderer.receiveShadows = true;
        }

        private static Gauge CreateGauge(float inside)
        {
            if (GaugeConstructor == null || inside <= 0f)
            {
                return Gauge.Standard;
            }

            return (Gauge)GaugeConstructor.Invoke(
                new object[] { inside, Gauge.Standard.HeadWidth, Gauge.Standard.RailHeight });
        }

        private static Material GetBridgeRailMaterial()
        {
            if (_bridgeRailMaterial != null)
            {
                return _bridgeRailMaterial;
            }

            var trackMaterial = TrackObjectManager.Instance != null &&
                                TrackObjectManager.Instance.profile != null
                ? TrackObjectManager.Instance.profile.trackMaterial
                : null;
            if (trackMaterial != null)
            {
                _bridgeRailMaterial = trackMaterial;
                return _bridgeRailMaterial;
            }

            foreach (var renderer in UnityEngine.Object.FindObjectsOfType<MeshRenderer>())
            {
                if (renderer == null ||
                    renderer.sharedMaterial == null ||
                    renderer.gameObject == null ||
                    renderer.gameObject.tag != TrackObjectBuilder.TagGenerated)
                {
                    continue;
                }

                var name = renderer.gameObject.name;
                if (name == "L" || name == "R" || name == "StockL" || name == "StockR")
                {
                    _bridgeRailMaterial = renderer.sharedMaterial;
                    return _bridgeRailMaterial;
                }
            }

            var shader = Shader.Find("Unlit/Color") ??
                         Shader.Find("Standard") ??
                         Shader.Find("Diffuse") ??
                         Shader.Find("Legacy Shaders/Diffuse");
            if (shader == null)
            {
                return null;
            }

            _bridgeRailMaterial = new Material(shader)
            {
                color = new Color(0.17f, 0.15f, 0.13f, 1f)
            };
            return _bridgeRailMaterial;
        }

        private static Type ResolveTrackMeshBuilderType()
        {
            var direct = Type.GetType("TrackMeshBuilder, Assembly-CSharp", false, false);
            if (direct != null)
            {
                return direct;
            }

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type;
                try
                {
                    type = assembly.GetType("TrackMeshBuilder", false, false);
                }
                catch
                {
                    continue;
                }

                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        private static string NormalizeSceneryAssetIdentifier(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            var marker = value.IndexOf("://", StringComparison.Ordinal);
            return marker < 0 ? value : value.Substring(marker + 3);
        }

        private static void ConfigureExternalController(
            GameObject root,
            Turntable turntable,
            Transform bridgeRoot,
            FuseTurntable definition)
        {
            var controllerTypeName = definition?.Visuals?.ControllerType;
            if (string.IsNullOrWhiteSpace(controllerTypeName))
            {
                RemoveCustomControllers(root, null);
                return;
            }

            var controllerType = ResolveControllerType(controllerTypeName);
            if (controllerType == null)
            {
                FuseLog.Warning($"FUSE turntable '{GetDefinitionTurntableId(turntable)}' could not resolve controller type '{controllerTypeName}'.");
                RemoveCustomControllers(root, null);
                return;
            }

            if (!typeof(MonoBehaviour).IsAssignableFrom(controllerType))
            {
                FuseLog.Warning($"FUSE turntable controller type '{controllerTypeName}' is not a Unity MonoBehaviour.");
                RemoveCustomControllers(root, null);
                return;
            }

            RemoveCustomControllers(root, controllerType);
            var behaviour = root.GetComponent(controllerType) as MonoBehaviour;
            if (behaviour == null)
            {
                behaviour = root.AddComponent(controllerType) as MonoBehaviour;
            }

            if (behaviour is IFuseTurntableController typedController)
            {
                typedController.Configure(turntable, bridgeRoot, definition);
                return;
            }

            var configure = controllerType.GetMethod(
                "Configure",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(Turntable), typeof(Transform), typeof(FuseTurntable) },
                null);
            if (configure != null)
            {
                configure.Invoke(behaviour, new object[] { turntable, bridgeRoot, definition });
                return;
            }

            FuseLog.Warning(
                $"FUSE turntable controller '{controllerType.FullName}' does not implement IFuseTurntableController " +
                "and does not expose Configure(Turntable, Transform, FuseTurntable).");
        }

        private static Type ResolveControllerType(string typeName)
        {
            var direct = Type.GetType(typeName, false, true);
            if (direct != null)
            {
                return direct;
            }

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type;
                try
                {
                    type = assembly.GetType(typeName, false, true);
                }
                catch
                {
                    continue;
                }

                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        private static void RemoveCustomVisuals(GameObject root)
        {
            var existing = root.transform.Find(CustomVisualRootName);
            if (existing == null)
            {
                return;
            }

            existing.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(existing.gameObject);
        }

        private static void RemoveVanillaTurntableTemplate(GameObject root)
        {
            foreach (var controller in root.GetComponentsInChildren<TurntableController>(true))
            {
                if (controller == null)
                {
                    continue;
                }

                var destroyTarget = controller.transform;
                while (destroyTarget.parent != null && destroyTarget.parent != root.transform)
                {
                    destroyTarget = destroyTarget.parent;
                }

                if (destroyTarget == root.transform)
                {
                    UnityEngine.Object.Destroy(controller);
                    continue;
                }

                destroyTarget.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(destroyTarget.gameObject);
            }
        }

        private static void RemoveCustomControllers(GameObject root, Type keepType)
        {
            foreach (var behaviour in root.GetComponents<MonoBehaviour>())
            {
                if (behaviour == null || behaviour is FuseTurntableVisualBinding)
                {
                    continue;
                }

                if (!(behaviour is IFuseTurntableController))
                {
                    continue;
                }

                if (keepType != null && behaviour.GetType() == keepType)
                {
                    continue;
                }

                UnityEngine.Object.Destroy(behaviour);
            }
        }

        private static void AttachTurntableTemplate(GameObject root, Turntable turntable)
        {
            var template = ResolveTurntableTemplateObject();
            if (template == null)
            {
                throw new InvalidOperationException("A turntable template could not be found in the active scene.");
            }

            var existing = root.GetComponentInChildren<TurntableController>(true);
            if (existing != null)
            {
                ApplyTemplateLocalTransform(existing.gameObject, template);
                ActivatePrimaryVisualRenderers(existing.gameObject);
                existing.enabled = true;
                existing.turntable = turntable;
                existing.gameObject.SetActive(true);
                FuseLog.Info($"FUSE turntable '{turntable.id}' refreshed visual template at {existing.transform.position}; {DescribeRendererState(existing.gameObject)}.");
                return;
            }

            var instance = UnityEngine.Object.Instantiate(template, root.transform, false);
            instance.name = "30m Turntable(Clone)";
            ApplyTemplateLocalTransform(instance, template);
            StripRuntimeCloneComponents(instance);

            var global = instance.GetComponent<GlobalKeyValueObject>();
            if (global != null)
            {
                global.globalObjectId = turntable.id;
            }

            var controller = instance.GetComponent<TurntableController>();
            if (controller != null)
            {
                controller.enabled = true;
                controller.turntable = turntable;
            }

            ActivatePrimaryVisualRenderers(instance);
            instance.SetActive(true);
            FuseLog.Info($"FUSE turntable '{turntable.id}' cloned visual template '{template.name}' at {instance.transform.position}; {DescribeRendererState(instance)}.");
        }

        private static void RefreshTurntableTemplateVisuals(GameObject root)
        {
            var controller = root != null ? root.GetComponentInChildren<TurntableController>(true) : null;
            if (controller == null)
            {
                return;
            }

            ActivatePrimaryVisualRenderers(controller.gameObject);
        }

        private static void RefreshTurntableVisuals(GameObject root, FuseTurntable definition)
        {
            if (UsesCustomVisuals(definition))
            {
                var binding = root != null ? root.GetComponent<FuseTurntableVisualBinding>() : null;
                binding?.Sync();
                RefreshCustomSceneryVisuals(root);
                return;
            }

            RefreshTurntableTemplateVisuals(root);
        }

        private static void RefreshCustomSceneryVisuals(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            foreach (var scenery in root.GetComponentsInChildren<SceneryAssetInstance>(true))
            {
                if (scenery == null || !scenery.gameObject.activeInHierarchy)
                {
                    continue;
                }

                try
                {
                    scenery.RequestUpdateCullingPosition();
                }
                catch (Exception ex)
                {
                    FuseLog.Warning(
                        $"FUSE turntable visual '{scenery.name}' could not refresh culling position: {ex.Message}");
                }

                try
                {
                    scenery.CullingSphereStateChanged(true, 0);
                    MapAPI.RefreshAttachedMapMasks(
                        scenery.gameObject,
                        $"turntable visual '{scenery.name}' refresh");
                }
                catch (Exception ex)
                {
                    FuseLog.Warning(
                        $"FUSE turntable visual '{scenery.name}' could not force initial scenery load: {ex.Message}");
                }
            }
        }

        private static void ApplyTemplateLocalTransform(GameObject instance, GameObject template)
        {
            if (instance == null || template == null)
            {
                return;
            }

            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = template.transform.localRotation;
            instance.transform.localScale = template.transform.localScale;
        }

        private static void ClearBridgeGroup(Turntable turntable)
        {
            if (turntable == null)
            {
                return;
            }

            BridgeGroupIdField?.SetValue(turntable, string.Empty);
            var bridgeSegment = BridgeSegmentField?.GetValue(turntable) as TrackSegment;
            if (bridgeSegment == null)
            {
                return;
            }

            bridgeSegment.groupId = string.Empty;
            bridgeSegment.GroupEnabled = true;
            bridgeSegment.Available = true;
            bridgeSegment.InvalidateCurve();
        }

        private static void ActivatePrimaryVisualRenderers(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            var lodControlledRenderers = new HashSet<Renderer>();
            var lod0Renderers = new HashSet<Renderer>();
            var lodGroups = root.GetComponentsInChildren<LODGroup>(true);
            for (var groupIndex = 0; groupIndex < lodGroups.Length; groupIndex++)
            {
                var lodGroup = lodGroups[groupIndex];
                if (lodGroup == null)
                {
                    continue;
                }

                lodGroup.enabled = true;
                lodGroup.ForceLOD(0);

                var lods = lodGroup.GetLODs();
                for (var lodIndex = 0; lodIndex < lods.Length; lodIndex++)
                {
                    var lodRenderers = lods[lodIndex].renderers;
                    for (var rendererIndex = 0; rendererIndex < lodRenderers.Length; rendererIndex++)
                    {
                        var renderer = lodRenderers[rendererIndex];
                        if (renderer == null)
                        {
                            continue;
                        }

                        lodControlledRenderers.Add(renderer);
                        if (lodIndex == 0)
                        {
                            lod0Renderers.Add(renderer);
                        }
                    }
                }
            }

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (var index = 0; index < renderers.Length; index++)
            {
                var renderer = renderers[index];
                if (renderer == null)
                {
                    continue;
                }

                var isColliderRenderer = IsTurntableColliderRenderer(root, renderer);
                var keepVisible = !isColliderRenderer &&
                    (!lodControlledRenderers.Contains(renderer) || lod0Renderers.Contains(renderer));
                renderer.enabled = keepVisible;
                renderer.forceRenderingOff = !keepVisible;
            }

            for (var groupIndex = 0; groupIndex < lodGroups.Length; groupIndex++)
            {
                var lodGroup = lodGroups[groupIndex];
                if (lodGroup == null)
                {
                    continue;
                }

                lodGroup.enabled = true;
                lodGroup.ForceLOD(0);
                lodGroup.RecalculateBounds();
            }
        }

        private static bool IsTurntableColliderRenderer(GameObject root, Renderer renderer)
        {
            if (renderer == null)
            {
                return false;
            }

            if (renderer.name.IndexOf("Collider", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return GetTransformPath(root != null ? root.transform : null, renderer.transform)
                .IndexOf("Collider", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string DescribeRendererState(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            var enabledCount = 0;
            var activeCount = 0;
            var hasBounds = false;
            var bounds = default(Bounds);

            foreach (var renderer in renderers)
            {
                if (renderer.enabled)
                {
                    enabledCount++;
                }

                if (renderer.gameObject.activeInHierarchy)
                {
                    activeCount++;
                }

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            if (!hasBounds)
            {
                return $"renderers={renderers.Length}, enabled={enabledCount}, active={activeCount}, rootActive={root.activeInHierarchy}";
            }

            return $"renderers={renderers.Length}, enabled={enabledCount}, active={activeCount}, rootActive={root.activeInHierarchy}, boundsCenter={bounds.center}, boundsSize={bounds.size}";
        }

        private static string GetTransformPath(Transform root, Transform current)
        {
            if (current == null)
            {
                return string.Empty;
            }

            var names = new Stack<string>();
            var cursor = current;
            while (cursor != null)
            {
                names.Push(cursor.name);
                if (cursor == root)
                {
                    break;
                }

                cursor = cursor.parent;
            }

            return string.Join("/", names.ToArray());
        }

        private static GameObject ResolveTurntableTemplateObject()
        {
            var namedTemplate = GameObject.Find("30m Turntable");
            if (namedTemplate != null)
            {
                return namedTemplate;
            }

            var exactTemplate = UnityEngine.Object.FindObjectsOfType<TurntableController>(true)
                .FirstOrDefault(controller => controller != null && string.Equals(controller.gameObject.name, "30m Turntable", StringComparison.OrdinalIgnoreCase));
            if (exactTemplate != null)
            {
                return exactTemplate.gameObject;
            }

            return UnityEngine.Object.FindObjectsOfType<TurntableController>(true)
                .Select(controller => controller != null ? controller.gameObject : null)
                .FirstOrDefault(gameObject =>
                    gameObject != null &&
                    !gameObject.name.EndsWith("(Clone)", StringComparison.OrdinalIgnoreCase) &&
                    gameObject.name.IndexOf("Turntable", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static void StripRuntimeCloneComponents(GameObject root)
        {
            var components = root.GetComponentsInChildren<Component>(true);
            for (var index = 0; index < components.Length; index++)
            {
                var component = components[index];
                if (component == null || component is Transform)
                {
                    continue;
                }

                var typeName = component.GetType().FullName;
                if (!RuntimeCloneComponentTypeNames.Contains(typeName, StringComparer.Ordinal))
                {
                    continue;
                }

                UnityEngine.Object.DestroyImmediate(component);
            }
        }

        private static void ConfigureRoundhouse(GameObject root, FuseTurntable definition)
        {
            var existing = root.transform.Find("Roundhouse");
            if (existing != null)
            {
                UnityEngine.Object.Destroy(existing.gameObject);
            }

            var roundhouse = definition.Roundhouse;
            if (roundhouse == null || roundhouse.Stalls <= 0)
            {
                return;
            }

            var roundhouseRoot = new GameObject("Roundhouse");
            roundhouseRoot.transform.SetParent(root.transform, false);
            roundhouseRoot.transform.localPosition = new Vector3(0f, -0.48f, 0f);
            roundhouseRoot.transform.localEulerAngles = Vector3.zero;
            roundhouseRoot.transform.localScale = Vector3.one;

            if (roundhouseRoot.GetComponent<KeyValueObject>() == null)
            {
                roundhouseRoot.AddComponent<KeyValueObject>();
            }

            var global = roundhouseRoot.GetComponent<GlobalKeyValueObject>() ?? roundhouseRoot.AddComponent<GlobalKeyValueObject>();
            global.globalObjectId = GetDefinitionTurntableId(root) + ".roundhouse";

            var angleStep = 360f / Mathf.Max(definition.Subdivisions, 1);
            var startPrefab = FusePrefabResolver.Resolve(roundhouse.StartPrefab ?? "vanilla://roundhouseStart");
            var endPrefab = FusePrefabResolver.Resolve(roundhouse.EndPrefab ?? "vanilla://roundhouseEnd");
            var stallPrefab = FusePrefabResolver.Resolve(roundhouse.StallPrefab ?? "vanilla://roundhouseStall");

            if (roundhouse.Stalls < definition.Subdivisions)
            {
                var start = UnityEngine.Object.Instantiate(startPrefab, roundhouseRoot.transform);
                ApplyRoundhousePartTransform(start, angleStep * Vector3.up);
                PatchRoundhouseDoors(start, "stall-doors.0");

                var end = UnityEngine.Object.Instantiate(endPrefab, roundhouseRoot.transform);
                ApplyRoundhousePartTransform(end, angleStep * roundhouse.Stalls * Vector3.up);
                PatchRoundhouseDoors(end, "stall-doors." + (roundhouse.Stalls - 1));
            }

            var startIndex = roundhouse.Stalls < definition.Subdivisions ? 1 : 0;
            var endIndex = roundhouse.Stalls < definition.Subdivisions ? roundhouse.Stalls - 1 : roundhouse.Stalls;
            for (var index = startIndex; index < endIndex; index++)
            {
                var stall = UnityEngine.Object.Instantiate(stallPrefab, roundhouseRoot.transform);
                ApplyRoundhousePartTransform(stall, (index + 1) * angleStep * Vector3.up);
                PatchRoundhouseDoors(stall, "stall-doors." + index);
            }

            EnableRenderers(roundhouseRoot);
            roundhouseRoot.SetActive(true);
        }

        private static void ApplyRoundhousePartTransform(GameObject part, Vector3 localEulerAngles)
        {
            if (part == null)
            {
                return;
            }

            part.transform.localPosition = Vector3.zero;
            part.transform.localEulerAngles = localEulerAngles;
            part.transform.localScale = Vector3.one;
            part.SetActive(true);
        }

        private static void PatchRoundhouseDoors(GameObject instance, string key)
        {
            var toggle = instance.GetComponentInChildren<KeyValuePickableToggle>(true);
            var animator = instance.GetComponentInChildren<KeyValueBoolAnimator>(true);
            if (toggle != null)
            {
                toggle.key = key;
            }

            if (animator != null)
            {
                animator.key = key;
            }
        }

        private static void EnableRenderers(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                renderer.enabled = true;
                renderer.forceRenderingOff = false;
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

        private static string GetDefinitionTurntableId(Turntable turntable)
        {
            return NormalizeDefinitionTurntableId(turntable != null ? turntable.id : null);
        }

        private static string GetDefinitionTurntableId(GameObject root)
        {
            if (root == null)
            {
                return string.Empty;
            }

            var turntable = root.GetComponent<Turntable>();
            if (turntable != null)
            {
                return GetDefinitionTurntableId(turntable);
            }

            var name = root.name ?? string.Empty;
            return name.StartsWith("Turntable-", StringComparison.OrdinalIgnoreCase)
                ? name.Substring("Turntable-".Length)
                : NormalizeDefinitionTurntableId(name);
        }

        private static string NormalizeDefinitionTurntableId(string runtimeTurntableId)
        {
            if (string.IsNullOrWhiteSpace(runtimeTurntableId))
            {
                return string.Empty;
            }

            return runtimeTurntableId.EndsWith(".turntable", StringComparison.OrdinalIgnoreCase)
                ? runtimeTurntableId.Substring(0, runtimeTurntableId.Length - ".turntable".Length)
                : runtimeTurntableId;
        }

        private static string ToRuntimeTurntableId(string definitionTurntableId)
        {
            if (string.IsNullOrWhiteSpace(definitionTurntableId))
            {
                return string.Empty;
            }

            return definitionTurntableId.EndsWith(".turntable", StringComparison.OrdinalIgnoreCase)
                ? definitionTurntableId
                : definitionTurntableId + ".turntable";
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
