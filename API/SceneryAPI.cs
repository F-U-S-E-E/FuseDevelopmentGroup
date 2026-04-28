using System;
using System.Collections.Generic;
using System.Linq;
using Helpers;
using RAIL.Cache;
using RAIL.Data;
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

            var gameObject = new GameObject(id);
            gameObject.SetActive(false);
            gameObject.transform.SetParent(GetSceneryRoot(), false);

            var scenery = gameObject.AddComponent<SceneryAssetInstance>();
            ApplyDefinition(scenery, definition);

            gameObject.SetActive(true);
            SceneryCache.Instance.Set(id, scenery);
            return scenery;
        }

        public static void UpdateScenery(string id, RailScenery definition)
        {
            var scenery = RequireScenery(id);
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            var modelChanged = !string.Equals(scenery.identifier, definition.Model, StringComparison.Ordinal);
            ApplyDefinition(scenery, definition);
            if (modelChanged && scenery.isActiveAndEnabled)
            {
                scenery.ReloadComponents();
            }

            SceneryCache.Instance.Set(id, scenery);
        }

        public static void RemoveScenery(string id)
        {
            var scenery = RequireScenery(id);
            scenery.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(scenery.gameObject);
            SceneryCache.Instance.Remove(id);
        }

        public static SceneryAssetInstance GetScenery(string id)
        {
            if (SceneryCache.Instance.TryGetValue(id, out var cached))
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

        private static void ApplyDefinition(SceneryAssetInstance scenery, RailScenery definition)
        {
            scenery.identifier = NormalizeSceneryIdentifier(definition.Model);
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
