using System;
using System.Collections.Generic;
using System.Linq;
using Helpers;
using KeyValue.Runtime;
using FUSE.Data;
using FUSE.Infrastructure;
using UnityEngine;

namespace FUSE.API
{
    public static class SceneCloneAPI
    {
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
                FuseLog.Warning($"FUSE world removal skipped missing scene clone '{id}'.");
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

        public static FuseSceneClone GetSceneCloneDefinition(string id)
        {
            return GetDefinition(GetSceneClone(id));
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

            var existing = FusePrefabResolver.ResolveScenePath(definition.TargetPath);
            GameObject targetObject;
            var clonedFromSource = !string.IsNullOrWhiteSpace(definition.Source);
            if (clonedFromSource)
            {
                if (existing != null)
                {
                    UnityEngine.Object.Destroy(existing);
                }

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
                targetObject.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
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

            if (clonedFromSource)
            {
                StripUnsupportedRuntimeComponents(targetObject);
            }
            if (definition.LocalPosition.HasValue)
            {
                targetObject.transform.localPosition = definition.LocalPosition.Value;
            }

            if (definition.LocalRotation.HasValue)
            {
                targetObject.transform.localRotation = Quaternion.Euler(definition.LocalRotation.Value);
            }

            if (definition.LocalScale.HasValue)
            {
                targetObject.transform.localScale = definition.LocalScale.Value;
            }

            if (definition.Enabled != null)
            {
                targetObject.SetActive(definition.Enabled.Value);
            }

            if (clonedFromSource && definition.Enabled != false)
            {
                ForceRenderable(id, targetObject);
            }

            if (clonedFromSource)
            {
                FusePrefabSanitizer.SanitizeSceneClone(targetObject, id).Log($"FUSE scene clone '{id}'");
            }

            FusePrefabSanitizer.ValidateSceneClonePostBind(targetObject, id).Log($"FUSE scene clone '{id}' post-bind");
            FuseLog.Info($"FUSE scene clone '{id}' materialized at {targetObject.transform.position}; {DescribeRendererState(targetObject)}.");
            FuseApiPersistence.RecordDefinition(FuseDefinitionKind.SceneClone, id, definition);
            return targetObject;
        }

        private static Transform EnsureTargetParent(string targetPath, out string targetName)
        {
            var segments = targetPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
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
        }
    }
}
