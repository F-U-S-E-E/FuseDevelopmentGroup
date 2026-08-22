using System;
using System.Collections.Generic;
using System.Linq;

namespace FUSE.Loading
{
    /// <summary>
    /// Legacy package identifiers whose runtime contract is supplied by FUSE.
    /// These are virtual capabilities, not ordinary data packages: they satisfy
    /// dependency checks, but do not create a data-package ordering node because
    /// the FUSE runtime is initialized before discovered packages are applied.
    /// </summary>
    internal static class FuseReplacementCapabilityCatalog
    {
        private static readonly string[] ExplicitPackageIds =
        {
            "FUSE",
            "railroader",
            "Railloader",
            "RailLoader.Injector",
            "RailLoader.Interchange",
            "AssetLoader",
            "AlinaNova21.AlinasMapMod",
            "AlinasMapMod",
            "AlinaMapMod",
            "AlinaNova21.MapEditor",
            "MapEditor",
            "MMapEditor",
            "Zamu.ConfusingSupplements",
            "Zamu.FallFromGrace",
            "Zamu.AbsoluteMadness",
            "Zamu.SomeKindOfMadness",
            "Zamu.ForYourConvenience",
            "Zamu.StrangeCustoms",
            "StrangeCustoms",
            "ConfusingSupplements",
            "FallFromGrace",
            "AbsoluteMadness",
            "SomeKindOfMadness",
            "ForYourConvenience"
        };

        private static readonly HashSet<string> ExplicitPackageIdSet =
            new HashSet<string>(ExplicitPackageIds.Select(Normalize), StringComparer.OrdinalIgnoreCase);

        internal static IEnumerable<string> AdvertisedPackageIds => ExplicitPackageIds;

        internal static bool IsProvided(string packageId)
        {
            var normalized = Normalize(packageId);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return false;
            }

            // Do not use a broad Zamu.* match here. Several ZAMU gameplay mods
            // are still hosted and do not yet have native parity; advertising
            // those ids would silently waive a real dependency and regress the
            // dependent package.
            return ExplicitPackageIdSet.Contains(normalized);
        }

        internal static string Normalize(string packageId)
        {
            var value = (packageId ?? string.Empty).Trim();
            while (value.EndsWith(".FUSE", StringComparison.OrdinalIgnoreCase) ||
                   value.EndsWith(".RAIL", StringComparison.OrdinalIgnoreCase))
            {
                value = value.Substring(0, value.Length - 5);
            }

            if (string.Equals(value, "rail-loader", StringComparison.OrdinalIgnoreCase))
            {
                return "Railloader";
            }

            return value;
        }
    }
}
