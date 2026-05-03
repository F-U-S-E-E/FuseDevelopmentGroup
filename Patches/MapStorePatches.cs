using System;
using System.IO;
using HarmonyLib;
using Map.Runtime;
using FUSE.Infrastructure;
using FUSE.Loading;
using UnityEngine;

namespace FUSE.Patches
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
                FuseMapTileRegistry.RefreshFromAvailablePackages();
                var directoryName = Path.GetFileName(basePath?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                var mountedCount = FuseMapTileRegistry.MountIntoStore(__instance, directoryName);
                if (mountedCount > 0)
                {
                    FuseLog.Info($"Mounted {mountedCount} FUSE map tile(s) for '{directoryName}'.");
                }
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE map tile mount failed after MapStore.Load.", ex);
            }
        }

        [HarmonyPatch(typeof(MapStore), "PathFor", typeof(Vector2Int))]
        [HarmonyPostfix]
        private static void MapStorePathForPostfix(Vector2Int tp, ref string __result)
        {
            try
            {
                if (FuseMapTileRegistry.TryGetMountedTilePath(tp, out var tilePath))
                {
                    __result = tilePath;
                }
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE map tile path override failed.", ex);
            }
        }

        [HarmonyPatch(typeof(MapManager), "UnloadStore")]
        [HarmonyPostfix]
        private static void MapManagerUnloadStorePostfix()
        {
            try
            {
                FuseMapTileRegistry.ClearActiveTilePaths();
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE failed to clear active map tile paths after MapManager.UnloadStore.", ex);
            }
        }
    }
}
