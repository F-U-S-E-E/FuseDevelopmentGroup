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

        public static string NewIndustryId(FuseOperationsDefinition operations) => UniqueId(operations.Industries.Keys, "ind");

        public static string NewLoadId(FuseOperationsDefinition operations) => UniqueId(operations.Loads.Keys, "load");

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
