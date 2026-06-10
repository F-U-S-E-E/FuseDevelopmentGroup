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

        public static string NewSceneryId(FuseWorldDefinition world) => AuthoringIds.UniqueId(world.Scenery.Keys, "scn");

        public static string NewSplineyId(FuseWorldDefinition world) => AuthoringIds.UniqueId(world.Splineys.Keys, "spl");

        /// <summary>
        /// Batch variant of <see cref="NewSceneryId(FuseWorldDefinition)"/> for callers minting many
        /// ids in one operation: build <paramref name="takenIds"/> from <c>world.Scenery.Keys</c>
        /// once, start <paramref name="nextIndex"/> at 1, and reuse both across calls. Each returned
        /// id is added to <paramref name="takenIds"/>, so as long as no ids are removed mid-batch the
        /// sequence matches repeated single-shot calls (first free slot, gaps filled) without
        /// rescanning every key per id.
        /// </summary>
        public static string NewSceneryId(ISet<string> takenIds, ref int nextIndex) => AuthoringIds.UniqueId(takenIds, "scn", ref nextIndex);

        /// <summary>Batch variant of <see cref="NewSplineyId(FuseWorldDefinition)"/>; see <see cref="NewSceneryId(ISet{string}, ref int)"/>.</summary>
        public static string NewSplineyId(ISet<string> takenIds, ref int nextIndex) => AuthoringIds.UniqueId(takenIds, "spl", ref nextIndex);
    }
}
