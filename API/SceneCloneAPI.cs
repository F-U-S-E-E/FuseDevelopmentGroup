using System;
using System.Linq;
using Helpers;
using KeyValue.Runtime;
using RAIL.Data;
using RAIL.Infrastructure;
using UnityEngine;

namespace RAIL.API
{
    public static class SceneCloneAPI
    {
        public static GameObject AddSceneClone(string id, RailSceneClone definition)
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

        public static void UpdateSceneClone(string id, RailSceneClone definition)
        {
            RequireId(id, nameof(id));
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            ApplyDefinition(id, definition);
        }

        public static GameObject GetSceneClone(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            return UnityEngine.Object.FindObjectsOfType<RailSceneCloneMarker>(true)
                .FirstOrDefault(marker => string.Equals(marker.Id, id, StringComparison.OrdinalIgnoreCase))
                ?.gameObject;
        }

        private static GameObject ApplyDefinition(string id, RailSceneClone definition)
        {
            if (string.IsNullOrWhiteSpace(definition.TargetPath))
            {
                throw new InvalidOperationException($"Scene clone '{id}' is missing a target path.");
            }

            var existing = RailPrefabResolver.ResolveScenePath(definition.TargetPath);
            GameObject targetObject;
            var clonedFromSource = !string.IsNullOrWhiteSpace(definition.Source);
            if (clonedFromSource)
            {
                if (existing != null)
                {
                    UnityEngine.Object.Destroy(existing);
                }

                var parent = EnsureTargetParent(definition.TargetPath, out var targetName);
                var source = RailPrefabResolver.Resolve(definition.Source);
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

            var marker = targetObject.GetComponent<RailSceneCloneMarker>() ?? targetObject.AddComponent<RailSceneCloneMarker>();
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
                ForceRenderable(targetObject);
            }

            RailLog.Info($"RAIL scene clone '{id}' materialized at {targetObject.transform.position}; {DescribeRendererState(targetObject)}.");
            return targetObject;
        }

        private static Transform EnsureTargetParent(string targetPath, out string targetName)
        {
            var segments = targetPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 2)
            {
                throw new InvalidOperationException($"Target path '{targetPath}' must include a root object and a child path.");
            }

            var root = RailPrefabResolver.ResolveScenePath(segments[0]);
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

        private static void ForceRenderable(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                transform.gameObject.SetActive(true);
            }

            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                renderer.enabled = true;
                renderer.forceRenderingOff = false;
            }
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

        private static void RequireId(string id, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("ID is required.", parameterName);
            }
        }

        private sealed class RailSceneCloneMarker : MonoBehaviour
        {
            public string Id;
            public string TargetPath;
        }
    }
}
