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
                FuseLog.Exception($"FUSE direct asset pack store patch failed softly", ex);
            }
            finally
            {
                // Even a partial store add changes the identifier population.
                FUSE.Runtime.API.SceneryAPI.InvalidateKnownSceneryIdentifierIndex();
            }
        }
    }
}
