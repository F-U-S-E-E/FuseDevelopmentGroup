using System;
using FUSE.Runtime.API;
using FUSE.Infrastructure;
using Game.State;
using HarmonyLib;
using Model.Ops;

namespace FUSE.Patches
{
    [HarmonyPatch(typeof(Interchange), "ServeInterchange")]
    internal static class FuseInterchangePatches
    {
        private static void Postfix(Interchange __instance, IIndustryContext ctx)
        {
            try
            {
                if (__instance == null || __instance.Industry == null || ctx == null)
                {
                    return;
                }

                var unloaders = __instance.Industry.GetComponentsInChildren<FuseInterchangedIndustryUnloader>();
                foreach (var unloader in unloaders)
                {
                    var componentContext = unloader.CreateContext(ctx.Now, 0f);
                    unloader.ServeInterchange(componentContext, __instance);
                }

                // C1CD compatibility: continuous service means an interchange
                // receives another extra-service slot even when this pass did
                // not leave any pending orders. Scheduling here avoids the old
                // brittle IL edit inside OpsController.CheckServiceInterchanges.
                if (FuseSettings.InterchangeContinuousService && StateManager.IsHost)
                {
                    __instance.ScheduleExtra(new Game.GameDateTime?(
                        FuseC1CdSchedulePolicy.CalculateNextServiceTime(
                            ctx.Now,
                            FuseSettings.InterchangeServiceIntervalMinutes,
                            FuseSettings.InterchangeNotBeforeHour,
                            FuseSettings.InterchangeNotAfterHour)));
                }
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE interchange export service failed.", ex);
            }
        }
    }
}
