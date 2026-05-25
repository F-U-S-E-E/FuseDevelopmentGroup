using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Model.Ops;
using FUSE.Runtime.Cache;
using FUSE.Authoring.Data;
using RollingStock;
using RollingStock.Controls;
using Track;
using UnityEngine;

namespace FUSE.Runtime.API
{
    public static class LoaderAPI
    {
        private static readonly FieldInfo IndustryHoverableIndustryField = typeof(IndustryContentHoverable).GetField("industry", BindingFlags.Instance | BindingFlags.NonPublic);
        private static Transform _fallbackRoot;

        public static GameObject AddLoader(string id, FuseLoader definition)
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
            FuseLoaderRuntimeIndex.Instance.Set(id, gameObject);
            FuseApiPersistence.RecordDefinition(FuseDefinitionKind.Loader, id, definition);
            return gameObject;
        }

        public static void UpdateLoader(string id, FuseLoader definition)
        {
            var loader = RequireLoader(id);
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            ApplyDefinition(loader, id, definition);
            FuseLoaderRuntimeIndex.Instance.Set(id, loader);
            FuseApiPersistence.RecordDefinition(FuseDefinitionKind.Loader, id, definition);
        }

        public static void RemoveLoader(string id)
        {
            var loader = RequireLoader(id);
            loader.SetActive(false);
            UnityEngine.Object.Destroy(loader);
            FuseLoaderRuntimeIndex.Instance.Remove(id);
            FuseRuntimeDefinitionCache.Remove(FuseDefinitionKind.Loader, id);
        }

        public static GameObject GetLoader(string id)
        {
            if (FuseLoaderRuntimeIndex.Instance.TryGetValue(id, out var cached))
            {
                return (GameObject)cached;
            }

            return !string.IsNullOrWhiteSpace(id) ? GameObject.Find("World/Loaders/" + id) ?? GameObject.Find("Loaders/" + id) : null;
        }

        public static IEnumerable<GameObject> GetAllLoaders()
        {
            return FuseLoaderRuntimeIndex.Instance.Values.Cast<GameObject>();
        }

        public static FuseLoader GetLoaderDefinition(string id)
        {
            return GetDefinition(GetLoader(id));
        }

        public static FuseLoader GetDefinition(GameObject loader)
        {
            if (loader == null)
            {
                return null;
            }

            var id = loader.name;
            FuseRuntimeDefinitionCache.TryGet(FuseDefinitionKind.Loader, id, out FuseLoader definition);
            definition = definition ?? new FuseLoader();
            definition.Position = loader.transform.localPosition;
            definition.Rotation = loader.transform.localEulerAngles;

            var targetLoader = loader.GetComponentInChildren<CarLoadTargetLoader>(true);
            if (targetLoader?.sourceIndustry != null)
            {
                definition.IndustryId = targetLoader.sourceIndustry.identifier;
            }

            return definition;
        }

        private static void ApplyDefinition(GameObject loader, string id, FuseLoader definition)
        {
            loader.transform.localPosition = definition.Position;
            loader.transform.localRotation = Quaternion.Euler(definition.Rotation);

            var oldPrefab = loader.transform.Find("prefab");
            if (oldPrefab != null)
            {
                UnityEngine.Object.Destroy(oldPrefab.gameObject);
            }

            var prefab = FusePrefabResolver.Resolve(definition.Prefab);
            if (prefab == null)
            {
                throw new InvalidOperationException($"Loader prefab '{definition.Prefab}' was not found.");
            }

            // CRITICAL: deactivate the loader parent before Instantiate so the
            // clone's GlobalKeyValueObject.OnEnable does NOT fire while we
            // still have the prefab's original globalObjectId. The vanilla
            // base-scene loader prefabs (water columns, fueling stands,
            // coaling towers) ship with globalObjectId values like
            // `wh-e-water`, `whittier-coaling-tower`, etc. If we instantiate
            // under an active parent, OnEnable runs immediately and registers
            // the clone under that same globalObjectId — OVERWRITING the
            // original scene loader's registration in StateManager — and the
            // subsequent SetActive(false) here unregisters it. The original
            // never re-registers, so clicks on the player-visible water
            // column produce "HandlePropertyChange: Unknown object
            // wh-e-water" in Player.log and the loader animation never
            // plays. The fix: keep the parent inactive while we rewrite the
            // globalObjectId, only then re-activate so OnEnable runs with
            // the unique id.
            var wasLoaderActive = loader.activeSelf;
            if (wasLoaderActive)
            {
                loader.SetActive(false);
            }

            try
            {
                var instance = UnityEngine.Object.Instantiate(prefab, loader.transform);
                // Belt-and-suspenders — even though the parent is inactive,
                // mark the clone inactive too in case any of our follow-up
                // mutations would otherwise trigger OnEnable.
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

                var requiresIndustry = !string.IsNullOrWhiteSpace(definition.IndustryId);
                var industry = AttachIndustry(instance, definition.IndustryId);
                FusePrefabSanitizer.SanitizeLoader(instance, id, industry, requiresIndustry).Log($"FUSE loader '{id}'");
                instance.SetActive(true);
                FuseLoaderRuntimeIndex.Instance.Set(id, loader);
                MapAPI.RefreshAttachedMapMasks(loader, $"loader '{id}' apply");
                FusePrefabSanitizer.ValidateLoaderPostBind(loader, id, industry, requiresIndustry).Log($"FUSE loader '{id}' post-bind");
            }
            finally
            {
                if (wasLoaderActive)
                {
                    // Reactivating the parent here is what finally fires
                    // GlobalKeyValueObject.OnEnable on the clone — with the
                    // correct unique `<id>.loader` globalObjectId — so the
                    // clone registers cleanly without disturbing whichever
                    // scene loader owns the same vanilla prefab's id.
                    loader.SetActive(true);
                }
            }
        }

        private static Industry AttachIndustry(GameObject instance, string industryId)
        {
            if (string.IsNullOrWhiteSpace(industryId))
            {
                return null;
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

            return industry;
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
