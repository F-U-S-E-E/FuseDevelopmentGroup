using System;
using System.Collections.Generic;
using System.Linq;
using Helpers;
using KeyValue.Runtime;
using FUSE.Authoring.Data;
using FUSE.Infrastructure;
using UnityEngine;

namespace FUSE.Runtime.API
{
    public static class SceneCloneAPI
    {
        private static readonly char[] PathSeparators = { '/' };

        public static GameObject AddSceneClone(string id, FuseSceneClone definition)
        {
            RequireId(id, nameof(id));
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (GetSceneClone(id) != null)
            {
                throw new InvalidOperationException($"Scene clone '{id}' already exists.");
            }

            return ApplyDefinition(id, definition);
        }

        public static void UpdateSceneClone(string id, FuseSceneClone definition)
        {
            RequireId(id, nameof(id));
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            ApplyDefinition(id, definition);
        }

        public static void RemoveSceneClone(string id)
        {
            if (!TryRemoveSceneClone(id))
            {
                throw new InvalidOperationException($"Scene clone '{id}' was not found.");
            }
        }

        public static bool TryRemoveSceneClone(string id)
        {
            var root = FindRemovableSceneClone(id);
            if (root == null)
            {
                FuseLog.Info($"FUSE world removal skipped missing scene clone '{id}'.");
                return false;
            }

            var path = GetTransformPath(root.transform);
            root.SetActive(false);
            UnityEngine.Object.Destroy(root);
            FuseRuntimeDefinitionCache.Remove(FuseDefinitionKind.SceneClone, id);
            FuseLog.Info($"FUSE removed scene clone '{id}' from '{path}'.");
            return true;
        }

        public static GameObject GetSceneClone(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            return UnityEngine.Object.FindObjectsOfType<FuseSceneCloneMarker>(true)
                .FirstOrDefault(marker => string.Equals(marker.Id, id, StringComparison.OrdinalIgnoreCase))
                ?.gameObject;
        }

        public static IEnumerable<GameObject> GetAllSceneClones()
        {
            return UnityEngine.Object.FindObjectsOfType<FuseSceneCloneMarker>(true)
                .Where(marker => marker != null && marker.gameObject != null)
                .Select(marker => marker.gameObject);
        }

        public static FuseSceneClone GetSceneCloneDefinition(string id)
        {
            return GetDefinition(GetSceneClone(id));
        }

        /// <summary>
        /// Re-applies the cached <c>FuseSceneClone.Enabled</c> active state to every scene clone
        /// in the scene. Called after the game's state snapshot restore so that a saved-state
        /// reactivation cannot defeat package mandela disables. No-op for clones whose cached
        /// definition leaves Enabled null.
        /// </summary>
        public static int ReapplyEnabledFromCache(string reason)
        {
            var reapplied = 0;
            var enabledForced = 0;
            var disabledForced = 0;
            var markers = UnityEngine.Object.FindObjectsOfType<FuseSceneCloneMarker>(true);
            for (var index = 0; index < markers.Length; index++)
            {
                var marker = markers[index];
                if (marker == null || marker.gameObject == null)
                {
                    continue;
                }

                var id = marker.Id;
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                if (!FuseRuntimeDefinitionCache.TryGet(FuseDefinitionKind.SceneClone, id, out FuseSceneClone definition) ||
                    definition?.Enabled == null)
                {
                    continue;
                }

                marker.DesiredEnabled = definition.Enabled;
                marker.Source = definition.Source;
                var desired = definition.Enabled.Value;
                var previous = marker.gameObject.activeSelf;
                if (previous == desired)
                {
                    continue;
                }

                marker.gameObject.SetActive(desired);
                LogEnabledReapplyObject(reason, marker, definition, previous, desired);
                reapplied++;
                if (desired)
                {
                    enabledForced++;
                }
                else
                {
                    disabledForced++;
                }
            }

            if (reapplied > 0)
            {
                FuseLog.Info(
                    $"FUSE scene clone enabled-state reapply for '{reason ?? "snapshot"}' " +
                    $"reapplied={reapplied} (forcedEnabled={enabledForced}, forcedDisabled={disabledForced}) " +
                    $"of {markers.Length} marker(s).");
            }

            return reapplied;
        }

        private static void LogEnabledReapplyObject(
            string reason,
            FuseSceneCloneMarker marker,
            FuseSceneClone definition,
            bool previous,
            bool desired)
        {
            var gameObject = marker?.gameObject;
            var path = gameObject != null ? GetTransformPath(gameObject.transform) : string.Empty;
            var targetPath = !string.IsNullOrWhiteSpace(marker?.TargetPath)
                ? marker.TargetPath
                : (!string.IsNullOrWhiteSpace(definition?.TargetPath) ? definition.TargetPath : path);

            FuseLog.Info(
                $"FUSE scene clone enabled-state reapply object reason='{reason ?? "snapshot"}' " +
                $"id='{marker?.Id ?? string.Empty}' target='{targetPath ?? string.Empty}' " +
                $"path='{path}' source='{definition?.Source ?? string.Empty}' " +
                $"previousActive='{(previous ? "true" : "false")}' desiredActive='{(desired ? "true" : "false")}'.");
        }

        /// <summary>
        /// If <paramref name="gameObject"/> carries a FUSE scene clone marker, returns the FUSE scene clone id
        /// and the original target path. Used by diagnostics to identify hovered scene clones without
        /// exposing the private marker component type.
        /// </summary>
        public static bool TryGetSceneCloneInfo(GameObject gameObject, out string id, out string targetPath)
        {
            id = null;
            targetPath = null;
            if (gameObject == null)
            {
                return false;
            }

            var marker = gameObject.GetComponent<FuseSceneCloneMarker>();
            if (marker == null)
            {
                return false;
            }

            id = marker.Id;
            targetPath = marker.TargetPath;
            return true;
        }

        public static FuseSceneClone GetDefinition(GameObject sceneClone)
        {
            if (sceneClone == null)
            {
                return null;
            }

            var marker = sceneClone.GetComponent<FuseSceneCloneMarker>();
            var id = marker != null ? marker.Id : sceneClone.name;
            FuseRuntimeDefinitionCache.TryGet(FuseDefinitionKind.SceneClone, id, out FuseSceneClone definition);
            definition = definition ?? new FuseSceneClone();
            definition.TargetPath = !string.IsNullOrWhiteSpace(marker?.TargetPath) ? marker.TargetPath : GetTransformPath(sceneClone.transform);
            definition.Enabled = sceneClone.activeSelf;
            definition.LocalPosition = sceneClone.transform.localPosition;
            definition.LocalRotation = sceneClone.transform.localEulerAngles;
            definition.LocalScale = sceneClone.transform.localScale;
            return definition;
        }

        private static GameObject FindRemovableSceneClone(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            return GetSceneClone(id) ?? FusePrefabResolver.ResolveScenePath(id) ?? GameObject.Find(id);
        }

        private static GameObject ApplyDefinition(string id, FuseSceneClone definition)
        {
            if (string.IsNullOrWhiteSpace(definition.TargetPath))
            {
                throw new InvalidOperationException($"Scene clone '{id}' is missing a target path.");
            }

            // The decision of WHAT mutations to perform lives in
            // FuseSceneCloneApplyPlanner so it can be unit-tested in
            // isolation (every mandela regression we've shipped was
            // rooted in this branching, and Unity refuses to spawn
            // GameObjects outside the Editor/Player so we cannot
            // exercise the executor itself from xUnit). This method
            // is the executor: it resolves the existing target, runs
            // the planner, and applies the resulting Plan against
            // real Unity APIs.
            var existing = FusePrefabResolver.ResolveScenePath(definition.TargetPath);
            var plan = FuseSceneCloneApplyPlanner.Compute(definition, existing != null);

            GameObject targetObject;
            if (plan.CloneFromSource)
            {
                if (plan.DestroyExistingTarget)
                {
                    UnityEngine.Object.Destroy(existing);
                }

                // EnsureParentChainExists currently always travels with
                // CloneFromSource — we never instantiate a clone without
                // walking the parent chain. The planner exposes the
                // flag separately to make the intent explicit and
                // future-proof, but the executor pairs them here.
                var parent = EnsureTargetParent(definition.TargetPath, out var targetName);
                var source = FusePrefabResolver.Resolve(definition.Source);
                if (source == null)
                {
                    throw new InvalidOperationException($"Scene clone '{id}' source '{definition.Source}' could not be resolved.");
                }

                if (source.GetComponentInChildren<KeyValueObject>(true) != null)
                {
                    throw new InvalidOperationException($"Scene clone '{id}' source '{definition.Source}' contains a KeyValueObject and cannot be cloned.");
                }

                if (source.GetComponentInChildren<SceneryAssetInstance>(true) != null)
                {
                    throw new InvalidOperationException($"Scene clone '{id}' source '{definition.Source}' contains a SceneryAssetInstance. Use world.scenery instead.");
                }

                targetObject = UnityEngine.Object.Instantiate(source, parent);
                targetObject.name = targetName;
                if (plan.ZeroLocalTransformBeforeOverride)
                {
                    // Identity baseline so the planner's nullable
                    // OverrideLocal* values are the SOLE source of
                    // truth for what the clone ends up at — no
                    // inherited prefab offset can sneak through.
                    targetObject.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                }
            }
            else
            {
                targetObject = existing;
                if (targetObject == null)
                {
                    throw new InvalidOperationException($"Scene clone '{id}' target '{definition.TargetPath}' was not found.");
                }
            }

            var marker = targetObject.GetComponent<FuseSceneCloneMarker>() ?? targetObject.AddComponent<FuseSceneCloneMarker>();
            marker.Id = id;
            marker.TargetPath = definition.TargetPath;
            marker.DesiredEnabled = definition.Enabled;
            marker.Source = definition.Source;

            if (plan.StripUnsupportedRuntimeComponents)
            {
                StripUnsupportedRuntimeComponents(targetObject);
            }

            // OverrideLocal* are nullable; HasValue == false means the
            // planner decided we should NOT touch the live transform.
            // This is the contract that an enabled-only mandela on a
            // vanilla GameObject leaves the base-game placement alone.
            if (plan.OverrideLocalPosition.HasValue)
            {
                targetObject.transform.localPosition = plan.OverrideLocalPosition.Value;
            }

            if (plan.OverrideLocalRotation.HasValue)
            {
                targetObject.transform.localRotation = Quaternion.Euler(plan.OverrideLocalRotation.Value);
            }

            if (plan.OverrideLocalScale.HasValue)
            {
                targetObject.transform.localScale = plan.OverrideLocalScale.Value;
            }

            if (plan.SetActiveState.HasValue)
            {
                targetObject.SetActive(plan.SetActiveState.Value);
            }

            if (plan.ForceRenderable)
            {
                ForceRenderable(id, targetObject);
            }

            if (plan.RunPrefabSanitizer)
            {
                FusePrefabSanitizer.SanitizeSceneClone(targetObject, id).Log($"FUSE scene clone '{id}'");
            }

            MapAPI.RefreshAttachedMapMasks(targetObject, $"scene clone '{id}' apply");
            if (plan.RunPostBindValidation)
            {
                FusePrefabSanitizer.ValidateSceneClonePostBind(targetObject, id).Log($"FUSE scene clone '{id}' post-bind");
            }
            else
            {
                FuseLog.Info($"FUSE scene clone '{id}' post-bind validation skipped because the legacy definition leaves the target disabled.");
            }

            FuseLog.Info($"FUSE scene clone '{id}' materialized at {targetObject.transform.position}; {DescribeRendererState(targetObject)}.");
            FuseApiPersistence.RecordDefinition(FuseDefinitionKind.SceneClone, id, definition);
            return targetObject;
        }


        private static Transform EnsureTargetParent(string targetPath, out string targetName)
        {
            var segments = targetPath.Split(PathSeparators, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 2)
            {
                throw new InvalidOperationException($"Target path '{targetPath}' must include a root object and a child path.");
            }

            var root = FusePrefabResolver.ResolveScenePath(segments[0]);
            if (root == null)
            {
                throw new InvalidOperationException($"Target root '{segments[0]}' for scene clone path '{targetPath}' was not found.");
            }

            var parent = root.transform;
            for (var index = 1; index < segments.Length - 1; index++)
            {
                var child = parent.Find(segments[index]);
                if (child == null)
                {
                    var container = new GameObject(segments[index]);
                    container.transform.SetParent(parent, false);
                    container.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                    container.transform.localScale = Vector3.one;
                    child = container.transform;
                }

                parent = child;
            }

            targetName = segments[segments.Length - 1];
            return parent;
        }

        private static void StripUnsupportedRuntimeComponents(GameObject root)
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
                if (!string.Equals(typeName, "KinematicCharacterController.PhysicsMover", StringComparison.Ordinal) &&
                    !string.Equals(typeName, "KinematicCharacterController.KinematicCharacterMotor", StringComparison.Ordinal))
                {
                    continue;
                }

                var rigidbody = component.gameObject.GetComponent("Rigidbody");
                UnityEngine.Object.DestroyImmediate(component);
                if (rigidbody != null)
                {
                    UnityEngine.Object.DestroyImmediate(rigidbody);
                }
            }
        }

        private static void ForceRenderable(string id, GameObject root)
        {
            if (root == null)
            {
                return;
            }

            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                transform.gameObject.SetActive(true);
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

            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var keepVisible = !lodControlledRenderers.Contains(renderer) || lod0Renderers.Contains(renderer);
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

            if (lodGroups.Length > 0)
            {
                FuseLog.Info($"FUSE scene clone '{id}' forced LOD0 on {lodGroups.Length} LOD group(s).");
            }
        }

        private static string DescribeRendererState(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            var lodGroups = root.GetComponentsInChildren<LODGroup>(true);
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
                return $"renderers={renderers.Length}, enabled={enabledCount}, active={activeCount}, lodGroups={lodGroups.Length}, rootActive={root.activeInHierarchy}";
            }

            return $"renderers={renderers.Length}, enabled={enabledCount}, active={activeCount}, lodGroups={lodGroups.Length}, rootActive={root.activeInHierarchy}, boundsCenter={bounds.center}, boundsSize={bounds.size}";
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

        private static void RequireId(string id, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("ID is required.", parameterName);
            }
        }

        private sealed class FuseSceneCloneMarker : MonoBehaviour
        {
            public string Id;
            public string TargetPath;
            public bool? DesiredEnabled;
            public string Source;
            private bool _enforcing;

            private void OnEnable()
            {
                if (_enforcing || DesiredEnabled != false || gameObject == null)
                {
                    return;
                }

                _enforcing = true;
                try
                {
                    FuseLog.Info(
                        $"FUSE scene clone disabled-on-enable guard id='{Id ?? string.Empty}' " +
                        $"target='{TargetPath ?? string.Empty}' path='{GetTransformPath(transform)}' " +
                        $"source='{Source ?? string.Empty}' message='object was re-enabled after package disabled it; forcing inactive again'.");
                    gameObject.SetActive(false);
                }
                finally
                {
                    _enforcing = false;
                }
            }
        }
    }
}
