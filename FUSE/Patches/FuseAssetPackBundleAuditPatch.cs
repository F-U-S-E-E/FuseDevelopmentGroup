using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AssetPack.Runtime;
using FUSE.Infrastructure;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FUSE.Patches
{
    /// <summary>
    /// Faults external catalog/bundle mismatches the moment they are knowable
    /// instead of letting them surface as per-retry load exceptions. Base-game
    /// packs are excluded because their catalogs contain compatibility aliases
    /// that can resolve through game-owned stores without a same-named bundle
    /// entry; treating those aliases as broken makes FUSE report stock content
    /// such as Allen Dual Shed and Great Northern 5 Chime as mod failures.
    ///
    /// An asset pack's Catalog.json declares assets by name/filename, but nothing
    /// validates that the pack's bundle actually contains them. A declared-but-
    /// missing asset (seen in the field: 'aspenbridgeclear' in the aspensassets
    /// pack) fails on every load attempt, and because scenery re-requests assets
    /// on every streaming pass, one bad entry becomes a burst of exceptions and
    /// crash-report uploads per visit — with nothing telling the user which pack
    /// to fix.
    ///
    /// The game only opens a pack's bundle on demand, so this audits at exactly
    /// that moment: a postfix on the store's bundle-load accessor diffs the
    /// bundle's real contents against the pack's Catalog.json once per store per
    /// map, and reports every declared-but-missing asset before it is requested.
    /// Catalog metadata is preserved so non-scenery assets (audio, textures, and
    /// materials) stay in the generic report instead of being misreported or
    /// quarantined as scenery. A prefab is only promoted to the scenery channel
    /// when a live <see cref="SceneryAssetInstance"/> confirms that use. Cost is
    /// one GetAllAssetNames + a set diff on a bundle the game just loaded anyway.
    /// Fail-open everywhere: an unreadable catalog (already surfaced as a
    /// load-report notice elsewhere) or a faulted bundle load simply skips the
    /// audit.
    /// </summary>
    [HarmonyPatch]
    internal static class FuseAssetPackBundleAuditPatch
    {
        // Store display name ("Location Identifier") -> audited this map. Cleared
        // alongside the load-failure dedupe (see ResetForNewMap) so the report
        // repopulates after a map reload.
        private static readonly HashSet<string> AuditedStores =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly object AuditLock = new object();

        private static System.Reflection.MethodInfo TargetMethod()
        {
            return AccessTools.Method(typeof(AssetPackRuntimeStore), "LoadedBundle");
        }

        internal static void ResetForNewMap()
        {
            lock (AuditLock)
            {
                AuditedStores.Clear();
            }
        }

        private static void Postfix(AssetPackRuntimeStore __instance, Task<AssetBundle> __result)
        {
            if (__instance == null || __result == null)
            {
                return;
            }

            try
            {
                // LoadedBundle is called once per asset request; claim the store
                // before attaching so only the first request per map audits. A
                // faulted bundle load consumes the claim without auditing —
                // acceptable, since a bundle that cannot load at all already
                // screams through the runtime failure nets.
                lock (AuditLock)
                {
                    if (!AuditedStores.Add(__instance.ToString()))
                    {
                        return;
                    }
                }

                __result.ContinueWith(
                    task => AuditBundle(__instance, task.Result),
                    TaskContinuationOptions.OnlyOnRanToCompletion |
                    TaskContinuationOptions.ExecuteSynchronously);
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE asset pack bundle audit could not attach", ex);
            }
        }

        // Runs on the main thread: the store resolves its bundle task from
        // Unity's AsyncOperation.completed callback (or synchronously for
        // static bundles), so ExecuteSynchronously lands here.
        private static void AuditBundle(AssetPackRuntimeStore store, AssetBundle bundle)
        {
            try
            {
                if (store == null || bundle == null)
                {
                    return;
                }

                var basePath = FuseAssetPackPatchHelpers.ResolveBasePath(store);
                if (IsBaseGameAssetPackPath(basePath, Application.streamingAssetsPath))
                {
                    return;
                }

                var declared = ReadDeclaredAssets(store, basePath);
                if (declared.Count == 0)
                {
                    return;
                }

                var missing = FindMissingDeclaredAssets(declared, bundle.GetAllAssetNames());
                if (missing.Count == 0)
                {
                    return;
                }

                FuseLog.Warning(
                    $"FUSE asset pack '{store.Identifier}' declares {missing.Count} asset(s) in its Catalog.json " +
                    $"that its bundle does not contain (first: '{missing[0].Identifier}', " +
                    $"type='{missing[0].Type}'). Each will fail if requested; the pack needs its bundle " +
                    "rebuilt or the catalog entries removed.");

                foreach (var entry in missing)
                {
                    // An audit proves a catalog/bundle mismatch, not how the asset
                    // is used. Keep the finding generic until the main-thread drain
                    // can confirm a scenery placement. In particular, prefab is not
                    // a scenery type: car parts and whistle definitions use it too.
                    FuseSceneryLoadFailurePatch.ReportCatalogMismatch(entry, store.Identifier);
                }
            }
            catch (Exception ex)
            {
                FuseLog.Exception(
                    $"FUSE asset pack bundle audit failed for '{store?.Identifier ?? "<unknown>"}'", ex);
            }
        }

        // Parse Catalog.json ourselves (same approach as the legacy-alias
        // inspection) rather than calling the store's Catalog(): that API throws
        // on the malformed catalogs some packs ship, and pulling the parsed
        // struct in would add an AssetPack.Common compile-time reference for two
        // string fields.
        private static List<CatalogAssetEntry> ReadDeclaredAssets(
            AssetPackRuntimeStore store,
            string basePath)
        {
            var declared = new List<CatalogAssetEntry>();
            if (string.IsNullOrWhiteSpace(basePath))
            {
                return declared;
            }

            var catalogPath = Path.Combine(basePath, "Catalog.json");
            if (!File.Exists(catalogPath))
            {
                return declared;
            }

            try
            {
                var root = JObject.Parse(File.ReadAllText(catalogPath));
                if (!(root["assets"] is JObject assets))
                {
                    return declared;
                }

                foreach (var property in assets.Properties())
                {
                    var name = property.Value?["name"]?.ToString() ?? property.Name;
                    var type = property.Value?["type"]?.ToString() ?? string.Empty;
                    var filename = property.Value?["filename"]?.ToString() ?? string.Empty;
                    declared.Add(new CatalogAssetEntry(property.Name, name, type, filename));
                }
            }
            catch (Exception ex)
            {
                // Malformed catalogs are already surfaced as load-report notices
                // by the alias inspection (with the parse position); keep this at
                // info so the audit skip is traceable without double-warning.
                FuseLog.Info(
                    $"FUSE bundle audit skipped pack '{store.Identifier}': its Catalog.json could not be parsed " +
                    $"({ex.GetBaseException().Message}).");
            }

            return declared;
        }

        internal static bool IsBaseGameAssetPackPath(string basePath, string streamingAssetsPath)
        {
            if (string.IsNullOrWhiteSpace(basePath) ||
                string.IsNullOrWhiteSpace(streamingAssetsPath))
            {
                return false;
            }

            try
            {
                var candidate = Path.GetFullPath(basePath)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var assetPackRoot = Path.GetFullPath(Path.Combine(streamingAssetsPath, "AssetPacks"))
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var rootPrefix = assetPackRoot + Path.DirectorySeparatorChar;
                return string.Equals(candidate, assetPackRoot, StringComparison.OrdinalIgnoreCase) ||
                       candidate.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Pure diff (unit-tested): declared catalog entries that
        /// match no bundle asset. Bundle asset names are lowercase full paths
        /// ("assets/.../name.prefab"); a declared entry counts as present when
        /// its filename matches a bundle entry's file name OR its identifier
        /// matches a bundle entry's file name without extension — mirroring
        /// that the loader requests assets by identifier, which the engine
        /// matches by path-less name.
        /// </summary>
        internal static List<CatalogAssetEntry> FindMissingDeclaredAssets(
            IReadOnlyList<CatalogAssetEntry> declared,
            IEnumerable<string> bundleAssetNames)
        {
            var missing = new List<CatalogAssetEntry>();
            if (declared == null || declared.Count == 0)
            {
                return missing;
            }

            var fileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var fileNamesWithoutExtension = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var assetName in bundleAssetNames ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(assetName))
                {
                    continue;
                }

                fileNames.Add(Path.GetFileName(assetName));
                fileNamesWithoutExtension.Add(Path.GetFileNameWithoutExtension(assetName));
            }

            foreach (var entry in declared)
            {
                var identifier = entry.Identifier;
                var filename = entry.Filename;
                if (string.IsNullOrWhiteSpace(identifier))
                {
                    continue;
                }

                var presentByFilename =
                    !string.IsNullOrWhiteSpace(filename) && fileNames.Contains(filename);
                var presentByIdentifier = fileNamesWithoutExtension.Contains(identifier);
                if (!presentByFilename && !presentByIdentifier)
                {
                    missing.Add(entry);
                }
            }

            return missing;
        }

        /// <summary>
        /// Immutable catalog metadata carried from parsing through diffing and
        /// reporting. The catalog key is the loader identifier; <see cref="Name"/>
        /// is retained separately because hand-authored packs do not always make
        /// the two values identical.
        /// </summary>
        internal readonly struct CatalogAssetEntry
        {
            internal CatalogAssetEntry(string identifier, string name, string type, string filename)
            {
                Identifier = identifier ?? string.Empty;
                Name = name ?? string.Empty;
                Type = type ?? string.Empty;
                Filename = filename ?? string.Empty;
            }

            internal string Identifier { get; }

            internal string Name { get; }

            internal string Type { get; }

            internal string Filename { get; }
        }

        /// <summary>
        /// Catalog type classification used by the main-thread report drain.
        /// A prefab is deliberately distinct from scenery: Railroader catalogs
        /// also use prefab entries for cars, trucks, and audio definitions.
        /// </summary>
        internal enum CatalogAssetTypeClassification
        {
            Unknown,
            Prefab,
            NonScenery,
            Scenery
        }

        internal static CatalogAssetTypeClassification ClassifyCatalogAssetType(string type)
        {
            if (string.IsNullOrWhiteSpace(type))
            {
                return CatalogAssetTypeClassification.Unknown;
            }

            switch (type.Trim().ToLowerInvariant())
            {
                case "prefab":
                    return CatalogAssetTypeClassification.Prefab;
                case "audio":
                case "audioclip":
                case "material":
                case "texture":
                    return CatalogAssetTypeClassification.NonScenery;
                case "scenery":
                case "sceneryprefab":
                    return CatalogAssetTypeClassification.Scenery;
                default:
                    return CatalogAssetTypeClassification.Unknown;
            }
        }
    }
}
