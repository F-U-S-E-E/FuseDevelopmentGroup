using System;
using HarmonyLib;
using FUSE.Infrastructure;
using FUSE.Lifecycle;

namespace FUSE.Patches
{
    [HarmonyPatch(typeof(TrainController), "HandleSnapshotTurntables")]
    internal static class TrainControllerPatches
    {
        private static void Prefix()
        {
            try
            {
                FuseRuntimeRebindService.RebindAfterSnapshot("before turntable restore");
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE turntable rebind (prefix) failed.", ex);
            }
        }

        private static void Postfix()
        {
            try
            {
                FuseRuntimeRebindService.RebindAfterSnapshot("after turntable restore");
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE turntable rebind (postfix) failed.", ex);
            }
        }
    }
}
