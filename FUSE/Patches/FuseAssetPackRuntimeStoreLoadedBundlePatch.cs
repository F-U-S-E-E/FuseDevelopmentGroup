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

    /// <summary>
    /// Stub patch retained for compatibility with existing reflection tests.
    /// Earlier iterations of this class attempted to redirect a "loser"
    /// store's <c>LoadedBundle</c> task to a sibling "winner" store's
    /// bundle file. That approach was wrong: pack folders that share a
    /// leaf name across a mod's root and its <c>SCAssetPacks/</c> legacy
    /// folder almost always contain DIFFERENT bundle content under the
    /// same internal CAB name (the duplicate pack folders are different
    /// versions kept side-by-side as legacy artifacts, not byte
    /// duplicates). Redirecting cross-version returned the wrong prefab
    /// for the calling store's catalog and produced visually broken cars.
    ///
    /// <para>The actual fix lives at registration time: see
    /// <see cref="FuseAssetPackRegistry"/> for the pack discovery
    /// order, which yields root-level packs ahead of
    /// <c>SCAssetPacks/*</c>. With that order in place,
    /// <c>PrefabStore.AssetPackContainingIdentifier</c> reaches the
    /// modern (root) bundle first and the legacy
    /// <c>SCAssetPacks/</c> bundle stays dormant, so Unity's
    /// same-CAB rejection never fires inside a single session.</para>
    /// </summary>
    [HarmonyPatch]
    internal static class FuseAssetPackRuntimeStoreLoadedBundlePatch
    {
        private static MethodInfo TargetMethod()
        {
            return AccessTools.Method(typeof(AssetPackRuntimeStore), "LoadedBundle");
        }

        private static bool Prefix(AssetPackRuntimeStore __instance, ref System.Threading.Tasks.Task<UnityEngine.AssetBundle> __result)
        {
            // Intentionally pass through — see class doc comment.
            return true;
        }
    }
}
