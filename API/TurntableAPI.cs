using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using KeyValue.Runtime;
using RAIL.Cache;
using RAIL.Data;
using RAIL.Infrastructure;
using RollingStock.Controls;
using Track;
using UnityEngine;

namespace RAIL.API
{
    public static class TurntableAPI
    {
        private static readonly FieldInfo NodesField = typeof(Turntable).GetField("nodes", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo BridgeGroupIdField = typeof(Turntable).GetField("bridgeGroupId", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo BridgeSegmentField = typeof(Turntable).GetField("_segment", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo CachedTurntableControllersField = typeof(Graph).GetField("_cachedTurntableControllers", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly string[] RuntimeCloneComponentTypeNames =
        {
            "KinematicCharacterController.PhysicsMover",
            "KinematicCharacterController.KinematicCharacterMotor",
            "Track.TurntableTransmitter",
            "Track.TurntableReceiver"
        };

        public static Turntable GetTurntable(string id)
        {
            if (RailTurntableRuntimeIndex.Instance.TryGetValue(id, out var cached))
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

        public static Turntable AddTurntable(string id, RailTurntable definition)
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

            var pitNodes = CreateOrUpdatePitNodes(turntable, definition);
            NodesField?.SetValue(turntable, pitNodes);
            CreateOrUpdateRoundhouseTracks(turntable, definition);

            AttachTurntableTemplate(root, turntable);
            ConfigureRoundhouse(root, definition);
            ClearBridgeGroup(turntable);
            turntable.UpdateSegmentIndex(false);
            ClearBridgeGroup(turntable);

            root.SetActive(true);
            RefreshTurntableTemplateVisuals(root);
            RailTurntableRuntimeIndex.Instance.Set(id, turntable);
            RailNodeRuntimeIndex.Instance.Rebuild();
            RailSegmentRuntimeIndex.Instance.Rebuild();
            InvalidateTurntableControllerCache();
            TrackAPI.RebuildGraph();
            return turntable;
        }

        public static void UpdateTurntable(string id, RailTurntable definition)
        {
            var turntable = RequireTurntable(id);
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

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

            var pitNodes = CreateOrUpdatePitNodes(turntable, definition);
            NodesField?.SetValue(turntable, pitNodes);
            CreateOrUpdateRoundhouseTracks(turntable, definition);
            AttachTurntableTemplate(turntable.gameObject, turntable);
            ConfigureRoundhouse(turntable.gameObject, definition);
            ClearBridgeGroup(turntable);
            turntable.UpdateSegmentIndex(false);
            ClearBridgeGroup(turntable);
            RefreshTurntableTemplateVisuals(turntable.gameObject);

            RailTurntableRuntimeIndex.Instance.Set(id, turntable);
            RailNodeRuntimeIndex.Instance.Rebuild();
            RailSegmentRuntimeIndex.Instance.Rebuild();
            InvalidateTurntableControllerCache();
            TrackAPI.RebuildGraph();
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
            TrackAPI.BeginBatch();
            try
            {
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
                RailTurntableRuntimeIndex.Instance.Remove(id);
            }
            finally
            {
                TrackAPI.EndBatch();
            }
        }

        public static string GetPitNodeId(string turntableId, int index)
        {
            return $"{turntableId}.pit.{index:D2}";
        }

        private static List<TrackNode> CreateOrUpdatePitNodes(Turntable turntable, RailTurntable definition)
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

        private static void CreateOrUpdateRoundhouseTracks(Turntable turntable, RailTurntable definition)
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
                RailLog.Info($"RAIL turntable '{turntable.id}' refreshed visual template at {existing.transform.position}; {DescribeRendererState(existing.gameObject)}.");
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
            RailLog.Info($"RAIL turntable '{turntable.id}' cloned visual template '{template.name}' at {instance.transform.position}; {DescribeRendererState(instance)}.");
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

        private static void ConfigureRoundhouse(GameObject root, RailTurntable definition)
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
            var startPrefab = RailPrefabResolver.Resolve(roundhouse.StartPrefab ?? "vanilla://roundhouseStart");
            var endPrefab = RailPrefabResolver.Resolve(roundhouse.EndPrefab ?? "vanilla://roundhouseEnd");
            var stallPrefab = RailPrefabResolver.Resolve(roundhouse.StallPrefab ?? "vanilla://roundhouseStall");

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

        internal static string GetPitNodeId(string turntableId, int index, RailTurntable definition)
        {
            var legacyIdentifier = definition?.LegacyIdentifier;
            if (!string.IsNullOrWhiteSpace(legacyIdentifier))
            {
                return $"N{legacyIdentifier}TurntableNode{index}";
            }

            return GetPitNodeId(turntableId, index);
        }

        internal static string GetRoundhouseNodeId(string turntableId, int index, RailTurntable definition)
        {
            var legacyIdentifier = definition?.LegacyIdentifier;
            if (!string.IsNullOrWhiteSpace(legacyIdentifier))
            {
                return $"N{legacyIdentifier}RoundhouseNode{index}";
            }

            return $"{turntableId}.roundhouse.node.{index:D2}";
        }

        internal static string GetRoundhouseSegmentId(string turntableId, int index, RailTurntable definition)
        {
            var legacyIdentifier = definition?.LegacyIdentifier;
            if (!string.IsNullOrWhiteSpace(legacyIdentifier))
            {
                return $"S{legacyIdentifier}RoundhouseSegment{index}";
            }

            return $"{turntableId}.roundhouse.segment.{index:D2}";
        }
    }
}
