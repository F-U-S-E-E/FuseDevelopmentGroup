using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AssetPack.Runtime;
using HarmonyLib;
using Model.Definition;
using Model.Database;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using FUSE.Infrastructure;
using FUSE.Patches;
using UnityEngine;
using Model.Definition.Data;

namespace FUSE.Loading
{
    internal static partial class FuseAssetPackRegistry
    {
        private static readonly FieldInfo PrefabStoreStoresField =
            AccessTools.Field(typeof(PrefabStore), "_stores");

        /// <summary>
        /// Mounts a FUSE-generated definitions folder (e.g. the whistle
        /// picker store) as a fuseasset:// direct store on the supplied
        /// PrefabStore, or refreshes it when already mounted. Unlike the
        /// discovery-driven <see cref="AddDirectAssetPackStores"/> path this
        /// targets a single known folder, so it skips the collision scan:
        /// the folder is FUSE-owned and its identifier can belong to nothing
        /// else. With <paramref name="invalidateContainer"/> the mounted
        /// store's cached container is dropped so the next <c>Container()</c>
        /// call re-reads a rewritten Definitions.json.
        /// </summary>
        internal static bool EnsureGeneratedDirectStore(
            PrefabStore prefabStore,
            string sourcePath,
            bool invalidateContainer)
        {
            if (prefabStore == null || string.IsNullOrWhiteSpace(sourcePath))
            {
                return false;
            }

            var identifier = ToDirectStoreIdentifier(sourcePath);
            try
            {
                if (PrefabStoreStoresField?.GetValue(prefabStore) is System.Collections.IEnumerable stores)
                {
                    foreach (var item in stores)
                    {
                        if (item is AssetPackRuntimeStore store &&
                            string.Equals(store.Identifier, identifier, StringComparison.Ordinal))
                        {
                            if (invalidateContainer)
                            {
                                RuntimeStoreContainerField?.SetValue(store, null);
                            }

                            return true;
                        }
                    }
                }

                var addStore = AccessTools.Method(
                    prefabStore.GetType(),
                    "AddStore",
                    new[] { typeof(string), typeof(AssetPackRuntimeStore.StoreLocation) });
                if (addStore == null)
                {
                    FuseLog.Warning(
                        $"FUSE could not locate PrefabStore.AddStore to mount generated store '{sourcePath}'.");
                    return false;
                }

                addStore.Invoke(prefabStore, new object[]
                {
                    identifier,
                    AssetPackRuntimeStore.StoreLocation.External
                });
                DirectAssetPackStoreIdentifiers.Add(identifier);
                return true;
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE could not mount generated direct store '{sourcePath}'", ex);
                return false;
            }
        }

        internal static void AddDirectAssetPackStores(PrefabStore prefabStore)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            if (prefabStore == null)
            {
                FusePerformanceMetrics.RecordTiming("direct asset pack stores", stopwatch.ElapsedMilliseconds);
                return;
            }

            var sourcePaths = EnumerateAvailableAssetPackFolders().ToArray();
            if (sourcePaths.Length == 0)
            {
                FusePerformanceMetrics.RecordTiming("direct asset pack stores", stopwatch.ElapsedMilliseconds);
                FusePerformanceMetrics.RecordCount("direct asset pack store count", 0);
                return;
            }

            // Detect within-mod asset-bundle collisions BEFORE we register
            // any store. The bundle-path patch consults the collision
            // table on every <c>AssetBundlePath</c> read, so populating
            // it here means the very first <c>LoadAsset</c> on a
            // colliding store already redirects to the winner's bundle.
            // The scan only flags collisions where two pack folders
            // inside the SAME mod share the SAME leaf folder name —
            // that's the structural signal a mod author duplicated a
            // pack (the TOFC root vs SCAssetPacks pattern). It
            // deliberately ignores catalog-identifier overlap because
            // mod authors routinely typo-share catalog identifiers
            // across distinct packs, and earlier attempts that keyed on
            // catalog identifier produced false positives that broke
            // unrelated content.
            FuseAssetCollisionRegistry.ScanForCollisions(
                sourcePaths,
                ReadHostingPackageId,
                ReadHostingPackageFolder);

            MethodInfo addStore;
            try
            {
                addStore = AccessTools.Method(
                    prefabStore.GetType(),
                    "AddStore",
                    new[] { typeof(string), typeof(AssetPackRuntimeStore.StoreLocation) });
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE could not locate PrefabStore.AddStore for direct asset pack mounting", ex);
                FuseLog.Warning(
                    "FUSE direct asset pack mounting is unavailable. " +
                    "Set Settings.MirrorAssetPacksToLocalLow=true in Info.json to use the slower LocalLow mirror fallback.");
                FusePerformanceMetrics.RecordTiming("direct asset pack stores", stopwatch.ElapsedMilliseconds);
                FusePerformanceMetrics.RecordCount("direct asset pack store count", DirectAssetPackStoreIdentifiers.Count);
                return;
            }

            if (addStore == null)
            {
                FuseLog.Warning("FUSE could not locate PrefabStore.AddStore for direct asset pack mounting.");
                FuseLog.Warning(
                    "FUSE direct asset pack mounting is unavailable. " +
                    "Set Settings.MirrorAssetPacksToLocalLow=true in Info.json to use the slower LocalLow mirror fallback.");
                FusePerformanceMetrics.RecordTiming("direct asset pack stores", stopwatch.ElapsedMilliseconds);
                FusePerformanceMetrics.RecordCount("direct asset pack store count", DirectAssetPackStoreIdentifiers.Count);
                return;
            }

            var added = 0;
            var skippedAlreadyAdded = 0;
            foreach (var sourcePath in sourcePaths)
            {
                var identifier = ToDirectStoreIdentifier(sourcePath);
                // MountAllAvailableAssetPacks is invoked twice per session (FusePlugin.Load
                // then FuseDataPackageDiscovery.LoadPackagesFromDisk). Without this check the
                // second pass re-invokes PrefabStore.AddStore for every identifier we already
                // mounted, leaving two AssetPackRuntimeStore instances per asset pack — each
                // with its own _container cache. Anything that iterates stores then triggers
                // two ContainerSerialization.Deserialize calls per pack, double-firing every
                // legacy Postfix on Deserialize (notably LegosLibraryOfStuff's, which mutates
                // shared Component instances on each pass — breaking per-car component-group
                // toggles like ERIE LOGO.)
                if (DirectAssetPackStoreIdentifiers.Contains(identifier))
                {
                    skippedAlreadyAdded++;
                    continue;
                }

                try
                {
                    addStore.Invoke(prefabStore, new object[]
                    {
                        identifier,
                        AssetPackRuntimeStore.StoreLocation.External
                    });
                    DirectAssetPackStoreIdentifiers.Add(identifier);
                    added++;
                }
                catch (Exception ex)
                {
                    FuseLog.Exception($"FUSE could not add direct asset pack store '{sourcePath}'", ex);
                }
            }

            if (added > 0 || skippedAlreadyAdded > 0)
            {
                FuseLog.Info(
                    $"FUSE added {added} direct asset pack store(s) to PrefabStore; " +
                    $"skippedAlreadyAdded={skippedAlreadyAdded}.");
            }

            // Verbose mode dumps the post-discovery state of the legacy
            // alias map and the PrefabStore._stores list so a follow-up
            // failure can be diagnosed without restarting under a
            // breakpoint. The dump is gated behind the existing
            // VerboseApplyReportDetails setting because both tables can
            // be hundreds of lines on a heavily-modded install.
            if (FuseSettings.VerboseApplyReportDetails)
            {
                try
                {
                    DumpAssetPackResolutionDiagnostics(prefabStore);
                }
                catch (Exception ex)
                {
                    FuseLog.Exception($"FUSE verbose asset-pack resolution diagnostics failed softly", ex);
                }
            }

            // Warm-mount the generated whistle picker store left by the last
            // audio registration. A brand-new PrefabStore only knows the
            // discovery-driven mod folders above; without this, FUSE whistles
            // would stay invisible until the next RegisterDefinition re-syncs
            // (which also refreshes the file if the whistle set changed).
            try
            {
                var generatedWhistleFolder = FUSE.Runtime.API.FuseWhistleDefinitionStore.StoreFolderPath;
                if (Directory.Exists(generatedWhistleFolder))
                {
                    EnsureGeneratedDirectStore(prefabStore, generatedWhistleFolder, invalidateContainer: false);
                }
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE could not warm-mount the generated whistle store", ex);
            }

            FusePerformanceMetrics.RecordTiming("direct asset pack stores", stopwatch.ElapsedMilliseconds);
            FusePerformanceMetrics.RecordCount("direct asset pack store count", DirectAssetPackStoreIdentifiers.Count);
        }

        /// <summary>
        /// Emits the legacy-identifier alias map and a snapshot of
        /// <c>PrefabStore._stores</c> to FUSE.log. Designed to be called
        /// once at startup when
        /// <see cref="FuseSettings.VerboseApplyReportDetails"/> is on,
        /// so a follow-up "wrong bundle picked for X" report can be
        /// answered just from the captured log without re-running under
        /// a debugger.
        /// </summary>
        internal static void DumpAssetPackResolutionDiagnostics(PrefabStore prefabStore)
        {
            // Alias map dump first — most useful when diagnosing
            // "AssetPackForIdentifier returned the wrong store",
            // because the patch fed through this table before exact
            // lookup ran.
            try
            {
                var aliases = GetLegacyAssetPackAliases();
                FuseLog.Info(
                    $"FUSE verbose asset-pack resolution: legacy alias map " +
                    $"contains {aliases.Count} entr(y/ies).");
                foreach (var pair in aliases.OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase))
                {
                    FuseLog.Info($"FUSE alias-map: '{pair.Key}' -> '{pair.Value}'");
                }
            }
            catch (Exception ex)
            {
                FuseLog.Warning($"FUSE alias map dump skipped: {ex.GetBaseException().Message}");
            }

            // Stores snapshot — order matters for
            // AssetPackContainingIdentifier (first-match wins), so dump
            // each store with its index, identifier, and resolved
            // BasePath.
            try
            {
                if (prefabStore == null)
                {
                    FuseLog.Info("FUSE verbose asset-pack resolution: PrefabStore reference is null; cannot dump _stores.");
                    return;
                }

                var storesField = AccessTools.Field(typeof(PrefabStore), "_stores");
                if (storesField == null)
                {
                    FuseLog.Info("FUSE verbose asset-pack resolution: PrefabStore._stores field not found; cannot dump.");
                    return;
                }

                if (!(storesField.GetValue(prefabStore) is System.Collections.IList stores))
                {
                    FuseLog.Info("FUSE verbose asset-pack resolution: PrefabStore._stores is null or not a list.");
                    return;
                }

                FuseLog.Info(
                    $"FUSE verbose asset-pack resolution: PrefabStore._stores contains " +
                    $"{stores.Count} store(s) (registration order):");
                for (var index = 0; index < stores.Count; index++)
                {
                    var store = stores[index] as AssetPackRuntimeStore;
                    if (store == null)
                    {
                        FuseLog.Info($"FUSE stores[{index}]: <null>");
                        continue;
                    }

                    string basePath = null;
                    try
                    {
                        basePath = (AccessTools.Property(typeof(AssetPackRuntimeStore), "BasePath")
                            ?.GetValue(store, null)) as string;
                    }
                    catch
                    {
                        basePath = "<base-path-error>";
                    }

                    FuseLog.Info(
                        $"FUSE stores[{index}]: location={store.Location} identifier='{store.Identifier}' " +
                        $"basePath='{basePath ?? "<null>"}'");
                }
            }
            catch (Exception ex)
            {
                FuseLog.Warning($"FUSE _stores dump skipped: {ex.GetBaseException().Message}");
            }
        }

        /// <summary>
        /// Walks the supplied pack folder up to its mod-package root and
        /// returns whatever package id that mod's Info.json / Definition.json
        /// declares (or the folder name as a final fallback). Used by the
        /// collision registry to attribute every collision participant to
        /// a host mod for reporting.
        /// </summary>
        private static string ReadHostingPackageId(string packFolderAbsolutePath)
        {
            var folder = ReadHostingPackageFolder(packFolderAbsolutePath);
            if (string.IsNullOrWhiteSpace(folder))
            {
                return null;
            }
            return TryReadPackageId(folder) ?? Path.GetFileName(folder);
        }

        /// <summary>
        /// Walks the supplied pack folder up to its mod-package root and
        /// returns the absolute path of that mod folder (the directory
        /// living directly under the Mods folder). Returns <c>null</c> if
        /// no such ancestor exists. Used by the collision registry to
        /// scope collisions to within a single mod — two packs are only
        /// considered colliding when this helper returns the SAME folder
        /// for both.
        /// </summary>
        private static string ReadHostingPackageFolder(string packFolderAbsolutePath)
        {
            if (string.IsNullOrWhiteSpace(packFolderAbsolutePath))
            {
                return null;
            }

            try
            {
                var modsRoot = FuseDataPackageDiscovery.GetModsRoot();
                if (string.IsNullOrWhiteSpace(modsRoot))
                {
                    return null;
                }
                var modsRootFull = Path.GetFullPath(modsRoot);
                var cursor = Path.GetFullPath(packFolderAbsolutePath);
                // Climb until the parent IS the mods root — the directory
                // we are sitting in at that point IS the mod-package
                // directory we want to return.
                while (!string.IsNullOrEmpty(cursor))
                {
                    var parent = Path.GetDirectoryName(cursor);
                    if (string.IsNullOrEmpty(parent))
                    {
                        break;
                    }

                    if (string.Equals(parent, modsRootFull, StringComparison.OrdinalIgnoreCase))
                    {
                        return cursor;
                    }

                    cursor = parent;
                }
            }
            catch
            {
                // Best-effort; the caller treats null as "skip this folder
                // from collision detection" which is the safe default.
            }

            return null;
        }

        internal static bool TryResolveDirectStoreBasePath(string identifier, out string path)
        {
            path = null;
            if (string.IsNullOrWhiteSpace(identifier) ||
                !identifier.StartsWith(DirectStorePrefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            try
            {
                path = Uri.UnescapeDataString(identifier.Substring(DirectStorePrefix.Length));
                return !string.IsNullOrWhiteSpace(path);
            }
            catch
            {
                return false;
            }
        }

        internal static bool TryLoadSanitizedDirectContainer(AssetPackRuntimeStore store, out Container container)
        {
            container = null;
            if (store == null || !TryResolveDirectStoreBasePath(store.Identifier, out var basePath))
            {
                return false;
            }

            try
            {
                var cached = RuntimeStoreContainerField?.GetValue(store) as Container;
                if (cached != null)
                {
                    container = cached;
                    return true;
                }

                var definitionsPath = Path.Combine(basePath, "Definitions.json");
                if (!File.Exists(definitionsPath))
                {
                    container = new Container();
                    RuntimeStoreContainerField?.SetValue(store, container);
                    return true;
                }

                var removedByKind = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var sourceText = File.ReadAllText(definitionsPath);
                container = LoadResilientDirectContainer(sourceText, store?.Identifier, removedByKind);
                SanitizeDeserializedDirectContainer(container);
                RuntimeStoreContainerField?.SetValue(store, container);

                if (removedByKind.Count > 0 && SanitizedDirectContainerWarnings.Add(store.Identifier))
                {
                    var packId = Path.GetFileName(basePath);
                    var summary = string.Join(", ", removedByKind.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                        .Select(item => $"{item.Key}={item.Value}"));
                    FuseLog.Info(
                        $"FUSE dropped unbindable component(s) from direct asset pack '{packId}' Definitions.json in memory: {summary}. " +
                        "Their defining library mod is likely missing or disabled; the source mod files were not modified.");
                }

                return true;
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    $"FUSE could not load direct asset pack definitions for '{store.Identifier}' from '{basePath}': {ex.Message}");
                return false;
            }
        }

        private static readonly HashSet<string> DeserializeBypassFallbackWarnings =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static Container LoadResilientDirectContainer(string sourceText, string storeIdentifier, IDictionary<string, int> droppedByKind)
        {
            // No reflection-based strict bypass available (rare): defer to the legacy
            // deserialize path, which itself falls back to ContainerSerialization.Deserialize.
            if (ContainerSerializerSettingsMethod == null)
            {
                return DeserializeContainerBypassingPostfix(sourceText, storeIdentifier);
            }

            try
            {
                // Happy path: with the defining library mods loaded, the game's
                // JsonSubtypes converter binds every component kind — including the
                // runtime-registered customization kinds (MaterialColorizerComponent,
                // DefaultLivelryComponent, ComponentGroup, ...) the old static
                // allow-list used to strip. The whole pack deserializes verbatim.
                return BypassDeserialize(sourceText);
            }
            catch (Exception)
            {
                // A component kind could not be bound — almost always because the
                // mod that defines it is absent or disabled. Drop ONLY the unbindable
                // components (never the whole pack) and retry, so every bindable
                // component still reaches the game. If the failure was not a component
                // kind (so nothing is dropped), the retry throws again and the caller
                // falls through to the game's native loader.
                var filtered = FilterUnbindableComponents(sourceText, droppedByKind, CanBindComponent);
                return BypassDeserialize(filtered);
            }
        }

        /// <summary>
        /// Deserializes a <see cref="Container"/> from Definitions.json text using the
        /// same serializer settings the game's own loader uses, but WITHOUT routing
        /// through the public <c>ContainerSerialization.Deserialize</c> (whose legacy
        /// Harmony postfixes double-apply edits — see <see cref="ContainerSerializerSettingsMethod"/>).
        /// Throws if any component kind cannot be bound.
        /// </summary>
        private static Container BypassDeserialize(string text)
        {
            var settings = (JsonSerializerSettings)ContainerSerializerSettingsMethod.Invoke(null, null);
            var container = JsonConvert.DeserializeObject<Container>(text, settings);
            container?.Awake();
            return container;
        }

        /// <summary>
        /// Returns <c>true</c> when the supplied component JSON binds to a concrete
        /// <see cref="Model.Definition.Component"/> subtype under the game's live JsonSubtypes
        /// registry (which loaded mods extend at runtime). Used only on the resilient retry
        /// path to single out the components whose defining mod is missing.
        /// </summary>
        private static bool CanBindComponent(JObject componentJson)
        {
            if (componentJson == null)
            {
                return false;
            }

            try
            {
                var settings = (JsonSerializerSettings)ContainerSerializerSettingsMethod.Invoke(null, null);
                return JsonConvert.DeserializeObject<Model.Definition.Component>(componentJson.ToString(), settings) != null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static Container DeserializeContainerBypassingPostfix(string text, string storeIdentifier)
        {
            if (ContainerSerializerSettingsMethod != null)
            {
                try
                {
                    return BypassDeserialize(text);
                }
                catch (Exception ex)
                {
                    var key = storeIdentifier ?? "<unknown>";
                    if (DeserializeBypassFallbackWarnings.Add(key))
                    {
                        FuseLog.Warning(
                            $"FUSE direct asset pack '{key}' fell back to ContainerSerialization.Deserialize " +
                            $"because the reflection-based bypass failed: {ex.GetBaseException().Message}. " +
                            "Old-loader Deserialize postfixes will re-fire for this pack, which may double-apply " +
                            "their edits and break per-car component-group toggles.");
                    }
                }
            }

            return ContainerSerialization.Deserialize(text);
        }

        private static void SanitizeDeserializedDirectContainer(Container container)
        {
            if (container?.Objects == null)
            {
                return;
            }

            foreach (var item in container.Objects)
            {
                var material = item?.Definition as MaterialDefinition;
                if (material == null)
                {
                    continue;
                }

                FusePrefabStoreMaterialDefinitionsPatch.SanitizeMaterialDefinition(
                    new TypedContainerItem<MaterialDefinition>
                    {
                        Identifier = item.Identifier,
                        Metadata = item.Metadata,
                        Definition = material
                    });
            }
        }
    }
}
