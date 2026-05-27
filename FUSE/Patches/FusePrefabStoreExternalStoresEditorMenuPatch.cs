using System;
using System.Collections.Generic;
using System.Linq;
using AssetPack.Runtime;
using FUSE.Infrastructure;
using FUSE.Loading;
using HarmonyLib;
using Model.Database;

namespace FUSE.Patches
{
    [HarmonyPatch(typeof(PrefabStore), "get_ExternalStores")]
    internal static class FusePrefabStoreExternalStoresEditorMenuPatch
    {
        private static void Postfix(ref IEnumerable<AssetPackRuntimeStore> __result)
        {
            if (__result == null)
            {
                return;
            }

            try
            {
                __result = __result.Where(store => ShouldShowExternalStoreIdentifier(store?.Identifier));
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE failed to filter direct asset stores from the editor store menu", ex);
            }
        }

        internal static bool ShouldShowExternalStoreIdentifier(string identifier)
        {
            return !FuseAssetPackRegistry.TryResolveDirectStoreBasePath(identifier, out _);
        }
    }
}
