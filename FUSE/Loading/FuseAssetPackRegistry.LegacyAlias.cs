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
        private static int ExactIdentifierLookupFailureLogged;

        internal static bool TryResolveLegacyAssetPackIdentifier(string identifier, out string resolvedIdentifier)
        {
            return TryResolveLegacyAssetPackIdentifier(null, identifier, out resolvedIdentifier);
        }

        internal static bool TryResolveLegacyAssetPackIdentifier(
            PrefabStore prefabStore,
            string identifier,
            out string resolvedIdentifier)
        {
            resolvedIdentifier = null;
            var normalized = NormalizeLegacyAssetPackIdentifier(identifier);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return false;
            }

            var aliases = GetLegacyAssetPackAliases();
            return aliases.TryGetValue(normalized, out resolvedIdentifier) &&
                   ShouldApplyLegacyAssetPackAlias(
                       identifier,
                       resolvedIdentifier,
                       HasExactRegisteredAssetPackIdentifier(prefabStore, identifier));
        }

        internal static bool ShouldApplyLegacyAssetPackAlias(
            string identifier,
            string resolvedIdentifier,
            bool hasExactRegisteredStore)
        {
            return !hasExactRegisteredStore &&
                   !string.IsNullOrWhiteSpace(resolvedIdentifier) &&
                   !string.Equals(resolvedIdentifier, identifier, StringComparison.Ordinal);
        }

        private static bool HasExactRegisteredAssetPackIdentifier(
            PrefabStore prefabStore,
            string identifier)
        {
            if (prefabStore == null ||
                string.IsNullOrWhiteSpace(identifier) ||
                PrefabStoreStoresField == null)
            {
                return false;
            }

            try
            {
                if (!(PrefabStoreStoresField.GetValue(prefabStore) is System.Collections.IEnumerable stores))
                {
                    return false;
                }

                foreach (var item in stores)
                {
                    if (item is AssetPackRuntimeStore store &&
                        string.Equals(store.Identifier, identifier, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                // Alias resolution is best-effort. If the host changes the
                // backing store shape, retain the prior behavior instead of
                // breaking every AssetPackForIdentifier call.
                if (System.Threading.Interlocked.Exchange(
                        ref ExactIdentifierLookupFailureLogged,
                        1) == 0)
                {
                    FuseLog.Warning(
                        "FUSE could not inspect PrefabStore identifiers before applying a legacy " +
                        $"asset-pack alias; falling back to legacy alias behavior: {ex.GetBaseException().Message}");
                }
            }

            return false;
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

            var directIdentifier = ToDirectStoreIdentifier(assetPackFolder);
            var resolved = ResolveStoreIdentifierForAssetPackFolder(assetPackFolder);
            // Preserve FUSE's URI as an alias even when AssetLoader already
            // owns this folder. Existing converted content may contain that URI,
            // but the lookup target must be the reused store's real identifier.
            AddLegacyAssetPackAlias(aliases, directIdentifier, resolved);
            AddLegacyAssetPackAlias(aliases, resolved, resolved);
            AddLegacyAssetPackAlias(aliases, Path.GetFileName(assetPackFolder), resolved);

            var relative = GetRelativePath(packagePath, assetPackFolder).Replace(Path.DirectorySeparatorChar, '/');
            if (!string.IsNullOrWhiteSpace(packageId) && !string.IsNullOrWhiteSpace(relative))
            {
                // Register the base-game-native "<owner>/<pack-relative-path>" reference
                // form, keyed by both the declared package id and the on-disk folder name.
                // The game composes an AssetReference's pack identifier this way (e.g.
                // "RLW RSP-4 Goldfinch Class/rlw-g3-bc"), and the declared id often differs
                // from the folder (hyphen vs space, etc.), so registering both lets a pack's
                // own components (PrefabModelComponent sub-prefabs, ComponentGroup parts, ...)
                // resolve to this direct store instead of a literal, non-existent bundle path.
                // Older scheme-prefixed references collapse onto these same keys via
                // NormalizeLegacyAssetPackIdentifier, so existing content keeps resolving.
                AddLegacyAssetPackAlias(aliases, $"{packageId}/{relative}", resolved);
                AddLegacyAssetPackAlias(aliases, $"{Path.GetFileName(packagePath)}/{relative}", resolved);
            }

            RegisterCatalogAliases(aliases, packagePath, packageId, assetPackFolder, resolved);
        }

        private static void RegisterCatalogAliases(
            IDictionary<string, string> aliases,
            string packagePath,
            string packageId,
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
                // Warning, not error: Catalog.json is third-party data and this inspection is
                // best-effort — the path-derived aliases were already registered above, so only
                // the optional catalog-declared aliases are lost when the file is unreadable.
                FuseLog.Warning(
                    $"FUSE could not inspect asset pack Catalog.json '{catalogPath}' for legacy aliases: {ex.Message}");

                // Bubble up to the user via the load report: content referencing the
                // catalog-declared aliases may silently fail to resolve, and the pack
                // author needs the file name to fix it.
                var package = !string.IsNullOrWhiteSpace(packageId)
                    ? packageId
                    : Path.GetFileName(packagePath);
                RecordCatalogInspectionFailure(
                    $"Asset pack Catalog.json could not be read: package='{package}' " +
                    $"pack='{Path.GetFileName(assetPackFolder)}' reason='{ex.Message}' — " +
                    "catalog-declared aliases were skipped; content referencing them may not resolve.");
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

        internal static string NormalizeLegacyAssetPackIdentifier(string identifier)
        {
            var normalized = (identifier ?? string.Empty).Trim().Replace('\\', '/').TrimEnd('/');

            // Preserve FUSE's own direct-store scheme verbatim — those identifiers must
            // keep resolving to themselves.
            if (normalized.Length == 0 ||
                normalized.StartsWith(DirectStorePrefix, StringComparison.OrdinalIgnoreCase))
            {
                return normalized;
            }

            // Tolerate older "<scheme>://<owner>/<pack>" asset-pack references (legacy
            // loaders mounted packs under a scheme-prefixed identifier) WITHOUT binding FUSE
            // to any specific legacy scheme name: drop a leading "<scheme>://" so the
            // reference collapses onto the base-game-native "<owner>/<pack>" aliases. This
            // keeps existing scheme-prefixed content resolving while FUSE's own source
            // references only its own scheme and the base-game form.
            var schemeIndex = normalized.IndexOf("://", StringComparison.Ordinal);
            return schemeIndex >= 0
                ? normalized.Substring(schemeIndex + 3)
                : normalized;
        }
    }
}
