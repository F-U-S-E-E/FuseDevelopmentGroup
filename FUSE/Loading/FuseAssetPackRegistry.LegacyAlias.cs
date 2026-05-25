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
using UnityEngine;

namespace FUSE.Loading
{
    internal static partial class FuseAssetPackRegistry
    {

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
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
            {
                FuseLog.Exception(
                    $"FUSE could not inspect asset pack Catalog.json '{catalogPath}' for legacy aliases", ex);
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
    }
}
