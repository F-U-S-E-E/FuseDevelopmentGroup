using System.Reflection;
using AssetPack.Runtime;
using HarmonyLib;
using Model.Definition;
using Model.Database;
using FUSE.Infrastructure;
using FUSE.Loading;

namespace FUSE.Patches
{
    [HarmonyPatch(typeof(PrefabStore), "Create")]
    internal static class FusePrefabStoreAssetPackPatch
    {
        private static void Postfix(PrefabStore __result)
        {
            try
            {
                FuseAssetPackRegistry.AddDirectAssetPackStores(__result);
            }
            catch (System.Exception ex)
            {
                FuseLog.Warning($"FUSE direct asset pack store patch failed softly: {ex.Message}");
            }
        }
    }

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
                FuseLog.Warning($"FUSE direct asset pack container patch failed softly: {ex.Message}");
            }

            return true;
        }
    }

    [HarmonyPatch]
    internal static class FuseAssetPackRuntimeStoreBasePathPatch
    {
        private static MethodBase TargetMethod()
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
