using System;
using System.Collections.Generic;
using System.Linq;
using Character;
using FUSE.Authoring.Data;
using FUSE.Infrastructure;
using UnityEngine;

namespace FUSE.Runtime.API
{
    public static class SpawnPointAPI
    {
        private const string SpawnRootName = "FUSE Spawn Points";

        private static readonly Dictionary<string, SpawnPoint> SpawnPoints =
            new Dictionary<string, SpawnPoint>(StringComparer.OrdinalIgnoreCase);

        private static Transform _fallbackRoot;

        public static SpawnPoint AddOrUpdateSpawnPoint(string packageId, FuseSpawnPoint definition)
        {
            if (string.IsNullOrWhiteSpace(packageId))
            {
                throw new ArgumentException("Package id is required.", nameof(packageId));
            }

            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (string.IsNullOrWhiteSpace(definition.Name))
            {
                throw new InvalidOperationException("Spawn point name is required.");
            }

            var key = GetSpawnPointKey(packageId, definition.Name);
            var spawnPoint = GetSpawnPoint(packageId, definition.Name);
            if (spawnPoint == null)
            {
                var gameObject = new GameObject(definition.Name);
                gameObject.transform.SetParent(GetSpawnRoot(), false);
                spawnPoint = gameObject.AddComponent<SpawnPoint>();
            }

            spawnPoint.name = definition.Name;
            spawnPoint.transform.localPosition = definition.Position;
            spawnPoint.transform.localRotation = Quaternion.Euler(definition.Rotation);
            spawnPoint.priority = definition.Priority.GetValueOrDefault();
            spawnPoint.radius = Mathf.Max(0.1f, definition.Radius.GetValueOrDefault(3f));
            SpawnPoints[key] = spawnPoint;
            FuseApiPersistence.RecordDefinition(FuseDefinitionKind.SpawnPoint, key, definition);
            FuseLog.Info($"FUSE registered spawn point package='{packageId}' name='{definition.Name}' position={definition.Position} radius={spawnPoint.radius} priority={spawnPoint.priority}.");
            return spawnPoint;
        }

        public static SpawnPoint GetSpawnPoint(string packageId, string name)
        {
            var key = GetSpawnPointKey(packageId, name);
            if (SpawnPoints.TryGetValue(key, out var cached) && cached != null)
            {
                return cached;
            }

            var root = GetSpawnRoot(false);
            if (root == null || string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            var found = root
                .GetComponentsInChildren<SpawnPoint>(true)
                .FirstOrDefault(spawnPoint => spawnPoint != null && string.Equals(spawnPoint.name, name, StringComparison.OrdinalIgnoreCase));
            if (found != null)
            {
                SpawnPoints[key] = found;
            }

            return found;
        }

        public static FuseSpawnPoint GetSpawnPointDefinition(string packageId, string name)
        {
            return FuseRuntimeDefinitionCache.TryGet<FuseSpawnPoint>(
                FuseDefinitionKind.SpawnPoint,
                GetSpawnPointKey(packageId, name),
                out var definition)
                ? definition
                : null;
        }

        public static bool TryRemoveSpawnPoint(string packageId, string name)
        {
            var key = GetSpawnPointKey(packageId, name);
            var spawnPoint = GetSpawnPoint(packageId, name);
            if (spawnPoint == null)
            {
                SpawnPoints.Remove(key);
                return false;
            }

            SpawnPoints.Remove(key);
            FuseRuntimeDefinitionCache.Remove(FuseDefinitionKind.SpawnPoint, key);
            spawnPoint.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(spawnPoint.gameObject);
            FuseLog.Info($"FUSE removed spawn point package='{packageId}' name='{name}'.");
            return true;
        }

        public static void ClearRuntimeCache()
        {
            SpawnPoints.Clear();
        }

        public static void ReleasePackage(string packageId)
        {
            if (string.IsNullOrWhiteSpace(packageId))
            {
                return;
            }

            var prefix = packageId.Trim() + "/";
            var keys = SpawnPoints.Keys
                .Where(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            foreach (var key in keys)
            {
                var name = key.Substring(prefix.Length);
                TryRemoveSpawnPoint(packageId, name);
            }
        }

        private static string GetSpawnPointKey(string packageId, string name)
        {
            return (packageId ?? string.Empty).Trim() + "/" + (name ?? string.Empty).Trim();
        }

        private static Transform GetSpawnRoot(bool create = true)
        {
            var world = GameObject.Find("World");
            if (world != null)
            {
                var existing = world.transform.Find(SpawnRootName);
                if (existing != null || !create)
                {
                    return existing;
                }

                var root = new GameObject(SpawnRootName);
                root.transform.SetParent(world.transform, false);
                return root.transform;
            }

            var rootObject = GameObject.Find(SpawnRootName);
            if (rootObject != null || !create)
            {
                return rootObject != null ? rootObject.transform : null;
            }

            if (_fallbackRoot == null)
            {
                _fallbackRoot = new GameObject(SpawnRootName).transform;
                UnityEngine.Object.DontDestroyOnLoad(_fallbackRoot.gameObject);
            }

            return _fallbackRoot;
        }
    }
}
