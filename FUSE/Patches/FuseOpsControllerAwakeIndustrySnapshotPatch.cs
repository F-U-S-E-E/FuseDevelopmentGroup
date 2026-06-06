using System;
using HarmonyLib;
using Model.Ops;
using FUSE.Infrastructure;
using FUSE.Runtime.API;
using FUSE.Loading;

namespace FUSE.Patches
{
    // Capture the live IndustryComponent state of every industry into
    // FuseBaseGameIndustrySnapshot at OpsController.Awake postfix. By the time FUSE's
    // legacy-package apply pass runs, some pre-existing IndustryComponents have already
    // been destroyed by an earlier phase, so this is the latest lifecycle point at
    // which the original pre-apply state is observable. The snapshot lets the partial
    // component materializer recover the destroyed component's original spans and
    // merge a partial patch onto them instead of standing up a fresh component with
    // only the added spans.
    [HarmonyPatch(typeof(OpsController), "Awake")]
    internal static class FuseOpsControllerAwakeIndustrySnapshotPatch
    {
        private static void Prefix()
        {
            try
            {
                FuseDataPackageDiscovery.LoadPackagesFromDisk(false);
                FuseModLoader.ApplyLoadedOperationRemovalsEarly("OpsController.Awake prefix");
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE early operation removals before OpsController.Awake failed", ex);
            }
        }

        private static void Postfix()
        {
            try
            {
                FuseBaseGameIndustrySnapshot.CaptureAll("OpsController.Awake postfix");
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE base-game industry snapshot capture failed", ex);
            }
        }
    }
}
