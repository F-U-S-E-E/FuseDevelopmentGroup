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

    [HarmonyPatch]
    internal static class FuseAssetPackRuntimeStoreBasePathPatch
    {
        private static MethodInfo TargetMethod()
        {
            return AccessTools.PropertyGetter(typeof(AssetPackRuntimeStore), "BasePath");
        }

        private static bool Prefix(AssetPackRuntimeStore __instance, ref string __result)
        {
            if (__instance != null &&
                FuseAssetPackRegistry.TryResolveDirectStoreBasePath(__instance.Identifier, out var path))
            {
                __result = path;
                return false;
            }

            return true;
        }
    }
}
