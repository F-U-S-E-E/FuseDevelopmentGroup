using System;
using FUSE.API;
using FUSE.Infrastructure;
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
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE interchange export service failed.", ex);
            }
        }
    }
}
