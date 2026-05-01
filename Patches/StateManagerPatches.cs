using System;
using Game.State;
using HarmonyLib;
using RAIL.Infrastructure;
using RAIL.Lifecycle;

namespace RAIL.Patches
{
    [HarmonyPatch(typeof(StateManager), "PopulateFromRemoteSnapshot")]
    internal static class StateManagerPatches
    {
        private static void Prefix()
        {
            try
            {
                RailRuntimeRebindService.RebindAfterSnapshot("before snapshot restore");
            }
            catch (Exception ex)
            {
                RailLog.Exception("RAIL snapshot rebind (prefix) failed.", ex);
            }
        }

        private static void Postfix()
        {
            try
            {
                RailRuntimeRebindService.RebindAfterSnapshot("after snapshot restore");
            }
            catch (Exception ex)
            {
                RailLog.Exception("RAIL snapshot rebind (postfix) failed.", ex);
            }
        }
    }
}
