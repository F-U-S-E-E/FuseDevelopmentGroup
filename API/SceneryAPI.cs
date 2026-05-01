using System;
using System.Collections.Generic;
using System.Linq;
using Helpers;
using RAIL.Cache;
using RAIL.Data;
using RAIL.Infrastructure;
using UnityEngine;

namespace RAIL.API
{
    public static class SceneryAPI
    {
        private static Transform _fallbackRoot;

        public static SceneryAssetInstance AddScenery(string id, RailScenery definition)
        {
            RequireId(id, nameof(id));
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (GetScenery(id) != null)
            {
                throw new InvalidOperationException($"Scenery '{id}' already exists.");
            }

            string assetIdentifier;
            if (!TryResolveAssetIdentifier(id, definition, out assetIdentifier))
            {
                RailLog.Warning(
                    $"RAIL skipped AddScenery '{id}' because no valid asset identifier could be resolved " +
                    $"(AssetIdentifier='{definition.AssetIdentifier ?? string.Empty}', Model='{definition.Model ?? string.Empty}').");
                return null;
            }

            var gameObject = new GameObject(id);
            gameObject.SetActive(false);
            gameObject.transform.SetParent(GetSceneryRoot(), false);

            var scenery = gameObject.AddComponent<SceneryAssetInstance>();
            ApplyDefinition(scenery, definition, assetIdentifier);

            gameObject.SetActive(true);
            RailSceneryRuntimeIndex.Instance.Set(id, scenery);
            RailApiPersistence.RecordDefinition(RailDefinitionKind.Scenery, id, definition);
            return scenery;
        }

        public static void UpdateScenery(string id, RailScenery definition)
        {
            var scenery = RequireScenery(id);
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            string assetIdentifier;
            if (!TryResolveAssetIdentifier(id, definition, out assetIdentifier))
            {
                RailLog.Warning(
                    $"RAIL skipped UpdateScenery '{id}' because no valid asset identifier could be resolved " +
                    $"(AssetIdentifier='{definition.AssetIdentifier ?? string.Empty}', Model='{definition.Model ?? string.Empty}'). " +
                    "Refusing to call SceneryAssetInstance.ReloadComponents with an unknown identifier.");
                return;
            }

            var modelChanged = !string.Equals(scenery.identifier, assetIdentifier, StringComparison.Ordinal);
            ApplyDefinition(scenery, definition, assetIdentifier);
            if (modelChanged && scenery.isActiveAndEnabled)
            {
                scenery.ReloadComponents();
            }

            RailSceneryRuntimeIndex.Instance.Set(id, scenery);
            RailApiPersistence.RecordDefinition(RailDefinitionKind.Scenery, id, definition);
        }

        public static void RemoveScenery(string id)
        {
            if (!TryRemoveScenery(id))
            {
                throw new InvalidOperationException($"Scenery '{id}' was not found.");
            }
        }

        public static bool TryRemoveScenery(string id)
        {
            var root = FindRemovableSceneryObject(id);
            if (root == null)
            {
                RailLog.Warning($"RAIL world removal skipped missing scenery '{id}'.");
                return false;
            }

            var path = GetTransformPath(root.transform);
            root.SetActive(false);
            UnityEngine.Object.Destroy(root);
            RailSceneryRuntimeIndex.Instance.Remove(id);
            RailRuntimeDefinitionCache.Remove(RailDefinitionKind.Scenery, id);
            RailLog.Info($"RAIL removed scenery '{id}' from '{path}'.");
            return true;
        }

        public static SceneryAssetInstance GetScenery(string id)
        {
            if (RailSceneryRuntimeIndex.Instance.TryGetValue(id, out var cached))
            {
                return (SceneryAssetInstance)cached;
            }

            return !string.IsNullOrWhiteSpace(id)
                ? UnityEngine.Object.FindObjectsOfType<SceneryAssetInstance>().FirstOrDefault(instance => instance.name == id)
                : null;
        }

        public static IEnumerable<SceneryAssetInstance> GetAllScenery()
        {
            return UnityEngine.Object.FindObjectsOfType<SceneryAssetInstance>();
        }

        public static IEnumerable<string> GetAvailableSceneryModels()
        {
            return SceneryAssetManager.Shared?.GetSceneryDefinitionIdentifiers() ?? Enumerable.Empty<string>();
        }

        public static RailScenery GetSceneryDefinition(string id)
        {
            return GetDefinition(GetScenery(id));
        }

        public static RailScenery GetDefinition(SceneryAssetInstance scenery)
        {
            if (scenery == null)
            {
                return null;
            }

            RailRuntimeDefinitionCache.TryGet(RailDefinitionKind.Scenery, scenery.name, out RailScenery definition);
            definition = definition ?? new RailScenery();
            // scenery.identifier is the asset identifier, never the display name.
            definition.AssetIdentifier = scenery.identifier;
            if (string.IsNullOrWhiteSpace(definition.Model))
            {
                definition.Model = scenery.identifier;
            }

            definition.Position = scenery.transform.localPosition;
            definition.Rotation = scenery.transform.localEulerAngles;
            definition.Scale = scenery.transform.localScale == default ? Vector3.one : scenery.transform.localScale;
            return definition;
        }

        /// <summary>
        /// Resolves the validated asset identifier for a scenery definition.
        /// Returns false if no identifier can be resolved against the active
        /// SceneryAssetManager registry; callers must skip rather than throw.
        /// </summary>
        public static bool TryResolveAssetIdentifier(string sceneryId, RailScenery definition, out string assetIdentifier)
        {
            assetIdentifier = null;
            if (definition == null)
            {
                return false;
            }

            // Prefer the explicit asset identifier. The Model field may carry a display
            // name (e.g. "Camp 1", "Mess Hall") and must never be used directly.
            var candidate = NormalizeSceneryIdentifier(definition.AssetIdentifier);
            if (!string.IsNullOrWhiteSpace(candidate) && IsKnownSceneryAssetIdentifier(candidate))
            {
                assetIdentifier = candidate;
                return true;
            }

            // Backward-compat fallback: treat Model as an asset identifier ONLY if
            // the manager actually recognises it. Otherwise it is a display name.
            var modelCandidate = NormalizeSceneryIdentifier(definition.Model);
            if (!string.IsNullOrWhiteSpace(modelCandidate) && IsKnownSceneryAssetIdentifier(modelCandidate))
            {
                assetIdentifier = modelCandidate;
                return true;
            }

            return false;
        }

        private static bool IsKnownSceneryAssetIdentifier(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return false;
            }

            var manager = SceneryAssetManager.Shared;
            if (manager == null)
            {
                // Without the manager we cannot validate. Skip to be safe rather
                // than risk an UnknownIdentifierException out of ReloadComponents.
                return false;
            }

            var known = manager.GetSceneryDefinitionIdentifiers();
            if (known == null)
            {
                return false;
            }

            foreach (var id in known)
            {
                if (string.Equals(id, candidate, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static void ApplyDefinition(SceneryAssetInstance scenery, RailScenery definition, string assetIdentifier)
        {
            // Only the validated asset identifier ever reaches scenery.identifier /
            // SceneryAssetManager.LoadScenery. Display names are kept on the definition.
            scenery.identifier = assetIdentifier;
            scenery.transform.localPosition = definition.Position;
            scenery.transform.localRotation = Quaternion.Euler(definition.Rotation);
            scenery.transform.localScale = definition.Scale == default ? Vector3.one : definition.Scale;
        }

        private static string NormalizeSceneryIdentifier(string model)
        {
            if (string.IsNullOrWhiteSpace(model))
            {
                return model;
            }

            var marker = model.IndexOf("://", StringComparison.Ordinal);
            if (marker < 0)
            {
                return model;
            }

            return model.Substring(marker + 3);
        }

        private static SceneryAssetInstance RequireScenery(string id)
        {
            var scenery = GetScenery(id);
            if (scenery == null)
            {
                throw new InvalidOperationException($"Scenery '{id}' was not found.");
            }

            return scenery;
        }

        private static GameObject FindRemovableSceneryObject(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            var scenery = GetScenery(id);
            if (scenery != null)
            {
                return scenery.gameObject;
            }

            return RailPrefabResolver.ResolveScenePath(id) ?? GameObject.Find(id);
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

        private static Transform GetSceneryRoot()
        {
            var existingRoot = GameObject.Find("World/Large Scenery") ?? GameObject.Find("Large Scenery");
            if (existingRoot != null)
            {
                return existingRoot.transform;
            }

            if (SceneryAssetManager.Shared != null)
            {
                return SceneryAssetManager.Shared.transform;
            }

            if (_fallbackRoot == null)
            {
                _fallbackRoot = new GameObject("RAIL Scenery").transform;
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
