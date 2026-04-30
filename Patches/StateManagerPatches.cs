using System;
using System.Collections;
using Game.State;
using HarmonyLib;
using RAIL.Cache;
using RAIL.Infrastructure;
using RAIL.Loading;

namespace RAIL.Patches
{
    [HarmonyPatch(typeof(StateManager), "PopulateFromRemoteSnapshot")]
    internal static class StateManagerPatches
    {
        private static void Prefix()
        {
            try
            {
                RailCacheRegistry.RebuildAll();
                var loadedCount = RailDataPackageDiscovery.LoadAllAvailablePackages();
                RailLog.Info($"RAIL ensured data packages before snapshot restore ({loadedCount} package folder(s) loaded from disk this pass).");
            }
            catch (Exception ex)
            {
                RailLog.Exception("RAIL failed to load data packages before snapshot restore.", ex);
            }
        }

        private static void Postfix(StateManager __instance)
        {
            try
            {
                RailCacheRegistry.RebuildAll();
                var reappliedCount = RailDataPackageDiscovery.ReapplyLoadedPackages("after snapshot restore");
                RailLog.Info($"RAIL reapplied data packages after snapshot restore ({reappliedCount} loaded definition(s) applied this pass).");
                __instance?.StartCoroutine(ReapplyAfterRestoreDelay());
            }
            catch (Exception ex)
            {
                RailLog.Exception("RAIL failed to reapply data packages after snapshot restore.", ex);
            }
        }

        private static IEnumerator ReapplyAfterRestoreDelay()
        {
            yield return null;

            var waited = 0;
            while (Track.Graph.Shared != null &&
                   !Track.Graph.Shared.HasPopulatedCollections &&
                   waited < 300)
            {
                yield return null;
                waited++;
            }

            try
            {
                RailCacheRegistry.RebuildAll();
                var reappliedCount = RailDataPackageDiscovery.ReapplyLoadedPackages("after snapshot settle delay");
                RailLog.Info($"RAIL reapplied data packages after snapshot settle delay ({reappliedCount} loaded definition(s) applied this pass).");
            }
            catch (Exception ex)
            {
                RailLog.Exception("RAIL failed to reapply data packages after snapshot settle delay.", ex);
            }
        }
    }
}
