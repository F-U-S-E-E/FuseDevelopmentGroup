using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Model.Ops;
using RAIL.Cache;
using RAIL.Data;
using RollingStock;
using RollingStock.Controls;
using Track;
using UnityEngine;

namespace RAIL.API
{
    public static class LoaderAPI
    {
        private static readonly FieldInfo IndustryHoverableIndustryField = typeof(IndustryContentHoverable).GetField("industry", BindingFlags.Instance | BindingFlags.NonPublic);
        private static Transform _fallbackRoot;

        public static GameObject AddLoader(string id, RailLoader definition)
        {
            RequireId(id, nameof(id));
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (GetLoader(id) != null)
            {
                throw new InvalidOperationException($"Loader '{id}' already exists.");
            }

            var gameObject = new GameObject(id);
            gameObject.transform.SetParent(GetLoaderRoot(), false);
            ApplyDefinition(gameObject, id, definition);
            RailLoaderRuntimeIndex.Instance.Set(id, gameObject);
            return gameObject;
        }

        public static void UpdateLoader(string id, RailLoader definition)
        {
            var loader = RequireLoader(id);
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            ApplyDefinition(loader, id, definition);
            RailLoaderRuntimeIndex.Instance.Set(id, loader);
        }

        public static void RemoveLoader(string id)
        {
            var loader = RequireLoader(id);
            loader.SetActive(false);
            UnityEngine.Object.Destroy(loader);
            RailLoaderRuntimeIndex.Instance.Remove(id);
        }

        public static GameObject GetLoader(string id)
        {
            if (RailLoaderRuntimeIndex.Instance.TryGetValue(id, out var cached))
            {
                return (GameObject)cached;
            }

            return !string.IsNullOrWhiteSpace(id) ? GameObject.Find("World/Loaders/" + id) ?? GameObject.Find("Loaders/" + id) : null;
        }

        public static IEnumerable<GameObject> GetAllLoaders()
        {
            return RailLoaderRuntimeIndex.Instance.Values.Cast<GameObject>();
        }

        private static void ApplyDefinition(GameObject loader, string id, RailLoader definition)
        {
            loader.transform.localPosition = definition.Position;
            loader.transform.localRotation = Quaternion.Euler(definition.Rotation);

            var oldPrefab = loader.transform.Find("prefab");
            if (oldPrefab != null)
            {
                UnityEngine.Object.Destroy(oldPrefab.gameObject);
            }

            var prefab = RailPrefabResolver.Resolve(definition.Prefab);
            if (prefab == null)
            {
                throw new InvalidOperationException($"Loader prefab '{definition.Prefab}' was not found.");
            }

            var instance = UnityEngine.Object.Instantiate(prefab, loader.transform);
            instance.SetActive(false);
            instance.name = "prefab";
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localEulerAngles = Vector3.zero;

            var marker = instance.GetComponent<TrackMarker>();
            if (marker != null)
            {
                marker.enabled = false;
            }

            var global = instance.GetComponent<GlobalKeyValueObject>();
            if (global != null)
            {
                global.globalObjectId = id + ".loader";
            }

            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                renderer.enabled = true;
            }

            AttachIndustry(instance, definition.IndustryId);
            instance.SetActive(true);
        }

        private static void AttachIndustry(GameObject instance, string industryId)
        {
            if (string.IsNullOrWhiteSpace(industryId))
            {
                return;
            }

            var industry = IndustryAPI.GetIndustry(industryId);
            if (industry == null)
            {
                throw new InvalidOperationException($"Industry '{industryId}' was not found for loader.");
            }

            var targetLoader = instance.GetComponentInChildren<CarLoadTargetLoader>(true);
            if (targetLoader != null)
            {
                targetLoader.sourceIndustry = industry;
            }

            var hoverable = instance.GetComponentInChildren<IndustryContentHoverable>(true);
            if (hoverable != null)
            {
                IndustryHoverableIndustryField?.SetValue(hoverable, industry);
            }
        }

        private static GameObject RequireLoader(string id)
        {
            var loader = GetLoader(id);
            if (loader == null)
            {
                throw new InvalidOperationException($"Loader '{id}' was not found.");
            }

            return loader;
        }

        private static Transform GetLoaderRoot()
        {
            var root = GameObject.Find("World");
            if (root != null)
            {
                var existing = root.transform.Find("Loaders");
                if (existing != null)
                {
                    return existing;
                }

                var loaders = new GameObject("Loaders");
                loaders.transform.SetParent(root.transform, false);
                return loaders.transform;
            }

            if (_fallbackRoot == null)
            {
                _fallbackRoot = new GameObject("Loaders").transform;
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
