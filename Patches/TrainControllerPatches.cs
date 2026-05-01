using System;
using HarmonyLib;
using RAIL.Infrastructure;
using RAIL.Lifecycle;

namespace RAIL.Patches
{
    [HarmonyPatch(typeof(TrainController), "HandleSnapshotTurntables")]
    internal static class TrainControllerPatches
    {
        private static void Prefix()
        {
            try
            {
                RailRuntimeRebindService.RebindAfterSnapshot("before turntable restore");
            }
            catch (Exception ex)
            {
                RailLog.Exception("RAIL turntable rebind (prefix) failed.", ex);
            }
        }

        private static void Postfix()
        {
            try
            {
                RailRuntimeRebindService.RebindAfterSnapshot("after turntable restore");
            }
            catch (Exception ex)
            {
                RailLog.Exception("RAIL turntable rebind (postfix) failed.", ex);
            }
        }
    }
}
