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
                FuseLog.Exception($"FUSE bridge rail mesh creation failed, using simple rail boxes", ex);
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
    }
}
