using System;
using System.IO;
using HarmonyLib;
using Map.Runtime;
using RAIL.Infrastructure;
using RAIL.Loading;
using UnityEngine;

namespace RAIL.Patches
{
    [HarmonyPatch]
    internal static class MapStorePatches
    {
        [HarmonyPatch(typeof(MapStore), "Load", typeof(string))]
        [HarmonyPostfix]
        private static void MapStoreLoadPostfix(MapStore __instance, string basePath)
        {
            try
            {
                RailMapTileRegistry.RefreshFromAvailablePackages();
                var directoryName = Path.GetFileName(basePath?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                var mountedCount = RailMapTileRegistry.MountIntoStore(__instance, directoryName);
                if (mountedCount > 0)
                {
                    RailLog.Info($"Mounted {mountedCount} RAIL map tile(s) for '{directoryName}'.");
                }
            }
            catch (Exception ex)
            {
                RailLog.Exception("RAIL map tile mount failed after MapStore.Load.", ex);
            }
        }

        [HarmonyPatch(typeof(MapStore), "PathFor", typeof(Vector2Int))]
        [HarmonyPostfix]
        private static void MapStorePathForPostfix(Vector2Int tp, ref string __result)
        {
            try
            {
                if (RailMapTileRegistry.TryGetMountedTilePath(tp, out var tilePath))
                {
                    __result = tilePath;
                }
            }
            catch (Exception ex)
            {
                RailLog.Exception("RAIL map tile path override failed.", ex);
            }
        }

        [HarmonyPatch(typeof(MapManager), "UnloadStore")]
        [HarmonyPostfix]
        private static void MapManagerUnloadStorePostfix()
        {
            try
            {
                RailMapTileRegistry.ClearActiveTilePaths();
            }
            catch (Exception ex)
            {
                RailLog.Exception("RAIL failed to clear active map tile paths after MapManager.UnloadStore.", ex);
            }
        }
    }
}
