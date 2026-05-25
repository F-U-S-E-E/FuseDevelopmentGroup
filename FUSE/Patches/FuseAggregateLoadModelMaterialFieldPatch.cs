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

    [HarmonyPatch]
    internal static class FuseAggregateLoadModelMaterialFieldPatch
    {
        private static readonly HashSet<string> WarnedLookupFailures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> WarnedNullFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> WarnedNullFieldLists = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static MethodInfo TargetMethod()
        {
            var type = AccessTools.TypeByName("RollingStock.LoadModels.AggregateLoadModelController");
            return type == null
                ? null
                : AccessTools.Method(type, "TryGetField");
        }

        private static bool Prefix(MaterialDefinition definition, string key, ref string value, ref bool __result)
        {
            __result = TryGetFieldSafely(definition, key, out value);
            return false;
        }

        /// <summary>
        /// Hardened replacement for
        /// <c>AggregateLoadModelController.TryGetField</c>. The
        /// stock implementation throws on the malformed
        /// <see cref="MaterialDefinition.Fields"/> lists shipped by
        /// some FUSE-loaded asset packs (null list, null entries,
        /// covariance-broken backing array); we catch every failure
        /// mode and fall through to a clean "no match" so the
        /// downstream load-model resolution doesn't blow up the
        /// frame. Internal so the body unit tests in FUSE.Tests can
        /// exercise each branch directly with crafted definitions.
        /// </summary>
        internal static bool TryGetFieldSafely(MaterialDefinition definition, string key, out string value)
        {
            value = null;
            if (definition == null)
            {
                return false;
            }

            var identifier = MaterialIdentifier(definition);
            if (definition.Fields == null)
            {
                if (WarnedNullFieldLists.Add($"{identifier}|{key}"))
                {
                    FuseLog.Warning($"FUSE ignored material field lookup '{key}' for '{identifier}' because its fields list was null.");
                }

                return false;
            }

            try
            {
                for (var index = 0; index < definition.Fields.Count; index++)
                {
                    var field = definition.Fields[index];
                    if (field == null)
                    {
                        if (WarnedNullFields.Add($"{identifier}|{index}"))
                        {
                            FuseLog.Warning($"FUSE skipped null material field item {index} for '{identifier}'.");
                        }

                        continue;
                    }

                    if (!string.Equals(field.Key, key, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    value = field.Value;
                    return true;
                }
            }
            catch (Exception ex)
            {
                if (WarnedLookupFailures.Add($"{identifier}|{key}|{ex.GetType().FullName}"))
                {
                    FuseLog.Exception($"FUSE ignored material field lookup '{key}' for '{identifier}' after exception", ex);
                }
            }

            return false;
        }

        private static string MaterialIdentifier(MaterialDefinition definition)
        {
            if (!string.IsNullOrWhiteSpace(definition.AssetIdentifier))
            {
                return definition.AssetIdentifier;
            }

            return "<unknown>";
        }

        /// <summary>
        /// Test seam: clears the one-per-identifier warning dedup
        /// state so each unit test starts from a clean slate.
        /// Production code never calls this.
        /// </summary>
        internal static void ResetLookupLoggingForTests()
        {
            WarnedLookupFailures.Clear();
            WarnedNullFields.Clear();
            WarnedNullFieldLists.Clear();
        }
    }
}
