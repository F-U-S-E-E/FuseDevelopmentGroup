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
    internal static class FuseAggregateLoadModelMaterialDefinitionPatch
    {
        private const string AggregateModelLoadIdField = "aggregateModelLoadId";

        private static readonly HashSet<string> LoggedDirectMatches =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> LoggedStoreFailures =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static FieldInfo CurrentLoadIdField;
        private static FieldInfo StoresField;

        private static MethodInfo TargetMethod()
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
}
