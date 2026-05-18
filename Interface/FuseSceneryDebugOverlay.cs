using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FUSE.API;
using FUSE.Cache;
using FUSE.Data;
using FUSE.Infrastructure;
using FUSE.Registry;
using Helpers;
using Model.Definition.Data;
using Track;
using UnityEngine;

namespace FUSE.Interface
{
    internal sealed class FuseSceneryDebugOverlay : MonoBehaviour
    {
        private const float PickScreenSlackPixels = 4f;
        private const float PickScreenFallbackRadiusPixels = 16f;
        private const float RefreshInterval = 0.08f;
        private const int MaxSuppressorsToShow = 8;
        private const int MaxComponentsToShow = 12;

        private static GameObject _host;

        private GUIStyle _boxStyle;
        private GUIStyle _labelStyle;
        private Texture2D _backgroundTexture;
        private float _nextRefreshAt;
        private string _cachedText;
        private bool _hasHover;
        private Vector2 _lastMouseGui;

        public static void Ensure()
        {
            if (_host != null)
            {
                return;
            }

            _host = new GameObject("FUSE Scenery Debug Overlay");
            DontDestroyOnLoad(_host);
            _host.hideFlags = HideFlags.HideAndDontSave;
            _host.AddComponent<FuseSceneryDebugOverlay>();
            FuseLog.Info("FUSE scenery debug overlay initialized.");
        }

        public static void Shutdown()
        {
            if (_host != null)
            {
                Destroy(_host);
                _host = null;
            }
        }

        private void OnGUI()
        {
            if (!FuseSettings.ShowSceneryDebugOverlay)
            {
                _hasHover = false;
                _cachedText = null;
                return;
            }

            var evt = Event.current;
            if (evt == null)
            {
                return;
            }

            if (evt.type == EventType.MouseMove || evt.type == EventType.Layout || evt.type == EventType.Repaint)
            {
                _lastMouseGui = evt.mousePosition;
            }

            if (evt.type == EventType.Layout)
            {
                MaybeRefresh();
            }

            if (evt.type != EventType.Repaint || !_hasHover || string.IsNullOrEmpty(_cachedText))
            {
                return;
            }

            EnsureGuiStyles();

            var screenX = _lastMouseGui.x + 18f;
            var screenY = _lastMouseGui.y + 18f;

            var content = new GUIContent(_cachedText);
            var size = _labelStyle.CalcSize(content);
            var width = Mathf.Min(Mathf.Max(300f, size.x + 16f), Screen.width - 32f);
            var height = Mathf.Min(_labelStyle.CalcHeight(content, width - 16f) + 16f, Screen.height - 32f);

            if (screenX + width > Screen.width - 8f)
            {
                screenX = Mathf.Max(8f, Screen.width - width - 8f);
            }

            if (screenY + height > Screen.height - 8f)
            {
                screenY = Mathf.Max(8f, Screen.height - height - 8f);
            }

            var boxRect = new Rect(screenX, screenY, width, height);
            GUI.Box(boxRect, GUIContent.none, _boxStyle);
            GUI.Label(new Rect(screenX + 8f, screenY + 8f, width - 16f, height - 16f), content, _labelStyle);
        }

        private void OnDestroy()
        {
            if (_backgroundTexture != null)
            {
                Destroy(_backgroundTexture);
                _backgroundTexture = null;
            }
        }

        private void MaybeRefresh()
        {
            if (Time.unscaledTime < _nextRefreshAt)
            {
                return;
            }

            _nextRefreshAt = Time.unscaledTime + RefreshInterval;

            try
            {
                var mouseScreen = GuiToScreen(_lastMouseGui);
                var hit = FindObjectNearCursor(mouseScreen);
                if (hit == null)
                {
                    _hasHover = false;
                    _cachedText = null;
                    return;
                }

                _cachedText = BuildReport(hit);
                _hasHover = !string.IsNullOrEmpty(_cachedText);
            }
            catch (Exception ex)
            {
                _hasHover = false;
                _cachedText = null;
                FuseLog.Warning($"FUSE scenery debug overlay refresh failed: {ex.GetBaseException().Message}");
            }
        }

        private static Vector2 GuiToScreen(Vector2 guiPosition)
        {
            return new Vector2(guiPosition.x, Screen.height - guiPosition.y);
        }

        private static GameObject FindObjectNearCursor(Vector2 mouseScreen)
        {
            var camera = Camera.main ?? Camera.current;
            if (camera == null)
            {
                return null;
            }

            if (mouseScreen.x < 0f || mouseScreen.y < 0f ||
                mouseScreen.x > Screen.width || mouseScreen.y > Screen.height)
            {
                return null;
            }

            return FindByRendererBounds(camera, mouseScreen);
        }

        private static GameObject FindByRendererBounds(Camera camera, Vector2 mouseScreen)
        {
            GameObject bestInside = null;
            var bestInsideDepth = float.MaxValue;
            var bestInsideArea = float.MaxValue;

            GameObject bestNear = null;
            var bestNearDistance = PickScreenFallbackRadiusPixels;
            var bestNearDepth = float.MaxValue;

            void Consider(GameObject root)
            {
                if (root == null)
                {
                    return;
                }

                var renderers = root.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0)
                {
                    return;
                }

                if (!TryAggregateBounds(renderers, out var bounds))
                {
                    return;
                }

                if (!TryProjectBoundsToScreen(camera, bounds, out var screenMin, out var screenMax, out var avgDepth))
                {
                    return;
                }

                var insideX = mouseScreen.x >= screenMin.x - PickScreenSlackPixels &&
                              mouseScreen.x <= screenMax.x + PickScreenSlackPixels;
                var insideY = mouseScreen.y >= screenMin.y - PickScreenSlackPixels &&
                              mouseScreen.y <= screenMax.y + PickScreenSlackPixels;

                if (insideX && insideY)
                {
                    var area = Mathf.Max(1f, (screenMax.x - screenMin.x) * (screenMax.y - screenMin.y));
                    if (avgDepth < bestInsideDepth - 0.5f ||
                        (Mathf.Abs(avgDepth - bestInsideDepth) < 0.5f && area < bestInsideArea))
                    {
                        bestInside = root;
                        bestInsideDepth = avgDepth;
                        bestInsideArea = area;
                    }

                    return;
                }

                if (bestInside != null)
                {
                    // Already have an inside-AABB hit; skip the near-edge fallback to avoid noise.
                    return;
                }

                var clampedX = Mathf.Clamp(mouseScreen.x, screenMin.x, screenMax.x);
                var clampedY = Mathf.Clamp(mouseScreen.y, screenMin.y, screenMax.y);
                var distance = Vector2.Distance(mouseScreen, new Vector2(clampedX, clampedY));
                if (distance > bestNearDistance)
                {
                    return;
                }

                if (distance < bestNearDistance - 0.5f || avgDepth < bestNearDepth)
                {
                    bestNear = root;
                    bestNearDistance = distance;
                    bestNearDepth = avgDepth;
                }
            }

            try
            {
                foreach (var clone in SceneCloneAPI.GetAllSceneClones())
                {
                    Consider(clone);
                }
            }
            catch
            {
                // Ignore enumeration failures so the fallback can still consider scenery.
            }

            try
            {
                foreach (var scenery in SceneryAPI.GetAllScenery())
                {
                    if (scenery != null)
                    {
                        Consider(scenery.gameObject);
                    }
                }
            }
            catch
            {
                // Ignore enumeration failures; no hit is better than a noisy one.
            }

            return bestInside ?? bestNear;
        }

        private static bool TryProjectBoundsToScreen(
            Camera camera,
            Bounds bounds,
            out Vector2 screenMin,
            out Vector2 screenMax,
            out float avgDepth)
        {
            screenMin = default;
            screenMax = default;
            avgDepth = float.MaxValue;

            var min = bounds.min;
            var max = bounds.max;

            var minX = float.MaxValue;
            var minY = float.MaxValue;
            var maxX = float.MinValue;
            var maxY = float.MinValue;
            var depthTotal = 0f;
            var depthCount = 0;

            for (var index = 0; index < 8; index++)
            {
                var corner = new Vector3(
                    (index & 1) == 0 ? min.x : max.x,
                    (index & 2) == 0 ? min.y : max.y,
                    (index & 4) == 0 ? min.z : max.z);

                var projected = camera.WorldToScreenPoint(corner);
                if (projected.z <= 0f)
                {
                    continue;
                }

                if (projected.x < minX)
                {
                    minX = projected.x;
                }

                if (projected.y < minY)
                {
                    minY = projected.y;
                }

                if (projected.x > maxX)
                {
                    maxX = projected.x;
                }

                if (projected.y > maxY)
                {
                    maxY = projected.y;
                }

                depthTotal += projected.z;
                depthCount++;
            }

            // Require at least half the corners to be in front of the camera. Fewer than that
            // means the object straddles the near plane and the projected AABB would be misleading.
            if (depthCount < 4)
            {
                return false;
            }

            screenMin = new Vector2(minX, minY);
            screenMax = new Vector2(maxX, maxY);
            avgDepth = depthTotal / depthCount;
            return true;
        }

        private static bool TryAggregateBounds(IReadOnlyList<Renderer> renderers, out Bounds bounds)
        {
            bounds = default;
            var found = false;
            for (var index = 0; index < renderers.Count; index++)
            {
                var renderer = renderers[index];
                if (renderer == null)
                {
                    continue;
                }

                if (!found)
                {
                    bounds = renderer.bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return found;
        }

        private static string BuildReport(GameObject hit)
        {
            var classification = Classify(hit);
            var builder = new StringBuilder();
            switch (classification.Kind)
            {
                case HitKind.FuseScenery:
                    BuildFuseSceneryReport(builder, classification);
                    break;
                case HitKind.SceneClone:
                    BuildSceneCloneReport(builder, classification);
                    break;
                case HitKind.VanillaScenery:
                    BuildVanillaSceneryReport(builder, classification);
                    break;
                default:
                    BuildSceneObjectReport(builder, classification);
                    break;
            }

            AppendScenePathSuppression(builder, classification.ScenePath);
            AppendProgressionImpacts(builder, classification);

            if (FuseSettings.ShowSceneryDebugAdvanced)
            {
                AppendAdvancedDetails(builder, classification);
            }

            return builder.ToString().TrimEnd();
        }

        private static void AppendProgressionImpacts(StringBuilder builder, HitInfo info)
        {
            var leafName = info.Root != null ? info.Root.name : null;
            var fuseId = info.Scenery != null
                ? info.Scenery.name
                : info.SceneCloneId;

            List<FuseProgressionImpactLookup.Impact> impacts;
            try
            {
                impacts = FuseProgressionImpactLookup.FindForGameObject(info.ScenePath, leafName, fuseId);
            }
            catch
            {
                return;
            }

            if (impacts == null || impacts.Count == 0)
            {
                return;
            }

            builder.AppendLine();
            builder.Append("<b>Progressions impacting</b> (").Append(impacts.Count).AppendLine(")");
            var shown = 0;
            const int MaxToShow = 8;
            foreach (var impact in impacts)
            {
                if (shown++ >= MaxToShow)
                {
                    builder.Append("  + ").Append(impacts.Count - MaxToShow).AppendLine(" more");
                    break;
                }

                builder.Append("  - ")
                    .Append(impact.SourceKind)
                    .Append(" '").Append(SafeId(impact.SourceId)).Append("'  ")
                    .Append(impact.Effect);
                if (!string.IsNullOrWhiteSpace(impact.State))
                {
                    builder.Append(" [").Append(impact.State).Append("]");
                }

                if (!string.IsNullOrWhiteSpace(impact.Target))
                {
                    builder.Append(" -> ").Append(impact.Target);
                }

                builder.AppendLine();
            }
        }

        private static HitInfo Classify(GameObject hit)
        {
            var info = new HitInfo
            {
                Leaf = hit,
                Root = hit
            };

            // Walk up to find a meaningful root (SceneryAssetInstance, FuseSceneCloneMarker, or stop at root)
            var cursor = hit != null ? hit.transform : null;
            while (cursor != null)
            {
                var scenery = cursor.GetComponent<SceneryAssetInstance>();
                if (scenery != null)
                {
                    info.Kind = HitKind.VanillaScenery;
                    info.Root = cursor.gameObject;
                    info.Scenery = scenery;
                    if (FuseSceneryRuntimeIndex.Instance.TryGetValue(scenery.name, out _))
                    {
                        info.Kind = HitKind.FuseScenery;
                    }

                    info.ScenePath = GetTransformPath(cursor);
                    return info;
                }

                if (SceneCloneAPI.TryGetSceneCloneInfo(cursor.gameObject, out var cloneId, out var targetPath))
                {
                    info.Kind = HitKind.SceneClone;
                    info.Root = cursor.gameObject;
                    info.SceneCloneId = cloneId;
                    info.SceneCloneTargetPath = targetPath;
                    info.ScenePath = !string.IsNullOrWhiteSpace(targetPath) ? targetPath : GetTransformPath(cursor);
                    return info;
                }

                cursor = cursor.parent;
            }

            info.Kind = HitKind.SceneObject;
            info.ScenePath = hit != null ? GetTransformPath(hit.transform) : "<null>";
            return info;
        }

        private static void BuildFuseSceneryReport(StringBuilder builder, HitInfo info)
        {
            builder.AppendLine("<b>FUSE Scenery</b>");
            var scenery = info.Scenery;
            var id = scenery != null ? scenery.name : null;
            builder.Append("id: ").AppendLine(SafeId(id));
            builder.Append("asset: ").AppendLine(SafeId(scenery != null ? scenery.identifier : null));

            var owner = TryGetOwner(FuseClaimKind.Scenery, id);
            if (!string.IsNullOrWhiteSpace(owner))
            {
                builder.Append("owner: ").AppendLine(owner);
            }

            AppendSource(builder, FusePackageSourceLookup.ItemKind.Scenery, id);

            FuseScenery definition = null;
            try
            {
                definition = SceneryAPI.GetDefinition(scenery);
            }
            catch
            {
                definition = null;
            }

            AppendTransformBlock(builder, scenery != null ? scenery.transform : info.Root.transform);

            if (definition?.AnchorSpanIds != null && definition.AnchorSpanIds.Length > 0)
            {
                builder.Append("anchorSpans: ").AppendLine(string.Join(", ", definition.AnchorSpanIds));
            }

            if (!string.IsNullOrWhiteSpace(definition?.Model) &&
                !string.Equals(definition.Model, scenery?.identifier, StringComparison.Ordinal))
            {
                builder.Append("modelHint: ").AppendLine(definition.Model);
            }

            AppendScenePath(builder, info.ScenePath);
            AppendActiveLine(builder, info.Root);
        }

        private static void BuildVanillaSceneryReport(StringBuilder builder, HitInfo info)
        {
            builder.AppendLine("<b>Scenery</b> (vanilla)");
            var scenery = info.Scenery;
            builder.Append("name: ").AppendLine(SafeId(scenery != null ? scenery.name : info.Root.name));
            builder.Append("asset: ").AppendLine(SafeId(scenery != null ? scenery.identifier : null));
            AppendTransformBlock(builder, scenery != null ? scenery.transform : info.Root.transform);
            AppendScenePath(builder, info.ScenePath);
            AppendActiveLine(builder, info.Root);
        }

        private static void BuildSceneCloneReport(StringBuilder builder, HitInfo info)
        {
            builder.AppendLine("<b>FUSE Scene Clone</b>");
            builder.Append("id: ").AppendLine(SafeId(info.SceneCloneId));
            builder.Append("target: ").AppendLine(SafeId(info.SceneCloneTargetPath));
            AppendSource(builder, FusePackageSourceLookup.ItemKind.SceneClone, info.SceneCloneId);

            // Read the original package definition from the cache directly. SceneCloneAPI.GetDefinition
            // overwrites Enabled with the live activeSelf, which hides authoring/runtime mismatches.
            FuseSceneClone cached = null;
            try
            {
                FuseRuntimeDefinitionCache.TryGet(FuseDefinitionKind.SceneClone, info.SceneCloneId, out cached);
            }
            catch
            {
                cached = null;
            }

            if (cached != null)
            {
                if (!string.IsNullOrWhiteSpace(cached.Source))
                {
                    builder.Append("source: ").AppendLine(cached.Source);
                }

                if (cached.Enabled.HasValue)
                {
                    builder.Append("package.enabled: ").AppendLine(cached.Enabled.Value ? "true" : "false");
                }
                else
                {
                    builder.AppendLine("package.enabled: <unset>");
                }
            }

            AppendTransformBlock(builder, info.Root.transform);
            AppendActiveLine(builder, info.Root);
        }

        private static void BuildSceneObjectReport(StringBuilder builder, HitInfo info)
        {
            builder.AppendLine("<b>Scene Object</b>");
            builder.Append("name: ").AppendLine(SafeId(info.Root != null ? info.Root.name : null));
            AppendTransformBlock(builder, info.Root != null ? info.Root.transform : null);
            AppendScenePath(builder, info.ScenePath);
            AppendActiveLine(builder, info.Root);
        }

        private static void AppendTransformBlock(StringBuilder builder, Transform transform)
        {
            if (transform == null)
            {
                return;
            }

            var world = transform.position;
            builder.Append("worldPos: (")
                .Append(world.x.ToString("0.0")).Append(", ")
                .Append(world.y.ToString("0.0")).Append(", ")
                .Append(world.z.ToString("0.0")).AppendLine(")");

            var rotation = transform.eulerAngles;
            if (rotation.sqrMagnitude > 0.0001f)
            {
                builder.Append("rotation: (")
                    .Append(rotation.x.ToString("0.0")).Append(", ")
                    .Append(rotation.y.ToString("0.0")).Append(", ")
                    .Append(rotation.z.ToString("0.0")).AppendLine(")");
            }

            var scale = transform.localScale;
            if (scale != Vector3.one && scale.sqrMagnitude > 0.0001f)
            {
                builder.Append("scale: (")
                    .Append(scale.x.ToString("0.000")).Append(", ")
                    .Append(scale.y.ToString("0.000")).Append(", ")
                    .Append(scale.z.ToString("0.000")).AppendLine(")");
            }
        }

        private static void AppendScenePath(StringBuilder builder, string scenePath)
        {
            if (string.IsNullOrWhiteSpace(scenePath))
            {
                return;
            }

            builder.Append("scenePath: ").AppendLine(scenePath);
        }

        private static void AppendActiveLine(StringBuilder builder, GameObject root)
        {
            if (root == null)
            {
                return;
            }

            builder.Append("active: ")
                .Append(root.activeSelf ? "self=true" : "self=false")
                .Append(", inHierarchy=")
                .AppendLine(root.activeInHierarchy ? "true" : "false");
        }

        private static void AppendScenePathSuppression(StringBuilder builder, string scenePath)
        {
            if (string.IsNullOrWhiteSpace(scenePath))
            {
                return;
            }

            IReadOnlyCollection<string> suppressors;
            try
            {
                suppressors = FuseRegistry.GetSharedOwners(FuseClaimKind.SuppressedScenePath, scenePath);
            }
            catch
            {
                return;
            }

            if (suppressors == null || suppressors.Count == 0)
            {
                return;
            }

            builder.AppendLine();
            builder.Append("<b>ScenePath suppressed by</b> (").Append(suppressors.Count).AppendLine(")");
            var shown = 0;
            foreach (var packageId in suppressors)
            {
                if (shown++ >= MaxSuppressorsToShow)
                {
                    builder.Append("  + ").Append(suppressors.Count - MaxSuppressorsToShow).AppendLine(" more");
                    break;
                }

                builder.Append("  - ").AppendLine(SafeId(packageId));
            }
        }

        private static void AppendAdvancedDetails(StringBuilder builder, HitInfo info)
        {
            var root = info.Root;
            if (root == null)
            {
                return;
            }

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            var lodGroups = root.GetComponentsInChildren<LODGroup>(true);
            var enabledRenderers = 0;
            for (var index = 0; index < renderers.Length; index++)
            {
                if (renderers[index] != null && renderers[index].enabled)
                {
                    enabledRenderers++;
                }
            }

            builder.AppendLine();
            builder.Append("<b>Renderers</b>: ").Append(renderers.Length)
                .Append(" total, ").Append(enabledRenderers).Append(" enabled");
            if (lodGroups.Length > 0)
            {
                builder.Append(", ").Append(lodGroups.Length).Append(" LOD group(s)");
            }

            builder.AppendLine();

            var components = root.GetComponents<Component>();
            if (components.Length > 1)
            {
                builder.Append("<b>Components</b> (").Append(components.Length - 1).AppendLine(")");
                var shown = 0;
                for (var index = 0; index < components.Length; index++)
                {
                    var component = components[index];
                    if (component == null || component is Transform)
                    {
                        continue;
                    }

                    if (shown++ >= MaxComponentsToShow)
                    {
                        builder.Append("  + more").AppendLine();
                        break;
                    }

                    builder.Append("  - ").AppendLine(component.GetType().Name);
                }
            }

            if (info.Leaf != null && info.Leaf != info.Root)
            {
                builder.Append("hitLeaf: ").AppendLine(GetTransformPath(info.Leaf.transform));
            }
        }

        private static void AppendSource(StringBuilder builder, FusePackageSourceLookup.ItemKind kind, string id)
        {
            FusePackageSourceLookup.Source source;
            try
            {
                if (!FusePackageSourceLookup.TryGetSource(kind, id, out source))
                {
                    return;
                }
            }
            catch
            {
                return;
            }

            builder.Append("source: ").AppendLine(source.Display);
        }

        private static string TryGetOwner(FuseClaimKind kind, string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            try
            {
                return FuseRegistry.GetExclusiveOwner(kind, id);
            }
            catch
            {
                return null;
            }
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

        private static string SafeId(string id)
        {
            return string.IsNullOrWhiteSpace(id) ? "<none>" : id;
        }

        private void EnsureGuiStyles()
        {
            if (_boxStyle != null && _labelStyle != null)
            {
                return;
            }

            if (_backgroundTexture == null)
            {
                _backgroundTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                var pixels = new Color[4];
                for (var index = 0; index < pixels.Length; index++)
                {
                    pixels[index] = new Color(0f, 0f, 0f, 0.78f);
                }

                _backgroundTexture.SetPixels(pixels);
                _backgroundTexture.Apply();
            }

            _boxStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = _backgroundTexture, textColor = Color.white },
                border = new RectOffset(2, 2, 2, 2),
                padding = new RectOffset(8, 8, 8, 8)
            };

            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                richText = true,
                alignment = TextAnchor.UpperLeft,
                wordWrap = true,
                fontSize = 12,
                normal = { textColor = Color.white }
            };
        }

        private enum HitKind
        {
            SceneObject,
            FuseScenery,
            VanillaScenery,
            SceneClone
        }

        private struct HitInfo
        {
            public HitKind Kind;
            public GameObject Leaf;
            public GameObject Root;
            public SceneryAssetInstance Scenery;
            public string SceneCloneId;
            public string SceneCloneTargetPath;
            public string ScenePath;
        }
    }
}
