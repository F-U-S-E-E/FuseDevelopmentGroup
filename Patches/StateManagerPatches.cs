using System;
using Game.State;
using HarmonyLib;
using FUSE.Infrastructure;
using FUSE.Lifecycle;

namespace FUSE.Patches
{
    [HarmonyPatch(typeof(StateManager), "PopulateFromRemoteSnapshot")]
    internal static class StateManagerPatches
    {
        private static void Prefix()
        {
            try
            {
                FuseRuntimeRebindService.RebindAfterSnapshot("before snapshot restore");
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE snapshot rebind (prefix) failed.", ex);
            }
        }

        private static void Postfix()
        {
            try
            {
                FuseRuntimeRebindService.RebindAfterSnapshot("after snapshot restore");
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE snapshot rebind (postfix) failed.", ex);
            }
        }
    }
}
