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
    internal static class FusePrefabStoreMaterialDefinitionsPatch
    {
        private static readonly HashSet<string> WarnedNullFieldLists = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> WarnedNullFieldPairs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static MethodInfo TargetMethod()
        {
            return AccessTools.Method(typeof(PrefabStore), "AllDefinitionInfosOfType")
                ?.MakeGenericMethod(typeof(MaterialDefinition));
        }

        private static void Postfix(ref IEnumerable<TypedContainerItem<MaterialDefinition>> __result)
        {
            __result = SanitizeMaterialDefinitions(__result);
        }

        private static IEnumerable<TypedContainerItem<MaterialDefinition>> SanitizeMaterialDefinitions(
            IEnumerable<TypedContainerItem<MaterialDefinition>> items)
        {
            foreach (var item in items ?? Enumerable.Empty<TypedContainerItem<MaterialDefinition>>())
            {
                SanitizeMaterialDefinition(item);
                yield return item;
            }
        }

        /// <summary>
        /// Pure-data sanitizer for a single
        /// <see cref="MaterialDefinition"/>: guarantees a non-null
        /// <see cref="MaterialDefinition.Fields"/> list and drops any
        /// null entries inside it. Internal so the body unit tests
        /// in FUSE.Tests can call it directly with crafted shapes
        /// the upstream PrefabStore enumeration can produce
        /// (null definition, null Fields list, mixed null/valid
        /// FieldPairs). Each fixup is logged at most once per
        /// material identifier so a single corrupted asset doesn't
        /// spam the log every frame.
        /// </summary>
        internal static void SanitizeMaterialDefinition(TypedContainerItem<MaterialDefinition> item)
        {
            var definition = item?.Definition;
            if (definition == null)
            {
                return;
            }

            var identifier = MaterialIdentifier(item, definition);
            if (definition.Fields == null)
            {
                definition.Fields = new List<MaterialDefinition.FieldPair>();
                if (WarnedNullFieldLists.Add(identifier))
                {
                    FuseLog.Warning($"FUSE sanitized material definition '{identifier}' because its fields list was null.");
                }

                return;
            }

            var removedCount = definition.Fields.RemoveAll(field => field == null);
            if (removedCount > 0 && WarnedNullFieldPairs.Add(identifier))
            {
                FuseLog.Warning($"FUSE sanitized material definition '{identifier}' by removing {removedCount} null field item(s).");
            }
        }

        /// <summary>
        /// Test seam: clears the one-fixup-per-identifier log-dedup
        /// state so each unit test starts from a clean slate.
        /// Production code never calls this; tests call it from
        /// SetUp/TearDown so observed log-fire behaviour is
        /// deterministic across test runs.
        /// </summary>
        internal static void ResetSanitizerLoggingForTests()
        {
            WarnedNullFieldLists.Clear();
            WarnedNullFieldPairs.Clear();
        }

        private static string MaterialIdentifier(
            TypedContainerItem<MaterialDefinition> item,
            MaterialDefinition definition)
        {
            if (!string.IsNullOrWhiteSpace(item?.Identifier))
            {
                return item.Identifier;
            }

            if (!string.IsNullOrWhiteSpace(definition.AssetIdentifier))
            {
                return definition.AssetIdentifier;
            }

            return "<unknown>";
        }
    }
}
