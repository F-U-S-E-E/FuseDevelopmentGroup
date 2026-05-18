using System;
using System.Diagnostics;
using System.Reflection;
using FUSE.API;
using FUSE.Cache;
using FUSE.Infrastructure;
using FUSE.Loading;

namespace FUSE.Lifecycle
{
    internal static class FuseRuntimeReloadService
    {
        private static readonly MethodInfo MapManagerRebuildAll =
            Type.GetType("Map.Runtime.MapManager, Map.Runtime")
                ?.GetMethod("RebuildAll", BindingFlags.Instance | BindingFlags.Public);

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
            TrackAPI.RemoveInvalidTrackSpans(operation);
            TrackAPI.ScrubCtcSignalReferences(operation);
            IndustryAPI.ScrubIndustryComponentCaches(operation);
            IndustryAPI.DisableOrphanedBaseGameIndustries(operation);
            TrackAPI.DisableInvalidTrackMarkers(operation);
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

                MapManagerRebuildAll.Invoke(instance, null);
                FuseLog.Info($"FUSE terrain reload completed operation='{operation}' elapsedMs={stopwatch.ElapsedMilliseconds}.");
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
