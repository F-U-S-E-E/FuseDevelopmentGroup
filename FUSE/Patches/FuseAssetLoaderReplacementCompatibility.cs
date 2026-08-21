using System;
using System.Linq;
using HarmonyLib;
using FUSE.Infrastructure;

namespace FUSE.Patches
{
    /// <summary>
    /// FUSE replaces AssetLoader's asset-pack discovery. Leaving the old mod's
    /// CheckDefinitions prefix active creates a second set of stores outside
    /// FUSE's quarantine path; one malformed Definitions.json can then abort
    /// the Equipment or Customize window while it enumerates those stores.
    /// Remove only patches owned by AssetLoader. The assembly stays loaded so
    /// packages with a historical UMM dependency continue to see it installed.
    /// </summary>
    internal static class FuseAssetLoaderReplacementCompatibility
    {
        internal const string LegacyHarmonyOwner = "AssetLoader";

        internal static string EnsureInstalled(Harmony harmony)
        {
            if (harmony == null)
            {
                return "unavailable";
            }

            var assetLoaderType = AccessTools.TypeByName("AssetLoader.Patches.AssetLoaderPatch");
            if (assetLoaderType == null ||
                !IsTargetAssemblyName(assetLoaderType.Assembly.GetName().Name))
            {
                return "absent";
            }

            var ownedBefore = CountOwnedPatches();
            if (ownedBefore == 0)
            {
                return "replaced (no active legacy patches)";
            }

            harmony.UnpatchAll(LegacyHarmonyOwner);
            var ownedAfter = CountOwnedPatches();
            if (ownedAfter > 0)
            {
                return $"failed ({ownedAfter} patch(es) remain)";
            }

            FuseLog.Info(
                $"FUSE disabled {ownedBefore} legacy AssetLoader Harmony patch(es). " +
                "FUSE asset discovery remains active; the AssetLoader assembly was not removed.");
            return $"replaced ({ownedBefore} patch(es) disabled)";
        }

        internal static bool IsTargetAssemblyName(string assemblyName) =>
            string.Equals(assemblyName, "AssetLoader", StringComparison.OrdinalIgnoreCase);

        private static int CountOwnedPatches()
        {
            try
            {
                return Harmony.GetAllPatchedMethods()
                    .Select(Harmony.GetPatchInfo)
                    .Where(info => info != null)
                    .Sum(info =>
                        info.Prefixes.Count(patch => IsLegacyOwner(patch.owner)) +
                        info.Postfixes.Count(patch => IsLegacyOwner(patch.owner)) +
                        info.Transpilers.Count(patch => IsLegacyOwner(patch.owner)) +
                        info.Finalizers.Count(patch => IsLegacyOwner(patch.owner)));
            }
            catch
            {
                return 0;
            }
        }

        private static bool IsLegacyOwner(string owner) =>
            string.Equals(owner, LegacyHarmonyOwner, StringComparison.OrdinalIgnoreCase);
    }
}
