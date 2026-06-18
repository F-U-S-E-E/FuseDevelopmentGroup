using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AssetPack.Runtime;
using HarmonyLib;
using Model.Definition;
using Model.Definition.Data;
using Model.Database;
using FUSE.Infrastructure;
using FUSE.Loading;

namespace FUSE.Patches
{

    [HarmonyPatch(typeof(AssetPackRuntimeStore), "Container")]
    internal static class FuseAssetPackRuntimeStoreContainerPatch
    {
        private static bool Prefix(AssetPackRuntimeStore __instance, ref Container __result)
        {
            try
            {
                if (FuseAssetPackRegistry.TryLoadSanitizedDirectContainer(__instance, out var container))
                {
                    __result = container;
                    return false;
                }
            }
            catch (System.Exception ex)
            {
                FuseLog.Exception($"FUSE direct asset pack container patch failed softly", ex);
            }

            return true;
        }

        private static void Postfix(AssetPackRuntimeStore __instance, ref Container __result)
        {
            try
            {
                FuseLegacyContainerMixintoRegistry.ApplyToContainer(__instance, __result);
            }
            catch (System.Exception ex)
            {
                FuseLog.Exception($"FUSE legacy support container mixinto patch failed softly", ex);
            }
        }
    }
}
