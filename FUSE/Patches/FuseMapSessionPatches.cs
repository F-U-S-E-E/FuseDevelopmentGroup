using System;
using HarmonyLib;
using Map.Runtime;
using FUSE.Infrastructure;
using FUSE.Loading;
using UI.Menu;

namespace FUSE.Patches
{
    /// <summary>
    /// Session plumbing for FUSE map packages. While <see cref="FuseMapSession"/>
    /// has an active map, <c>MapStore.Load</c> is redirected from
    /// StreamingAssets/Maps/&lt;dir&gt; to the pack's map folder, so the pack's
    /// Map.json supplies the origin and the complete tile set — a wholesale
    /// replacement of the stock terrain rather than an overlay. Returning to the
    /// main menu always deactivates the session, so a subsequent stock-map
    /// launch is untouched.
    /// </summary>
    [HarmonyPatch]
    internal static class FuseMapSessionPatches
    {
        [HarmonyPatch(typeof(MapStore), "Load", typeof(string))]
        [HarmonyPrefix]
        private static void MapStoreLoadPrefix(ref string basePath)
        {
            try
            {
                var activeMapId = FuseMapSession.ActiveMapId;
                if (string.IsNullOrEmpty(activeMapId))
                {
                    return;
                }

                if (!FuseMapPackageRegistry.TryGetMap(activeMapId, out var map) || !map.IsValid)
                {
                    var fault = map == null ? "map is not registered" : map.FaultReason;
                    FuseLog.Warning(
                        $"FUSE map session '{activeMapId}' cannot load: {fault}. Falling back to the stock map for this session.");
                    FuseMapSession.Deactivate("registered map missing or faulted at MapStore.Load");
                    return;
                }

                FuseLog.Info($"FUSE redirected MapStore.Load from '{basePath}' to '{map.MapFolder}' for map '{map.MapId}'.");
                basePath = map.MapFolder;
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE map session redirect failed in MapStore.Load; loading the stock map.", ex);
            }
        }

        [HarmonyPatch(typeof(MainMenu), "Awake")]
        [HarmonyPostfix]
        private static void MainMenuAwakePostfix()
        {
            try
            {
                FuseMapSession.Deactivate("main menu");
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE failed to deactivate the map session at the main menu.", ex);
            }
        }
    }
}
