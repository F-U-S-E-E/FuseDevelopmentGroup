using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RAIL.Authoring
{
    public static class RailAuthoringRegistry
    {
        private static readonly Dictionary<string, RailAuthoringEntity> ByEntityId =
            new Dictionary<string, RailAuthoringEntity>(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<int, RailAuthoringEntity> ByRuntimeInstanceId =
            new Dictionary<int, RailAuthoringEntity>();

        private static readonly Dictionary<string, List<RailAuthoringEntity>> ByPackageId =
            new Dictionary<string, List<RailAuthoringEntity>>(StringComparer.OrdinalIgnoreCase);

        public static IEnumerable<RailAuthoringEntity> AllEntities => ByEntityId.Values;

        public static void Register(RailAuthoringEntity entity)
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
                    packageEntities = new List<RailAuthoringEntity>();
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

        private static void RemoveRuntimeBindings(RailAuthoringEntity entity)
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

        public static bool TryGet(string entityId, out RailAuthoringEntity entity)
        {
            if (string.IsNullOrWhiteSpace(entityId))
            {
                entity = null;
                return false;
            }

            return ByEntityId.TryGetValue(entityId, out entity);
        }

        public static bool TryGet(RailAuthoringEntity candidate, out RailAuthoringEntity entity)
        {
            if (candidate == null || string.IsNullOrWhiteSpace(candidate.Id))
            {
                entity = null;
                return false;
            }

            return ByEntityId.TryGetValue(candidate.Id, out entity);
        }

        public static RailAuthoringEntity Get(string entityId)
        {
            TryGet(entityId, out var entity);
            return entity;
        }

        public static bool TryGet(GameObject gameObject, out RailAuthoringEntity entity)
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

        public static bool TryGet(Component component, out RailAuthoringEntity entity)
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

        public static IReadOnlyList<RailAuthoringEntity> GetPackageEntities(string packageId)
        {
            return !string.IsNullOrWhiteSpace(packageId) && ByPackageId.TryGetValue(packageId, out var entities)
                ? entities.ToArray()
                : new RailAuthoringEntity[0];
        }

        public static void BindRuntime(RailAuthoringEntity entity, GameObject gameObject, Component component = null)
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
