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
    [HarmonyPatch(typeof(PrefabStore), "Create")]
    internal static class FusePrefabStoreAssetPackPatch
    {
        private static void Postfix(PrefabStore __result)
        {
            try
            {
                FuseAssetPackRegistry.AddDirectAssetPackStores(__result);
            }
            catch (System.Exception ex)
            {
                FuseLog.Warning($"FUSE direct asset pack store patch failed softly: {ex.Message}");
            }
        }
    }

    [HarmonyPatch]
    internal static class FusePrefabStoreMaterialDefinitionsPatch
    {
        private static readonly HashSet<string> WarnedNullFieldLists = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> WarnedNullFieldPairs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static MethodBase TargetMethod()
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

        private static void SanitizeMaterialDefinition(TypedContainerItem<MaterialDefinition> item)
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

    [HarmonyPatch]
    internal static class FuseAggregateLoadModelMaterialFieldPatch
    {
        private static readonly HashSet<string> WarnedLookupFailures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> WarnedNullFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> WarnedNullFieldLists = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static MethodBase TargetMethod()
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

        private static bool TryGetFieldSafely(MaterialDefinition definition, string key, out string value)
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
                    FuseLog.Warning($"FUSE ignored material field lookup '{key}' for '{identifier}' after exception: {ex.Message}");
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
    }

    [HarmonyPatch]
    internal static class FuseAggregateLoadModelMaterialDefinitionPatch
    {
        private const string AggregateModelLoadIdField = "aggregateModelLoadId";

        private static readonly HashSet<string> LoggedDirectMatches =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> LoggedStoreFailures =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static FieldInfo CurrentLoadIdField;
        private static FieldInfo StoresField;

        private static MethodBase TargetMethod()
        {
            var type = AccessTools.TypeByName("RollingStock.LoadModels.AggregateLoadModelController");
            return type == null
                ? null
                : AccessTools.Method(type, "TryGetMaterialDefinition");
        }

        private static bool Prefix(
            object __instance,
            IPrefabStore prefabStore,
            ref TypedContainerItem<MaterialDefinition> materialDefinitionItem,
            ref bool __result)
        {
            var loadId = GetCurrentLoadIdField()?.GetValue(__instance) as string;
            if (string.IsNullOrWhiteSpace(loadId) || prefabStore == null)
            {
                return true;
            }

            if (!TryFindExactAggregateMaterial(prefabStore, loadId, out var exactMatch, out var storeIdentifier))
            {
                return true;
            }

            materialDefinitionItem = exactMatch;
            __result = true;
            if (LoggedDirectMatches.Add(loadId))
            {
                FuseLog.Info(
                    $"FUSE aggregate material lookup resolved load '{loadId}' " +
                    $"to material definition '{exactMatch.Identifier}' from asset store '{storeIdentifier}'.");
            }

            return false;
        }

        private static bool TryFindExactAggregateMaterial(
            IPrefabStore prefabStore,
            string loadId,
            out TypedContainerItem<MaterialDefinition> materialDefinitionItem,
            out string storeIdentifier)
        {
            materialDefinitionItem = null;
            storeIdentifier = null;

            foreach (var store in EnumerateStores(prefabStore))
            {
                Container container;
                try
                {
                    container = store.Container();
                }
                catch (Exception ex)
                {
                    if (LoggedStoreFailures.Add(store.Identifier))
                    {
                        FuseLog.Warning(
                            $"FUSE aggregate material lookup skipped asset store '{store.Identifier}' " +
                            $"because its definitions could not be inspected: {ex.Message}");
                    }

                    continue;
                }

                foreach (var item in container?.Objects ?? Enumerable.Empty<ContainerItem>())
                {
                    var definition = item?.Definition as MaterialDefinition;
                    if (definition == null ||
                        !TryGetAggregateModelLoadId(definition, out var aggregateLoadId) ||
                        !string.Equals(aggregateLoadId, loadId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    materialDefinitionItem = new TypedContainerItem<MaterialDefinition>
                    {
                        Identifier = item.Identifier,
                        Metadata = item.Metadata,
                        Definition = definition
                    };
                    storeIdentifier = store.Identifier;
                    return true;
                }
            }

            return false;
        }

        private static IEnumerable<AssetPackRuntimeStore> EnumerateStores(IPrefabStore prefabStore)
        {
            var stores = GetStoresField()?.GetValue(prefabStore) as IEnumerable<AssetPackRuntimeStore>;
            return stores ?? Enumerable.Empty<AssetPackRuntimeStore>();
        }

        private static bool TryGetAggregateModelLoadId(MaterialDefinition definition, out string value)
        {
            value = null;
            if (definition?.Fields == null)
            {
                return false;
            }

            foreach (var field in definition.Fields)
            {
                if (field == null)
                {
                    continue;
                }

                if (string.Equals(field.Key, AggregateModelLoadIdField, StringComparison.Ordinal))
                {
                    value = field.Value;
                    return !string.IsNullOrWhiteSpace(value);
                }
            }

            return false;
        }

        private static FieldInfo GetCurrentLoadIdField()
        {
            if (CurrentLoadIdField != null)
            {
                return CurrentLoadIdField;
            }

            var type = AccessTools.TypeByName("RollingStock.LoadModels.AggregateLoadModelController");
            CurrentLoadIdField = type == null
                ? null
                : AccessTools.Field(type, "_currentLoadId");
            return CurrentLoadIdField;
        }

        private static FieldInfo GetStoresField()
        {
            if (StoresField != null)
            {
                return StoresField;
            }

            StoresField = AccessTools.Field(typeof(PrefabStore), "_stores");
            return StoresField;
        }
    }

    [HarmonyPatch(typeof(AssetPackRuntimeStore), "Container")]
    internal static class FuseAssetPackRuntimeStoreContainerPatch
    {
        private static bool Prefix(AssetPackRuntimeStore __instance, ref Container __result)
        {
            try
            {
                if (FuseAssetPackRegistry.TryLoadSanitizedDirectContainer(__instance, out var container))
                {
                    __result = container;
                    return false;
                }
            }
            catch (System.Exception ex)
            {
                FuseLog.Warning($"FUSE direct asset pack container patch failed softly: {ex.Message}");
            }

            return true;
        }

        private static void Postfix(AssetPackRuntimeStore __instance, ref Container __result)
        {
            try
            {
                FuseLegacyContainerMixintoRegistry.ApplyToContainer(__instance, __result);
            }
            catch (System.Exception ex)
            {
                FuseLog.Warning($"FUSE legacy support container mixinto patch failed softly: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(PrefabStore), "AssetPackForIdentifier")]
    internal static class FusePrefabStoreLegacyAssetPackIdentifierPatch
    {
        private static void Prefix(ref string assetPackIdentifier)
        {
            if (FuseAssetPackRegistry.TryResolveLegacyAssetPackIdentifier(assetPackIdentifier, out var resolved))
            {
                assetPackIdentifier = resolved;
            }
        }
    }

    [HarmonyPatch]
    internal static class FuseAssetPackRuntimeStoreBasePathPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.PropertyGetter(typeof(AssetPackRuntimeStore), "BasePath");
        }

        private static bool Prefix(AssetPackRuntimeStore __instance, ref string __result)
        {
            if (__instance != null &&
                FuseAssetPackRegistry.TryResolveDirectStoreBasePath(__instance.Identifier, out var path))
            {
                __result = path;
                return false;
            }

            return true;
        }
    }
}
