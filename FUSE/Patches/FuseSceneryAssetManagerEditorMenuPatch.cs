using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AssetPack.Runtime;
using HarmonyLib;
using Helpers;
using Model.Database;
using Model.Definition;
using Model.Definition.Data;
using FUSE.Infrastructure;
using FUSE.Loading;

namespace FUSE.Patches
{
    [HarmonyPatch]
    internal static class FuseSceneryAssetManagerEditorMenuPatch
    {
        private static readonly FieldInfo StoresField = AccessTools.Field(typeof(PrefabStore), "_stores");
        private static readonly PropertyInfo PrefabStoreProperty = AccessTools.Property(typeof(SceneryAssetManager), "PrefabStore");
        private static bool LoggedFilterSummary;

        private static MethodInfo TargetMethod()
        {
            return AccessTools.Method(typeof(SceneryAssetManager), "GetSceneryDefinitionIdentifiers");
        }

        private static void Postfix(SceneryAssetManager __instance, ref List<string> __result)
        {
            if (__result == null || __result.Count == 0)
            {
                return;
            }

            try
            {
                var prefabStore = PrefabStoreProperty?.GetValue(__instance, null) as PrefabStore;
                var directOnly = ComputeDirectOnlySceneryIdentifiers(prefabStore);
                var before = __result.Count;
                __result = FilterDirectOnlyIdentifiersForEditorMenu(__result, directOnly);
                var removed = before - __result.Count;
                if (removed > 0 && !LoggedFilterSummary)
                {
                    LoggedFilterSummary = true;
                    FuseLog.Info(
                        $"FUSE filtered {removed} direct mod-folder scenery asset(s) out of the editor asset menu. " +
                        "Install an asset pack normally if it should be available for editor placement.");
                }
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    $"FUSE editor scenery asset menu filter failed softly; direct mod-folder assets may be visible: " +
                    $"{ex.GetBaseException().Message}");
            }
        }

        internal static List<string> FilterDirectOnlyIdentifiersForEditorMenu(
            IEnumerable<string> identifiers,
            ISet<string> directOnlyIdentifiers)
        {
            if (identifiers == null)
            {
                return new List<string>();
            }

            if (directOnlyIdentifiers == null || directOnlyIdentifiers.Count == 0)
            {
                return identifiers.ToList();
            }

            return identifiers
                .Where(identifier => string.IsNullOrEmpty(identifier) || !directOnlyIdentifiers.Contains(identifier))
                .ToList();
        }

        private static HashSet<string> ComputeDirectOnlySceneryIdentifiers(PrefabStore prefabStore)
        {
            var directIdentifiers = new HashSet<string>(StringComparer.Ordinal);
            var installedIdentifiers = new HashSet<string>(StringComparer.Ordinal);
            var stores = StoresField?.GetValue(prefabStore) as IEnumerable<AssetPackRuntimeStore>;
            if (stores == null)
            {
                return directIdentifiers;
            }

            foreach (var store in stores)
            {
                if (store == null)
                {
                    continue;
                }

                Container container;
                try
                {
                    container = store.Container();
                }
                catch
                {
                    continue;
                }

                var isDirectStore = FuseAssetPackRegistry.TryResolveDirectStoreBasePath(store.Identifier, out _);
                foreach (var item in container?.Objects ?? Enumerable.Empty<ContainerItem>())
                {
                    if (item?.Definition is SceneryDefinition && !string.IsNullOrEmpty(item.Identifier))
                    {
                        if (isDirectStore)
                        {
                            directIdentifiers.Add(item.Identifier);
                        }
                        else
                        {
                            installedIdentifiers.Add(item.Identifier);
                        }
                    }
                }
            }

            directIdentifiers.ExceptWith(installedIdentifiers);
            return directIdentifiers;
        }
    }
}
