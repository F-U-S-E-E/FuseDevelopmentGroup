using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FUSE.Authoring
{
    public static class FuseAuthoringRegistry
    {
        private static readonly Dictionary<string, FuseAuthoringEntity> ByEntityId =
            new Dictionary<string, FuseAuthoringEntity>(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<int, FuseAuthoringEntity> ByRuntimeInstanceId =
            new Dictionary<int, FuseAuthoringEntity>();

        private static readonly Dictionary<string, List<FuseAuthoringEntity>> ByPackageId =
            new Dictionary<string, List<FuseAuthoringEntity>>(StringComparer.OrdinalIgnoreCase);

        public static IEnumerable<FuseAuthoringEntity> AllEntities => ByEntityId.Values;

        public static void Register(FuseAuthoringEntity entity)
        {
            if (entity == null || string.IsNullOrWhiteSpace(entity.Id))
            {
                return;
            }

            ByEntityId[entity.Id] = entity;
            RemoveRuntimeBindings(entity);
            foreach (var packageEntities in ByPackageId.Values)
            {
                packageEntities.Remove(entity);
            }

            if (!string.IsNullOrWhiteSpace(entity.PackageId))
            {
                if (!ByPackageId.TryGetValue(entity.PackageId, out var packageEntities))
                {
                    packageEntities = new List<FuseAuthoringEntity>();
                    ByPackageId[entity.PackageId] = packageEntities;
                }

                if (!packageEntities.Contains(entity))
                {
                    packageEntities.Add(entity);
                }
            }

            BindRuntime(entity, entity.RuntimeGameObject, entity.RuntimeComponent);
        }

        public static bool Unregister(string entityId)
        {
            if (string.IsNullOrWhiteSpace(entityId) || !ByEntityId.TryGetValue(entityId, out var entity))
            {
                return false;
            }

            ByEntityId.Remove(entityId);
            foreach (var packageEntities in ByPackageId.Values)
            {
                packageEntities.Remove(entity);
            }

            var runtimeKeys = ByRuntimeInstanceId
                .Where(pair => ReferenceEquals(pair.Value, entity))
                .Select(pair => pair.Key)
                .ToArray();
            for (var index = 0; index < runtimeKeys.Length; index++)
            {
                ByRuntimeInstanceId.Remove(runtimeKeys[index]);
            }

            return true;
        }

        private static void RemoveRuntimeBindings(FuseAuthoringEntity entity)
        {
            var runtimeKeys = ByRuntimeInstanceId
                .Where(pair => ReferenceEquals(pair.Value, entity))
                .Select(pair => pair.Key)
                .ToArray();

            for (var index = 0; index < runtimeKeys.Length; index++)
            {
                ByRuntimeInstanceId.Remove(runtimeKeys[index]);
            }
        }

        public static void Clear()
        {
            ByEntityId.Clear();
            ByRuntimeInstanceId.Clear();
            ByPackageId.Clear();
        }

        public static bool TryGet(string entityId, out FuseAuthoringEntity entity)
        {
            if (string.IsNullOrWhiteSpace(entityId))
            {
                entity = null;
                return false;
            }

            return ByEntityId.TryGetValue(entityId, out entity);
        }

        public static bool TryGet(FuseAuthoringEntity candidate, out FuseAuthoringEntity entity)
        {
            if (candidate == null || string.IsNullOrWhiteSpace(candidate.Id))
            {
                entity = null;
                return false;
            }

            return ByEntityId.TryGetValue(candidate.Id, out entity);
        }

        public static FuseAuthoringEntity Get(string entityId)
        {
            TryGet(entityId, out var entity);
            return entity;
        }

        public static bool TryGet(GameObject gameObject, out FuseAuthoringEntity entity)
        {
            if (gameObject == null)
            {
                entity = null;
                return false;
            }

            for (var cursor = gameObject.transform; cursor != null; cursor = cursor.parent)
            {
                if (ByRuntimeInstanceId.TryGetValue(cursor.gameObject.GetInstanceID(), out entity))
                {
                    return true;
                }
            }

            entity = null;
            return false;
        }

        public static bool TryGet(Component component, out FuseAuthoringEntity entity)
        {
            if (component == null)
            {
                entity = null;
                return false;
            }

            if (ByRuntimeInstanceId.TryGetValue(component.GetInstanceID(), out entity))
            {
                return true;
            }

            return component.gameObject != null &&
                   TryGet(component.gameObject, out entity);
        }

        public static IReadOnlyList<FuseAuthoringEntity> GetPackageEntities(string packageId)
        {
            return !string.IsNullOrWhiteSpace(packageId) && ByPackageId.TryGetValue(packageId, out var entities)
                ? entities.ToArray()
                : new FuseAuthoringEntity[0];
        }

        public static void BindRuntime(FuseAuthoringEntity entity, GameObject gameObject, Component component = null)
        {
            if (entity == null)
            {
                return;
            }

            if (gameObject != null)
            {
                ByRuntimeInstanceId[gameObject.GetInstanceID()] = entity;
                var components = gameObject.GetComponentsInChildren<Component>(true);
                for (var index = 0; index < components.Length; index++)
                {
                    if (components[index] != null)
                    {
                        ByRuntimeInstanceId[components[index].GetInstanceID()] = entity;
                    }
                }
            }

            if (component != null)
            {
                ByRuntimeInstanceId[component.GetInstanceID()] = entity;
            }
        }
    }
}
