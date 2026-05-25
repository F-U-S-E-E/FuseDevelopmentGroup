using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Map.Runtime.MapModifiers;
using Map.Runtime.MaskComponents;
using FUSE.Runtime.Cache;
using FUSE.Authoring.Data;
using FUSE.Infrastructure;
using TelegraphPoles;
using TMPro;
using UI.Map;
using UnityEngine;
using UnityEngine.UI;
using RuntimeSimpleGraph = SimpleGraph.Runtime.SimpleGraph;

namespace FUSE.Runtime.API
{
    public static class MapAPI
    {
        private const string MapMaskRootName = "FUSE Map Masks";
        private const string TelegraphRootName = "FUSE Telegraph Poles";
        private const string SpeedLimitCircleName = "FUSE Speed Limit Circle";

        private static readonly FieldInfo CanvasField = typeof(MapLabel).GetField("_canvas", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo PolePrefabsField = typeof(TelegraphPoleManager).GetField("polePrefabs", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo WirePrefabField = typeof(TelegraphPoleManager).GetField("wirePrefab", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo TelegraphRebuildMethod = typeof(TelegraphPoleManager).GetMethod("Rebuild", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly Regex SpeedLimitTextPattern = new Regex(@"^\s*(?<mph>\d{1,3})\s*MPH\.?\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex SpeedLimitNumberPattern = new Regex(@"^\s*(?<mph>\d{1,3})\s*$", RegexOptions.Compiled);

        private static readonly Dictionary<string, FuseTelegraphPoleMovement[]> TelegraphPoleMovementClaims =
            new Dictionary<string, FuseTelegraphPoleMovement[]>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<int, Vector3> TelegraphPoleOriginalPositions = new Dictionary<int, Vector3>();

        private static Transform _fallbackMapMaskRoot;
        private static Transform _fallbackTelegraphRoot;
        private static Sprite _speedLimitCircleSprite;

        public static MapLabel AddMapLabel(string id, FuseMapLabel definition)
        {
            RequireId(id, nameof(id));
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (GetMapLabel(id) != null)
            {
                throw new InvalidOperationException($"Map label '{id}' already exists.");
            }

            var parent = GameObject.Find("Map Labels");
            if (parent == null)
            {
                throw new InvalidOperationException("Map Labels parent was not found.");
            }

            var template = parent.GetComponentInChildren<MapLabel>();
            if (template == null)
            {
                throw new InvalidOperationException("No MapLabel template was found.");
            }

            GameObject wrapper = null;
            try
            {
                wrapper = new GameObject(id);
                wrapper.transform.SetParent(parent.transform, false);

                var labelObject = UnityEngine.Object.Instantiate(template.gameObject, wrapper.transform, true);
                labelObject.name = "MapLabel";
                labelObject.transform.localPosition = Vector3.zero;

                var label = labelObject.GetComponent<MapLabel>();
                label.name = id;
                CanvasField?.SetValue(label, labelObject.GetComponent<Canvas>());
                ApplyMapLabelDefinition(label, definition);
                FuseMapLabelRuntimeIndex.Instance.Set(id, label);
                FuseApiPersistence.RecordDefinition(FuseDefinitionKind.MapLabel, id, definition);
                return label;
            }
            catch (Exception ex)
            {
                if (wrapper != null)
                {
                    UnityEngine.Object.Destroy(wrapper);
                }

                FuseMapLabelRuntimeIndex.Instance.Remove(id);
                FuseLog.Exception($"FUSE failed to create map label '{id}' and cleaned up the partial object", ex);
                throw;
            }
        }

        public static void UpdateMapLabel(string id, FuseMapLabel definition)
        {
            var label = RequireMapLabel(id);
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            ApplyMapLabelDefinition(label, definition);
            FuseMapLabelRuntimeIndex.Instance.Set(id, label);
            FuseApiPersistence.RecordDefinition(FuseDefinitionKind.MapLabel, id, definition);
        }

        public static void RemoveMapLabel(string id)
        {
            if (!TryRemoveMapLabel(id))
            {
                throw new InvalidOperationException($"Map label '{id}' was not found.");
            }
        }

        public static bool TryRemoveMapLabel(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return false;
            }

            var label = GetMapLabel(id);
            GameObject wrapper;
            if (label != null)
            {
                wrapper = label.transform.parent != null ? label.transform.parent.gameObject : label.gameObject;
            }
            else
            {
                wrapper = FusePrefabResolver.ResolveScenePath(id) ?? GameObject.Find(id);
            }

            if (wrapper == null)
            {
                FuseLog.Info($"FUSE world removal skipped missing map label '{id}'.");
                return false;
            }

            var path = GetTransformPath(wrapper.transform);
            wrapper.SetActive(false);
            UnityEngine.Object.Destroy(wrapper);
            FuseMapLabelRuntimeIndex.Instance.Remove(id);
            FuseRuntimeDefinitionCache.Remove(FuseDefinitionKind.MapLabel, id);
            FuseLog.Info($"FUSE removed map label '{id}' from '{path}'.");
            return true;
        }

        public static MapLabel GetMapLabel(string id)
        {
            if (FuseMapLabelRuntimeIndex.Instance.TryGetValue(id, out var cached))
            {
                return (MapLabel)cached;
            }

            return !string.IsNullOrWhiteSpace(id)
                ? UnityEngine.Object.FindObjectsOfType<MapLabel>().FirstOrDefault(label => label.name == id)
                : null;
        }

        public static IEnumerable<MapLabel> GetAllMapLabels()
        {
            return UnityEngine.Object.FindObjectsOfType<MapLabel>();
        }

        public static FuseMapLabel GetMapLabelDefinition(string id)
        {
            return GetDefinition(GetMapLabel(id));
        }

        public static FuseMapLabel GetDefinition(MapLabel label)
        {
            if (label == null)
            {
                return null;
            }

            FuseRuntimeDefinitionCache.TryGet(FuseDefinitionKind.MapLabel, label.name, out FuseMapLabel definition);
            definition = definition ?? new FuseMapLabel();
            definition.Text = label.text;
            var transform = label.transform.parent != null ? label.transform.parent : label.transform;
            definition.Position = transform.localPosition;
            definition.Rotation = transform.localEulerAngles;
            var text = label.GetComponentInChildren<TMP_Text>();
            if (text != null)
            {
                definition.Size = text.fontSize;
                definition.Color = "#" + ColorUtility.ToHtmlStringRGBA(text.color);
            }

            return definition;
        }

        public static GameObject AddMapMask(string id, FuseMapMask definition)
        {
            RequireId(id, nameof(id));
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (GetMapMask(id) != null)
            {
                throw new InvalidOperationException($"Map mask '{id}' already exists.");
            }

            var root = new GameObject(id);
            root.transform.SetParent(GetOrCreateWorldRoot(MapMaskRootName, ref _fallbackMapMaskRoot), false);
            ApplyMapMaskDefinition(root, definition);
            FuseApiPersistence.RecordDefinition(FuseDefinitionKind.MapMask, id, definition);
            return root;
        }

        public static void UpdateMapMask(string id, FuseMapMask definition)
        {
            var root = RequireMapMask(id);
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            ApplyMapMaskDefinition(root, definition);
            FuseApiPersistence.RecordDefinition(FuseDefinitionKind.MapMask, id, definition);
        }

        public static void RemoveMapMask(string id)
        {
            if (!TryRemoveMapMask(id))
            {
                throw new InvalidOperationException($"Map mask '{id}' was not found.");
            }
        }

        public static bool TryRemoveMapMask(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return false;
            }

            var root = GetMapMask(id) ?? FusePrefabResolver.ResolveScenePath(id) ?? GameObject.Find(id);
            if (root == null)
            {
                FuseLog.Info($"FUSE world removal skipped missing map mask '{id}'.");
                return false;
            }

            var path = GetTransformPath(root.transform);
            root.SetActive(false);
            UnityEngine.Object.Destroy(root);
            FuseRuntimeDefinitionCache.Remove(FuseDefinitionKind.MapMask, id);
            FuseLog.Info($"FUSE removed map mask '{id}' from '{path}'.");
            return true;
        }

        public static GameObject GetMapMask(string id)
        {
            return !string.IsNullOrWhiteSpace(id)
                ? GameObject.Find("World/" + MapMaskRootName + "/" + id) ?? GameObject.Find(MapMaskRootName + "/" + id)
                : null;
        }

        public static IEnumerable<GameObject> GetAllMapMasks()
        {
            return GetChildren(GetOrCreateWorldRoot(MapMaskRootName, ref _fallbackMapMaskRoot));
        }

        public static FuseMapMask GetMapMaskDefinition(string id)
        {
            return GetMapMaskDefinition(GetMapMask(id));
        }

        public static FuseMapMask GetMapMaskDefinition(GameObject mapMask)
        {
            if (mapMask == null)
            {
                return null;
            }

            FuseRuntimeDefinitionCache.TryGet(FuseDefinitionKind.MapMask, mapMask.name, out FuseMapMask definition);
            definition = definition ?? new FuseMapMask();
            var circle = mapMask.GetComponent<CircleMapMask>();
            if (circle != null)
            {
                definition.Type = "circle";
                definition.Center = mapMask.transform.position;
                definition.Radius = circle.radius;
                return definition;
            }

            var rectangle = mapMask.GetComponent<RectangleMapMask>();
            if (rectangle != null)
            {
                definition.Type = "rectangle";
                definition.Center = mapMask.transform.position;
                definition.Rotation = mapMask.transform.eulerAngles;
                definition.Size = new Vector3(rectangle.sizeX, 0f, rectangle.sizeZ);
                return definition;
            }

            var curves = mapMask.GetComponentsInChildren<CurveMapMask>(true);
            if (curves.Length > 0)
            {
                definition.Type = "curve";
                definition.Width = curves[0].radius;
                var points = new List<Vector3>();
                foreach (var curve in curves.OrderBy(curve => curve.name, StringComparer.OrdinalIgnoreCase))
                {
                    if (points.Count == 0)
                    {
                        points.Add(curve.positionA);
                    }

                    points.Add(curve.positionB);
                }

                definition.Points = points.ToArray();
            }

            return definition;
        }

        public static void RefreshAttachedMapMasks(GameObject root, string reason = null)
        {
            if (root == null)
            {
                return;
            }

            var refreshed = 0;
            foreach (var component in root.GetComponentsInChildren<MaskComponentBase>(true))
            {
                if (component == null || !component.isActiveAndEnabled)
                {
                    continue;
                }

                try
                {
                    component.Rebuild();
                    refreshed++;
                }
                catch (Exception ex)
                {
                    FuseLog.Warning(
                        $"FUSE map mask refresh failed root='{root.name}' operation='{reason ?? "unspecified"}' " +
                        $"component='{component.GetType().Name}': {ex.Message}");
                }
            }

            if (refreshed > 0)
            {
                FuseLog.Info(
                    $"FUSE refreshed {refreshed} attached map mask component(s) on '{root.name}' " +
                    $"after '{reason ?? "unspecified"}'.");
            }
        }

        public static GameObject AddTelegraphPoles(string id, FuseTelegraphPoles definition)
        {
            RequireId(id, nameof(id));
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (GetTelegraphPoles(id) != null)
            {
                throw new InvalidOperationException($"Telegraph pole set '{id}' already exists.");
            }

            var root = new GameObject(id);
            root.transform.SetParent(GetOrCreateWorldRoot(TelegraphRootName, ref _fallbackTelegraphRoot), false);
            ApplyTelegraphPolesDefinition(root, definition);
            FuseApiPersistence.RecordDefinition(FuseDefinitionKind.TelegraphPoles, id, definition);
            return root;
        }

        public static void UpdateTelegraphPoles(string id, FuseTelegraphPoles definition)
        {
            var root = RequireTelegraphPoles(id);
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            ApplyTelegraphPolesDefinition(root, definition);
            FuseApiPersistence.RecordDefinition(FuseDefinitionKind.TelegraphPoles, id, definition);
        }

        public static void RemoveTelegraphPoles(string id)
        {
            if (!TryRemoveTelegraphPoles(id))
            {
                throw new InvalidOperationException($"Telegraph pole set '{id}' was not found.");
            }
        }

        public static bool TryRemoveTelegraphPoles(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return false;
            }

            var root = GetTelegraphPoles(id) ?? FusePrefabResolver.ResolveScenePath(id) ?? GameObject.Find(id);
            if (root == null)
            {
                FuseLog.Info($"FUSE world removal skipped missing telegraph pole set '{id}'.");
                return false;
            }

            var path = GetTransformPath(root.transform);
            root.SetActive(false);
            UnityEngine.Object.Destroy(root);
            FuseRuntimeDefinitionCache.Remove(FuseDefinitionKind.TelegraphPoles, id);
            FuseLog.Info($"FUSE removed telegraph pole set '{id}' from '{path}'.");
            return true;
        }

        public static GameObject GetTelegraphPoles(string id)
        {
            return !string.IsNullOrWhiteSpace(id)
                ? GameObject.Find("World/" + TelegraphRootName + "/" + id) ?? GameObject.Find(TelegraphRootName + "/" + id)
                : null;
        }

        public static IEnumerable<GameObject> GetAllTelegraphPoles()
        {
            return GetChildren(GetOrCreateWorldRoot(TelegraphRootName, ref _fallbackTelegraphRoot));
        }

        public static FuseTelegraphPoles GetTelegraphPolesDefinition(string id)
        {
            return GetTelegraphPolesDefinition(GetTelegraphPoles(id));
        }

        public static FuseTelegraphPoles GetTelegraphPolesDefinition(GameObject telegraphPoles)
        {
            if (telegraphPoles == null)
            {
                return null;
            }

            FuseRuntimeDefinitionCache.TryGet(FuseDefinitionKind.TelegraphPoles, telegraphPoles.name, out FuseTelegraphPoles definition);
            definition = definition ?? new FuseTelegraphPoles();
            definition.Points = telegraphPoles.GetComponentsInChildren<TelegraphPole>(true)
                .OrderBy(pole => pole.name, StringComparer.OrdinalIgnoreCase)
                .Select(pole => pole.transform.position)
                .ToArray();
            return definition;
        }

        public static void ApplyTelegraphPoleMovements(string packageId, FuseTelegraphPoleMovement[] movements)
        {
            if (string.IsNullOrWhiteSpace(packageId))
            {
                throw new ArgumentException("Package id is required.", nameof(packageId));
            }

            var normalized = (movements ?? Array.Empty<FuseTelegraphPoleMovement>())
                .Where(movement => movement != null && movement.PoleIndices != null && movement.PoleIndices.Length > 0)
                .ToArray();

            if (normalized.Length == 0)
            {
                ReleaseTelegraphPoleMovements(packageId);
                return;
            }

            TelegraphPoleMovementClaims[packageId] = normalized;
            ReapplyTelegraphPoleMovements($"package '{packageId}' apply");
        }

        public static bool HasTelegraphPoleMovementClaim(string packageId)
        {
            return !string.IsNullOrWhiteSpace(packageId) && TelegraphPoleMovementClaims.ContainsKey(packageId);
        }

        public static void ReleaseTelegraphPoleMovements(string packageId)
        {
            if (string.IsNullOrWhiteSpace(packageId) || !TelegraphPoleMovementClaims.Remove(packageId))
            {
                return;
            }

            ReapplyTelegraphPoleMovements($"package '{packageId}' unload");
        }

        public static void RestoreAllTelegraphPoleMovements(string reason)
        {
            if (TelegraphPoleOriginalPositions.Count == 0 && TelegraphPoleMovementClaims.Count == 0)
            {
                return;
            }

            var manager = FindTelegraphPoleManager();
            var graph = manager != null ? manager.GetComponent<RuntimeSimpleGraph>() : null;
            var restored = 0;
            var touched = new HashSet<int>();
            if (graph != null)
            {
                foreach (var entry in TelegraphPoleOriginalPositions.ToArray())
                {
                    var node = graph.NodeForId(entry.Key);
                    if (node == null)
                    {
                        continue;
                    }

                    node.position = entry.Value;
                    touched.Add(entry.Key);
                    restored++;
                }

                NotifyTelegraphNodesChanged(manager, graph, touched);
            }

            TelegraphPoleOriginalPositions.Clear();
            TelegraphPoleMovementClaims.Clear();
            FuseLog.Info($"FUSE restored telegraph pole movements for '{reason ?? "unspecified"}' restored={restored}.");
        }

        private static void ReapplyTelegraphPoleMovements(string reason)
        {
            var manager = FindTelegraphPoleManager();
            if (manager == null)
            {
                FuseLog.Warning($"FUSE telegraph pole movement skipped for '{reason}' because TelegraphPoleManager was not found.");
                return;
            }

            var graph = manager.GetComponent<RuntimeSimpleGraph>();
            if (graph == null)
            {
                FuseLog.Warning($"FUSE telegraph pole movement skipped for '{reason}' because TelegraphPoleManager has no SimpleGraph.");
                return;
            }

            var aggregate = new Dictionary<int, Vector3>();
            foreach (var package in TelegraphPoleMovementClaims)
            {
                foreach (var movement in package.Value ?? Array.Empty<FuseTelegraphPoleMovement>())
                {
                    if (movement?.PoleIndices == null)
                    {
                        continue;
                    }

                    foreach (var poleIndex in movement.PoleIndices)
                    {
                        if (poleIndex < 0)
                        {
                            FuseLog.Warning($"FUSE telegraph pole movement skipped invalid pole index package='{package.Key}' poleIndex={poleIndex}.");
                            continue;
                        }

                        aggregate[poleIndex] = aggregate.TryGetValue(poleIndex, out var existing)
                            ? existing + movement.Offset
                            : movement.Offset;
                    }
                }
            }

            var touched = new HashSet<int>();
            var moved = 0;
            var restored = 0;
            foreach (var original in TelegraphPoleOriginalPositions.Keys.ToArray())
            {
                if (aggregate.ContainsKey(original))
                {
                    continue;
                }

                var node = graph.NodeForId(original);
                if (node == null)
                {
                    TelegraphPoleOriginalPositions.Remove(original);
                    continue;
                }

                node.position = TelegraphPoleOriginalPositions[original];
                TelegraphPoleOriginalPositions.Remove(original);
                touched.Add(original);
                restored++;
            }

            foreach (var movement in aggregate)
            {
                var node = graph.NodeForId(movement.Key);
                if (node == null)
                {
                    FuseLog.Warning($"FUSE telegraph pole movement skipped missing base pole node package='<aggregate>' poleIndex={movement.Key}.");
                    continue;
                }

                if (!TelegraphPoleOriginalPositions.TryGetValue(movement.Key, out var originalPosition))
                {
                    originalPosition = node.position;
                    TelegraphPoleOriginalPositions[movement.Key] = originalPosition;
                }

                node.position = originalPosition + movement.Value;
                touched.Add(movement.Key);
                moved++;
            }

            NotifyTelegraphNodesChanged(manager, graph, touched);
            FuseLog.Info($"FUSE applied telegraph pole movements for '{reason}' moved={moved} restored={restored} activePackages={TelegraphPoleMovementClaims.Count}.");
        }

        private static TelegraphPoleManager FindTelegraphPoleManager()
        {
            return UnityEngine.Object.FindObjectsOfType<TelegraphPoleManager>(true).FirstOrDefault();
        }

        private static void NotifyTelegraphNodesChanged(TelegraphPoleManager manager, RuntimeSimpleGraph graph, HashSet<int> touched)
        {
            if (graph == null || touched == null || touched.Count == 0)
            {
                return;
            }

            try
            {
                graph.NotifyDidChangeNodes(touched);
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE telegraph pole movement could not notify node changes", ex);
            }

            if (manager == null || !manager.isActiveAndEnabled || TelegraphRebuildMethod == null)
            {
                return;
            }

            try
            {
                TelegraphRebuildMethod.Invoke(manager, Array.Empty<object>());
            }
            catch (Exception ex)
            {
                FuseLog.Warning($"FUSE telegraph pole movement could not force telegraph manager rebuild: {ex.GetBaseException().Message}");
            }
        }

        private static void ApplyMapLabelDefinition(MapLabel label, FuseMapLabel definition)
        {
            if (label.transform.parent != null)
            {
                label.transform.parent.localPosition = definition.Position;
                label.transform.parent.localRotation = Quaternion.Euler(definition.Rotation);
            }

            label.text = string.IsNullOrWhiteSpace(definition.Text) ? label.name : definition.Text;
            var isSpeedLimit = TryGetSpeedLimitMph(definition, label.text, out var speedLimitMph);

            var text = label.GetComponentInChildren<TMP_Text>();
            if (text != null)
            {
                text.text = isSpeedLimit ? speedLimitMph.ToString() : label.text;
                if (definition.Size.HasValue)
                {
                    text.fontSize = definition.Size.Value;
                }

                if (!string.IsNullOrWhiteSpace(definition.Color) && ColorUtility.TryParseHtmlString(definition.Color, out var color))
                {
                    text.color = color;
                }

                if (isSpeedLimit)
                {
                    ConfigureSpeedLimitLabel(text, speedLimitMph);
                }
                else
                {
                    RemoveSpeedLimitCircle(text);
                    ConfigureMapLabelText(text, label.text);
                }
            }
        }

        private static bool TryGetSpeedLimitMph(FuseMapLabel definition, string text, out int speedLimitMph)
        {
            speedLimitMph = 0;
            if (definition?.SpeedLimitMph is int explicitSpeed && explicitSpeed > 0)
            {
                speedLimitMph = explicitSpeed;
                return true;
            }

            var style = definition?.Style ?? string.Empty;
            if (style.Equals("speedLimit", StringComparison.OrdinalIgnoreCase) ||
                style.Equals("speed-limit", StringComparison.OrdinalIgnoreCase))
            {
                var numberMatch = SpeedLimitNumberPattern.Match(text ?? string.Empty);
                if (numberMatch.Success && int.TryParse(numberMatch.Groups["mph"].Value, out speedLimitMph))
                {
                    return true;
                }
            }

            var mphMatch = SpeedLimitTextPattern.Match(text ?? string.Empty);
            return mphMatch.Success && int.TryParse(mphMatch.Groups["mph"].Value, out speedLimitMph);
        }

        private static void ConfigureMapLabelText(TMP_Text text, string value)
        {
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Overflow;

            var rect = text.GetComponent<RectTransform>();
            if (rect != null)
            {
                var fontSize = Mathf.Max(text.fontSize, 12f);
                var preferred = text.GetPreferredValues(value ?? string.Empty);
                var estimatedWidth = ((value?.Length ?? 0) + 2) * fontSize * 0.75f;
                var width = Mathf.Max(256f, preferred.x + 32f, estimatedWidth);
                var height = Mathf.Max(64f, preferred.y + 16f, fontSize * 2f);

                rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
                rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            }

            text.ForceMeshUpdate();
        }

        private static void ConfigureSpeedLimitLabel(TMP_Text text, int speedLimitMph)
        {
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Overflow;
            text.alignment = TextAlignmentOptions.Center;
            text.fontSize = Mathf.Clamp(text.fontSize > 0f ? text.fontSize * 0.72f : 10f, 8f, 11f);

            var rect = text.GetComponent<RectTransform>();
            var diameter = Mathf.Max(23f, text.fontSize * 2.2f);
            if (rect != null)
            {
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = Vector2.zero;
                rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, diameter);
                rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, diameter);
            }

            var circle = GetOrCreateSpeedLimitCircle(text);
            if (circle != null)
            {
                circle.gameObject.SetActive(true);
                circle.color = text.color.a > 0.01f ? text.color : Color.white;
                circle.sprite = GetSpeedLimitCircleSprite();
                circle.type = Image.Type.Simple;
                circle.preserveAspect = true;
                circle.raycastTarget = false;

                var circleRect = circle.GetComponent<RectTransform>();
                if (circleRect != null)
                {
                    circleRect.anchorMin = new Vector2(0.5f, 0.5f);
                    circleRect.anchorMax = new Vector2(0.5f, 0.5f);
                    circleRect.pivot = new Vector2(0.5f, 0.5f);
                    circleRect.anchoredPosition = Vector2.zero;
                    circleRect.localScale = Vector3.one;
                    circleRect.rotation = text.transform.rotation;
                    circleRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, diameter);
                    circleRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, diameter);
                    circleRect.SetAsFirstSibling();
                }
            }

            text.text = speedLimitMph.ToString();
            text.ForceMeshUpdate();
        }

        private static Image GetOrCreateSpeedLimitCircle(TMP_Text text)
        {
            if (text == null)
            {
                return null;
            }

            var parent = text.transform.parent ?? text.transform;
            var existing = parent.Find(SpeedLimitCircleName);
            if (existing != null)
            {
                return existing.GetComponent<Image>() ?? existing.gameObject.AddComponent<Image>();
            }

            var circleObject = new GameObject(SpeedLimitCircleName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            circleObject.transform.SetParent(parent, false);
            circleObject.transform.SetSiblingIndex(Mathf.Max(0, text.transform.GetSiblingIndex()));
            return circleObject.GetComponent<Image>();
        }

        private static void RemoveSpeedLimitCircle(TMP_Text text)
        {
            var parent = text?.transform.parent;
            if (parent == null)
            {
                return;
            }

            var existing = parent.Find(SpeedLimitCircleName);
            if (existing != null)
            {
                UnityEngine.Object.Destroy(existing.gameObject);
            }
        }

        private static Sprite GetSpeedLimitCircleSprite()
        {
            if (_speedLimitCircleSprite != null)
            {
                return _speedLimitCircleSprite;
            }

            const int size = 64;
            const float center = (size - 1) * 0.5f;
            const float outerRadius = 30f;
            const float innerRadius = 25f;
            var texture = new Texture2D(size, size, TextureFormat.ARGB32, false)
            {
                name = "FUSE Speed Limit Circle",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var distance = Mathf.Sqrt(Mathf.Pow(x - center, 2f) + Mathf.Pow(y - center, 2f));
                    var outerAlpha = Mathf.Clamp01(outerRadius - distance + 1f);
                    var innerAlpha = Mathf.Clamp01(distance - innerRadius + 1f);
                    var ringAlpha = Mathf.Min(outerAlpha, innerAlpha);
                    var fillAlpha = Mathf.Clamp01(innerRadius - distance + 1f);

                    if (ringAlpha > 0.01f)
                    {
                        var byteAlpha = (byte)Mathf.RoundToInt(ringAlpha * 255f);
                        pixels[(y * size) + x] = new Color32(255, 255, 255, byteAlpha);
                    }
                    else if (fillAlpha > 0.01f)
                    {
                        var byteAlpha = (byte)Mathf.RoundToInt(fillAlpha * 230f);
                        pixels[(y * size) + x] = new Color32(0, 0, 0, byteAlpha);
                    }
                    else
                    {
                        pixels[(y * size) + x] = new Color32(0, 0, 0, 0);
                    }
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            _speedLimitCircleSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
            _speedLimitCircleSprite.name = "FUSE Speed Limit Circle";
            return _speedLimitCircleSprite;
        }

        private static void ApplyMapMaskDefinition(GameObject root, FuseMapMask definition)
        {
            var wasActive = root.activeSelf;
            if (wasActive)
            {
                root.SetActive(false);
            }

            ClearComponents<MapMaskBase>(root);
            DestroyChildren(root.transform);

            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

            var type = (definition.Type ?? string.Empty).Trim().ToLowerInvariant();
            switch (type)
            {
                case "circle":
                    if (!definition.Radius.HasValue || definition.Radius.Value <= 0f)
                    {
                        throw new InvalidOperationException("Circle map masks require a positive radius.");
                    }

                    root.transform.position = definition.Center;
                    var circle = root.AddComponent<CircleMapMask>();
                    ConfigureDefaultMapMask(circle, definition);
                    circle.radius = definition.Radius.Value;
                    break;

                case "rectangle":
                    if (!definition.Size.HasValue || definition.Size.Value.x <= 0f || definition.Size.Value.z <= 0f)
                    {
                        throw new InvalidOperationException("Rectangle map masks require a positive size.");
                    }

                    root.transform.position = definition.Center;
                    root.transform.rotation = Quaternion.Euler(definition.Rotation);
                    var rectangle = root.AddComponent<RectangleMapMask>();
                    ConfigureDefaultMapMask(rectangle, definition);
                    rectangle.radius = 0f;
                    rectangle.falloff = definition.Falloff ?? 10f;
                    rectangle.sizeX = definition.Size.Value.x;
                    rectangle.sizeZ = definition.Size.Value.z;
                    rectangle.degrees = 0f;
                    break;

                case "curve":
                    if (definition.Points == null || definition.Points.Length < 2)
                    {
                        throw new InvalidOperationException("Curve map masks require at least two points.");
                    }

                    var width = definition.Width.GetValueOrDefault(8f);
                    for (var index = 0; index < definition.Points.Length - 1; index++)
                    {
                        var pointA = definition.Points[index];
                        var pointB = definition.Points[index + 1];
                        if ((pointB - pointA).sqrMagnitude <= 0.0001f)
                        {
                            continue;
                        }

                        var segment = new GameObject("segment-" + index.ToString("D3"));
                        segment.transform.SetParent(root.transform, false);
                        segment.transform.position = Vector3.zero;
                        segment.transform.rotation = Quaternion.identity;

                        var curve = segment.AddComponent<CurveMapMask>();
                        ConfigureDefaultMapMask(curve, definition);
                        curve.radius = width;
                        curve.falloff = definition.Falloff ?? 10f;
                        curve.positionA = pointA;
                        curve.positionB = pointB;
                        curve.rotationA = Quaternion.LookRotation((pointB - pointA).normalized, Vector3.up).eulerAngles;
                        curve.rotationB = curve.rotationA;
                        curve.sizeA = 1f;
                        curve.sizeB = 1f;
                        curve.radiusNoise = 0f;
                        curve.noiseScale = 1f;
                    }
                    break;

                default:
                    throw new InvalidOperationException($"Unknown map mask type '{definition.Type}'.");
            }

            root.SetActive(wasActive);
            if (wasActive)
            {
                RefreshAttachedMapMasks(root, $"map mask definition '{root.name}'");
            }
        }

        private static void ApplyTelegraphPolesDefinition(GameObject root, FuseTelegraphPoles definition)
        {
            if (definition.Points == null || definition.Points.Length < 2)
            {
                throw new InvalidOperationException("Telegraph pole sets require at least two points.");
            }

            DestroyChildren(root.transform);

            var poleTemplate = ResolveTelegraphPolePrefab(definition);
            var wireTemplate = ResolveTelegraphWirePrefab(definition);
            if (poleTemplate == null)
            {
                throw new InvalidOperationException("A telegraph pole prefab could not be resolved.");
            }

            if (wireTemplate == null)
            {
                throw new InvalidOperationException("A telegraph wire prefab could not be resolved.");
            }

            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

            var positions = SamplePolyline(definition.Points, Mathf.Max(definition.Spacing.GetValueOrDefault(40f), 1f));
            var poles = new List<TelegraphPole>(positions.Count);
            for (var index = 0; index < positions.Count; index++)
            {
                var tangent = GetTangent(positions, index);
                if (tangent.sqrMagnitude <= 0.0001f)
                {
                    tangent = Vector3.forward;
                }

                var rotation = Quaternion.LookRotation(tangent.normalized, Vector3.up);
                var pole = UnityEngine.Object.Instantiate(poleTemplate, positions[index], rotation, root.transform);
                pole.name = "pole-" + index.ToString("D3");
                pole.localBasePosition = Vector3.zero;
                poles.Add(pole);
            }

            var wireIndex = 0;
            for (var index = 0; index < poles.Count - 1; index++)
            {
                wireIndex = CreateWiresBetween(root.transform, poles[index], poles[index + 1], wireTemplate, wireIndex);
            }

            root.SetActive(true);
        }

        private static void ConfigureDefaultMapMask(MapMaskBase mask, FuseMapMask definition = null)
        {
            mask.enableCutTrees = definition?.EnableCutTrees ?? false;
            mask.enableMaskModifier = definition?.EnableMaskModifier ?? false;
            mask.enableSetHeight = definition?.EnableSetHeight ?? false;

            // Convert from the FuseMapMask MaskName (from FUSE.Authoring.Data) into the Map runtime MaskName
            if (definition?.MaskName.HasValue == true)
            {
                var sourceName = definition.MaskName.Value.ToString();
                if (Enum.TryParse<Map.Runtime.MapModifiers.MaskName>(sourceName, true, out var mapped))
                {
                    mask.maskName = mapped;
                }
                else
                {
                    // Fallback if names don't match
                    mask.maskName = Map.Runtime.MapModifiers.MaskName.Object;
                }
            }
            else
            {
                mask.maskName = Map.Runtime.MapModifiers.MaskName.Object;
            }

            mask.order = definition?.Order ?? 0;
            mask.falloff = definition?.Falloff ?? 10f;
        }

        private static int CreateWiresBetween(Transform parent, TelegraphPole a, TelegraphPole b, TelegraphWire wireTemplate, int wireIndex)
        {
            if (a.rows == null || b.rows == null || a.rows.Length == 0 || b.rows.Length == 0)
            {
                return wireIndex;
            }

            var sameDirection = Vector3.Dot(a.transform.forward, b.transform.forward) > 0f;
            var maxConnections = Mathf.Min(a.CountPoints(), b.CountPoints());
            var rowA = a.rows.Length - 1;
            var rowB = b.rows.Length - 1;
            var pointA = 0;
            var pointB = 0;

            for (var index = 0; index < maxConnections && rowA >= 0 && rowB >= 0; index++)
            {
                var rowPointsA = a.rows[rowA].points;
                var rowPointsB = b.rows[rowB].points;
                if (rowPointsA == null || rowPointsB == null || rowPointsA.Length == 0 || rowPointsB.Length == 0)
                {
                    break;
                }

                var bPointIndex = sameDirection ? pointB : (rowPointsB.Length - 1 - pointB);
                var positionA = a.transform.TransformPoint(rowPointsA[pointA]);
                var positionB = b.transform.TransformPoint(rowPointsB[bPointIndex]);

                var wire = UnityEngine.Object.Instantiate(wireTemplate, parent);
                wire.name = "wire-" + wireIndex.ToString("D3");
                wire.Configure(positionA, positionB);
                wireIndex++;

                pointA++;
                pointB++;

                if (pointA >= rowPointsA.Length)
                {
                    rowA--;
                    pointA = 0;
                }

                if (pointB >= rowPointsB.Length)
                {
                    rowB--;
                    pointB = 0;
                }
            }

            return wireIndex;
        }

        private static TelegraphPole ResolveTelegraphPolePrefab(FuseTelegraphPoles definition)
        {
            if (!string.IsNullOrWhiteSpace(definition.PolePrefab))
            {
                var prefab = FusePrefabResolver.Resolve(definition.PolePrefab);
                if (prefab == null)
                {
                    return null;
                }

                return prefab.GetComponent<TelegraphPole>() ?? prefab.GetComponentInChildren<TelegraphPole>(true);
            }

            var manager = UnityEngine.Object.FindObjectsOfType<TelegraphPoleManager>(true).FirstOrDefault();
            var prefabs = PolePrefabsField?.GetValue(manager) as IEnumerable<TelegraphPole>;
            if (prefabs == null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(definition.Profile))
            {
                var match = prefabs.FirstOrDefault(prefab => prefab != null && prefab.name.IndexOf(definition.Profile, StringComparison.OrdinalIgnoreCase) >= 0);
                if (match != null)
                {
                    return match;
                }
            }

            return prefabs.FirstOrDefault(prefab => prefab != null);
        }

        private static TelegraphWire ResolveTelegraphWirePrefab(FuseTelegraphPoles definition)
        {
            if (!string.IsNullOrWhiteSpace(definition.WirePrefab))
            {
                var prefab = FusePrefabResolver.Resolve(definition.WirePrefab);
                if (prefab == null)
                {
                    return null;
                }

                return prefab.GetComponent<TelegraphWire>() ?? prefab.GetComponentInChildren<TelegraphWire>(true);
            }

            var manager = UnityEngine.Object.FindObjectsOfType<TelegraphPoleManager>(true).FirstOrDefault();
            return WirePrefabField?.GetValue(manager) as TelegraphWire;
        }

        private static List<Vector3> SamplePolyline(Vector3[] sourcePoints, float spacing)
        {
            var points = new List<Vector3>();
            if (sourcePoints == null || sourcePoints.Length == 0)
            {
                return points;
            }

            points.Add(sourcePoints[0]);
            var carry = 0f;
            for (var index = 0; index < sourcePoints.Length - 1; index++)
            {
                var start = sourcePoints[index];
                var end = sourcePoints[index + 1];
                var delta = end - start;
                var length = delta.magnitude;
                if (length <= 0.0001f)
                {
                    continue;
                }

                var consumed = 0f;
                while (carry + (length - consumed) >= spacing)
                {
                    var nextStep = spacing - carry;
                    consumed += nextStep;
                    points.Add(Vector3.Lerp(start, end, consumed / length));
                    carry = 0f;
                }

                carry += length - consumed;
            }

            if ((points[points.Count - 1] - sourcePoints[sourcePoints.Length - 1]).sqrMagnitude > 0.0001f)
            {
                points.Add(sourcePoints[sourcePoints.Length - 1]);
            }

            return points;
        }

        private static Vector3 GetTangent(List<Vector3> positions, int index)
        {
            if (positions == null || positions.Count == 0)
            {
                return Vector3.zero;
            }

            if (positions.Count == 1)
            {
                return Vector3.forward;
            }

            if (index <= 0)
            {
                return positions[1] - positions[0];
            }

            if (index >= positions.Count - 1)
            {
                return positions[index] - positions[index - 1];
            }

            return positions[index + 1] - positions[index - 1];
        }

        private static MapLabel RequireMapLabel(string id)
        {
            var label = GetMapLabel(id);
            if (label == null)
            {
                throw new InvalidOperationException($"Map label '{id}' was not found.");
            }

            return label;
        }

        private static GameObject RequireMapMask(string id)
        {
            var mapMask = GetMapMask(id);
            if (mapMask == null)
            {
                throw new InvalidOperationException($"Map mask '{id}' was not found.");
            }

            return mapMask;
        }

        private static GameObject RequireTelegraphPoles(string id)
        {
            var telegraph = GetTelegraphPoles(id);
            if (telegraph == null)
            {
                throw new InvalidOperationException($"Telegraph pole set '{id}' was not found.");
            }

            return telegraph;
        }

        private static Transform GetOrCreateWorldRoot(string name, ref Transform fallbackRoot)
        {
            var world = GameObject.Find("World");
            if (world != null)
            {
                var existing = world.transform.Find(name);
                if (existing != null)
                {
                    return existing;
                }

                var root = new GameObject(name);
                root.transform.SetParent(world.transform, false);
                return root.transform;
            }

            if (fallbackRoot == null)
            {
                fallbackRoot = new GameObject(name).transform;
                UnityEngine.Object.DontDestroyOnLoad(fallbackRoot.gameObject);
            }

            return fallbackRoot;
        }

        private static IEnumerable<GameObject> GetChildren(Transform root)
        {
            if (root == null)
            {
                return Enumerable.Empty<GameObject>();
            }

            var children = new List<GameObject>(root.childCount);
            for (var index = 0; index < root.childCount; index++)
            {
                children.Add(root.GetChild(index).gameObject);
            }

            return children;
        }

        private static string GetTransformPath(Transform transform)
        {
            if (transform == null)
            {
                return string.Empty;
            }

            var names = new Stack<string>();
            var cursor = transform;
            while (cursor != null)
            {
                names.Push(cursor.name);
                cursor = cursor.parent;
            }

            return string.Join("/", names.ToArray());
        }

        private static void DestroyChildren(Transform transform)
        {
            for (var index = transform.childCount - 1; index >= 0; index--)
            {
                UnityEngine.Object.Destroy(transform.GetChild(index).gameObject);
            }
        }

        private static void ClearComponents<T>(GameObject gameObject) where T : Component
        {
            foreach (var component in gameObject.GetComponents<T>())
            {
                UnityEngine.Object.Destroy(component);
            }
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
