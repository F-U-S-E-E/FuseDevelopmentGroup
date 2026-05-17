using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AssetPack.Runtime;
using HarmonyLib;
using Model.Definition;
using Model.Database;
using Newtonsoft.Json.Linq;
using FUSE.Infrastructure;
using UnityEngine;

namespace FUSE.Loading
{
    internal static class FuseAssetPackRegistry
    {
        private const string FuseAssetPacksProperty = "FuseAssetPacks";
        private const string DirectStorePrefix = "fuseasset://";

        private static readonly HashSet<string> SupportedDefinitionComponentKinds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "AggregateLoadModel",
                "Bell",
                "PrefabControl",
                "FireboxEffect",
                "Chuff",
                "ClassLight",
                "Colorizer",
                "Compressor",
                "CylinderCock",
                "Decal",
                "DetailModel",
                "DieselExhaust",
                "Dynamo",
                "Gauge",
                "Headlight",
                "Horn",
                "Ladder",
                "LightFixture",
                "LoadAnimation",
                "LoadModel",
                "LoadTarget",
                "MapMask",
                "RectangleMapMask",
                "CircleMapMask",
                "MarkerLight",
                "RadialControl",
                "Seat",
                "ToggleAnimation",
                "ToggleControl",
                "Whistle"
            };

        private static bool _mountComplete;
        private static readonly Dictionary<string, string> MountedAssetPackSourcesById =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> DirectAssetPackStoreIdentifiers =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> SanitizedDirectContainerWarnings =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly object LegacyAssetPackAliasLock = new object();
        private static Dictionary<string, string> LegacyAssetPackAliases;
        private static readonly FieldInfo RuntimeStoreContainerField =
            AccessTools.Field(typeof(AssetPackRuntimeStore), "_container");

        public static int MountAllAvailableAssetPacks()
        {
            if (_mountComplete)
            {
                return 0;
            }

            if (!FuseSettings.MirrorAssetPacksToLocalLow)
            {
                _mountComplete = true;
                FuseLog.Info("FUSE skipped LocalLow asset pack mirror because direct asset pack stores are the default.");
                return 0;
            }

            var modsRoot = FuseDataPackageDiscovery.GetModsRoot();
            if (string.IsNullOrWhiteSpace(modsRoot) || !Directory.Exists(modsRoot))
            {
                FuseLog.Warning("FUSE could not locate the Unity Mod Manager Mods folder for asset pack discovery.");
                return 0;
            }

            var mountedCount = 0;
            foreach (var packagePath in Directory.GetDirectories(modsRoot).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                if (!ShouldInspectPackage(packagePath))
                {
                    continue;
                }

                try
                {
                    mountedCount += MountAssetPacksFromPackage(packagePath);
                }
                catch (Exception ex)
                {
                    FuseLog.Exception($"FUSE failed to mount asset packs from '{packagePath}'", ex);
                }
            }

            _mountComplete = true;
            if (mountedCount > 0)
            {
                FuseLog.Info($"FUSE mounted {mountedCount} asset pack(s).");
            }

            return mountedCount;
        }

        public static void Reset()
        {
            if (!_mountComplete)
            {
                return;
            }

            _mountComplete = false;
            MountedAssetPackSourcesById.Clear();
            DirectAssetPackStoreIdentifiers.Clear();
            lock (LegacyAssetPackAliasLock)
            {
                LegacyAssetPackAliases = null;
            }
            FuseLegacyContainerMixintoRegistry.Reset();
            FuseLog.Info("FUSE asset pack mount state reset.");
        }

        private static int MountAssetPacksFromPackage(string packagePath)
        {
            var infoPath = Path.Combine(packagePath, "Info.json");
            if (!File.Exists(infoPath))
            {
                return MountAssetPackFolders(packagePath, EnumerateFallbackAssetPackFolders(packagePath));
            }

            JObject info;
            try
            {
                info = FuseLegacyDataConverter.ReadLegacyObject(infoPath);
            }
            catch (Exception ex)
            {
                FuseLog.Warning($"FUSE ignored asset pack declaration in '{packagePath}' because Info.json could not be parsed: {ex.Message}");
                return 0;
            }

            var sourceRoots = EnumerateAssetPackRoots(info[FuseAssetPacksProperty]).ToArray();
            var folders = EnumerateAssetPackFoldersFromRoots(packagePath, sourceRoots)
                .Concat(EnumerateFallbackAssetPackFolders(packagePath))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return MountAssetPackFolders(packagePath, folders);
        }

        private static int MountAssetPackFolders(string packagePath, IEnumerable<string> folders)
        {
            var mountedCount = 0;
            foreach (var folder in folders ?? Enumerable.Empty<string>())
            {
                try
                {
                    mountedCount += MountAssetPackFolder(folder);
                }
                catch (Exception ex)
                {
                    FuseLog.Exception($"FUSE failed to mount asset pack folder '{folder}' from '{packagePath}'", ex);
                }
            }

            return mountedCount;
        }

        internal static IEnumerable<string> EnumerateAvailableAssetPackFolders()
        {
            var modsRoot = FuseDataPackageDiscovery.GetModsRoot();
            if (string.IsNullOrWhiteSpace(modsRoot) || !Directory.Exists(modsRoot))
            {
                yield break;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var packagePath in Directory.GetDirectories(modsRoot).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                if (!ShouldInspectPackage(packagePath))
                {
                    continue;
                }

                foreach (var folder in EnumerateAssetPackFoldersForPackage(packagePath))
                {
                    if (seen.Add(folder))
                    {
                        yield return folder;
                    }
                }
            }
        }

        internal static FuseAssetPackDiagnostics GetDiagnostics()
        {
            var folders = EnumerateAvailableAssetPackFolders().ToArray();
            var firstSourceByKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var duplicateKeys = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var failedLookups = new List<string>();

            foreach (var folder in folders)
            {
                var definitionsPath = Path.Combine(folder, "Definitions.json");
                if (!File.Exists(definitionsPath))
                {
                    continue;
                }

                try
                {
                    var root = FuseLegacyDataConverter.ReadLegacyObject(definitionsPath);
                    foreach (var obj in (root["objects"] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
                    {
                        var key =
                            GetStringProperty(obj, "name") ??
                            GetStringProperty(obj, "identifier") ??
                            GetStringProperty(obj, "id") ??
                            GetStringProperty(obj["definition"] as JObject, "name") ??
                            GetStringProperty(obj["definition"] as JObject, "identifier") ??
                            GetStringProperty(obj["definition"] as JObject, "id");
                        if (string.IsNullOrWhiteSpace(key))
                        {
                            continue;
                        }

                        if (!firstSourceByKey.TryGetValue(key, out var existing))
                        {
                            firstSourceByKey[key] = folder;
                            continue;
                        }

                        if (!duplicateKeys.TryGetValue(key, out var sources))
                        {
                            sources = new List<string> { existing };
                            duplicateKeys[key] = sources;
                        }

                        if (!sources.Contains(folder, StringComparer.OrdinalIgnoreCase))
                        {
                            sources.Add(folder);
                        }
                    }
                }
                catch (Exception ex)
                {
                    failedLookups.Add($"{Path.GetFileName(folder)}: {ex.Message}");
                }
            }

            return new FuseAssetPackDiagnostics
            {
                StoreFolders = folders,
                UniqueAssetKeys = firstSourceByKey.Count,
                DuplicateKeys = duplicateKeys
                    .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(item => new FuseDuplicateAssetKey
                    {
                        Key = item.Key,
                        Sources = item.Value.Select(Path.GetFileName).ToArray()
                    })
                    .ToArray(),
                FailedDefinitionLoads = failedLookups.ToArray()
            };
        }

        private static bool ShouldInspectPackage(string packagePath)
        {
            if (string.IsNullOrWhiteSpace(packagePath))
            {
                return false;
            }

            var packageId = TryReadPackageId(packagePath);
            if (FuseUmmState.TryGetDisabledReason(packagePath, packageId, out _))
            {
                return false;
            }

            return FuseModSetService.IsPackageEnabledByActiveSet(packageId, packagePath);
        }

        private static string TryReadPackageId(string packagePath)
        {
            try
            {
                var infoPath = Path.Combine(packagePath, "Info.json");
                if (File.Exists(infoPath))
                {
                    var info = FuseLegacyDataConverter.ReadLegacyObject(infoPath);
                    var id = (string)info["Id"];
                    if (!string.IsNullOrWhiteSpace(id))
                    {
                        return id.Trim();
                    }
                }

                var definitionPath = Path.Combine(packagePath, "Definition.json");
                if (File.Exists(definitionPath))
                {
                    var definition = FuseLegacyDataConverter.ReadLegacyObject(definitionPath);
                    var id = (string)definition["id"] ?? (string)definition["Id"];
                    if (!string.IsNullOrWhiteSpace(id))
                    {
                        return id.Trim();
                    }
                }
            }
            catch
            {
                // Asset-pack discovery can still fall back to folder name.
            }

            return Path.GetFileName(packagePath);
        }

        private static IEnumerable<string> EnumerateAssetPackFoldersForPackage(string packagePath)
        {
            JObject info = null;
            var infoPath = Path.Combine(packagePath, "Info.json");
            if (File.Exists(infoPath))
            {
                try
                {
                    info = FuseLegacyDataConverter.ReadLegacyObject(infoPath);
                }
                catch
                {
                    info = null;
                }
            }

            foreach (var folder in EnumerateAssetPackFoldersFromRoots(packagePath, EnumerateAssetPackRoots(info?[FuseAssetPacksProperty])))
            {
                yield return folder;
            }

            foreach (var folder in EnumerateFallbackAssetPackFolders(packagePath))
            {
                yield return folder;
            }
        }

        private static IEnumerable<string> EnumerateAssetPackRoots(JToken token)
        {
            if (token == null)
            {
                yield break;
            }

            if (token.Type == JTokenType.String)
            {
                var value = (string)token;
                if (!string.IsNullOrWhiteSpace(value))
                {
                    yield return value;
                }

                yield break;
            }

            if (token.Type != JTokenType.Array)
            {
                yield break;
            }

            foreach (var item in token.Children())
            {
                if (item.Type != JTokenType.String)
                {
                    continue;
                }

                var value = (string)item;
                if (!string.IsNullOrWhiteSpace(value))
                {
                    yield return value;
                }
            }
        }

        private static int MountAssetPackSource(string packagePath, string relativeSource)
        {
            if (string.IsNullOrWhiteSpace(relativeSource))
            {
                FuseLog.Warning($"FUSE ignored blank asset pack source in '{packagePath}'.");
                return 0;
            }

            var packageRoot = Path.GetFullPath(packagePath);
            var sourcePath = Path.GetFullPath(Path.Combine(packageRoot, relativeSource));
            if (!sourcePath.StartsWith(packageRoot, StringComparison.OrdinalIgnoreCase))
            {
                FuseLog.Warning($"FUSE ignored asset pack source outside package root: '{relativeSource}'.");
                return 0;
            }

            if (!Directory.Exists(sourcePath))
            {
                FuseLog.Warning($"FUSE asset pack source '{relativeSource}' was not found in '{packagePath}'.");
                return 0;
            }

            if (IsAssetPackFolder(sourcePath))
            {
                return MountAssetPackFolder(sourcePath);
            }

            var assetPackFolders = EnumerateAssetPackFolders(sourcePath).ToArray();
            var mountedCount = 0;
            foreach (var child in assetPackFolders)
            {
                mountedCount += MountAssetPackFolder(child);
            }

            if (mountedCount == 0)
            {
                FuseLog.Warning($"FUSE asset pack source '{relativeSource}' did not contain any valid asset pack folders.");
            }

            return mountedCount;
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
                FuseLog.Warning($"FUSE could not locate PrefabStore.AddStore for direct asset pack mounting: {ex.Message}");
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
            var skipped = 0;
            foreach (var sourcePath in sourcePaths)
            {
                var identifier = ToDirectStoreIdentifier(sourcePath);
                if (!DirectAssetPackStoreIdentifiers.Add(identifier))
                {
                    skipped++;
                    continue;
                }

                try
                {
                    addStore.Invoke(prefabStore, new object[]
                    {
                        identifier,
                        AssetPackRuntimeStore.StoreLocation.External
                    });
                    added++;
                }
                catch (Exception ex)
                {
                    DirectAssetPackStoreIdentifiers.Remove(identifier);
                    FuseLog.Warning($"FUSE could not add direct asset pack store '{sourcePath}': {ex.Message}");
                }
            }

            if (added > 0)
            {
                FuseLog.Info($"FUSE added {added} direct asset pack store(s) to PrefabStore; skippedAlreadyAdded={skipped}.");
            }
            else if (skipped > 0)
            {
                FuseLog.Info($"FUSE skipped {skipped} already-registered direct asset pack store(s).");
            }

            FusePerformanceMetrics.RecordTiming("direct asset pack stores", stopwatch.ElapsedMilliseconds);
            FusePerformanceMetrics.RecordCount("direct asset pack store count", DirectAssetPackStoreIdentifiers.Count);
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

        internal static bool TryResolveLegacyAssetPackIdentifier(string identifier, out string resolvedIdentifier)
        {
            resolvedIdentifier = null;
            var normalized = NormalizeLegacyAssetPackIdentifier(identifier);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return false;
            }

            var aliases = GetLegacyAssetPackAliases();
            return aliases.TryGetValue(normalized, out resolvedIdentifier) &&
                   !string.IsNullOrWhiteSpace(resolvedIdentifier) &&
                   !string.Equals(resolvedIdentifier, identifier, StringComparison.Ordinal);
        }

        internal static string ResolveLegacyAssetPackIdentifier(string identifier)
        {
            return TryResolveLegacyAssetPackIdentifier(identifier, out var resolved)
                ? resolved
                : identifier;
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
                var sanitizedText = SanitizeDefinitionsJson(sourceText, removedByKind);
                container = ContainerSerialization.Deserialize(sanitizedText);
                RuntimeStoreContainerField?.SetValue(store, container);

                if (removedByKind.Count > 0 && SanitizedDirectContainerWarnings.Add(store.Identifier))
                {
                    var packId = Path.GetFileName(basePath);
                    var summary = string.Join(", ", removedByKind.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                        .Select(item => $"{item.Key}={item.Value}"));
                    FuseLog.Info(
                        $"FUSE sanitized direct asset pack '{packId}' Definitions.json in memory by removing unsupported component kind(s): {summary}. " +
                        "The source mod files were not modified.");
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

        private static IEnumerable<string> EnumerateAssetPackFoldersFromRoots(string packagePath, IEnumerable<string> relativeSources)
        {
            foreach (var relativeSource in relativeSources ?? Enumerable.Empty<string>())
            {
                foreach (var folder in EnumerateAssetPackFoldersFromRoot(packagePath, relativeSource))
                {
                    yield return folder;
                }
            }
        }

        private static IEnumerable<string> EnumerateAssetPackFoldersFromRoot(string packagePath, string relativeSource)
        {
            if (string.IsNullOrWhiteSpace(relativeSource))
            {
                yield break;
            }

            var packageRoot = Path.GetFullPath(packagePath);
            var sourcePath = Path.GetFullPath(Path.Combine(packageRoot, relativeSource));
            if (!sourcePath.StartsWith(packageRoot, StringComparison.OrdinalIgnoreCase) ||
                !Directory.Exists(sourcePath))
            {
                yield break;
            }

            if (IsAssetPackFolder(sourcePath))
            {
                yield return sourcePath;
                yield break;
            }

            foreach (var folder in EnumerateAssetPackFolders(sourcePath))
            {
                yield return folder;
            }
        }

        private static IEnumerable<string> EnumerateFallbackAssetPackFolders(string packagePath)
        {
            var scAssetPacks = Path.Combine(packagePath, "SCAssetPacks");
            if (!Directory.Exists(scAssetPacks))
            {
                yield break;
            }

            if (IsAssetPackFolder(scAssetPacks))
            {
                yield return scAssetPacks;
                yield break;
            }

            foreach (var folder in EnumerateAssetPackFolders(scAssetPacks))
            {
                yield return folder;
            }
        }

        private static IEnumerable<string> EnumerateAssetPackFolders(string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !Directory.Exists(sourcePath))
            {
                yield break;
            }

            foreach (var child in Directory.GetDirectories(sourcePath, "*", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                if (IsAssetPackFolder(child))
                {
                    yield return child;
                }
            }
        }

        private static bool IsAssetPackFolder(string folderPath)
        {
            return File.Exists(Path.Combine(folderPath, "Bundle")) &&
                   File.Exists(Path.Combine(folderPath, "Catalog.json")) &&
                   File.Exists(Path.Combine(folderPath, "Definitions.json"));
        }

        private static Dictionary<string, string> GetLegacyAssetPackAliases()
        {
            lock (LegacyAssetPackAliasLock)
            {
                if (LegacyAssetPackAliases != null)
                {
                    return LegacyAssetPackAliases;
                }

                var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var modsRoot = FuseDataPackageDiscovery.GetModsRoot();
                if (!string.IsNullOrWhiteSpace(modsRoot) && Directory.Exists(modsRoot))
                {
                    foreach (var packagePath in Directory.GetDirectories(modsRoot).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                    {
                        if (!ShouldInspectPackage(packagePath))
                        {
                            continue;
                        }

                        var packageId = TryReadPackageId(packagePath);
                        foreach (var folder in EnumerateAssetPackFoldersForPackage(packagePath))
                        {
                            RegisterLegacyAssetPackAliases(aliases, packagePath, packageId, folder);
                        }
                    }
                }

                LegacyAssetPackAliases = aliases;
                return LegacyAssetPackAliases;
            }
        }

        private static void RegisterLegacyAssetPackAliases(
            IDictionary<string, string> aliases,
            string packagePath,
            string packageId,
            string assetPackFolder)
        {
            if (aliases == null || string.IsNullOrWhiteSpace(assetPackFolder))
            {
                return;
            }

            var resolved = ToDirectStoreIdentifier(assetPackFolder);
            AddLegacyAssetPackAlias(aliases, resolved, resolved);
            AddLegacyAssetPackAlias(aliases, Path.GetFileName(assetPackFolder), resolved);

            var relative = GetRelativePath(packagePath, assetPackFolder).Replace(Path.DirectorySeparatorChar, '/');
            if (!string.IsNullOrWhiteSpace(packageId) && !string.IsNullOrWhiteSpace(relative))
            {
                AddLegacyAssetPackAlias(aliases, $"zsc://{packageId}/{relative}", resolved);
                AddLegacyAssetPackAlias(aliases, $"zsc://{Path.GetFileName(packagePath)}/{relative}", resolved);
            }

            RegisterCatalogAliases(aliases, assetPackFolder, resolved);
        }

        private static void RegisterCatalogAliases(
            IDictionary<string, string> aliases,
            string assetPackFolder,
            string resolved)
        {
            var catalogPath = Path.Combine(assetPackFolder, "Catalog.json");
            if (!File.Exists(catalogPath))
            {
                return;
            }

            try
            {
                var catalog = FuseLegacyDataConverter.ReadLegacyObject(catalogPath);
                AddLegacyAssetPackAlias(aliases, GetStringProperty(catalog, "identifier"), resolved);
                AddLegacyAssetPackAlias(aliases, GetStringProperty(catalog, "indentifier"), resolved);
                AddLegacyAssetPackAlias(aliases, GetStringProperty(catalog, "name"), resolved);
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    $"FUSE could not inspect asset pack Catalog.json '{catalogPath}' for legacy aliases: {ex.Message}");
            }
        }

        private static void AddLegacyAssetPackAlias(
            IDictionary<string, string> aliases,
            string alias,
            string resolved)
        {
            var normalizedAlias = NormalizeLegacyAssetPackIdentifier(alias);
            if (string.IsNullOrWhiteSpace(normalizedAlias) || string.IsNullOrWhiteSpace(resolved))
            {
                return;
            }

            if (!aliases.ContainsKey(normalizedAlias))
            {
                aliases[normalizedAlias] = resolved;
            }
        }

        private static string NormalizeLegacyAssetPackIdentifier(string identifier)
        {
            return (identifier ?? string.Empty).Trim().Replace('\\', '/').TrimEnd('/');
        }

        private static int MountAssetPackFolder(string sourcePath)
        {
            var packId = GetDestinationPackId(sourcePath);
            var destinationRoot = Path.Combine(Application.persistentDataPath, "AssetPacks");
            var destinationPath = Path.Combine(destinationRoot, packId);
            Directory.CreateDirectory(destinationPath);

            var copiedCount = CopyDirectoryIfChanged(sourcePath, destinationPath);
            FuseLog.Info(copiedCount > 0
                ? $"FUSE mounted asset pack '{packId}' to '{destinationPath}' ({copiedCount} file(s) updated)."
                : $"FUSE asset pack '{packId}' already mounted at '{destinationPath}'.");
            return 1;
        }

        private static int CopyDirectoryIfChanged(string sourcePath, string destinationPath)
        {
            var copiedCount = 0;
            foreach (var directory in Directory.GetDirectories(sourcePath, "*", SearchOption.AllDirectories))
            {
                var relativeDirectory = GetRelativePath(sourcePath, directory);
                Directory.CreateDirectory(Path.Combine(destinationPath, relativeDirectory));
            }

            foreach (var sourceFile in Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories))
            {
                var relativeFile = GetRelativePath(sourcePath, sourceFile);
                var destinationFile = Path.Combine(destinationPath, relativeFile);
                var destinationDirectory = Path.GetDirectoryName(destinationFile);
                if (!string.IsNullOrWhiteSpace(destinationDirectory))
                {
                    Directory.CreateDirectory(destinationDirectory);
                }

                copiedCount += CopyAssetPackFileIfChanged(sourcePath, sourceFile, destinationFile);
            }

            return copiedCount;
        }

        private static int CopyAssetPackFileIfChanged(string assetPackRoot, string sourceFile, string destinationFile)
        {
            if (string.Equals(Path.GetFileName(sourceFile), "Definitions.json", StringComparison.OrdinalIgnoreCase))
            {
                return CopySanitizedDefinitionsFile(assetPackRoot, sourceFile, destinationFile);
            }

            if (!NeedsCopy(sourceFile, destinationFile))
            {
                return 0;
            }

            File.Copy(sourceFile, destinationFile, true);
            File.SetLastWriteTimeUtc(destinationFile, File.GetLastWriteTimeUtc(sourceFile));
            return 1;
        }

        private static string GetDestinationPackId(string sourcePath)
        {
            var baseId = SanitizePackId(Path.GetFileName(sourcePath));
            var fullSource = Path.GetFullPath(sourcePath);
            if (!MountedAssetPackSourcesById.TryGetValue(baseId, out var existingSource))
            {
                MountedAssetPackSourcesById[baseId] = fullSource;
                return baseId;
            }

            if (string.Equals(existingSource, fullSource, StringComparison.OrdinalIgnoreCase))
            {
                return baseId;
            }

            var parentId = SanitizePackId(Path.GetFileName(Path.GetDirectoryName(sourcePath)));
            var candidate = string.IsNullOrWhiteSpace(parentId) ? baseId : parentId + "_" + baseId;
            var unique = candidate;
            var index = 2;
            while (MountedAssetPackSourcesById.TryGetValue(unique, out existingSource) &&
                   !string.Equals(existingSource, fullSource, StringComparison.OrdinalIgnoreCase))
            {
                unique = candidate + "_" + index;
                index++;
            }

            MountedAssetPackSourcesById[unique] = fullSource;
            FuseLog.Warning(
                $"FUSE mounted duplicate asset pack folder '{baseId}' from '{sourcePath}' as '{unique}' " +
                "so it does not overwrite another pack with the same folder name.");
            return unique;
        }

        private static string SanitizePackId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "AssetPack";
            }

            var chars = value.Trim()
                .Select(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' || ch == '.'
                    ? ch
                    : '_')
                .ToArray();
            var result = new string(chars).Trim('_');
            return string.IsNullOrWhiteSpace(result) ? "AssetPack" : result;
        }

        private static string ToDirectStoreIdentifier(string sourcePath)
        {
            return DirectStorePrefix + Uri.EscapeDataString(Path.GetFullPath(sourcePath));
        }

        private static int CopySanitizedDefinitionsFile(string assetPackRoot, string sourceFile, string destinationFile)
        {
            string outputText;
            var removedByKind = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            try
            {
                outputText = SanitizeDefinitionsJson(File.ReadAllText(sourceFile), removedByKind);
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    $"FUSE could not sanitize asset pack Definitions.json '{sourceFile}'; copying original file: {ex.Message}");
                if (!NeedsCopy(sourceFile, destinationFile))
                {
                    return 0;
                }

                File.Copy(sourceFile, destinationFile, true);
                File.SetLastWriteTimeUtc(destinationFile, File.GetLastWriteTimeUtc(sourceFile));
                return 1;
            }

            if (removedByKind.Count > 0)
            {
                var packId = Path.GetFileName(assetPackRoot);
                var summary = string.Join(", ", removedByKind.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(item => $"{item.Key}={item.Value}"));
                FuseLog.Info(
                    $"FUSE sanitized asset pack '{packId}' Definitions.json by removing unsupported component kind(s): {summary}. " +
                    "The source mod files were not modified.");
            }

            if (File.Exists(destinationFile) && string.Equals(File.ReadAllText(destinationFile), outputText, StringComparison.Ordinal))
            {
                return 0;
            }

            File.WriteAllText(destinationFile, outputText);
            File.SetLastWriteTimeUtc(destinationFile, File.GetLastWriteTimeUtc(sourceFile));
            return 1;
        }

        private static string SanitizeDefinitionsJson(string sourceText, IDictionary<string, int> removedByKind)
        {
            var root = JObject.Parse(sourceText);
            var objects = root["objects"] as JArray;
            if (objects == null)
            {
                return sourceText;
            }

            foreach (var objectToken in objects.OfType<JObject>())
            {
                var components = objectToken["definition"]?["components"] as JArray;
                if (components == null)
                {
                    continue;
                }

                for (var index = components.Count - 1; index >= 0; index--)
                {
                    var component = components[index] as JObject;
                    var kind = GetStringProperty(component, "kind");
                    if (!string.IsNullOrWhiteSpace(kind) && SupportedDefinitionComponentKinds.Contains(kind))
                    {
                        continue;
                    }

                    components.RemoveAt(index);
                    var key = string.IsNullOrWhiteSpace(kind) ? "<missing>" : kind;
                    removedByKind[key] = removedByKind.TryGetValue(key, out var count) ? count + 1 : 1;
                }
            }

            return removedByKind.Count == 0
                ? sourceText
                : root.ToString(Newtonsoft.Json.Formatting.Indented);
        }

        private static string GetStringProperty(JObject obj, string propertyName)
        {
            if (obj == null)
            {
                return null;
            }

            return obj.TryGetValue(propertyName, StringComparison.OrdinalIgnoreCase, out var token)
                ? (string)token
                : null;
        }

        private static bool NeedsCopy(string sourceFile, string destinationFile)
        {
            if (!File.Exists(destinationFile))
            {
                return true;
            }

            var sourceInfo = new FileInfo(sourceFile);
            var destinationInfo = new FileInfo(destinationFile);
            if (sourceInfo.Length != destinationInfo.Length)
            {
                return true;
            }

            return sourceInfo.LastWriteTimeUtc > destinationInfo.LastWriteTimeUtc.AddSeconds(1);
        }

        private static string GetRelativePath(string rootPath, string fullPath)
        {
            var rootUri = new Uri(AppendDirectorySeparatorChar(Path.GetFullPath(rootPath)));
            var fullUri = new Uri(Path.GetFullPath(fullPath));
            return Uri.UnescapeDataString(rootUri.MakeRelativeUri(fullUri).ToString())
                .Replace('/', Path.DirectorySeparatorChar);
        }

        private static string AppendDirectorySeparatorChar(string path)
        {
            if (path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ||
                path.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            {
                return path;
            }

            return path + Path.DirectorySeparatorChar;
        }
    }

    internal sealed class FuseAssetPackDiagnostics
    {
        public string[] StoreFolders { get; set; } = Array.Empty<string>();
        public int UniqueAssetKeys { get; set; }
        public FuseDuplicateAssetKey[] DuplicateKeys { get; set; } = Array.Empty<FuseDuplicateAssetKey>();
        public string[] FailedDefinitionLoads { get; set; } = Array.Empty<string>();
    }

    internal sealed class FuseDuplicateAssetKey
    {
        public string Key { get; set; } = string.Empty;
        public string[] Sources { get; set; } = Array.Empty<string>();
    }
}
