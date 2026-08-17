using System;
using System.Collections;
using System.Reflection;
using System.Threading;
using AssetPack.Runtime;
using FUSE.Infrastructure;
using FUSE.Loading;
using FUSE.Runtime.Lifecycle;
using HarmonyLib;

namespace FUSE.Patches
{
    /// <summary>
    /// Drops a completed per-asset request when its reference count reaches
    /// zero even if another asset keeps the same bundle open.
    ///
    /// The stock store retains zero-reference AssetBundleRequest objects in
    /// _loadRequests until every reference in the bundle reaches zero. On a
    /// route that continuously uses one asset from a large pack, that makes
    /// every prefab visited earlier remain reachable for the entire session.
    /// </summary>
    [HarmonyPatch(
        typeof(AssetPackRuntimeStore),
        nameof(AssetPackRuntimeStore.DecrementReferenceCount))]
    internal static class FuseAssetPackZeroReferenceEvictionPatch
    {
        private static readonly FieldInfo LoadRequestsField =
            AccessTools.Field(typeof(AssetPackRuntimeStore), "_loadRequests");

        private static int _reflectionFailureLogged;

        private static void Postfix(AssetPackRuntimeStore __instance, string identifier)
        {
            if (!FuseConstrainedTextureMemoryPolicy.IsApplied ||
                __instance == null ||
                string.IsNullOrWhiteSpace(identifier) ||
                !FuseAssetPackRegistry.IsFuseManagedStoreIdentifier(__instance.Identifier))
            {
                return;
            }

            try
            {
                if (!(LoadRequestsField?.GetValue(__instance) is IDictionary requests) ||
                    !requests.Contains(identifier))
                {
                    // The stock method clears the dictionary when the whole
                    // bundle reaches zero; there is nothing left to evict.
                    return;
                }

                var requestState = requests[identifier];
                if (requestState == null)
                {
                    return;
                }

                var referenceCountProperty =
                    AccessTools.Property(requestState.GetType(), "ReferenceCount");
                if (!(referenceCountProperty?.GetValue(requestState) is int referenceCount) ||
                    !ShouldEvict(referenceCount))
                {
                    return;
                }

                requests.Remove(identifier);
                FuseUnusedAssetReclaimer.RecordEviction();
            }
            catch (Exception ex)
            {
                if (Interlocked.Exchange(ref _reflectionFailureLogged, 1) == 0)
                {
                    FuseLog.Exception(
                        "FUSE could not evict a zero-reference asset-pack request; " +
                        "later failures will be suppressed",
                        ex);
                }
            }
        }

        internal static bool ShouldEvict(int referenceCount)
        {
            return referenceCount <= 0;
        }
    }
}
