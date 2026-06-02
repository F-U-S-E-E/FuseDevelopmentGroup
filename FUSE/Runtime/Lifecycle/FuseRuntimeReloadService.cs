using System;
using System.Diagnostics;
using System.Reflection;
using FUSE.Runtime.API;
using FUSE.Runtime.Cache;
using FUSE.Infrastructure;
using FUSE.Loading;
using UnityEngine;

namespace FUSE.Runtime.Lifecycle
{
    internal static class FuseRuntimeReloadService
    {
        private static readonly MethodInfo MapManagerRebuildAll =
            Type.GetType("Map.Runtime.MapManager, Map.Runtime")
                ?.GetMethod("RebuildAll", BindingFlags.Instance | BindingFlags.Public);

        // Private void Invalidate(Bounds) — re-bakes only the tiles overlapping the
        // given world bounds (vs RebuildAll's full teardown+reload). Used by the
        // opt-in targeted-invalidation path; null-checked so a rename just falls back
        // to the full rebuild. Covered by the reflection-surface canary test.
        private static readonly MethodInfo MapManagerInvalidateBounds =
            Type.GetType("Map.Runtime.MapManager, Map.Runtime")
                ?.GetMethod("Invalidate", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(Bounds) }, null);

        private static readonly PropertyInfo MapManagerInstance =
            Type.GetType("Map.Runtime.MapManager, Map.Runtime")
                ?.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public);

        public static int ReloadTrackAndData(string reason)
        {
            var operation = string.IsNullOrWhiteSpace(reason) ? "runtime track reload" : reason.Trim();
            var stopwatch = Stopwatch.StartNew();
            if (!FuseMultiplayerGuard.CanApplyWorldMutations(operation))
            {
                FuseLog.Warning($"FUSE runtime track reload skipped operation='{operation}' reason='{FuseMultiplayerGuard.GetWorldMutationBlockReason(operation)}'.");
                return 0;
            }

            FuseCacheRegistry.RebuildAll();
            var applied = FuseDataPackageDiscovery.ReapplyLoadedPackages(operation);
            // Batch the cleanup cluster so RemoveInvalidTrackSpans's rebuild
            // request folds into one final rebuild instead of firing while the
            // rest of the cleanup is still in progress.
            TrackAPI.BeginBatch();
            try
            {
                TrackAPI.RemoveInvalidTrackSpans(operation);
                TrackAPI.ScrubCtcSignalReferences(operation);
                IndustryAPI.ScrubIndustryComponentCaches(operation);
                IndustryAPI.DisableOrphanedBaseGameIndustries(operation);
                TrackAPI.DisableInvalidTrackMarkers(operation);
            }
            finally
            {
                TrackAPI.EndBatch(true);
            }
            FuseLog.Info($"FUSE runtime track/data reload completed operation='{operation}' applied={applied} elapsedMs={stopwatch.ElapsedMilliseconds}.");
            return applied;
        }

        public static bool ReloadTerrain(string reason)
        {
            var operation = string.IsNullOrWhiteSpace(reason) ? "runtime terrain reload" : reason.Trim();
            var stopwatch = Stopwatch.StartNew();
            if (!FuseMultiplayerGuard.CanApplyWorldMutations(operation))
            {
                FuseLog.Warning($"FUSE terrain reload skipped operation='{operation}' reason='{FuseMultiplayerGuard.GetWorldMutationBlockReason(operation)}'.");
                return false;
            }

            try
            {
                if (MapManagerInstance == null || MapManagerRebuildAll == null)
                {
                    FuseLog.Warning($"FUSE terrain reload skipped operation='{operation}': MapManager.Instance or RebuildAll could not be resolved via reflection.");
                    return false;
                }

                var instance = MapManagerInstance.GetValue(null);
                if (instance == null)
                {
                    FuseLog.Warning($"FUSE terrain reload skipped operation='{operation}': MapManager.Instance is null.");
                    return false;
                }

                // Targeted invalidation (#4, opt-in): if we captured the footprint of
                // what FUSE touched during this apply, re-bake just those tiles instead
                // of tearing down and re-streaming the whole map. Default off, and we
                // fall back to the full rebuild whenever the toggle is off, the method
                // didn't resolve, or no footprint was captured (e.g. a manual reload).
                if (FuseSettings.EnableTargetedTerrainInvalidation &&
                    MapManagerInvalidateBounds != null &&
                    FuseTerrainRefreshScope.TryGetAccumulatedBounds(out var bounds))
                {
                    MapManagerInvalidateBounds.Invoke(instance, new object[] { bounds });
                    FuseLog.Info(
                        $"FUSE terrain reload (targeted invalidation) operation='{operation}' " +
                        $"bounds.center={bounds.center} bounds.size={bounds.size} " +
                        $"deferredRefreshCalls={FuseTerrainRefreshScope.DeferredRefreshCalls} elapsedMs={stopwatch.ElapsedMilliseconds}.");
                    return true;
                }

                MapManagerRebuildAll.Invoke(instance, null);
                FuseLog.Info(
                    $"FUSE terrain reload (full rebuild) operation='{operation}' " +
                    $"deferredRefreshCalls={FuseTerrainRefreshScope.DeferredRefreshCalls} elapsedMs={stopwatch.ElapsedMilliseconds}.");
                return true;
            }
            catch (Exception ex)
            {
                FuseLog.Warning($"FUSE terrain reload failed operation='{operation}': {ex.GetBaseException().Message}");
                return false;
            }
        }
    }
}
