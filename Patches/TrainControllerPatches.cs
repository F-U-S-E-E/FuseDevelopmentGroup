using System;
using HarmonyLib;
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
                RailLog.Info($"RAIL ensured data packages before turntable restore ({loadedCount} package(s) loaded this pass).");
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
