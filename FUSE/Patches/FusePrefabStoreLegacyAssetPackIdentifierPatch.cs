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

    [HarmonyPatch(typeof(PrefabStore), "AssetPackForIdentifier")]
    internal static class FusePrefabStoreLegacyAssetPackIdentifierPatch
    {
        private static void Prefix(
            PrefabStore __instance,
            ref string assetPackIdentifier,
            out string __state)
        {
            // Capture the input so the Postfix can produce a single line
            // covering "<incoming> -> <resolved> -> store at <basepath>"
            // — three independent moving parts that together describe
            // the lookup outcome for one call.
            __state = assetPackIdentifier;
            if (FuseAssetPackRegistry.TryResolveLegacyAssetPackIdentifier(
                    __instance,
                    assetPackIdentifier,
                    out var resolved))
            {
                assetPackIdentifier = resolved;
            }
        }

        private static void Postfix(string __state, ref string assetPackIdentifier, AssetPackRuntimeStore __result)
        {
            // Verbose-mode one-shot trace. We dedup by the INCOMING
            // identifier so an asset pack that's queried hundreds of
            // times during a session still produces a single log line.
            if (!FUSE.Infrastructure.FuseSettings.VerboseApplyReportDetails)
            {
                return;
            }

            FuseAssetPackResolutionTrace.LogPackForIdentifierOnce(__state, assetPackIdentifier, __result);
        }
    }
}
