using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace FUSE.Converter.Conversion
{
    /// <summary>
    /// Port of <c>_collect_load_references</c> and
    /// <c>ensure_known_compat_loads</c>. Walks a converted fragment
    /// to find load ids referenced by industries / progression and,
    /// for any reference that points at a "compat load"
    /// (Strange Customs supplied them implicitly), injects a
    /// hard-coded load definition so FUSE can resolve the reference
    /// without the modder having to ship the load data themselves.
    /// </summary>
    internal static class LegacyLoadHelpers
    {
        /// <summary>
        /// Recursively walks the supplied token, collecting every
        /// string value attached to a <c>loadId</c> /
        /// <c>convertedLoadId</c> / <c>load</c> property into
        /// <paramref name="sink"/>. Used by
        /// <see cref="EnsureKnownCompatLoads"/> to know which
        /// implicit-load ids need backfilling.
        /// </summary>
        public static void CollectLoadReferences(JToken value, HashSet<string> sink)
        {
            if (value == null || sink == null) return;

            if (value is JArray arr)
            {
                foreach (var item in arr)
                {
                    CollectLoadReferences(item, sink);
                }
                return;
            }

            if (!(value is JObject obj)) return;

            foreach (var prop in obj.Properties())
            {
                if ((prop.Name == "loadId" || prop.Name == "convertedLoadId" || prop.Name == "load")
                    && prop.Value.Type == JTokenType.String)
                {
                    var text = prop.Value.Value<string>()?.Trim();
                    if (!string.IsNullOrEmpty(text))
                    {
                        sink.Add(text);
                    }
                }
                else
                {
                    CollectLoadReferences(prop.Value, sink);
                }
            }
        }

        /// <summary>
        /// For every compat load id referenced by
        /// <paramref name="rail"/>'s operations + progression but NOT
        /// defined in its operations.loads, inject a hard-coded load
        /// definition pulled from
        /// <see cref="LegacyConverterConstants.KnownCompatLoads"/>.
        /// In-place on <paramref name="rail"/>.
        /// </summary>
        public static void EnsureKnownCompatLoads(JObject rail)
        {
            if (rail == null) return;

            var operations = rail["operations"] as JObject;
            var loads = operations?["loads"] as JObject;
            if (loads == null) return;

            var defined = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var prop in loads.Properties())
            {
                defined.Add(prop.Name);
            }

            var referenced = new HashSet<string>(System.StringComparer.Ordinal);
            CollectLoadReferences(operations, referenced);
            CollectLoadReferences(rail["progression"], referenced);

            // Walk in sorted order so the output is deterministic
            // (matches the Python source's `sorted(...)` call).
            var missing = new List<string>();
            foreach (var loadId in referenced)
            {
                if (defined.Contains(loadId)) continue;
                if (LegacyConverterConstants.KnownCompatLoads.ContainsKey(loadId))
                {
                    missing.Add(loadId);
                }
            }
            missing.Sort(System.StringComparer.Ordinal);

            foreach (var loadId in missing)
            {
                loads[loadId] = (JObject)LegacyConverterConstants.KnownCompatLoads[loadId].DeepClone();
            }
        }
    }
}
