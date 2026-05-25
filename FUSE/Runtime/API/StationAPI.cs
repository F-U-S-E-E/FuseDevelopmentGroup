using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Helpers;
using Model;
using Model.Ops;
using FUSE.Runtime.Cache;
using FUSE.Authoring.Data;
using FUSE.Infrastructure;
using TMPro;
using UI.Map;
using UnityEngine;
using UnityEngine.UI;

namespace FUSE.Runtime.API
{
    public static class StationAPI
    {
        private static readonly FieldInfo AreaField = typeof(StationAgent).GetField("area", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo PassengerStopField = typeof(StationAgent).GetField("passengerStop", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo SecondaryAreasField = typeof(StationAgent).GetField("secondaryAreas", BindingFlags.Instance | BindingFlags.NonPublic);
        private const float MapIconElevation = 100f;
        private const float StationMapIconWidth = 96f;
        private const float StationMapIconHeight = 42f;
        private const float StationMapIconGraphicOffset = 24f;
        private const float StationMapIconRotationOffsetDegrees = 90f;
        private static Transform _fallbackRoot;
        private static Sprite _stationIconSprite;
        private static Material _stationIconMaterial;

        public static StationAgent AddStationAgent(string id, FuseStation definition)
        {
            RequireId(id, nameof(id));
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (GetStationAgent(id) != null)
            {
                throw new InvalidOperationException($"Station agent '{id}' already exists.");
            }

            var gameObject = new GameObject(id);
            gameObject.transform.SetParent(GetStationRoot(), false);
            var stationAgent = ApplyDefinition(gameObject, id, definition);
            FuseStationRuntimeIndex.Instance.Set(id, stationAgent);
            FuseApiPersistence.RecordDefinition(FuseDefinitionKind.Station, id, definition);
            return stationAgent;
        }

        public static void UpdateStationAgent(string id, FuseStation definition)
        {
            var agent = RequireStationAgent(id);
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            ApplyDefinition(GetStationRootObject(agent, id), id, definition);
            FuseStationRuntimeIndex.Instance.Set(id, RequireStationAgent(id));
            FuseApiPersistence.RecordDefinition(FuseDefinitionKind.Station, id, definition);
        }

        public static void RemoveStationAgent(string id)
        {
            var agent = RequireStationAgent(id);
            var root = GetStationRootObject(agent, id);
            root.SetActive(false);
            UnityEngine.Object.Destroy(root);
            FuseStationRuntimeIndex.Instance.Remove(id);
            FuseRuntimeDefinitionCache.Remove(FuseDefinitionKind.Station, id);
        }

        public static StationAgent GetStationAgent(string id)
        {
            if (FuseStationRuntimeIndex.Instance.TryGetValue(id, out var cached))
            {
                return (StationAgent)cached;
            }

            return !string.IsNullOrWhiteSpace(id)
                ? UnityEngine.Object.FindObjectsOfType<StationAgent>(true).FirstOrDefault(agent => agent.name == id)
                : null;
        }

        public static IEnumerable<StationAgent> GetAllStationAgents()
        {
            return UnityEngine.Object.FindObjectsOfType<StationAgent>(true);
        }

        public static PassengerStop GetPassengerStop(string id)
        {
            return !string.IsNullOrWhiteSpace(id)
                ? PassengerStop.FindAll().FirstOrDefault(stop =>
                    string.Equals(stop.identifier, id, StringComparison.OrdinalIgnoreCase))
                : null;
        }

        public static IEnumerable<PassengerStop> GetAllPassengerStops()
        {
            return PassengerStop.FindAll();
        }

        public static FuseStation GetStationDefinition(string id)
        {
            return GetDefinition(GetStationAgent(id));
        }

        public static FuseStation GetDefinition(StationAgent stationAgent)
        {
            if (stationAgent == null)
            {
                return null;
            }

            var id = stationAgent.name;
            FuseRuntimeDefinitionCache.TryGet(FuseDefinitionKind.Station, id, out FuseStation definition);
            definition = definition ?? new FuseStation();
            var root = GetStationRootObject(stationAgent, id);
            definition.Position = root.transform.localPosition;
            definition.Rotation = root.transform.localEulerAngles;

            var stop = PassengerStopField?.GetValue(stationAgent) as PassengerStop;
            if (stop != null)
            {
                definition.PassengerStopId = stop.identifier;
            }

            return definition;
        }

        private static StationAgent ApplyDefinition(GameObject root, string id, FuseStation definition)
        {
            root.transform.localPosition = definition.Position;
            root.transform.localRotation = Quaternion.Euler(definition.Rotation);

            var prefab = FusePrefabResolver.Resolve(definition.Prefab);
            if (prefab == null)
            {
                throw new InvalidOperationException($"Station prefab '{definition.Prefab}' was not found.");
            }

            var stop = GetPassengerStop(definition.PassengerStopId);
            if (stop == null)
            {
                throw new InvalidOperationException($"Passenger stop '{definition.PassengerStopId}' was not found.");
            }

            for (var index = root.transform.childCount - 1; index >= 0; index--)
            {
                UnityEngine.Object.Destroy(root.transform.GetChild(index).gameObject);
            }

            var instance = UnityEngine.Object.Instantiate(prefab, root.transform);
            instance.name = "prefab";
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localEulerAngles = Vector3.zero;

            root.name = id;
            root.SetActive(false);

            var stationAgent = instance.GetComponentInChildren<StationAgent>(true);
            if (stationAgent == null)
            {
                throw new InvalidOperationException($"Station prefab '{definition.Prefab}' does not contain a StationAgent.");
            }

            stationAgent.name = id;
            var area = stop.GetComponentInParent<Area>(true);
            AreaField?.SetValue(stationAgent, area);
            PassengerStopField?.SetValue(stationAgent, stop);
            var secondaryAreas = SecondaryAreasField?.GetValue(stationAgent) as IList<Area>;
            secondaryAreas?.Clear();

            var stationLabel = area != null ? area.name : stop.TimetableName;
            if (!string.IsNullOrWhiteSpace(stationLabel))
            {
                foreach (var textMesh in instance.GetComponentsInChildren<TextMeshPro>(true))
                {
                    if (!textMesh.transform.parent.name.StartsWith("Sign-Station", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    textMesh.text = stationLabel;
                    var sign = textMesh.transform.Find("Sign-Station");
                    if (sign != null)
                    {
                        var localScale = sign.localScale;
                        localScale.y = 100f;
                        sign.localScale = localScale;
                    }
                }
            }

            root.SetActive(true);
            instance.SetActive(true);
            ApplyStationRendererState(instance, id, definition.Prefab);
            ConfigureMapIcons(instance, stop, root.transform, id, definition.Prefab);
            FuseStationRuntimeIndex.Instance.Set(id, stationAgent);
            MapAPI.RefreshAttachedMapMasks(root, $"station '{id}' apply");
            FusePrefabSanitizer.SanitizeStation(root, id, stationAgent, area, stop).Log($"FUSE station '{id}'");
            FusePrefabSanitizer.ValidateStationPostBind(root, id, stationAgent, area, stop).Log($"FUSE station '{id}' post-bind");
            return stationAgent;
        }

        private static void ApplyStationRendererState(GameObject instance, string id, string prefab)
        {
            if (instance == null)
            {
                return;
            }

            var lodControlledRenderers = new HashSet<Renderer>();
            var lod0Renderers = new HashSet<Renderer>();
            var lodGroups = instance.GetComponentsInChildren<LODGroup>(true);
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
                    var renderers = lods[lodIndex].renderers;
                    for (var rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
                    {
                        var renderer = renderers[rendererIndex];
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

            var enabledCount = 0;
            var suppressedProxyCount = 0;
            var hiddenLodCount = 0;
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null)
                {
                    continue;
                }

                if (lodControlledRenderers.Contains(renderer))
                {
                    var keepVisible = lod0Renderers.Contains(renderer);
                    renderer.enabled = keepVisible;
                    renderer.forceRenderingOff = !keepVisible;
                    if (!keepVisible)
                    {
                        hiddenLodCount++;
                    }

                    continue;
                }

                // Some vanilla station clones include disabled proxy/collider shells that
                // render as plain boxes if we wake every renderer. Enable real station
                // renderers, but keep those helper shells dark.
                if (ShouldSuppressStationProxyRenderer(instance.transform, renderer, out var reason))
                {
                    renderer.enabled = false;
                    renderer.forceRenderingOff = true;
                    suppressedProxyCount++;
                    FuseLog.Info($"FUSE station '{id}' suppressed renderer '{GetRelativePath(instance.transform, renderer.transform)}' from prefab '{prefab}' reason='{reason}'.");
                }
                else
                {
                    renderer.enabled = true;
                    renderer.forceRenderingOff = false;
                    enabledCount++;
                }
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

            if (lodGroups.Length > 0 || suppressedProxyCount > 0 || hiddenLodCount > 0)
            {
                FuseLog.Info(
                    $"FUSE station '{id}' prefab '{prefab}' renderer state applied: " +
                    $"lodGroups={lodGroups.Length}, lod0={lod0Renderers.Count}, hiddenLodRenderers={hiddenLodCount}, " +
                    $"enabledRenderers={enabledCount}, suppressedProxyRenderers={suppressedProxyCount}.");
            }
        }

        private static bool ShouldSuppressStationProxyRenderer(Transform root, Renderer renderer, out string reason)
        {
            reason = string.Empty;
            if (renderer == null)
            {
                return false;
            }

            var path = GetRelativePath(root, renderer.transform);
            if (ContainsAny(path, "collider", "collision", "proxy", "physics", "navmesh", "occlusion", "bounds"))
            {
                reason = "helper-name";
                return true;
            }

            if (ContainsAny(path, "lod1", "lod2", "lod3", "lod_1", "lod_2", "lod_3", "lod 1", "lod 2", "lod 3"))
            {
                reason = "non-lod0-name";
                return true;
            }

            var hasTexture = false;
            var materialNames = string.Empty;
            var materials = renderer.sharedMaterials;
            if (materials != null)
            {
                for (var index = 0; index < materials.Length; index++)
                {
                    var material = materials[index];
                    if (material == null)
                    {
                        continue;
                    }

                    materialNames += material.name + " ";
                    if (material.mainTexture != null)
                    {
                        hasTexture = true;
                    }
                }
            }

            if (ContainsAny(materialNames, "collider", "collision", "proxy", "occlusion", "bounds"))
            {
                reason = "helper-material";
                return true;
            }

            var localCenter = root != null ? root.InverseTransformPoint(renderer.bounds.center) : renderer.bounds.center;
            var size = renderer.bounds.size;
            var largeUntypedShell =
                !hasTexture &&
                Mathf.Abs(localCenter.y) > 1f &&
                size.x > 8f &&
                size.y > 2f &&
                size.z > 8f;
            if (largeUntypedShell)
            {
                reason = $"large-untextured-shell center={localCenter} size={size}";
                return true;
            }

            return false;
        }

        private static bool ContainsAny(string value, params string[] needles)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            for (var index = 0; index < needles.Length; index++)
            {
                if (value.IndexOf(needles[index], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static string GetRelativePath(Transform root, Transform current)
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

        private static void ConfigureMapIcons(GameObject instance, PassengerStop passengerStop, Transform stationRoot, string id, string prefab)
        {
            foreach (var existing in instance.GetComponentsInChildren<MapIcon>(true))
            {
                if (existing == null)
                {
                    continue;
                }

                MapBuilder.Shared?.Remove(existing);
                existing.gameObject.SetActive(false);
            }

            var iconRotation = GetMapIconRotation(passengerStop);
            var graphicOffset = GetMapIconGraphicLocalOffset(passengerStop, stationRoot, iconRotation);
            var generated = CreateStationMapIcon(stationRoot, id, graphicOffset);
            var icons = generated != null
                ? new List<MapIcon> { generated }
                : new List<MapIcon>();
            FuseLog.Info($"FUSE station '{id}' prefab '{prefab}' using generated schematic map icon at station prefab position.");

            var mapLayer = LayerMask.NameToLayer("Map");
            if (mapLayer < 0)
            {
                FuseLog.Warning($"FUSE station '{id}' map icon could not find Unity layer 'Map'; icon may render but map clicking may fail.");
            }

            foreach (var icon in icons)
            {
                if (icon == null)
                {
                    continue;
                }

                icon.gameObject.SetActive(true);
                if (mapLayer >= 0)
                {
                    SetLayerRecursive(icon.gameObject, mapLayer);
                }

                icon.transform.SetPositionAndRotation(
                    GetMapIconWorldPosition(passengerStop, stationRoot),
                    iconRotation);
                icon.SetText(string.Empty);
                icon.OnClick = () => CameraSelector.shared.JumpTo(passengerStop);
                MapBuilder.Shared?.Add(icon);
            }
        }

        private static Quaternion GetMapIconRotation(PassengerStop passengerStop)
        {
            if (TryGetPassengerStopTrackDirection(passengerStop, out var direction))
            {
                var yaw = (Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg) + StationMapIconRotationOffsetDegrees;
                return Quaternion.Euler(-90f, yaw, 0f);
            }

            return Quaternion.Euler(-90f, StationMapIconRotationOffsetDegrees, 0f);
        }

        private static bool TryGetPassengerStopTrackDirection(PassengerStop passengerStop, out Vector3 direction)
        {
            direction = Vector3.zero;
            if (passengerStop == null)
            {
                return false;
            }

            try
            {
                var spans = passengerStop.TrackSpans;
                if (spans == null)
                {
                    return false;
                }

                foreach (var span in spans)
                {
                    if (span == null)
                    {
                        continue;
                    }

                    var points = span.GetPoints();
                    if (points == null || points.Count < 2)
                    {
                        continue;
                    }

                    var first = points.First();
                    var last = points.Last();
                    direction = WorldTransformer.GameToWorld(last) - WorldTransformer.GameToWorld(first);
                    direction.y = 0f;
                    if (direction.sqrMagnitude > 1f)
                    {
                        direction.Normalize();
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE station map icon could not calculate track-aligned rotation; using fixed rotation.", ex);
            }

            direction = Vector3.zero;
            return false;
        }

        private static Vector3 GetMapIconGraphicLocalOffset(PassengerStop passengerStop, Transform stationRoot, Quaternion iconRotation)
        {
            if (passengerStop == null || stationRoot == null)
            {
                return Vector3.zero;
            }

            try
            {
                var stopWorld = WorldTransformer.GameToWorld(passengerStop.CenterPoint);
                var awayFromTrack = stationRoot.position - stopWorld;
                awayFromTrack.y = 0f;
                if (awayFromTrack.sqrMagnitude < 1f)
                {
                    awayFromTrack = stationRoot.TransformDirection(Vector3.back);
                    awayFromTrack.y = 0f;
                }

                if (awayFromTrack.sqrMagnitude < 1f)
                {
                    return Vector3.zero;
                }

                awayFromTrack.Normalize();
                var localOffset = Quaternion.Inverse(iconRotation) * (awayFromTrack * StationMapIconGraphicOffset);
                localOffset.z = 0f;
                return localOffset;
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE station map icon could not calculate station-side graphic offset; using centered icon.", ex);
                return Vector3.zero;
            }
        }

        private static Vector3 GetMapIconWorldPosition(PassengerStop passengerStop, Transform stationRoot)
        {
            if (stationRoot != null && IsFinite(stationRoot.position))
            {
                var position = stationRoot.position;
                position.y = stationRoot.position.y + MapIconElevation;
                return position;
            }

            try
            {
                return WorldTransformer.GameToWorld(passengerStop.CenterPoint) + Vector3.up * MapIconElevation;
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE could not place station map icon from station transform; using fallback.", ex);
                return stationRoot != null
                    ? stationRoot.position + Vector3.up * MapIconElevation
                    : Vector3.up * MapIconElevation;
            }
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        private static GameObject GetStationRootObject(StationAgent agent, string id)
        {
            var stationRoot = GetStationRoot();
            var cursor = agent.transform;
            while (cursor != null && cursor.parent != stationRoot)
            {
                cursor = cursor.parent;
            }

            if (cursor != null)
            {
                return cursor.gameObject;
            }

            var namedRoot = stationRoot.Find(id);
            if (namedRoot != null)
            {
                return namedRoot.gameObject;
            }

            return agent.transform.parent != null ? agent.transform.parent.gameObject : agent.gameObject;
        }

        private static void SetLayerRecursive(GameObject gameObject, int layer)
        {
            gameObject.layer = layer;
            for (var index = 0; index < gameObject.transform.childCount; index++)
            {
                SetLayerRecursive(gameObject.transform.GetChild(index).gameObject, layer);
            }
        }

        private static MapIcon CreateStationMapIcon(Transform stationRoot, string id, Vector3 graphicOffset)
        {
            // Prefer cloning the entire MapIcon GameObject from a real
            // PassengerStop. The whole assembly — Canvas, Image material,
            // sizing, sorting, layer, sprite — is set up by the base game to
            // render correctly through the map camera. Rebuilding from scratch
            // and only borrowing the sprite produces an invisible icon because
            // the default UI material does not render through the map view.
            var iconObject = new GameObject("FUSE Station MapIcon", typeof(RectTransform));
            iconObject.transform.SetParent(stationRoot, false);
            var canvas = iconObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = MapBuilder.Shared != null ? MapBuilder.Shared.mapCamera : null;
            canvas.sortingOrder = 20;
            var icon = iconObject.AddComponent<MapIcon>();

            var rect = iconObject.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(StationMapIconWidth, StationMapIconHeight);

            CreateStationIconMesh(iconObject.transform, graphicOffset);

            AddMapIconCollider(iconObject, graphicOffset);
            iconObject.name = $"FUSE Station MapIcon - {id}";
            return icon;
        }

        /// <summary>
        /// Finds a base-game PassengerStop's MapIcon GameObject, clones it whole,
        /// reparents the clone to our station, resets transform, and returns the
        /// cloned MapIcon component. Returns null if no clonable source is found.
        /// </summary>
        private static MapIcon TryCloneStationMapIcon(Transform stationRoot, string id)
        {
            try
            {
                var source = FindStationMapIconTemplate();

                if (source == null)
                {
                    return null;
                }

                var clone = UnityEngine.Object.Instantiate(source.gameObject, stationRoot, false);
                clone.transform.localPosition = Vector3.zero;
                clone.transform.localRotation = Quaternion.identity;
                clone.transform.localScale = Vector3.one;
                clone.name = $"FUSE Station MapIcon - {id}";
                clone.SetActive(true);

                var clonedIcon = clone.GetComponent<MapIcon>();
                if (clonedIcon == null)
                {
                    clonedIcon = clone.AddComponent<MapIcon>();
                }

                return clonedIcon;
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    $"FUSE station map icon: failed to clone MapIcon for '{id}': {ex.Message}. " +
                    "Falling back to procedural construction.");
                return null;
            }
        }

        private static MapIcon FindStationMapIconTemplate()
        {
            foreach (var stop in UnityEngine.Object.FindObjectsOfType<PassengerStop>(true))
            {
                var icon = FindCloneableMapIconUnder(stop != null ? stop.gameObject : null);
                if (icon != null)
                {
                    return icon;
                }
            }

            foreach (var agent in UnityEngine.Object.FindObjectsOfType<StationAgent>(true))
            {
                var icon = FindCloneableMapIconUnder(agent != null ? agent.gameObject : null);
                if (icon != null)
                {
                    return icon;
                }
            }

            MapIcon best = null;
            var bestScore = 0;
            var seen = new HashSet<MapIcon>();
            foreach (var icon in UnityEngine.Object.FindObjectsOfType<MapIcon>(true).Concat(Resources.FindObjectsOfTypeAll<MapIcon>()))
            {
                if (icon == null || !seen.Add(icon))
                {
                    continue;
                }

                var score = ScoreStationMapIconTemplate(icon);
                if (score > bestScore)
                {
                    best = icon;
                    bestScore = score;
                }
            }

            return best;
        }

        private static MapIcon FindCloneableMapIconUnder(GameObject host)
        {
            if (host == null)
            {
                return null;
            }

            foreach (var icon in host.GetComponentsInChildren<MapIcon>(true))
            {
                if (IsCloneableStationMapIcon(icon))
                {
                    return icon;
                }
            }

            return null;
        }

        private static int ScoreStationMapIconTemplate(MapIcon icon)
        {
            if (!IsCloneableStationMapIcon(icon))
            {
                return 0;
            }

            var hierarchyName = GetHierarchyName(icon.transform);
            var sprite = ExtractSpriteFromMapIcon(icon);
            var spriteName = sprite != null ? sprite.name ?? string.Empty : string.Empty;
            var score = 0;

            if (LooksLikeStationIconName(hierarchyName))
            {
                score += 100;
            }

            if (LooksLikeStationIconName(spriteName))
            {
                score += 60;
            }

            if (hierarchyName.IndexOf("MapIcon", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                score += 5;
            }

            if (icon.GetComponentInChildren<Image>(true) != null)
            {
                score += 5;
            }

            return score >= 60 ? score : 0;
        }

        private static bool IsCloneableStationMapIcon(MapIcon icon)
        {
            if (icon == null || icon.gameObject == null)
            {
                return false;
            }

            var hierarchyName = GetHierarchyName(icon.transform);
            return !hierarchyName.Contains("FUSE Station MapIcon") &&
                   !LooksLikeNonStationIconName(hierarchyName);
        }

        private static string GetHierarchyName(Transform transform)
        {
            var names = new List<string>();
            var cursor = transform;
            while (cursor != null)
            {
                names.Add(cursor.name ?? string.Empty);
                cursor = cursor.parent;
            }

            names.Reverse();
            return string.Join("/", names.ToArray());
        }

        private static bool LooksLikeNonStationIconName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            return name.IndexOf("Character", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("Avatar", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("Locomotive", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("Loco", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("Player", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("Switch", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("Speed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("MPH", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("Industry", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void CreateStationIconMesh(Transform parent, Vector3 graphicOffset)
        {
            var meshObject = new GameObject("Station Icon Mesh", typeof(MeshFilter), typeof(MeshRenderer));
            meshObject.transform.SetParent(parent, false);
            meshObject.transform.localPosition = graphicOffset;
            meshObject.transform.localRotation = Quaternion.identity;
            meshObject.transform.localScale = Vector3.one;

            var filter = meshObject.GetComponent<MeshFilter>();
            filter.sharedMesh = BuildStationIconMesh();

            var renderer = meshObject.GetComponent<MeshRenderer>();
            var material = GetStationIconMaterial();
            if (material != null)
            {
                renderer.sharedMaterial = material;
            }

            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private static Mesh BuildStationIconMesh()
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            const float lineWidth = 1.8f;
            var halfWidth = StationMapIconWidth * 0.5f;
            var halfHeight = StationMapIconHeight * 0.5f;

            var topLeft = new Vector2(-halfWidth, halfHeight);
            var topRight = new Vector2(halfWidth, halfHeight);
            var bottomRight = new Vector2(halfWidth, -halfHeight);
            var bottomLeft = new Vector2(-halfWidth, -halfHeight);
            var center = Vector2.zero;

            AddLineQuad(vertices, triangles, topLeft, topRight, lineWidth);
            AddLineQuad(vertices, triangles, topRight, bottomRight, lineWidth);
            AddLineQuad(vertices, triangles, bottomRight, bottomLeft, lineWidth);
            AddLineQuad(vertices, triangles, bottomLeft, topLeft, lineWidth);
            AddLineQuad(vertices, triangles, topLeft, center, lineWidth);
            AddLineQuad(vertices, triangles, center, topRight, lineWidth);
            AddLineQuad(vertices, triangles, bottomLeft, center, lineWidth);
            AddLineQuad(vertices, triangles, center, bottomRight, lineWidth);

            var mesh = new Mesh
            {
                name = "FUSE Station MapIcon Mesh"
            };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void AddLineQuad(List<Vector3> vertices, List<int> triangles, Vector2 start, Vector2 end, float width)
        {
            var delta = end - start;
            if (delta.sqrMagnitude < 0.001f)
            {
                return;
            }

            var normal = new Vector2(-delta.y, delta.x).normalized * width * 0.5f;
            var index = vertices.Count;
            vertices.Add(new Vector3(start.x + normal.x, start.y + normal.y, 0f));
            vertices.Add(new Vector3(start.x - normal.x, start.y - normal.y, 0f));
            vertices.Add(new Vector3(end.x - normal.x, end.y - normal.y, 0f));
            vertices.Add(new Vector3(end.x + normal.x, end.y + normal.y, 0f));
            triangles.Add(index);
            triangles.Add(index + 1);
            triangles.Add(index + 2);
            triangles.Add(index);
            triangles.Add(index + 2);
            triangles.Add(index + 3);
        }

        private static Material GetStationIconMaterial()
        {
            if (_stationIconMaterial != null)
            {
                return _stationIconMaterial;
            }

            var shader = Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
            if (shader == null)
            {
                FuseLog.Warning("FUSE station map icon: no compatible unlit shader was found; generated station icon may not render.");
                return null;
            }

            _stationIconMaterial = new Material(shader)
            {
                name = "FUSE Station MapIcon Material",
                hideFlags = HideFlags.DontSave,
                color = Color.white,
                renderQueue = 3000
            };

            if (_stationIconMaterial.HasProperty("_Color"))
            {
                _stationIconMaterial.SetColor("_Color", Color.white);
            }

            if (_stationIconMaterial.HasProperty("_Cull"))
            {
                _stationIconMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            }

            return _stationIconMaterial;
        }

        private static void AddMapIconCollider(GameObject iconObject, Vector3 graphicOffset)
        {
            var boxColliderType = Type.GetType("UnityEngine.BoxCollider, UnityEngine.PhysicsModule");
            if (boxColliderType == null)
            {
                FuseLog.Warning("FUSE station map icon click collider could not be created because UnityEngine.PhysicsModule was not available.");
                return;
            }

            var collider = iconObject.AddComponent(boxColliderType);
            boxColliderType.GetProperty("size")?.SetValue(collider, new Vector3(StationMapIconWidth, StationMapIconHeight, 2f), null);
            boxColliderType.GetProperty("center")?.SetValue(collider, graphicOffset, null);
        }

        private static Sprite GetStationIconSprite()
        {
            if (_stationIconSprite != null)
            {
                return _stationIconSprite;
            }

            // Prefer the game's built-in passenger-station map icon over FUSE's
            // procedural fallback. We locate any existing MapIcon in the scene
            // (skipping ones we created ourselves), lift the sprite off its child
            // Image, cache it, and reuse it. Stations FUSE adds will then render
            // visually identical to base-game stations.
            var found = TryFindGameStationIconSprite();
            if (found != null)
            {
                _stationIconSprite = found;
                FuseLog.Info(
                    $"FUSE station icon: bound to existing game sprite='{found.name ?? string.Empty}' " +
                    "operation='station map icon' message='reusing base-game MapIcon sprite'.");
                return _stationIconSprite;
            }

            FuseLog.Warning(
                "FUSE station icon: no base-game MapIcon sprite found at runtime; " +
                "falling back to FUSE procedural sprite. Stations may not visually match base-game icons.");

            const int size = 64;
            const float center = (size - 1) * 0.5f;
            const float outerRadius = 30f;
            const float innerRadius = 24f;
            var texture = new Texture2D(size, size, TextureFormat.ARGB32, false)
            {
                name = "FUSE Station Icon",
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

                    var pixel = new Color32(0, 0, 0, 0);
                    if (ringAlpha > 0.01f)
                    {
                        pixel = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(ringAlpha * 255f));
                    }
                    else if (fillAlpha > 0.01f)
                    {
                        pixel = new Color32(0, 0, 0, (byte)Mathf.RoundToInt(fillAlpha * 230f));
                    }

                    if (IsStationGlyphPixel(x, y))
                    {
                        pixel = new Color32(255, 255, 255, 255);
                    }

                    pixels[(y * size) + x] = pixel;
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            _stationIconSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
            _stationIconSprite.name = "FUSE Station Icon";
            return _stationIconSprite;
        }

        /// <summary>
        /// Finds the sprite the game uses for passenger-station map icons.
        /// We have to be selective: Resources.FindObjectsOfTypeAll&lt;MapIcon&gt;
        /// returns icons for characters, industries, signals, etc. — picking
        /// the first one gives us, e.g., MapCharacterIcon (a conductor head)
        /// instead of a station marker.
        ///
        /// Lookup order:
        ///   1. MapIcon nested under a PassengerStop in the active scene.
        ///   2. MapIcon under a StationAgent (rare, but covers some prefabs).
        ///   3. Any MapIcon whose owning GameObject name suggests a station.
        ///   4. null → caller falls back to FUSE's procedural sprite.
        /// </summary>
        private static Sprite TryFindGameStationIconSprite()
        {
            try
            {
                var stops = UnityEngine.Object.FindObjectsOfType<PassengerStop>(true);
                foreach (var stop in stops)
                {
                    var sprite = ExtractStationSpriteFromHost(stop != null ? stop.gameObject : null);
                    if (sprite != null)
                    {
                        return sprite;
                    }
                }

                var agents = UnityEngine.Object.FindObjectsOfType<StationAgent>(true);
                foreach (var agent in agents)
                {
                    var sprite = ExtractStationSpriteFromHost(agent != null ? agent.gameObject : null);
                    if (sprite != null)
                    {
                        return sprite;
                    }
                }

                // Final pass over every MapIcon, ranked by name. Avoids picking
                // up MapCharacterIcon and similar non-station icons.
                var icons = Resources.FindObjectsOfTypeAll<MapIcon>();
                MapIcon nameMatch = null;
                foreach (var icon in icons)
                {
                    if (icon == null || icon.gameObject == null)
                    {
                        continue;
                    }

                    var name = icon.gameObject.name ?? string.Empty;
                    if (name.StartsWith("FUSE Station MapIcon", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (LooksLikeStationIconName(name) || LooksLikeStationIconName(icon.transform.parent != null ? icon.transform.parent.name : string.Empty))
                    {
                        var sprite = ExtractSpriteFromMapIcon(icon);
                        if (sprite != null)
                        {
                            nameMatch = icon;
                            break;
                        }
                    }
                }

                return nameMatch != null ? ExtractSpriteFromMapIcon(nameMatch) : null;
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    $"FUSE station icon: failed to scan for an existing MapIcon sprite: {ex.Message}. " +
                    "Falling back to procedural sprite.");
                return null;
            }
        }

        private static Sprite ExtractStationSpriteFromHost(GameObject host)
        {
            if (host == null)
            {
                return null;
            }

            var icons = host.GetComponentsInChildren<MapIcon>(true);
            foreach (var icon in icons)
            {
                if (icon == null || icon.gameObject == null)
                {
                    continue;
                }

                var name = icon.gameObject.name ?? string.Empty;
                if (name.StartsWith("FUSE Station MapIcon", StringComparison.Ordinal))
                {
                    continue;
                }

                var sprite = ExtractSpriteFromMapIcon(icon);
                if (sprite != null)
                {
                    return sprite;
                }
            }

            return null;
        }

        private static bool LooksLikeStationIconName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            return name.IndexOf("Station", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("Depot", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("PassengerStop", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static Sprite ExtractSpriteFromMapIcon(MapIcon icon)
        {
            if (icon == null)
            {
                return null;
            }

            var image = icon.GetComponentInChildren<Image>(true);
            return image != null && image.sprite != null ? image.sprite : null;
        }

        private static bool IsStationGlyphPixel(int x, int y)
        {
            if (y >= 28 && y <= 40 && x >= 28 && x <= 36)
            {
                return false;
            }

            if (y >= 22 && y <= 40 && x >= 20 && x <= 44)
            {
                return true;
            }

            if (y >= 18 && y <= 22 && x >= 17 && x <= 47)
            {
                var centerOffset = Mathf.Abs(x - 32);
                return centerOffset <= y - 14;
            }

            if (y >= 40 && y <= 44 && x >= 18 && x <= 46)
            {
                return true;
            }

            return false;
        }

        private static StationAgent RequireStationAgent(string id)
        {
            var agent = GetStationAgent(id);
            if (agent == null)
            {
                throw new InvalidOperationException($"Station agent '{id}' was not found.");
            }

            return agent;
        }

        private static Transform GetStationRoot()
        {
            var world = GameObject.Find("World");
            if (world != null)
            {
                var existing = world.transform.Find("StationAgents");
                if (existing != null)
                {
                    return existing;
                }

                var stationRoot = new GameObject("StationAgents");
                stationRoot.transform.SetParent(world.transform, false);
                return stationRoot.transform;
            }

            if (_fallbackRoot == null)
            {
                _fallbackRoot = new GameObject("StationAgents").transform;
                UnityEngine.Object.DontDestroyOnLoad(_fallbackRoot.gameObject);
            }

            return _fallbackRoot;
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
