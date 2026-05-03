using System;
using System.Collections.Generic;
using System.Linq;
using FUSE.Authoring;
using FUSE.Cache;
using FUSE.Infrastructure;
using UnityEngine;

namespace FUSE.Lifecycle
{
    /// <summary>
    /// Snapshot/turntable callbacks must NOT trigger a full package or world reapply.
    /// Doing so re-runs SceneryAPI.UpdateScenery, which calls SceneryAssetInstance.ReloadComponents
    /// and throws UnknownIdentifierException for display-named scenery (e.g. "Camp 1").
    ///
    /// This service refreshes runtime indexes and repairs already-bound authoring entity
    /// references to existing runtime objects only. It never mutates the world, never
    /// reloads packages, and never calls ReloadComponents.
    /// </summary>
    public static class FuseRuntimeRebindService
    {
        // Each unknown EntityKind we encounter during a session is logged exactly
        // once. Cleared on map unload via ResetUnknownKindLog().
        private static readonly HashSet<string> LoggedUnknownKinds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly object UnknownKindSync = new object();

        public static void ResetUnknownKindLog()
        {
            lock (UnknownKindSync)
            {
                LoggedUnknownKinds.Clear();
            }
        }

        public static void RebindAfterSnapshot(string reason)
        {
            var label = string.IsNullOrWhiteSpace(reason) ? "snapshot" : reason;

            try
            {
                FuseCacheRegistry.RebuildAll();
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE snapshot rebind failed to rebuild runtime indexes for '{label}'.", ex);
            }

            var rebound = 0;
            var missing = 0;
            try
            {
                foreach (var entity in FuseAuthoringRegistry.AllEntities.ToArray())
                {
                    if (entity == null || string.IsNullOrWhiteSpace(entity.Id))
                    {
                        continue;
                    }

                    if (TryRebindEntityToExistingRuntime(entity))
                    {
                        rebound++;
                    }
                    else
                    {
                        missing++;
                    }
                }
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE snapshot rebind failed while re-binding authoring entities for '{label}'.", ex);
            }

            FuseLog.Info(
                $"FUSE snapshot rebind completed for '{label}' without world mutation " +
                $"(rebound={rebound}, missing={missing}). No package files reloaded, " +
                "no world definitions applied, no scenery created or updated, " +
                "no graph nodes/segments mutated, no SceneryAssetInstance.ReloadComponents calls.");
        }

        private static bool TryRebindEntityToExistingRuntime(FuseAuthoringEntity entity)
        {
            // Re-locate already-existing runtime object by id via the rebuilt indexes.
            // We never AddScenery, UpdateScenery, AddNode/Segment, etc. here.
            object cached;
            var kind = entity.EntityKind ?? string.Empty;
            switch (kind)
            {
                case "scenery":
                    return FuseSceneryRuntimeIndex.Instance.TryGetValue(entity.Id, out cached) &&
                           BindIfComponent(entity, cached);
                case "spline":
                    return FuseSplineyRuntimeIndex.Instance.TryGetValue(entity.Id, out cached) &&
                           BindIfGameObject(entity, cached);
                case "industry":
                    return FuseIndustryRuntimeIndex.Instance.TryGetValue(entity.Id, out cached) &&
                           BindIfComponent(entity, cached);
                case "industry-component":
                    return FuseIndustryComponentRuntimeIndex.Instance.TryGetValue(entity.Id, out cached) &&
                           BindIfComponent(entity, cached);
                case "station":
                    return FuseStationRuntimeIndex.Instance.TryGetValue(entity.Id, out cached) &&
                           BindIfComponent(entity, cached);
                case "turntable":
                    return FuseTurntableRuntimeIndex.Instance.TryGetValue(entity.Id, out cached) &&
                           BindIfComponent(entity, cached);
                case "map-label":
                    return FuseMapLabelRuntimeIndex.Instance.TryGetValue(entity.Id, out cached) &&
                           BindIfComponent(entity, cached);
                case "track":
                case "world":
                case "operations":
                case "configurable-structure":
                    // These kinds do not have a single runtime index entry to rebind
                    // through; their child objects (nodes/segments/spans, scene clone
                    // GameObject, etc.) are rebound via the cache rebuild itself.
                    return false;
                default:
                    LogUnknownEntityKindOnce(kind, entity.Id);
                    return false;
            }
        }

        private static void LogUnknownEntityKindOnce(string kind, string entityId)
        {
            var key = string.IsNullOrWhiteSpace(kind) ? "<empty>" : kind;
            lock (UnknownKindSync)
            {
                if (!LoggedUnknownKinds.Add(key))
                {
                    return;
                }
            }

            FuseLog.Warning(
                $"FUSE snapshot rebind package='<unknown>' operation='rebind' kind='{key}' " +
                $"id='{entityId ?? string.Empty}' message='entity kind has no rebind handler; " +
                "snapshot will not restore runtime references for this kind. Logged once per session.'");
        }

        private static bool BindIfComponent(FuseAuthoringEntity entity, object cached)
        {
            var component = cached as Component;
            if (component != null)
            {
                entity.BindRuntime(component.gameObject, component);
                return true;
            }

            var go = cached as GameObject;
            if (go != null)
            {
                entity.BindRuntime(go);
                return true;
            }

            return false;
        }

        private static bool BindIfGameObject(FuseAuthoringEntity entity, object cached)
        {
            var go = cached as GameObject ?? (cached as Component)?.gameObject;
            if (go != null)
            {
                entity.BindRuntime(go, cached as Component);
                return true;
            }

            return false;
        }
    }
}
