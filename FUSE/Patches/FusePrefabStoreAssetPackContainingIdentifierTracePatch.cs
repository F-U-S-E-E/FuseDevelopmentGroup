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
                var skippedMalformedStore = false;
                foreach (var store in stores)
                {
                    if (store == null)
                    {
                        continue;
                    }

                    if (IsQuarantinedDefinitionStore(store))
                    {
                        skippedMalformedStore = true;
                        continue;
                    }

                    bool containsIdentifier;
                    try
                    {
                        containsIdentifier = store.ContainsIdentifier(identifier);
                    }
                    catch (JsonException ex)
                    {
                        QuarantineDefinitionStore(__instance, store, ex);
                        skippedMalformedStore = true;
                        continue;
                    }

                    if (!containsIdentifier)
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

                // The stock method would revisit every store and hit the same
                // malformed Definitions.json again. We already completed the
                // ordered scan of every usable store, so preserve the stock
                // unknown-identifier outcome without re-entering the bad pack.
                if (skippedMalformedStore)
                {
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
        private static SourceStoreScanState<AssetPackRuntimeStore> _sourceStoreScanState;
        private static readonly DefinitionStoreQuarantine<AssetPackRuntimeStore> QuarantinedDefinitionStoreRegistry =
            new DefinitionStoreQuarantine<AssetPackRuntimeStore>();

        internal sealed class SourceStoreScanState<TStore>
            where TStore : class
        {
            private readonly HashSet<TStore> _malformedStores = new HashSet<TStore>();

            internal int FirstOpaqueStoreIndex { get; private set; } = -1;

            internal int MalformedStoreCount => _malformedStores.Count;

            internal bool MarkMalformed(TStore store)
            {
                return store != null && _malformedStores.Add(store);
            }

            internal void MarkOpaque(int storeIndex)
            {
                if (FirstOpaqueStoreIndex < 0)
                {
                    FirstOpaqueStoreIndex = storeIndex;
                }
            }

            internal bool ShouldProbe(TStore store)
            {
                return store != null && !_malformedStores.Contains(store);
            }

            internal bool CanUseCandidate(int candidateIndex)
            {
                return candidateIndex >= 0 &&
                       (FirstOpaqueStoreIndex < 0 || FirstOpaqueStoreIndex >= candidateIndex);
            }

        }

        internal sealed class DefinitionStoreQuarantine<TStore>
            where TStore : class
        {
            private readonly HashSet<TStore> _stores = new HashSet<TStore>();

            internal bool Add(TStore store)
            {
                return store != null && _stores.Add(store);
            }

            internal bool Contains(TStore store)
            {
                return store != null && _stores.Contains(store);
            }

            internal SourceStoreScanState<TStore> CreateScanState(IEnumerable<TStore> stores)
            {
                var state = new SourceStoreScanState<TStore>();
                foreach (var store in stores ?? Enumerable.Empty<TStore>())
                {
                    if (Contains(store))
                    {
                        state.MarkMalformed(store);
                    }
                }

                return state;
            }

            internal bool CanUseCandidate(
                SourceStoreScanState<TStore> scanState,
                TStore store,
                int storeIndex)
            {
                return !Contains(store) &&
                       scanState != null &&
                       scanState.ShouldProbe(store) &&
                       scanState.CanUseCandidate(storeIndex);
            }

            internal void Clear()
            {
                _stores.Clear();
            }
        }

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
                    !QuarantinedDefinitionStoreRegistry.CanUseCandidate(
                        _sourceStoreScanState,
                        candidate,
                        candidateIndex))
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

                indexComplete = _sourceStoreScanState != null &&
                                _sourceStoreScanState.FirstOpaqueStoreIndex < 0;
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
            var scanState = QuarantinedDefinitionStoreRegistry.CreateScanState(stores);

            for (var storeIndex = 0; storeIndex < stores.Count; storeIndex++)
            {
                var store = stores[storeIndex];
                if (store == null)
                {
                    continue;
                }

                storeIndexes[store] = storeIndex;
                if (!scanState.ShouldProbe(store))
                {
                    continue;
                }

                string basePath;
                try
                {
                    basePath = FuseAssetPackPatchHelpers.ResolveBasePath(store);
                }
                catch
                {
                    scanState.MarkOpaque(storeIndex);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(basePath))
                {
                    scanState.MarkOpaque(storeIndex);
                    continue;
                }

                try
                {
                    var definitionsPath = Path.Combine(basePath, "Definitions.json");
                    if (!File.Exists(definitionsPath))
                    {
                        continue;
                    }

                    if (!TryReadCompleteTopLevelObjectIdentifiers(
                            definitionsPath,
                            out var objectIdentifiers,
                            out var jsonException))
                    {
                        QuarantinedDefinitionStoreRegistry.Add(store);
                        scanState.MarkMalformed(store);
                        LogQuarantinedDefinitionStoreOnce(store, basePath, jsonException);
                        continue;
                    }

                    // Resolve collision status before committing any entries so
                    // an opaque registry failure cannot leave a partial store in
                    // the source index either.
                    var isLoserFolder = FuseAssetCollisionRegistry.IsLoserFolder(basePath);
                    foreach (var objectIdentifier in objectIdentifiers)
                    {
                        if (string.IsNullOrEmpty(objectIdentifier) ||
                            index.ContainsKey(objectIdentifier))
                        {
                            continue;
                        }

                        if (isLoserFolder)
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
                    scanState.MarkOpaque(storeIndex);
                }
            }

            _sourceIndexOwner = prefabStore;
            _sourceIndexStoreCount = stores.Count;
            _sourceIndex = index;
            _sourceCanonicalIdentifierIndex = canonicalIndex;
            _sourceLoserIndex = loserIndex;
            _sourceStoreIndexes = storeIndexes;
            _sourceStoreScanState = scanState;
            FusePerformanceMetrics.RecordCount("prefab source identifier index count", index.Count);
            FuseLog.Info(
                $"FUSE prefab source identifier index built identifiers={index.Count} " +
                $"stores={stores.Count} quarantinedStores={scanState.MalformedStoreCount} " +
                $"firstUnindexedStore={scanState.FirstOpaqueStoreIndex}.");
        }

        internal static bool TryReadCompleteTopLevelObjectIdentifiers(
            string definitionsPath,
            out string[] identifiers,
            out JsonException jsonException)
        {
            identifiers = Array.Empty<string>();
            jsonException = null;

            try
            {
                identifiers = ReadTopLevelObjectIdentifiers(definitionsPath).ToArray();
                return true;
            }
            catch (JsonException ex)
            {
                jsonException = ex;
                return false;
            }
        }

        internal static IEnumerable<string> ReadTopLevelObjectIdentifiers(string definitionsPath)
        {
            var identifiers = new List<string>();
            using (var stream = File.OpenRead(definitionsPath))
            using (var textReader = new StreamReader(stream))
            using (var reader = new JsonTextReader(textReader))
            {
                var objectsArrayDepth = -1;
                var objectDepth = -1;
                var objectsArrayFinished = false;
                var rootObjectStarted = false;
                var rootObjectFinished = false;
                while (reader.Read())
                {
                    if (reader.TokenType == JsonToken.StartObject && reader.Depth == 0)
                    {
                        rootObjectStarted = true;
                    }
                    else if (reader.TokenType == JsonToken.EndObject && reader.Depth == 0)
                    {
                        rootObjectFinished = true;
                    }

                    if (!objectsArrayFinished &&
                        objectsArrayDepth < 0 &&
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
                        objectsArrayDepth = -1;
                        objectDepth = -1;
                        objectsArrayFinished = true;
                        continue;
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
                        identifiers.Add(reader.Value as string);
                    }

                    if (objectDepth >= 0 &&
                        reader.TokenType == JsonToken.EndObject &&
                        reader.Depth == objectDepth)
                    {
                        objectDepth = -1;
                    }
                }

                if (!rootObjectStarted || !rootObjectFinished)
                {
                    throw new JsonReaderException(
                        "Definitions.json must contain a complete root JSON object.");
                }
            }

            return identifiers;
        }

        internal static bool IsQuarantinedDefinitionStore(AssetPackRuntimeStore store)
        {
            lock (SourceIndexSync)
            {
                return QuarantinedDefinitionStoreRegistry.Contains(store);
            }
        }

        internal static void QuarantineDefinitionStore(
            PrefabStore prefabStore,
            AssetPackRuntimeStore store,
            JsonException jsonException)
        {
            var added = false;
            lock (SourceIndexSync)
            {
                added = QuarantinedDefinitionStoreRegistry.Add(store);
                var activeIndexContainsStore = _sourceStoreIndexes != null &&
                                               _sourceStoreIndexes.ContainsKey(store);
                if (ReferenceEquals(_sourceIndexOwner, prefabStore) || activeIndexContainsStore)
                {
                    if (_sourceStoreScanState == null)
                    {
                        _sourceStoreScanState = new SourceStoreScanState<AssetPackRuntimeStore>();
                    }

                    if (_sourceStoreScanState.MarkMalformed(store) || activeIndexContainsStore)
                    {
                        // A store can become malformed after the index was built.
                        // Rebuild on the next lookup so no stale entry can select it.
                        _sourceIndex = null;
                        _sourceCanonicalIdentifierIndex = null;
                        _sourceLoserIndex = null;
                        _sourceStoreIndexes = null;
                    }
                }
            }

            if (!added)
            {
                return;
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

            LogQuarantinedDefinitionStoreOnce(store, basePath, jsonException);
        }

        private static readonly HashSet<string> LoggedQuarantinedDefinitionStores =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly object LoggedQuarantinedDefinitionStoresSync = new object();

        private static void LogQuarantinedDefinitionStoreOnce(
            AssetPackRuntimeStore store,
            string basePath,
            JsonException jsonException)
        {
            try
            {
                string definitionsPath = null;
                if (!string.IsNullOrWhiteSpace(basePath))
                {
                    definitionsPath = Path.Combine(basePath, "Definitions.json");
                }

                var storeIdentifier = store?.Identifier;
                var logKey = definitionsPath ?? storeIdentifier ?? "<unknown>";
                lock (LoggedQuarantinedDefinitionStoresSync)
                {
                    if (!LoggedQuarantinedDefinitionStores.Add(logKey))
                    {
                        return;
                    }
                }

                FuseLog.Warning(
                    "FUSE quarantined malformed asset pack definitions at " +
                    $"'{definitionsPath ?? storeIdentifier ?? "<unknown>"}' from prefab identifier lookup: " +
                    $"{jsonException?.GetBaseException().Message ?? "invalid JSON"}. " +
                    "Other asset packs will continue to resolve; the source file was not modified.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"FUSE could not log quarantined asset pack diagnostics: {ex.Message}");
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
                _sourceStoreScanState = null;
                QuarantinedDefinitionStoreRegistry.Clear();
            }

            lock (LoggedQuarantinedDefinitionStoresSync)
            {
                LoggedQuarantinedDefinitionStores.Clear();
            }
        }
    }
}
