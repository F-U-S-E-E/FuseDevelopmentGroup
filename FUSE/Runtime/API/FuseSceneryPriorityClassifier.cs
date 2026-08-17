using System;

namespace FUSE.Runtime.API
{
    /// <summary>
    /// Identifies authored structures that should enter the teleport destination
    /// load lane ahead of vegetation. Scenery definitions expose no semantic kind
    /// or prefab bounds before their model loads, so the only pre-load signal is
    /// the authoring id/model identifier. Unknown assets stay on the normal lane.
    /// </summary>
    internal static class FuseSceneryPriorityClassifier
    {
        private static readonly string[] BackgroundFragments =
        {
            "tree",
            "oak",
            "pine",
            "birch",
            "maple",
            "grass",
            "bush",
            "shrub",
            "fern",
            "plant",
            "flower",
            "weed",
            "forest",
            "foliage",
            "aspenflat",
            "rock",
            "boulder"
        };

        private static readonly string[] StructureFragments =
        {
            "building",
            "house",
            "office",
            "depot",
            "station",
            "mill",
            "shed",
            "shop",
            "warehouse",
            "tower",
            "tipple",
            "barn",
            "shack",
            "bungal",
            "hotel",
            "school",
            "church",
            "store",
            "drug",
            "palace",
            "freight",
            "telegraph",
            "caboose",
            "roundhouse",
            "factory",
            "mine",
            "supply",
            "stenzel",
            "dorsey",
            "roane",
            "calloway",
            "sunbeam",
            "genwood",
            "cape",
            "continuous",
            "stall"
        };

        internal static bool IsPriorityStructure(
            string placementId,
            string assetIdentifier)
        {
            // A descriptive placement id such as "HouseTree03" must not turn an
            // actual tree into a structure, so the model identifier gets the
            // background veto before either positive test.
            if (ContainsAny(assetIdentifier, BackgroundFragments))
            {
                return false;
            }

            if (ContainsAny(assetIdentifier, StructureFragments))
            {
                return true;
            }

            if (ContainsAny(placementId, BackgroundFragments))
            {
                return false;
            }

            return ContainsAny(placementId, StructureFragments);
        }

        private static bool ContainsAny(string value, string[] fragments)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            for (var index = 0; index < fragments.Length; index++)
            {
                if (value.IndexOf(
                        fragments[index],
                        StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
