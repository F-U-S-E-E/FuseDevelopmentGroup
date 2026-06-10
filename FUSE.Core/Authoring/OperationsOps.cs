using System.Collections.Generic;
using Fuse.Core.Model;

namespace Fuse.Core.Authoring
{
    /// <summary>Industry + load CRUD on a <see cref="FuseOperationsDefinition"/> (Unity-free).</summary>
    public static class OperationsOps
    {
        public static FuseIndustry AddIndustry(FuseOperationsDefinition operations, string id, string name, string areaId = null)
        {
            var industry = new FuseIndustry { Name = name, AreaId = areaId };
            operations.Industries[id] = industry;
            return industry;
        }

        public static bool DeleteIndustry(FuseOperationsDefinition operations, string id) => operations.Industries.Remove(id);

        public static FuseLoad AddLoad(FuseOperationsDefinition operations, string id, string name, string units = "quantity")
        {
            var load = new FuseLoad { Name = name, Units = units };
            operations.Loads[id] = load;
            return load;
        }

        public static bool DeleteLoad(FuseOperationsDefinition operations, string id) => operations.Loads.Remove(id);

        public static string NewIndustryId(FuseOperationsDefinition operations) => AuthoringIds.UniqueId(operations.Industries.Keys, "ind");

        public static string NewLoadId(FuseOperationsDefinition operations) => AuthoringIds.UniqueId(operations.Loads.Keys, "load");

        /// <summary>
        /// Batch variant of <see cref="NewIndustryId(FuseOperationsDefinition)"/> for callers minting
        /// many ids in one operation: build <paramref name="takenIds"/> from <c>operations.Industries.Keys</c>
        /// once, start <paramref name="nextIndex"/> at 1, and reuse both across calls. Each returned
        /// id is added to <paramref name="takenIds"/>, so as long as no ids are removed mid-batch the
        /// sequence matches repeated single-shot calls (first free slot, gaps filled) without
        /// rescanning every key per id.
        /// </summary>
        public static string NewIndustryId(ISet<string> takenIds, ref int nextIndex) => AuthoringIds.UniqueId(takenIds, "ind", ref nextIndex);

        /// <summary>Batch variant of <see cref="NewLoadId(FuseOperationsDefinition)"/>; see <see cref="NewIndustryId(ISet{string}, ref int)"/>.</summary>
        public static string NewLoadId(ISet<string> takenIds, ref int nextIndex) => AuthoringIds.UniqueId(takenIds, "load", ref nextIndex);
    }
}
