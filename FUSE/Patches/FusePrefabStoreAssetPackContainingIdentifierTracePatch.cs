using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AssetPack.Runtime;
using HarmonyLib;
using Model.Definition;
using Model.Definition.Data;
using Model.Database;
using FUSE.Infrastructure;
using FUSE.Loading;
using Newtonsoft.Json;

namespace FUSE.Patches
{

    /// <summary>
    /// Filters <c>PrefabStore.AssetPackContainingIdentifier</c> so a
    /// definition-identifier lookup never lands on a "loser" pack
    /// folder — a pack folder that shares a leaf name with a sibling
    /// at the mod's root and was therefore demoted by the collision
    /// scanner. Without this filter the random interchange spawn
    /// pool would pick a legacy car definition that lives in
    /// <c>SCAssetPacks/X</c>, the game would resolve it to that pack,
    /// and the subsequent bundle load would collide with the modern
    /// root pack's bundle CAB (Unity refuses two bundles with the
    /// same internal manifest name).
    ///
    /// <para>The transpiler skips loser stores during the per-store
    /// scan. If no non-loser store matches the identifier, the
    /// original method's throw path runs and produces
    /// <c>UnknownIdentifierException</c>, which the game's existing
    /// cleanup code handles by removing the orphaned car instance —
    /// the same behavior the legacy mod stack produces for cars whose
    /// identifier the host loader cannot resolve.</para>
    ///
    /// <para>Verbose-mode tracing is preserved as a Postfix so we
    /// still get a one-shot log line per identifier when diagnosing
    /// resolution outcomes.</para>
    /// </summary>
    [HarmonyPatch(typeof(PrefabStore), "AssetPackContainingIdentifier")]
    internal static class FusePrefabStoreAssetPackContainingIdentifierTracePatch
    {
        private static readonly FieldInfo StoresField =
            AccessTools.Field(typeof(PrefabStore), "_stores");

        private static bool Prefix(PrefabStore __instance, string identifier, ref AssetPackRuntimeStore __result)
        {
            if (string.IsNullOrEmpty(identifier))
            {
                // Original method handles null/empty itself — pass
                // through.
                return true;
            }

            try
            {
                var stores = ReadStoresList(__instance);
                if (stores == null)
                {
                    return true;
                }

                if (TryResolveFromSourceIndex(__instance, stores, identifier, out var indexedStore))
                {
                    __result = indexedStore;
                    return false;
                }

                AssetPackRuntimeStore firstMatch = null;
                foreach (var store in stores)
                {
                    if (store == null || !store.ContainsIdentifier(identifier))
                    {
                        continue;
                    }

                    if (firstMatch == null)
                    {
                        firstMatch = store;
                    }

                    string basePath = null;
                    try
                    {
                        basePath = FuseAssetPackPatchHelpers.ResolveBasePath(store);
                    }
                    catch
                    {
                        basePath = null;
                    }

                    if (string.IsNullOrEmpty(basePath))
                    {
                        // Can't classify; accept this match.
                        __result = store;
                        return false;
                    }

                    if (FuseAssetCollisionRegistry.IsLoserFolder(basePath))
                    {
                        // Loser store — skip it. The interchange must
                        // never spawn a legacy duplicate, and any
                        // car already referencing this identifier
                        // should fall through to UnknownIdentifierException
                        // so the game cleans it up like it would
                        // under the legacy stack.
                        continue;
                    }

                    __result = store;
                    return false;
                }

                // No non-loser store matched. If we observed a loser
                // match but no winner, log a FUSE-distinct warning
                // (deduped per identifier per session) and throw the
                // game's standard exception so its cleanup path
                // removes the orphan car. The FUSE-specific message
                // is what lets users distinguish a filtered identifier
                // from a generic "unknown identifier" the game would
                // report under any other mod loader — without it the
                // log would be indistinguishable from a vanilla
                // missing-definition error.
                if (firstMatch != null)
                {
                    LogFuseFilteredIdentifierOnce(identifier, firstMatch);
                    throw new Model.Database.PrefabStore.UnknownIdentifierException(identifier);
                }

                // Identifier truly absent — let the original method
                // produce its own throw (or whatever the upstream code
                // does today).
                return true;
            }
            catch (Model.Database.PrefabStore.UnknownIdentifierException)
            {
                // Re-throw exactly as the original would have so the
                // caller's catch path runs unchanged.
                throw;
            }
            catch (Exception ex)
            {
                FUSE.Infrastructure.FuseLog.Warning(
                    $"FUSE AssetPackContainingIdentifier filter failed softly for '{identifier}'; " +
                    $"letting original method run: {ex.GetBaseException().Message}");
                return true;
            }
        }

        private static void Postfix(string identifier, AssetPackRuntimeStore __result)
        {
            if (!FUSE.Infrastructure.FuseSettings.VerboseApplyReportDetails)
            {
                return;
            }

            FuseAssetPackResolutionTrace.LogContainingIdentifierOnce(identifier, __result);
        }

        private static System.Collections.Generic.IList<AssetPackRuntimeStore> ReadStoresList(PrefabStore prefabStore)
        {
            if (prefabStore == null || StoresField == null)
            {
                return null;
            }

            return StoresField.GetValue(prefabStore) as System.Collections.Generic.IList<AssetPackRuntimeStore>;
        }

        // PrefabStore's stock lookup calls ContainsIdentifier on every store in
        // registration order. ContainsIdentifier deserializes the entire
        // Definitions.json for every cold store, so the first FUSE scenery
        // lookup paid roughly two seconds just to discover which one of ~80
        // stores owned the identifier. This index scans only top-level
        // objects[].identifier tokens, preserves first-registration and
        // collision-loser semantics, and leaves full deserialization to the one
        // selected store.
        private static readonly object SourceIndexSync = new object();
        private static PrefabStore _sourceIndexOwner;
        private static int _sourceIndexStoreCount = -1;
        private static Dictionary<string, AssetPackRuntimeStore> _sourceIndex;
        private static Dictionary<string, string> _sourceCanonicalIdentifierIndex;
        private static Dictionary<string, AssetPackRuntimeStore> _sourceLoserIndex;
        private static Dictionary<AssetPackRuntimeStore, int> _sourceStoreIndexes;
        private static int _firstUnindexedStore;

        private static bool TryResolveFromSourceIndex(
            PrefabStore prefabStore,
            System.Collections.Generic.IList<AssetPackRuntimeStore> stores,
            string identifier,
            out AssetPackRuntimeStore store)
        {
            store = null;
            if (prefabStore == null || stores == null || string.IsNullOrEmpty(identifier))
            {
                return false;
            }

            lock (SourceIndexSync)
            {
                if (!ReferenceEquals(_sourceIndexOwner, prefabStore) ||
                    _sourceIndexStoreCount != stores.Count ||
                    _sourceIndex == null)
                {
                    BuildSourceIndex(prefabStore, stores);
                }

                if (_sourceIndex == null ||
                    !_sourceIndex.TryGetValue(identifier, out var candidate))
                {
                    return false;
                }

                if (_sourceStoreIndexes == null ||
                    !_sourceStoreIndexes.TryGetValue(candidate, out var candidateIndex) ||
                    (_firstUnindexedStore >= 0 && _firstUnindexedStore < candidateIndex))
                {
                    return false;
                }

                store = candidate;
                return true;
            }
        }

        /// <summary>
        /// Resolves the source spelling of an identifier without asking
        /// SceneryAssetManager to enumerate every mounted Container. The latter
        /// eagerly deserializes hundreds of cold Definitions.json files and was
        /// responsible for a four-second main-thread stall on the first optional
        /// missing scenery asset during map load.
        ///
        /// <paramref name="indexComplete"/> distinguishes a proven miss from an
        /// index that had to skip an opaque store. Callers may safely cache a miss
        /// only when it is true.
        /// </summary>
        internal static bool TryResolveCanonicalSourceIdentifier(
            PrefabStore prefabStore,
            string identifier,
            out string resolvedIdentifier,
            out bool indexComplete)
        {
            resolvedIdentifier = null;
            indexComplete = false;
            if (prefabStore == null || string.IsNullOrWhiteSpace(identifier))
            {
                return false;
            }

            var stores = ReadStoresList(prefabStore);
            if (stores == null)
            {
                return false;
            }

            lock (SourceIndexSync)
            {
                if (!ReferenceEquals(_sourceIndexOwner, prefabStore) ||
                    _sourceIndexStoreCount != stores.Count ||
                    _sourceIndex == null)
                {
                    BuildSourceIndex(prefabStore, stores);
                }

                indexComplete = _firstUnindexedStore < 0;
                if (!indexComplete || _sourceCanonicalIdentifierIndex == null)
                {
                    return false;
                }

                return _sourceCanonicalIdentifierIndex.TryGetValue(
                    identifier,
                    out resolvedIdentifier);
            }
        }

        private static void BuildSourceIndex(
            PrefabStore prefabStore,
            System.Collections.Generic.IList<AssetPackRuntimeStore> stores)
        {
            var index = new Dictionary<string, AssetPackRuntimeStore>(StringComparer.Ordinal);
            var canonicalIndex = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var loserIndex = new Dictionary<string, AssetPackRuntimeStore>(StringComparer.Ordinal);
            var storeIndexes = new Dictionary<AssetPackRuntimeStore, int>();
            var firstUnindexedStore = -1;

            for (var storeIndex = 0; storeIndex < stores.Count; storeIndex++)
            {
                var store = stores[storeIndex];
                if (store == null)
                {
                    continue;
                }

                storeIndexes[store] = storeIndex;

                try
                {
                    var basePath = FuseAssetPackPatchHelpers.ResolveBasePath(store);
                    if (string.IsNullOrWhiteSpace(basePath))
                    {
                        if (firstUnindexedStore < 0)
                        {
                            firstUnindexedStore = storeIndex;
                        }
                        continue;
                    }

                    var definitionsPath = Path.Combine(basePath, "Definitions.json");
                    if (!File.Exists(definitionsPath))
                    {
                        continue;
                    }

                    foreach (var objectIdentifier in ReadTopLevelObjectIdentifiers(definitionsPath))
                    {
                        if (string.IsNullOrEmpty(objectIdentifier) ||
                            index.ContainsKey(objectIdentifier))
                        {
                            continue;
                        }

                        if (FuseAssetCollisionRegistry.IsLoserFolder(basePath))
                        {
                            if (!loserIndex.ContainsKey(objectIdentifier))
                            {
                                loserIndex.Add(objectIdentifier, store);
                            }
                            continue;
                        }

                        index.Add(objectIdentifier, store);
                        if (!canonicalIndex.ContainsKey(objectIdentifier))
                        {
                            canonicalIndex.Add(objectIdentifier, objectIdentifier);
                        }
                    }
                }
                catch
                {
                    // An incomplete earlier store makes later source-index hits
                    // ambiguous. The caller falls back to the stock ordered scan.
                    if (firstUnindexedStore < 0)
                    {
                        firstUnindexedStore = storeIndex;
                    }
                }
            }

            _sourceIndexOwner = prefabStore;
            _sourceIndexStoreCount = stores.Count;
            _sourceIndex = index;
            _sourceCanonicalIdentifierIndex = canonicalIndex;
            _sourceLoserIndex = loserIndex;
            _sourceStoreIndexes = storeIndexes;
            _firstUnindexedStore = firstUnindexedStore;
            FusePerformanceMetrics.RecordCount("prefab source identifier index count", index.Count);
            FuseLog.Info(
                $"FUSE prefab source identifier index built identifiers={index.Count} " +
                $"stores={stores.Count} firstUnindexedStore={firstUnindexedStore}.");
        }

        internal static IEnumerable<string> ReadTopLevelObjectIdentifiers(string definitionsPath)
        {
            using (var stream = File.OpenRead(definitionsPath))
            using (var textReader = new StreamReader(stream))
            using (var reader = new JsonTextReader(textReader))
            {
                var objectsArrayDepth = -1;
                var objectDepth = -1;
                while (reader.Read())
                {
                    if (objectsArrayDepth < 0 &&
                        reader.TokenType == JsonToken.PropertyName &&
                        reader.Depth == 1 &&
                        string.Equals((string)reader.Value, "objects", StringComparison.Ordinal))
                    {
                        if (reader.Read() && reader.TokenType == JsonToken.StartArray)
                        {
                            objectsArrayDepth = reader.Depth;
                        }
                        continue;
                    }

                    if (objectsArrayDepth < 0)
                    {
                        continue;
                    }

                    if (reader.TokenType == JsonToken.EndArray &&
                        reader.Depth == objectsArrayDepth)
                    {
                        yield break;
                    }

                    if (reader.TokenType == JsonToken.StartObject &&
                        reader.Depth == objectsArrayDepth + 1)
                    {
                        objectDepth = reader.Depth;
                        continue;
                    }

                    if (objectDepth >= 0 &&
                        reader.TokenType == JsonToken.PropertyName &&
                        reader.Depth == objectDepth + 1 &&
                        string.Equals((string)reader.Value, "identifier", StringComparison.Ordinal) &&
                        reader.Read() &&
                        reader.TokenType == JsonToken.String)
                    {
                        yield return reader.Value as string;
                    }

                    if (objectDepth >= 0 &&
                        reader.TokenType == JsonToken.EndObject &&
                        reader.Depth == objectDepth)
                    {
                        objectDepth = -1;
                    }
                }
            }
        }

        // Per-process dedup of the FUSE-distinct filter-log line. We
        // only want one warning per filtered identifier per session
        // even though the lookup itself can run hundreds of times for
        // the same identifier (every car of that type, every save
        // load, every interchange roll).
        private static readonly System.Collections.Generic.HashSet<string> LoggedFilteredIdentifiers =
            new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        private static readonly object LoggedFilteredIdentifiersSync = new object();

        private static void LogFuseFilteredIdentifierOnce(
            string identifier,
            AssetPackRuntimeStore loserStore)
        {
            lock (LoggedFilteredIdentifiersSync)
            {
                if (!LoggedFilteredIdentifiers.Add(identifier))
                {
                    return;
                }
            }

            string loserBasePath = null;
            try
            {
                loserBasePath = FuseAssetPackPatchHelpers.ResolveBasePath(loserStore);
            }
            catch
            {
                loserBasePath = null;
            }

            try
            {
                // The prefix "FUSE filtered orphan car identifier" is
                // intentionally specific and unique to FUSE — grep
                // for it in any user's log to confirm FUSE was the
                // one that suppressed the spawn, vs. a generic
                // upstream "Unknown identifier" that any loader could
                // produce. The shortened path uses the same helper as
                // the other asset-pack trace lines so log readers see
                // a consistent format.
                FUSE.Infrastructure.FuseLog.Warning(
                    $"FUSE filtered orphan car identifier '{identifier}' — only definition lives in a " +
                    $"duplicate-leaf-name SCAssetPacks pack ('{FuseAssetPackPatchHelpers.ShortenForLog(loserBasePath)}') " +
                    $"whose bundle conflicts with the modern root pack's bundle. Game will treat the car as " +
                    $"unknown and clean it up; new interchange spawns will not pick this identifier.");
            }
            catch
            {
                // Log failure must not break the lookup.
            }
        }

        /// <summary>
        /// Internal hook so tests / other patches can reset the
        /// once-per-process dedup state. Not used in production.
        /// </summary>
        internal static void ResetFilteredIdentifierLogging()
        {
            lock (LoggedFilteredIdentifiersSync)
            {
                LoggedFilteredIdentifiers.Clear();
            }

            lock (SourceIndexSync)
            {
                _sourceIndexOwner = null;
                _sourceIndexStoreCount = -1;
                _sourceIndex = null;
                _sourceCanonicalIdentifierIndex = null;
                _sourceLoserIndex = null;
                _sourceStoreIndexes = null;
                _firstUnindexedStore = -1;
            }
        }
    }
}
