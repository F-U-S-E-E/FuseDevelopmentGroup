using System;
using HarmonyLib;
using RAIL.Cache;
using RAIL.Infrastructure;
using RAIL.Loading;

namespace RAIL.Patches
{
    [HarmonyPatch(typeof(TrainController), "HandleSnapshotTurntables")]
    internal static class TrainControllerPatches
    {
        private static void Prefix()
        {
            try
            {
                var loadedCount = RailDataPackageDiscovery.LoadAllAvailablePackages();
                RailCacheRegistry.RebuildAll();
                var reappliedCount = RailDataPackageDiscovery.ReapplyLoadedPackages("before turntable restore");
                RailLog.Info($"RAIL ensured data packages before turntable restore ({loadedCount} package folder(s) loaded from disk, {reappliedCount} loaded definition(s) reapplied).");
            }
            catch (Exception ex)
            {
                RailLog.Exception("RAIL failed to load data packages before turntable restore.", ex);
            }
        }

        private static void Postfix()
        {
            RailLog.Info("RAIL turntable restore completed.");
        }
    }
}
