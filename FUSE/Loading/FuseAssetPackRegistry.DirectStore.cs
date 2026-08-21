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

        private static PrefabStore _activePrefabStore;

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

            _activePrefabStore = prefabStore;

            // AssetLoader supported definitions-only immediate child folders
            // in addition to ordinary Catalog.json stores. Build that routing
            // table before any store's Container is opened.
            RefreshLegacyDefinitionOverrides();
            InvalidateLegacyDefinitionOverrideTargetContainers(prefabStore);

            var sourcePaths = EnumerateAvailableAssetPackFolders().ToArray();
            if (sourcePaths.Length == 0)
            {
                ReplaceStoreIdentifierMappings(null);
                ValidateLegacyDefinitionOverrideTargets(prefabStore);
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

            // AssetLoader has already registered its normal stores by the time
            // PrefabStore.Create returns. Index by resolved physical BasePath so
            // FUSE can reuse those exact stores instead of appending a second
            // store (and a second Container cache) for the same folder.
            var existingStoreIndex = IndexExistingStoresByRegistrationOrder(prefabStore);

            MethodInfo addStore = null;
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
            }

            if (addStore == null)
            {
                FuseLog.Warning("FUSE could not locate PrefabStore.AddStore for direct asset pack mounting.");
                FuseLog.Warning(
                    "FUSE direct asset pack mounting is unavailable. " +
                    "Set Settings.MirrorAssetPacksToLocalLow=true in Info.json to use the slower LocalLow mirror fallback.");
            }

            var added = 0;
            var reusedExistingPhysicalStore = 0;
            var failedToAdd = 0;
            var selectedIdentifiersByPath =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var sourcePath in sourcePaths)
            {
                var identifier = ToDirectStoreIdentifier(sourcePath);
                var normalizedPath = NormalizeAssetPackPhysicalPath(sourcePath);
                var plan = PlanStoreRegistration(
                    sourcePath,
                    existingStoreIndex,
                    identifier);

                if (plan.Action == AssetPackStoreRegistrationAction.ReuseExisting)
                {
                    selectedIdentifiersByPath[normalizedPath] = plan.SelectedIdentifier;
                    reusedExistingPhysicalStore++;
                    continue;
                }

                if (plan.Action == AssetPackStoreRegistrationAction.IdentifierConflict)
                {
                    failedToAdd++;
                    FuseLog.Warning(
                        $"FUSE skipped direct asset pack store '{sourcePath}' because identifier " +
                        $"'{plan.SelectedIdentifier}' is already owned by another PrefabStore entry.");
                    continue;
                }

                if (addStore == null)
                {
                    failedToAdd++;
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
                    if (!string.IsNullOrWhiteSpace(normalizedPath))
                    {
                        selectedIdentifiersByPath[normalizedPath] = identifier;
                        existingStoreIndex.Observe(identifier, sourcePath);
                    }
                    added++;
                }
                catch (Exception ex)
                {
                    failedToAdd++;
                    FuseLog.Exception($"FUSE could not add direct asset pack store '{sourcePath}'", ex);
                }
            }

            // Alias generation is lazy and may have run before this postfix.
            // Publish the physical-folder selections and invalidate that cache
            // atomically so catalog/path aliases rebuild against real stores.
            ReplaceStoreIdentifierMappings(selectedIdentifiersByPath);

            if (added > 0 || reusedExistingPhysicalStore > 0 || failedToAdd > 0)
            {
                FuseLog.Info(
                    $"FUSE added {added} direct asset pack store(s) to PrefabStore; " +
                    $"reusedExistingPhysicalStore={reusedExistingPhysicalStore}; " +
                    $"failedToAdd={failedToAdd}; discovered={sourcePaths.Length}.");
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

            ValidateLegacyDefinitionOverrideTargets(prefabStore);

            FusePerformanceMetrics.RecordTiming("direct asset pack stores", stopwatch.ElapsedMilliseconds);
            FusePerformanceMetrics.RecordCount("direct asset pack store count", DirectAssetPackStoreIdentifiers.Count);
            FusePerformanceMetrics.RecordCount(
                "reused existing physical asset pack store count",
                reusedExistingPhysicalStore);
        }

        private static AssetPackStoreRegistrationIndex IndexExistingStoresByRegistrationOrder(
            PrefabStore prefabStore)
        {
            var result = new AssetPackStoreRegistrationIndex();
            if (prefabStore == null || PrefabStoreStoresField == null)
            {
                return result;
            }

            try
            {
                if (!(PrefabStoreStoresField.GetValue(prefabStore) is System.Collections.IEnumerable stores))
                {
                    return result;
                }

                // Preserve registration order. AssetPackForIdentifier compares
                // identifiers case-sensitively and returns the first match, so a
                // later store with the same exact ID is reusable only when it
                // resolves to the same physical path as that first registration.
                foreach (var item in stores)
                {
                    var store = item as AssetPackRuntimeStore;
                    if (store == null || string.IsNullOrWhiteSpace(store.Identifier))
                    {
                        continue;
                    }

                    string basePath;
                    try
                    {
                        // Use the shared accessor so AssetLoader's BasePath patch
                        // participates; its resolved physical folder is the
                        // identity FUSE must compare, not the raw identifier.
                        basePath = FuseAssetPackPatchHelpers.ResolveBasePath(store);
                    }
                    catch
                    {
                        // Record unknown first ownership. A later store with this
                        // same exact ID cannot safely claim a path because host
                        // lookup still selects this unresolved first store.
                        basePath = null;
                    }

                    result.Observe(store.Identifier, basePath);
                }
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    $"FUSE could not index existing PrefabStore asset-pack folders; " +
                    $"direct-store deduplication will fall back to direct registration: " +
                    $"{ex.GetBaseException().Message}");
            }

            return result;
        }

        internal static string NormalizeAssetPackPhysicalPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            try
            {
                var fullPath = Path.GetFullPath(path.Trim());
                var root = Path.GetPathRoot(fullPath) ?? string.Empty;
                while (fullPath.Length > root.Length &&
                       (fullPath.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ||
                        fullPath.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal)))
                {
                    fullPath = fullPath.Substring(0, fullPath.Length - 1);
                }

                return fullPath;
            }
            catch
            {
                return null;
            }
        }

        internal static bool TrySelectExistingStoreIdentifier(
            string sourcePath,
            IReadOnlyDictionary<string, string> existingIdentifiersByNormalizedPath,
            out string identifier)
        {
            identifier = null;
            var normalized = NormalizeAssetPackPhysicalPath(sourcePath);
            return !string.IsNullOrWhiteSpace(normalized) &&
                   existingIdentifiersByNormalizedPath != null &&
                   existingIdentifiersByNormalizedPath.TryGetValue(normalized, out identifier) &&
                   !string.IsNullOrWhiteSpace(identifier);
        }

        internal static string SelectStoreIdentifierForPhysicalPath(
            string sourcePath,
            IReadOnlyDictionary<string, string> existingIdentifiersByNormalizedPath,
            string fallbackIdentifier)
        {
            return TrySelectExistingStoreIdentifier(
                sourcePath,
                existingIdentifiersByNormalizedPath,
                out var existingIdentifier)
                ? existingIdentifier
                : fallbackIdentifier;
        }

        internal static AssetPackStoreRegistrationPlan PlanStoreRegistration(
            string sourcePath,
            AssetPackStoreRegistrationIndex registrationIndex,
            string directIdentifier)
        {
            if (TrySelectExistingStoreIdentifier(
                    sourcePath,
                    registrationIndex?.ReusableIdentifiersByNormalizedPath,
                    out var existingIdentifier))
            {
                return new AssetPackStoreRegistrationPlan(
                    AssetPackStoreRegistrationAction.ReuseExisting,
                    existingIdentifier);
            }

            // PrefabStore resolves identifiers by first registration. Adding the
            // same exact identifier for a different (or unresolved) path would
            // create a shadowed store that can never be selected, while making
            // the registry report a successful mount. Treat that as a conflict.
            if (registrationIndex?.ContainsIdentifier(directIdentifier) == true)
            {
                return new AssetPackStoreRegistrationPlan(
                    AssetPackStoreRegistrationAction.IdentifierConflict,
                    directIdentifier);
            }

            // Historical entries in DirectAssetPackStoreIdentifiers are
            // deliberately irrelevant here. Every PrefabStore instance needs
            // its own store unless that current instance already owns the path.
            return new AssetPackStoreRegistrationPlan(
                AssetPackStoreRegistrationAction.AddDirect,
                directIdentifier);
        }

        internal enum AssetPackStoreRegistrationAction
        {
            ReuseExisting,
            AddDirect,
            IdentifierConflict
        }

        internal readonly struct AssetPackStoreRegistrationPlan
        {
            internal AssetPackStoreRegistrationPlan(
                AssetPackStoreRegistrationAction action,
                string selectedIdentifier)
            {
                Action = action;
                SelectedIdentifier = selectedIdentifier;
            }

            internal AssetPackStoreRegistrationAction Action { get; }

            internal string SelectedIdentifier { get; }
        }

        internal sealed class AssetPackStoreRegistrationIndex
        {
            // Host lookup is exact/case-sensitive; "Owner/Pack" and
            // "owner/pack" establish independent first registrations.
            private readonly Dictionary<string, string> _firstNormalizedPathByIdentifier =
                new Dictionary<string, string>(StringComparer.Ordinal);
            private readonly Dictionary<string, string> _reusableIdentifiersByNormalizedPath =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            internal IReadOnlyDictionary<string, string> ReusableIdentifiersByNormalizedPath =>
                _reusableIdentifiersByNormalizedPath;

            internal bool ContainsIdentifier(string identifier)
            {
                return !string.IsNullOrWhiteSpace(identifier) &&
                       _firstNormalizedPathByIdentifier.ContainsKey(identifier);
            }

            internal bool Observe(string identifier, string resolvedBasePath)
            {
                if (string.IsNullOrWhiteSpace(identifier))
                {
                    return false;
                }

                var normalizedPath = NormalizeAssetPackPhysicalPath(resolvedBasePath);
                if (!_firstNormalizedPathByIdentifier.TryGetValue(identifier, out var firstPath))
                {
                    // Null is intentional: an unresolved first registration owns
                    // the identifier but cannot safely lend it to any later path.
                    _firstNormalizedPathByIdentifier[identifier] = normalizedPath;
                    firstPath = normalizedPath;
                }

                if (string.IsNullOrWhiteSpace(firstPath) ||
                    string.IsNullOrWhiteSpace(normalizedPath) ||
                    !string.Equals(firstPath, normalizedPath, StringComparison.OrdinalIgnoreCase) ||
                    _reusableIdentifiersByNormalizedPath.ContainsKey(normalizedPath))
                {
                    return false;
                }

                _reusableIdentifiersByNormalizedPath[normalizedPath] = identifier;
                return true;
            }
        }

        private static void ReplaceStoreIdentifierMappings(
            IDictionary<string, string> identifiersByNormalizedPath)
        {
            lock (LegacyAssetPackAliasLock)
            {
                StoreIdentifiersByPhysicalPath.Clear();
                if (identifiersByNormalizedPath != null)
                {
                    foreach (var pair in identifiersByNormalizedPath)
                    {
                        if (!string.IsNullOrWhiteSpace(pair.Key) &&
                            !string.IsNullOrWhiteSpace(pair.Value))
                        {
                            StoreIdentifiersByPhysicalPath[pair.Key] = pair.Value;
                        }
                    }
                }

                LegacyAssetPackAliases = null;
            }
        }

        private static string ResolveStoreIdentifierForAssetPackFolder(string sourcePath)
        {
            var directIdentifier = ToDirectStoreIdentifier(sourcePath);
            lock (LegacyAssetPackAliasLock)
            {
                return SelectStoreIdentifierForPhysicalPath(
                    sourcePath,
                    StoreIdentifiersByPhysicalPath,
                    directIdentifier);
            }
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
            if (store == null || string.IsNullOrWhiteSpace(store.Identifier) ||
                !store.Identifier.StartsWith(DirectStorePrefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string basePath = null;
            try
            {
                // Container() is called repeatedly during prefab lookup. The
                // common post-first-load path needs only the cached field; avoid
                // URI unescaping and path work unless the direct store is cold.
                var cached = RuntimeStoreContainerField?.GetValue(store) as Container;
                if (cached != null)
                {
                    container = cached;
                    return true;
                }

                if (!TryResolveDirectStoreBasePath(store.Identifier, out basePath))
                {
                    return false;
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

                RecordMissingComponentKinds(store.Identifier, removedByKind.Keys);

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

        private static readonly HashSet<string> NativeDeserializeFallbackNotices =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static Container LoadResilientDirectContainer(string sourceText, string storeIdentifier, IDictionary<string, int> droppedByKind)
        {
            if (ConsumeLateComponentReload(storeIdentifier) && ContainerSerializerSettingsMethod != null)
            {
                // The first cold load already passed its filtered text through the
                // public ContainerSerialization entry point. Re-entering that method
                // would make old-loader postfixes (notably Lego's definition edits)
                // mutate the same pack twice. The newly registered subtype is visible
                // to the identical serializer settings here, so reload the untouched
                // source without firing those postfixes again.
                return BypassDeserialize(sourceText);
            }

            // No reflection-based strict bypass available (rare): defer to the legacy
            // deserialize path, which itself falls back to ContainerSerialization.Deserialize.
            if (ContainerSerializerSettingsMethod == null)
            {
                return DeserializeContainerBypassingPostfix(sourceText, storeIdentifier);
            }

            // Cold load of a direct (fuseasset://) store. The game's own
            // AssetPackRuntimeStore.Container() body never runs for these stores — the
            // FuseAssetPackRuntimeStoreContainerPatch prefix replaces it — so this is
            // the ONLY deserialization the pack ever gets in this PrefabStore
            // generation. Route it through the public ContainerSerialization.Deserialize
            // entry point exactly like the native loader would, so old-loader Harmony
            // patches on that method fire once per pack: LegosLibraryOfStuff's postfix
            // injects its clone definitions (repaint liveries, LLW tender swaps) into
            // the returned container, and without this call clones of mod-pack cars
            // never exist, which orphans every saved car that references one (issues
            // #224, #222). Clones of vanilla cars were unaffected because vanilla stores
            // still load natively.
            //
            // The double-apply that motivated the bypass (commit c188ad1: per-car
            // ComponentGroup toggles going dead) came from a SECOND pass over the same
            // pack inside one PrefabStore generation — at the time AssetLoader's native
            // store and FUSE's direct store both existed for one folder (physical-path
            // reuse arrived later in 12d3f80) and the sanitize path re-entered
            // Deserialize. The public entry point is now called exactly once per cold
            // load; every RE-deserialize (per-item mixinto re-deserialize, whistle store
            // refresh is itself a fresh cold load) stays on the Newtonsoft bypass.
            // DirectStoreNativeDeserialize=false restores the old bypass-only cold load.
            var nativeThrew = false;
            if (FuseSettings.DirectStoreNativeDeserialize)
            {
                try
                {
                    var native = ContainerSerialization.Deserialize(sourceText);
                    if (native != null)
                    {
                        return native;
                    }
                }
                catch (Exception ex)
                {
                    // The stock serializer rejected the pack — almost always an
                    // unbindable component kind whose defining library mod is absent,
                    // occasionally object-shaped Unity structs the game's Vec3Conv
                    // rejects. Harmony runs no postfix for a throwing original, so
                    // nothing was applied. Fall through to the tolerant path; a pack
                    // that ends up losing old-loader clones is called out below.
                    nativeThrew = true;
                    NoteNativeDeserializeFallback(storeIdentifier, ex);
                }
            }

            try
            {
                // Tolerant path: with the defining library mods loaded, the game's
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
                if (nativeThrew && droppedByKind.Count > 0)
                {
                    // The only thing wrong with the pack was one or more unbindable
                    // kinds. Retry the FILTERED text through the public entry point so
                    // the pack still gets its old-loader edits (a pack missing one
                    // unrelated library must not forfeit its LegosLibraryOfStuff
                    // clones). The first native call threw, so no postfix pass has run
                    // yet and this cannot double-apply.
                    try
                    {
                        var native = ContainerSerialization.Deserialize(filtered);
                        if (native != null)
                        {
                            return native;
                        }
                    }
                    catch (Exception ex)
                    {
                        // Still not stock-deserializable (e.g. object-shaped structs);
                        // the tolerant retry below is the last resort.
                        FuseLog.Info(
                            $"FUSE direct asset pack '{storeIdentifier ?? "<unknown>"}' still failed the native " +
                            $"deserialize path after filtering ({ex.GetBaseException().GetType().Name}: " +
                            $"{ex.GetBaseException().Message}); using the tolerant loader.");
                    }
                }

                return BypassDeserialize(filtered);
            }
        }

        private static void RecordMissingComponentKinds(string storeIdentifier, IEnumerable<string> kinds)
        {
            if (string.IsNullOrWhiteSpace(storeIdentifier) || kinds == null)
            {
                return;
            }

            lock (LateComponentRegistrationLock)
            {
                foreach (var kind in kinds)
                {
                    if (string.IsNullOrWhiteSpace(kind))
                    {
                        continue;
                    }

                    if (!StoresMissingComponentKind.TryGetValue(kind, out var stores))
                    {
                        stores = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        StoresMissingComponentKind[kind] = stores;
                    }

                    stores.Add(storeIdentifier);
                }
            }
        }

        private static bool ConsumeLateComponentReload(string storeIdentifier)
        {
            if (string.IsNullOrWhiteSpace(storeIdentifier))
            {
                return false;
            }

            lock (LateComponentRegistrationLock)
            {
                return StoresRequiringLateComponentReload.Remove(storeIdentifier);
            }
        }

        internal static void OnLegacyComponentKindRegistered(string kind)
        {
            if (string.IsNullOrWhiteSpace(kind))
            {
                return;
            }

            string[] affectedStoreIdentifiers;
            lock (LateComponentRegistrationLock)
            {
                if (!StoresMissingComponentKind.TryGetValue(kind, out var stores) || stores.Count == 0)
                {
                    return;
                }

                affectedStoreIdentifiers = stores.ToArray();
                StoresMissingComponentKind.Remove(kind);
                foreach (var identifier in affectedStoreIdentifiers)
                {
                    StoresRequiringLateComponentReload.Add(identifier);
                }
            }

            var invalidated = 0;
            try
            {
                if (_activePrefabStore != null &&
                    PrefabStoreStoresField?.GetValue(_activePrefabStore) is System.Collections.IEnumerable stores)
                {
                    var affected = new HashSet<string>(affectedStoreIdentifiers, StringComparer.OrdinalIgnoreCase);
                    foreach (var item in stores)
                    {
                        if (item is AssetPackRuntimeStore store && affected.Contains(store.Identifier))
                        {
                            RuntimeStoreContainerField?.SetValue(store, null);
                            invalidated++;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    $"FUSE could not invalidate asset packs after component kind '{kind}' registered: {ex.Message}");
            }

            FusePrefabStoreAllCarDefinitionInfosFilterPatch.InvalidateCache(_activePrefabStore);
            FuseLog.Info(
                $"FUSE component kind '{kind}' became available after asset-pack discovery; " +
                $"invalidated {invalidated} affected store(s) so their full definitions load before use.");
        }

        private static void NoteNativeDeserializeFallback(string storeIdentifier, Exception ex)
        {
            var key = storeIdentifier ?? "<unknown>";
            if (!NativeDeserializeFallbackNotices.Add(key))
            {
                return;
            }

            FuseLog.Warning(
                $"FUSE direct asset pack '{key}' did not deserialize through the game's native path " +
                $"({ex.GetBaseException().GetType().Name}: {ex.GetBaseException().Message}); " +
                "retrying with unbindable components dropped, then FUSE's tolerant loader. If the tolerant " +
                "loader ends up handling it, old-loader definition edits (e.g. LegosLibraryOfStuff clones) " +
                "will not apply to this pack.");
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
            // Railroader's stock Vec2/Vec3/Quaternion converters accept only
            // array-shaped values. Some otherwise-valid asset packs serialize
            // Unity structs as objects (for example {"x":0,"y":0,"z":0}).
            // Put the tolerant reader first so those packs deserialize in one
            // pass instead of throwing, reparsing the full container, and
            // incorrectly classifying the component as an unknown subtype.
            settings.Converters.Insert(0, TolerantUnityStructConverter.Instance);
            var container = JsonConvert.DeserializeObject<Container>(text, settings);
            container?.Awake();
            return container;
        }

        private sealed class TolerantUnityStructConverter : JsonConverter
        {
            internal static readonly TolerantUnityStructConverter Instance =
                new TolerantUnityStructConverter();

            public override bool CanWrite => false;

            public override bool CanConvert(Type objectType)
            {
                return objectType == typeof(Vector2) ||
                       objectType == typeof(Vector3) ||
                       objectType == typeof(Quaternion);
            }

            public override object ReadJson(
                JsonReader reader,
                Type objectType,
                object existingValue,
                JsonSerializer serializer)
            {
                var token = JToken.Load(reader);
                if (objectType == typeof(Vector2))
                {
                    return new Vector2(
                        ReadComponent(token, "x", 0),
                        ReadComponent(token, "y", 1));
                }

                if (objectType == typeof(Vector3))
                {
                    return new Vector3(
                        ReadComponent(token, "x", 0),
                        ReadComponent(token, "y", 1),
                        ReadComponent(token, "z", 2));
                }

                if (objectType == typeof(Quaternion))
                {
                    return new Quaternion(
                        ReadComponent(token, "x", 0),
                        ReadComponent(token, "y", 1),
                        ReadComponent(token, "z", 2),
                        ReadComponent(token, "w", 3));
                }

                throw new JsonSerializationException(
                    $"Unsupported Unity struct type '{objectType}'.");
            }

            public override void WriteJson(
                JsonWriter writer,
                object value,
                JsonSerializer serializer)
            {
                throw new NotSupportedException(
                    "The tolerant Unity struct converter is read-only.");
            }

            private static float ReadComponent(
                JToken token,
                string propertyName,
                int arrayIndex)
            {
                JToken value = null;
                if (token is JArray array && arrayIndex < array.Count)
                {
                    value = array[arrayIndex];
                }
                else if (token is JObject obj)
                {
                    value = obj.GetValue(
                        propertyName,
                        StringComparison.OrdinalIgnoreCase);
                }

                if (value == null || value.Type == JTokenType.Null)
                {
                    throw new JsonSerializationException(
                        $"Unity struct value is missing component '{propertyName}'.");
                }

                return value.ToObject<float>();
            }
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
