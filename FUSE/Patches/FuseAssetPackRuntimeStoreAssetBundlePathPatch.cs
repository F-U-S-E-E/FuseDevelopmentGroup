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
    /// Stub patch retained for compatibility with existing reflection tests;
    /// the real collision resolution happens inside
    /// <see cref="FuseAssetPackRuntimeStoreLoadedBundlePatch"/> via CAB-based
    /// AssetBundle dedup. Earlier iterations redirected the loser's
    /// <c>AssetBundlePath</c> getter to the winner's bundle file, which
    /// caused the LOSER to race the WINNER to <c>LoadFromFile</c> on the
    /// SAME path and the second caller to fail with Unity's
    /// "another AssetBundle with the same files is already loaded" error.
    /// The redirect is intentionally a no-op now — see the LoadedBundle
    /// patch below for the actual deduplication.
    /// </summary>
    [HarmonyPatch]
    internal static class FuseAssetPackRuntimeStoreAssetBundlePathPatch
    {
        private static MethodInfo TargetMethod()
        {
            return AccessTools.PropertyGetter(typeof(AssetPackRuntimeStore), "AssetBundlePath");
        }

        private static bool Prefix(AssetPackRuntimeStore __instance, ref string __result)
        {
            // Intentionally pass through — see class doc comment.
            return true;
        }
    }
}
