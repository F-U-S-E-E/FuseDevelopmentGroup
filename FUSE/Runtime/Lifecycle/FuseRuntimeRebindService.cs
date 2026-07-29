using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FUSE.Runtime.API;
using FUSE.Authoring.Entities;
using FUSE.Runtime.Cache;
using FUSE.Infrastructure;
using UnityEngine;

namespace FUSE.Runtime.Lifecycle
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
        private static FuseRuntimeRebindHost _host;
        private static bool _deferredSceneCloneReapplyScheduled;
        private static int _deferredSceneCloneReapplyRequests;
        private static string _lastDeferredSceneCloneReapplyReason;
        private static int _snapshotRestoreDepth;
        private static int _coalescedSnapshotRebindRequests;
        private static string _lastCoalescedSnapshotRebindReason;

        internal static bool IsSnapshotRestoreInProgress =>
            _snapshotRestoreDepth > 0;

        public static void ResetUnknownKindLog()
        {
            lock (UnknownKindSync)
            {
                LoggedUnknownKinds.Clear();
            }
        }

        public static void Shutdown()
        {
            _deferredSceneCloneReapplyScheduled = false;
            _deferredSceneCloneReapplyRequests = 0;
            _lastDeferredSceneCloneReapplyReason = null;
            _snapshotRestoreDepth = 0;
            _coalescedSnapshotRebindRequests = 0;
            _lastCoalescedSnapshotRebindReason = null;

            if (_host != null)
            {
                UnityEngine.Object.Destroy(_host.gameObject);
                _host = null;
            }
        }

        /// <summary>
        /// Marks the start of the game's outer snapshot restore. Turntable restore
        /// callbacks run inside this operation and used to trigger their own complete
        /// scene-cache rebuilds before and after the turntable pass. While the world
        /// is still changing, those intermediate indexes are immediately obsolete.
        /// Keep their requests, then perform one complete rebind after the outer
        /// snapshot reaches its settled state.
        /// </summary>
        public static IDisposable BeginSnapshotRestore(string completionReason)
        {
            _snapshotRestoreDepth++;
            return new SnapshotRestoreScope(completionReason);
        }

        /// <summary>
        /// Completes an outer snapshot restore and executes the single coalesced
        /// rebind requested by its nested callbacks. An unmatched call is a no-op,
        /// which lets both a Harmony postfix and finalizer safely invoke this method.
        /// </summary>
        private static void EndSnapshotRestore(string reason)
        {
            if (_snapshotRestoreDepth <= 0)
            {
                return;
            }

            _snapshotRestoreDepth--;
            if (_snapshotRestoreDepth > 0)
            {
                return;
            }

            var nestedRequests = _coalescedSnapshotRebindRequests;
            var nestedReason = _lastCoalescedSnapshotRebindReason;
            _coalescedSnapshotRebindRequests = 0;
            _lastCoalescedSnapshotRebindReason = null;

            var label = string.IsNullOrWhiteSpace(reason)
                ? nestedReason ?? "after snapshot restore"
                : reason;
            RebindAfterSnapshotCore(label, nestedRequests);
        }

        public static void RebindAfterSnapshot(string reason)
        {
            var label = string.IsNullOrWhiteSpace(reason) ? "snapshot" : reason;
            if (_snapshotRestoreDepth > 0)
            {
                _coalescedSnapshotRebindRequests++;
                _lastCoalescedSnapshotRebindReason = label;
                return;
            }

            RebindAfterSnapshotCore(label, 0);
        }

        private static void RebindAfterSnapshotCore(string label, int coalescedRequests)
        {

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
                $"(rebound={rebound}, missing={missing}, coalescedNestedRequests={coalescedRequests}). " +
                "No package files reloaded, " +
                "no world definitions applied, no scenery created or updated, " +
                "no graph nodes/segments mutated, no SceneryAssetInstance.ReloadComponents calls.");

            ReapplySceneCloneEnabledState(label);
            ScheduleDeferredSceneCloneEnabledReapply(label);
        }

        private static void ReapplySceneCloneEnabledState(string label)
        {
            try
            {
                // FUSE operates at runtime AFTER the base-game managers (OpsController,
                // MapFeatureManager, ProgressionManager) have already woken and captured
                // their state. When the game later runs StateManager.PopulateFromRemoteSnapshot
                // it can re-activate scene clones that a package mandela had disabled,
                // because the snapshot was captured before FUSE applied. This call
                // re-asserts each cached FuseSceneClone.Enabled state by toggling only
                // GameObject.activeSelf; no world objects are created, destroyed, or
                // otherwise mutated.
                SceneCloneAPI.ReapplyEnabledFromCache(label);
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE snapshot rebind failed to reapply scene clone enabled state for '{label}'.", ex);
            }
        }

        private static void ScheduleDeferredSceneCloneEnabledReapply(string reason)
        {
            _deferredSceneCloneReapplyRequests++;
            _lastDeferredSceneCloneReapplyReason = string.IsNullOrWhiteSpace(reason) ? "snapshot" : reason;
            if (_deferredSceneCloneReapplyScheduled)
            {
                return;
            }

            _deferredSceneCloneReapplyScheduled = true;
            EnsureHost().StartCoroutine(ReapplySceneCloneEnabledAfterSnapshotFrame());
        }

        private static IEnumerator ReapplySceneCloneEnabledAfterSnapshotFrame()
        {
            yield return null;
            ReapplySceneCloneEnabledState(BuildDeferredSceneCloneReapplyReason("+1 frame"));

            yield return null;
            ReapplySceneCloneEnabledState(BuildDeferredSceneCloneReapplyReason("+2 frames"));

            _deferredSceneCloneReapplyScheduled = false;
            _deferredSceneCloneReapplyRequests = 0;
            _lastDeferredSceneCloneReapplyReason = null;
        }

        private static string BuildDeferredSceneCloneReapplyReason(string suffix)
        {
            var reason = string.IsNullOrWhiteSpace(_lastDeferredSceneCloneReapplyReason)
                ? "snapshot restore"
                : _lastDeferredSceneCloneReapplyReason;
            return $"{reason} deferred scene-clone reapply {suffix} (requests={_deferredSceneCloneReapplyRequests})";
        }

        private static FuseRuntimeRebindHost EnsureHost()
        {
            if (_host != null)
            {
                return _host;
            }

            var gameObject = new GameObject("FUSE Runtime Rebind");
            UnityEngine.Object.DontDestroyOnLoad(gameObject);
            gameObject.hideFlags = HideFlags.HideAndDontSave;
            _host = gameObject.AddComponent<FuseRuntimeRebindHost>();
            return _host;
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

        private sealed class FuseRuntimeRebindHost : MonoBehaviour
        {
        }

        /// <summary>
        /// Per-Harmony-invocation scope. Harmony runs a finalizer after a successful
        /// postfix as well as after a fault; idempotent disposal prevents that pair
        /// from decrementing the nesting depth twice.
        /// </summary>
        private sealed class SnapshotRestoreScope : IDisposable
        {
            private readonly string _completionReason;
            private bool _disposed;

            internal SnapshotRestoreScope(string completionReason)
            {
                _completionReason = completionReason;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                EndSnapshotRestore(_completionReason);
            }
        }
    }
}
