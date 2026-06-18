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

                // Targeted invalidation (#4, opt-in): if we captured an ACCURATE,
                // complete footprint of what FUSE touched during this apply, re-bake
                // just those tiles instead of tearing down and re-streaming the whole
                // map. Default off, and we fall back to the full rebuild whenever the
                // toggle is off, the method didn't resolve, the footprint is incomplete
                // (e.g. masks still streaming in — see MapAPI.RefreshAttachedMapMasks),
                // or nothing was captured (e.g. a manual reload).
                if (FuseSettings.EnableTargetedTerrainInvalidation &&
                    MapManagerInvalidateBounds != null &&
                    FuseTerrainRefreshScope.BoundsComplete &&
                    FuseTerrainRefreshScope.TryGetAccumulatedBounds(out var bounds))
                {
                    try
                    {
                        MapManagerInvalidateBounds.Invoke(instance, new object[] { bounds });
                        FuseLog.Info(
                            $"FUSE terrain reload (targeted invalidation) operation='{operation}' " +
                            $"bounds.center={bounds.center} bounds.size={bounds.size} " +
                            $"deferredRefreshCalls={FuseTerrainRefreshScope.DeferredRefreshCalls} elapsedMs={stopwatch.ElapsedMilliseconds}.");
                        return true;
                    }
                    catch (Exception ex)
                    {
                        // A failed optimization must not become a missed refresh: fall
                        // through to the full rebuild rather than leaving terrain stale.
                        FuseLog.Warning(
                            $"FUSE terrain reload targeted invalidation failed operation='{operation}': " +
                            $"{ex.GetBaseException().Message}; falling back to full rebuild.");
                    }
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

        /// <summary>
        /// Re-bakes terrain after FUSE decoupled map masks that registered AFTER the map-load
        /// terrain rebuild (masks stream in with their building model and self-apply their
        /// MapManager modifiers; the game's per-modifier invalidate is debounced and starved
        /// behind the spawn tile-load backlog, so an already-built tile never re-evaluates them).
        /// Prefers a targeted invalidate of just the touched tiles (terrain-only, so it never
        /// re-streams scenery and cannot re-enter the decouple path); falls back to a full
        /// rebuild only if the targeted reflection surface is unavailable. Called, coalesced,
        /// by <see cref="FuseDecoupledMaskTerrainRebaker"/> once the decouple burst settles.
        /// </summary>
        public static bool RebakeDecoupledMaskTerrain(Bounds? gameBounds)
        {
            const string operation = "decoupled-mask post-stream terrain rebake";
            if (!FuseMultiplayerGuard.CanApplyWorldMutations(operation))
            {
                return false;
            }

            try
            {
                var instance = MapManagerInstance?.GetValue(null);
                if (instance == null)
                {
                    return false;
                }

                // The footprint arrives already in GAME space — the offset-independent space
                // MapManager.Invalidate(Bounds) tiles in and modifiers are stored in (AddModifier
                // does OffsetBy(-gameToWorldOffset)). No conversion here: converting at fire time
                // through a live offset would shift bounds that were captured before a
                // floating-origin rebase by a whole origin block.
                if (MapManagerInvalidateBounds != null && gameBounds.HasValue)
                {
                    try
                    {
                        MapManagerInvalidateBounds.Invoke(instance, new object[] { gameBounds.Value });
                        FuseLog.Info(
                            $"FUSE {operation} (targeted invalidate) gameBounds.center={gameBounds.Value.center} size={gameBounds.Value.size}.");
                        return true;
                    }
                    catch (Exception ex)
                    {
                        FuseLog.Warning(
                            $"FUSE {operation} targeted invalidate failed: {ex.GetBaseException().Message}; falling back to full rebuild.");
                    }
                }

                if (MapManagerRebuildAll != null)
                {
                    MapManagerRebuildAll.Invoke(instance, null);
                    FuseLog.Info($"FUSE {operation} (full rebuild).");
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                FuseLog.Warning($"FUSE {operation} failed: {ex.GetBaseException().Message}");
                return false;
            }
        }
    }
}
