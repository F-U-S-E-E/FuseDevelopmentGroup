using System.Collections.Generic;
using Fuse.Core.Model;

namespace Fuse.Core.Authoring
{
    /// <summary>Scenery + spliney CRUD on a <see cref="FuseWorldDefinition"/> (Unity-free).</summary>
    public static class WorldOps
    {
        public static FuseScenery AddScenery(FuseWorldDefinition world, string id, string assetIdentifier, FuseVector3 position, FuseVector3 rotation)
        {
            var scenery = new FuseScenery
            {
                AssetIdentifier = assetIdentifier,
                Position = position,
                Rotation = rotation,
                Scale = FuseVector3.one,
            };
            world.Scenery[id] = scenery;
            return scenery;
        }

        public static bool DeleteScenery(FuseWorldDefinition world, string id) => world.Scenery.Remove(id);

        public static FuseSpliney AddSpliney(FuseWorldDefinition world, string id, string type, FuseSplineyPoint[] points)
        {
            var spliney = new FuseSpliney { Type = type, Points = points };
            world.Splineys[id] = spliney;
            return spliney;
        }

        public static bool DeleteSpliney(FuseWorldDefinition world, string id) => world.Splineys.Remove(id);

        public static string NewSceneryId(FuseWorldDefinition world) => UniqueId(world.Scenery.Keys, "scn");

        public static string NewSplineyId(FuseWorldDefinition world) => UniqueId(world.Splineys.Keys, "spl");

        private static string UniqueId(IEnumerable<string> existing, string prefix)
        {
            var set = new HashSet<string>(existing);
            var i = 1;
            while (set.Contains($"{prefix}_{i:D4}"))
            {
                i++;
            }

            return $"{prefix}_{i:D4}";
        }
    }
}
