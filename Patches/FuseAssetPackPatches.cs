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
    [HarmonyPatch(typeof(PrefabStore), "Create")]
    internal static class FusePrefabStoreAssetPackPatch
    {
        private static void Postfix(PrefabStore __result)
        {
            try
            {
                FuseAssetPackRegistry.AddDirectAssetPackStores(__result);
            }
            catch (System.Exception ex)
            {
                FuseLog.Warning($"FUSE direct asset pack store patch failed softly: {ex.Message}");
            }
        }
    }

    [HarmonyPatch]
    internal static class FusePrefabStoreMaterialDefinitionsPatch
    {
        private static readonly HashSet<string> WarnedNullFieldLists = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> WarnedNullFieldPairs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(PrefabStore), "AllDefinitionInfosOfType")
                ?.MakeGenericMethod(typeof(MaterialDefinition));
        }

        private static void Postfix(ref IEnumerable<TypedContainerItem<MaterialDefinition>> __result)
        {
            __result = SanitizeMaterialDefinitions(__result);
        }

        private static IEnumerable<TypedContainerItem<MaterialDefinition>> SanitizeMaterialDefinitions(
            IEnumerable<TypedContainerItem<MaterialDefinition>> items)
        {
            foreach (var item in items ?? Enumerable.Empty<TypedContainerItem<MaterialDefinition>>())
            {
                SanitizeMaterialDefinition(item);
                yield return item;
            }
        }

        /// <summary>
        /// Pure-data sanitizer for a single
        /// <see cref="MaterialDefinition"/>: guarantees a non-null
        /// <see cref="MaterialDefinition.Fields"/> list and drops any
        /// null entries inside it. Internal so the body unit tests
        /// in FUSE.Tests can call it directly with crafted shapes
        /// the upstream PrefabStore enumeration can produce
        /// (null definition, null Fields list, mixed null/valid
        /// FieldPairs). Each fixup is logged at most once per
        /// material identifier so a single corrupted asset doesn't
        /// spam the log every frame.
        /// </summary>
        internal static void SanitizeMaterialDefinition(TypedContainerItem<MaterialDefinition> item)
        {
            var definition = item?.Definition;
            if (definition == null)
            {
                return;
            }

            var identifier = MaterialIdentifier(item, definition);
            if (definition.Fields == null)
            {
                definition.Fields = new List<MaterialDefinition.FieldPair>();
                if (WarnedNullFieldLists.Add(identifier))
                {
                    FuseLog.Warning($"FUSE sanitized material definition '{identifier}' because its fields list was null.");
                }

                return;
            }

            var removedCount = definition.Fields.RemoveAll(field => field == null);
            if (removedCount > 0 && WarnedNullFieldPairs.Add(identifier))
            {
                FuseLog.Warning($"FUSE sanitized material definition '{identifier}' by removing {removedCount} null field item(s).");
            }
        }

        /// <summary>
        /// Test seam: clears the one-fixup-per-identifier log-dedup
        /// state so each unit test starts from a clean slate.
        /// Production code never calls this; tests call it from
        /// SetUp/TearDown so observed log-fire behaviour is
        /// deterministic across test runs.
        /// </summary>
        internal static void ResetSanitizerLoggingForTests()
        {
            WarnedNullFieldLists.Clear();
            WarnedNullFieldPairs.Clear();
        }

        private static string MaterialIdentifier(
            TypedContainerItem<MaterialDefinition> item,
            MaterialDefinition definition)
        {
            if (!string.IsNullOrWhiteSpace(item?.Identifier))
            {
                return item.Identifier;
            }

            if (!string.IsNullOrWhiteSpace(definition.AssetIdentifier))
            {
                return definition.AssetIdentifier;
            }

            return "<unknown>";
        }
    }

    [HarmonyPatch]
    internal static class FuseAggregateLoadModelMaterialFieldPatch
    {
        private static readonly HashSet<string> WarnedLookupFailures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> WarnedNullFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> WarnedNullFieldLists = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static MethodBase TargetMethod()
        {
            var type = AccessTools.TypeByName("RollingStock.LoadModels.AggregateLoadModelController");
            return type == null
                ? null
                : AccessTools.Method(type, "TryGetField");
        }

        private static bool Prefix(MaterialDefinition definition, string key, ref string value, ref bool __result)
        {
            __result = TryGetFieldSafely(definition, key, out value);
            return false;
        }

        /// <summary>
        /// Hardened replacement for
        /// <c>AggregateLoadModelController.TryGetField</c>. The
        /// stock implementation throws on the malformed
        /// <see cref="MaterialDefinition.Fields"/> lists shipped by
        /// some FUSE-loaded asset packs (null list, null entries,
        /// covariance-broken backing array); we catch every failure
        /// mode and fall through to a clean "no match" so the
        /// downstream load-model resolution doesn't blow up the
        /// frame. Internal so the body unit tests in FUSE.Tests can
        /// exercise each branch directly with crafted definitions.
        /// </summary>
        internal static bool TryGetFieldSafely(MaterialDefinition definition, string key, out string value)
        {
            value = null;
            if (definition == null)
            {
                return false;
            }

            var identifier = MaterialIdentifier(definition);
            if (definition.Fields == null)
            {
                if (WarnedNullFieldLists.Add($"{identifier}|{key}"))
                {
                    FuseLog.Warning($"FUSE ignored material field lookup '{key}' for '{identifier}' because its fields list was null.");
                }

                return false;
            }

            try
            {
                for (var index = 0; index < definition.Fields.Count; index++)
                {
                    var field = definition.Fields[index];
                    if (field == null)
                    {
                        if (WarnedNullFields.Add($"{identifier}|{index}"))
                        {
                            FuseLog.Warning($"FUSE skipped null material field item {index} for '{identifier}'.");
                        }

                        continue;
                    }

                    if (!string.Equals(field.Key, key, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    value = field.Value;
                    return true;
                }
            }
            catch (Exception ex)
            {
                if (WarnedLookupFailures.Add($"{identifier}|{key}|{ex.GetType().FullName}"))
                {
                    FuseLog.Warning($"FUSE ignored material field lookup '{key}' for '{identifier}' after exception: {ex.Message}");
                }
            }

            return false;
        }

        private static string MaterialIdentifier(MaterialDefinition definition)
        {
            if (!string.IsNullOrWhiteSpace(definition.AssetIdentifier))
            {
                return definition.AssetIdentifier;
            }

            return "<unknown>";
        }

        /// <summary>
        /// Test seam: clears the one-per-identifier warning dedup
        /// state so each unit test starts from a clean slate.
        /// Production code never calls this.
        /// </summary>
        internal static void ResetLookupLoggingForTests()
        {
            WarnedLookupFailures.Clear();
            WarnedNullFields.Clear();
            WarnedNullFieldLists.Clear();
        }
    }

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

        private static MethodBase TargetMethod()
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

    [HarmonyPatch(typeof(AssetPackRuntimeStore), "Container")]
    internal static class FuseAssetPackRuntimeStoreContainerPatch
    {
        private static bool Prefix(AssetPackRuntimeStore __instance, ref Container __result)
        {
            try
            {
                if (FuseAssetPackRegistry.TryLoadSanitizedDirectContainer(__instance, out var container))
                {
                    __result = container;
                    return false;
                }
            }
            catch (System.Exception ex)
            {
                FuseLog.Warning($"FUSE direct asset pack container patch failed softly: {ex.Message}");
            }

            return true;
        }

        private static void Postfix(AssetPackRuntimeStore __instance, ref Container __result)
        {
            try
            {
                FuseLegacyContainerMixintoRegistry.ApplyToContainer(__instance, __result);
            }
            catch (System.Exception ex)
            {
                FuseLog.Warning($"FUSE legacy support container mixinto patch failed softly: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(PrefabStore), "AssetPackForIdentifier")]
    internal static class FusePrefabStoreLegacyAssetPackIdentifierPatch
    {
        private static void Prefix(ref string assetPackIdentifier, out string __state)
        {
            // Capture the input so the Postfix can produce a single line
            // covering "<incoming> -> <resolved> -> store at <basepath>"
            // — three independent moving parts that together describe
            // the lookup outcome for one call.
            __state = assetPackIdentifier;
            if (FuseAssetPackRegistry.TryResolveLegacyAssetPackIdentifier(assetPackIdentifier, out var resolved))
            {
                assetPackIdentifier = resolved;
            }
        }

        private static void Postfix(string __state, ref string assetPackIdentifier, AssetPackRuntimeStore __result)
        {
            // Verbose-mode one-shot trace. We dedup by the INCOMING
            // identifier so an asset pack that's queried hundreds of
            // times during a session still produces a single log line.
            if (!FUSE.Infrastructure.FuseSettings.VerboseApplyReportDetails)
            {
                return;
            }

            FuseAssetPackResolutionTrace.LogPackForIdentifierOnce(__state, assetPackIdentifier, __result);
        }
    }

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
            if (prefabStore == null)
            {
                return null;
            }
            var storesField = AccessTools.Field(typeof(PrefabStore), "_stores");
            if (storesField == null)
            {
                return null;
            }
            return storesField.GetValue(prefabStore) as System.Collections.Generic.IList<AssetPackRuntimeStore>;
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
        }
    }

    /// <summary>
    /// Filters <c>PrefabStore.AllCarDefinitionInfos</c> so the
    /// interchange's random car-type picker never sees a car
    /// definition that lives in a recorded loser pack folder. Without
    /// this filter the picker rolls from a pool that includes the
    /// legacy SCAssetPacks duplicates (e.g. <c>spinecar1</c>) and
    /// every roll has a chance of producing a car the game cannot
    /// actually render. With the filter on, the picker only sees
    /// definitions from non-loser stores — which matches what the
    /// legacy mod stack produces for an in-game spawn pool.
    ///
    /// <para>Implemented as a Postfix that rewrites
    /// <c>__result</c> to drop any definition whose owning store is a
    /// recorded loser. Looking up the owning store for each
    /// definition uses <c>AssetPackContainingIdentifier</c>, which
    /// our other patch already filters — meaning a loser definition
    /// would already throw <c>UnknownIdentifierException</c> when
    /// resolved. Pre-filtering at the enumeration source avoids that
    /// throw firing every time the random picker rolls.</para>
    /// </summary>
    [HarmonyPatch]
    internal static class FusePrefabStoreAllCarDefinitionInfosFilterPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.PropertyGetter(typeof(PrefabStore), "AllCarDefinitionInfos");
        }

        private static void Postfix(
            PrefabStore __instance,
            ref System.Collections.Generic.IEnumerable<TypedContainerItem<CarDefinition>> __result)
        {
            if (__result == null)
            {
                return;
            }

            try
            {
                __result = FilterLoserDefinitions(__instance, __result);
            }
            catch (Exception ex)
            {
                FUSE.Infrastructure.FuseLog.Warning(
                    $"FUSE AllCarDefinitionInfos loser-filter failed softly; " +
                    $"interchange pool will include legacy duplicates this session: {ex.GetBaseException().Message}");
            }
        }

        private static System.Collections.Generic.IEnumerable<TypedContainerItem<CarDefinition>>
            FilterLoserDefinitions(
                PrefabStore prefabStore,
                System.Collections.Generic.IEnumerable<TypedContainerItem<CarDefinition>> source)
        {
            // Build a small cache of (identifier → owning store base
            // folder) up front so we don't reflect into every store on
            // every yield. The pool can be hundreds of definitions;
            // doing this per-item once per pool enumeration is fine.
            System.Collections.Generic.HashSet<string> loserIdentifiers = null;
            try
            {
                loserIdentifiers = ComputeLoserIdentifiers(prefabStore);
            }
            catch
            {
                loserIdentifiers = null;
            }

            if (loserIdentifiers == null || loserIdentifiers.Count == 0)
            {
                // No collisions recorded, or we failed to compute the
                // set — keep the original enumeration verbatim so we
                // never silently drop content the game expected.
                return source;
            }

            return EnumerateNonLoser(source, loserIdentifiers);
        }

        private static System.Collections.Generic.IEnumerable<TypedContainerItem<CarDefinition>>
            EnumerateNonLoser(
                System.Collections.Generic.IEnumerable<TypedContainerItem<CarDefinition>> source,
                System.Collections.Generic.HashSet<string> loserIdentifiers)
        {
            // The upstream <c>AllCarDefinitionInfos</c> getter returns
            // a lazy <c>HashSet.Select(CarDefinitionInfoForIdentifier)</c>
            // — and <c>CarDefinitionInfoForIdentifier</c> calls
            // <c>AssetPackContainingIdentifier</c>, which our filter
            // Prefix throws on for loser-only identifiers. A naive
            // <c>foreach</c> over <c>source</c> dies on the first
            // such identifier. Iterate manually so we can swallow
            // those throws per-item: LINQ's <c>SelectIterator</c>
            // advances its source enumerator before invoking the
            // selector, so the next <c>MoveNext</c> continues with
            // the next item even when the selector for the current
            // one threw. That gives us "skip throwing items, keep
            // going" semantics for free.
            using (var enumerator = source.GetEnumerator())
            {
                while (true)
                {
                    bool hasNext;
                    TypedContainerItem<CarDefinition> current = null;
                    try
                    {
                        hasNext = enumerator.MoveNext();
                        if (hasNext)
                        {
                            current = enumerator.Current;
                        }
                    }
                    catch (Model.Database.PrefabStore.UnknownIdentifierException)
                    {
                        // Loser identifier the filter rejected.
                        // Move past it and keep going.
                        continue;
                    }
                    catch
                    {
                        // Any other failure inside the selector —
                        // skip the item but don't tear the whole
                        // enumeration down.
                        continue;
                    }

                    if (!hasNext)
                    {
                        yield break;
                    }
                    if (current == null || current.Identifier == null)
                    {
                        yield return current;
                        continue;
                    }
                    if (loserIdentifiers.Contains(current.Identifier))
                    {
                        continue;
                    }
                    yield return current;
                }
            }
        }

        /// <summary>
        /// Walks every store in <c>PrefabStore._stores</c> whose
        /// BasePath is a recorded loser folder and collects each
        /// CarDefinition identifier inside that store. The resulting
        /// set is what the postfix uses to drop entries from
        /// <c>AllCarDefinitionInfos</c>.
        /// </summary>
        private static System.Collections.Generic.HashSet<string> ComputeLoserIdentifiers(PrefabStore prefabStore)
        {
            var result = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
            if (prefabStore == null)
            {
                return result;
            }

            var storesField = AccessTools.Field(typeof(PrefabStore), "_stores");
            if (storesField == null)
            {
                return result;
            }

            if (!(storesField.GetValue(prefabStore) is System.Collections.Generic.IList<AssetPackRuntimeStore> stores))
            {
                return result;
            }

            foreach (var store in stores)
            {
                if (store == null)
                {
                    continue;
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

                if (string.IsNullOrWhiteSpace(basePath) || !FuseAssetCollisionRegistry.IsLoserFolder(basePath))
                {
                    continue;
                }

                Container container = null;
                try
                {
                    container = store.Container();
                }
                catch
                {
                    container = null;
                }

                if (container?.Objects == null)
                {
                    continue;
                }

                foreach (var item in container.Objects)
                {
                    if (item == null || string.IsNullOrEmpty(item.Identifier))
                    {
                        continue;
                    }
                    if (item.Definition is CarDefinition)
                    {
                        result.Add(item.Identifier);
                    }
                }
            }

            return result;
        }
    }

    /// <summary>
    /// One-shot diagnostic logger for asset-pack lookup tracing. Lives
    /// alongside the patches because both
    /// <see cref="FusePrefabStoreLegacyAssetPackIdentifierPatch"/> and
    /// <see cref="FusePrefabStoreAssetPackContainingIdentifierTracePatch"/>
    /// dedupe by their input string — so for any given session, each
    /// (path, identifier) pair logs at most once even though the
    /// underlying lookups run hundreds or thousands of times.
    /// </summary>
    internal static class FuseAssetPackResolutionTrace
    {
        private static readonly System.Collections.Generic.HashSet<string> LoggedPackForIdentifier =
            new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
        private static readonly System.Collections.Generic.HashSet<string> LoggedContainingIdentifier =
            new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
        private static readonly object Sync = new object();
        private static System.Reflection.PropertyInfo _basePathProperty;

        public static void Reset()
        {
            lock (Sync)
            {
                LoggedPackForIdentifier.Clear();
                LoggedContainingIdentifier.Clear();
            }
        }

        public static void LogPackForIdentifierOnce(
            string incomingIdentifier,
            string resolvedIdentifier,
            AssetPackRuntimeStore returnedStore)
        {
            var key = incomingIdentifier ?? "<null>";
            lock (Sync)
            {
                if (!LoggedPackForIdentifier.Add(key))
                {
                    return;
                }
            }

            var basePath = ResolveBasePath(returnedStore);
            var resolved = string.Equals(incomingIdentifier, resolvedIdentifier, StringComparison.Ordinal)
                ? "(unchanged)"
                : $"'{resolvedIdentifier ?? "<null>"}'";

            try
            {
                FUSE.Infrastructure.FuseLog.Info(
                    $"FUSE asset-pack trace AssetPackForIdentifier('{incomingIdentifier ?? "<null>"}') " +
                    $"-> alias-resolved={resolved} -> store basePath='{basePath ?? "<null>"}'.");
            }
            catch
            {
                // Logging itself failed; swallow so a trace line never
                // breaks the underlying lookup.
            }
        }

        public static void LogContainingIdentifierOnce(string identifier, AssetPackRuntimeStore returnedStore)
        {
            var key = identifier ?? "<null>";
            lock (Sync)
            {
                if (!LoggedContainingIdentifier.Add(key))
                {
                    return;
                }
            }

            var basePath = ResolveBasePath(returnedStore);
            try
            {
                FUSE.Infrastructure.FuseLog.Info(
                    $"FUSE asset-pack trace AssetPackContainingIdentifier('{identifier ?? "<null>"}') " +
                    $"-> store identifier='{returnedStore?.Identifier ?? "<null>"}' basePath='{basePath ?? "<null>"}'.");
            }
            catch
            {
                // Same swallow as above; the trace is best-effort.
            }
        }

        private static string ResolveBasePath(AssetPackRuntimeStore store)
        {
            if (store == null)
            {
                return null;
            }
            try
            {
                var property = _basePathProperty
                               ?? (_basePathProperty = AccessTools.Property(typeof(AssetPackRuntimeStore), "BasePath"));
                return property?.GetValue(store, null) as string;
            }
            catch
            {
                return null;
            }
        }
    }

    [HarmonyPatch]
    internal static class FuseAssetPackRuntimeStoreBasePathPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.PropertyGetter(typeof(AssetPackRuntimeStore), "BasePath");
        }

        private static bool Prefix(AssetPackRuntimeStore __instance, ref string __result)
        {
            if (__instance != null &&
                FuseAssetPackRegistry.TryResolveDirectStoreBasePath(__instance.Identifier, out var path))
            {
                __result = path;
                return false;
            }

            return true;
        }
    }

    /// <summary>
    /// Stub patch retained for compatibility with existing reflection tests;
    /// the real collision resolution happens inside
    /// <see cref="FuseAssetPackRuntimeStoreLoadedBundlePatch"/> via CAB-based
    /// AssetBundle dedup. Earlier iterations redirected the loser's
    /// <c>AssetBundlePath</c> getter to the winner's bundle file, which
    /// caused the LOSER to race the WINNER to <c>LoadFromFile</c> on the
    /// SAME path and the second caller to fail with Unity's
    /// "another AssetBundle with the same files is already loaded" error.
    /// The redirect is intentionally a no-op now — see the LoadedBundle
    /// patch below for the actual deduplication.
    /// </summary>
    [HarmonyPatch]
    internal static class FuseAssetPackRuntimeStoreAssetBundlePathPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.PropertyGetter(typeof(AssetPackRuntimeStore), "AssetBundlePath");
        }

        private static bool Prefix(AssetPackRuntimeStore __instance, ref string __result)
        {
            // Intentionally pass through — see class doc comment.
            return true;
        }
    }

    /// <summary>
    /// Stub patch retained for compatibility with existing reflection tests.
    /// Earlier iterations of this class attempted to redirect a "loser"
    /// store's <c>LoadedBundle</c> task to a sibling "winner" store's
    /// bundle file. That approach was wrong: pack folders that share a
    /// leaf name across a mod's root and its <c>SCAssetPacks/</c> legacy
    /// folder almost always contain DIFFERENT bundle content under the
    /// same internal CAB name (the duplicate pack folders are different
    /// versions kept side-by-side as legacy artifacts, not byte
    /// duplicates). Redirecting cross-version returned the wrong prefab
    /// for the calling store's catalog and produced visually broken cars.
    ///
    /// <para>The actual fix lives at registration time: see
    /// <see cref="FuseAssetPackRegistry"/> for the pack discovery
    /// order, which yields root-level packs ahead of
    /// <c>SCAssetPacks/*</c>. With that order in place,
    /// <c>PrefabStore.AssetPackContainingIdentifier</c> reaches the
    /// modern (root) bundle first and the legacy
    /// <c>SCAssetPacks/</c> bundle stays dormant, so Unity's
    /// same-CAB rejection never fires inside a single session.</para>
    /// </summary>
    [HarmonyPatch]
    internal static class FuseAssetPackRuntimeStoreLoadedBundlePatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(AssetPackRuntimeStore), "LoadedBundle");
        }

        private static bool Prefix(AssetPackRuntimeStore __instance, ref System.Threading.Tasks.Task<UnityEngine.AssetBundle> __result)
        {
            // Intentionally pass through — see class doc comment.
            return true;
        }
    }

    /// <summary>
    /// Helpers shared between the AssetBundlePath redirect patch and the
    /// LoadedBundle delegation patch. Centralizes the reflection cache so
    /// we only resolve <c>BasePath</c> /
    /// <c>LoadedBundle</c> / the <c>_stores</c> field once. Also owns the
    /// one-shot diagnostic-log dedup state and the path-shortening
    /// formatter so both can be exercised from unit tests without
    /// constructing a live Unity store.
    /// </summary>
    internal static class FuseAssetPackPatchHelpers
    {
        private static System.Reflection.PropertyInfo _basePathProperty;
        private static System.Reflection.MethodInfo _loadedBundleMethod;
        private static System.Reflection.FieldInfo _prefabStoreStoresField;

        // First-fire tracking for diagnostic logs. The LoadedBundle
        // patch fires every time a colliding loser store's bundle is
        // requested — which is many times per session per pack — so we
        // dedupe by loser folder and emit each outcome once per process
        // lifetime. Exposed as internal so tests can drive the dedup
        // logic directly and reset between cases.
        private static readonly System.Collections.Generic.HashSet<string> LoggedLoserRedirects =
            new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        private static readonly object LoggedLoserRedirectsSync = new object();

        /// <summary>
        /// Returns true the first time the (loserFolder) tuple is seen
        /// this process lifetime, false on every subsequent call. Used
        /// by the LoadedBundle Prefix to log each redirect's outcome
        /// exactly once.
        /// </summary>
        public static bool ShouldLogLoserRedirectOnce(string loserFolder)
        {
            var key = loserFolder ?? "<null>";
            lock (LoggedLoserRedirectsSync)
            {
                return LoggedLoserRedirects.Add(key);
            }
        }

        /// <summary>
        /// Resets the one-shot redirect-log dedup state. Provided so
        /// tests can run case-after-case against a deterministic
        /// initial state; production callers never invoke this.
        /// </summary>
        public static void ResetLoggedLoserRedirects()
        {
            lock (LoggedLoserRedirectsSync)
            {
                LoggedLoserRedirects.Clear();
            }
        }

        /// <summary>
        /// Formats an absolute path for log lines by keeping only the
        /// last three path components prefixed with <c>".../"</c>. Empty
        /// or null input becomes <c>"&lt;unknown&gt;"</c>. Short paths
        /// (two segments or fewer) are returned unchanged. Designed for
        /// human readability in the FUSE log — long paths through deeply
        /// nested mods become "FUSE asset-collision redirect on
        /// '.../TOFC Cars/SCAssetPacks/spinecar1' -> winner '.../...'"
        /// rather than the full 70+ character absolute paths.
        /// </summary>
        public static string ShortenForLog(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
            {
                return "<unknown>";
            }
            try
            {
                var parts = fullPath.Split(
                    new[] { System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar },
                    System.StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length <= 2)
                {
                    return fullPath;
                }
                return ".../" + string.Join("/", parts.Skip(parts.Length - 3));
            }
            catch
            {
                return fullPath;
            }
        }

        public static string ResolveBasePath(AssetPackRuntimeStore store)
        {
            if (store == null)
            {
                return null;
            }
            var property = _basePathProperty
                           ?? (_basePathProperty = AccessTools.Property(typeof(AssetPackRuntimeStore), "BasePath"));
            return property?.GetValue(store, null) as string;
        }

        // Cached reflection handle for the private _loadAssetBundleTask
        // field on AssetPackRuntimeStore. Cached on first use; the field
        // signature is stable across Unity versions and pack-runtime
        // builds so a single MissingFieldException would be a strong
        // signal that the host SDK changed (handle by falling back to
        // null, which makes the LoadedBundle dedup degrade gracefully).
        private static System.Reflection.FieldInfo _loadAssetBundleTaskField;

        private static System.Reflection.FieldInfo GetLoadAssetBundleTaskField()
        {
            return _loadAssetBundleTaskField
                   ?? (_loadAssetBundleTaskField =
                       AccessTools.Field(typeof(AssetPackRuntimeStore), "_loadAssetBundleTask"));
        }

        /// <summary>
        /// Reads the existing cached <c>_loadAssetBundleTask</c> off a
        /// store. The original <c>LoadedBundle</c> uses this field to
        /// avoid issuing a second <c>LoadFromFileAsync</c> for the same
        /// store; mirroring it in the Prefix lets us hand back the same
        /// task on subsequent calls without re-running the CAB dedup.
        /// Returns null when the field is missing, the store is null, or
        /// the cached value is not yet set.
        /// </summary>
        public static System.Threading.Tasks.Task<UnityEngine.AssetBundle> GetCachedLoadAssetBundleTask(
            AssetPackRuntimeStore store)
        {
            if (store == null)
            {
                return null;
            }

            try
            {
                var field = GetLoadAssetBundleTaskField();
                return field?.GetValue(store) as System.Threading.Tasks.Task<UnityEngine.AssetBundle>;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Stamps a resolved <c>Task&lt;AssetBundle&gt;</c> onto the
        /// store's private cache field so the next call to the original
        /// <c>LoadedBundle</c> returns it directly. Best-effort; on
        /// reflection failure the call silently no-ops (the dedup
        /// outcome is still correct because the Prefix re-checks the
        /// cache on every entry).
        /// </summary>
        public static void SetCachedLoadAssetBundleTask(
            AssetPackRuntimeStore store,
            System.Threading.Tasks.Task<UnityEngine.AssetBundle> task)
        {
            if (store == null || task == null)
            {
                return;
            }

            try
            {
                var field = GetLoadAssetBundleTaskField();
                field?.SetValue(store, task);
            }
            catch
            {
                // Field-set failure means the original LoadedBundle will
                // re-enter the Prefix and re-run the CAB dedup, which
                // still produces the correct result — just slower.
            }
        }

        /// <summary>
        /// Reads the embedded CAB-&lt;hex&gt; name from the first bytes
        /// of a Unity AssetBundle file. The bundle file format
        /// (<c>UnityFS</c>, the only one Railroader emits) prefixes its
        /// SerializedFile metadata with a null-terminated ASCII string
        /// of the form <c>CAB-&lt;guid-hex&gt;</c> within the first ~128
        /// bytes, even when block data is compressed; Unity uses the
        /// embedded name (not the file path) as the bundle's identity
        /// for the "already loaded" check. Returns null when the file
        /// is missing, unreadable, smaller than the header window, or
        /// when the header does not start with <c>UnityFS</c>.
        /// </summary>
        public static string TryReadBundleCabName(string bundlePath)
        {
            if (string.IsNullOrWhiteSpace(bundlePath))
            {
                return null;
            }

            try
            {
                if (!System.IO.File.Exists(bundlePath))
                {
                    return null;
                }

                // 512 bytes is comfortably larger than every CAB-name
                // offset I've measured (typically ~58-70). Bundles
                // smaller than this header are almost certainly not
                // valid UnityFS bundles anyway.
                var buffer = new byte[512];
                int read;
                using (var stream = System.IO.File.OpenRead(bundlePath))
                {
                    read = stream.Read(buffer, 0, buffer.Length);
                }

                if (read < 16)
                {
                    return null;
                }

                // Quick signature check; saves us from scanning garbage
                // bytes on a non-AssetBundle file that happens to share
                // the .Bundle name extension.
                if (buffer[0] != (byte)'U' || buffer[1] != (byte)'n' ||
                    buffer[2] != (byte)'i' || buffer[3] != (byte)'t' ||
                    buffer[4] != (byte)'y' || buffer[5] != (byte)'F' ||
                    buffer[6] != (byte)'S')
                {
                    return null;
                }

                for (var i = 0; i < read - 5; i++)
                {
                    if (buffer[i] != (byte)'C' || buffer[i + 1] != (byte)'A' ||
                        buffer[i + 2] != (byte)'B' || buffer[i + 3] != (byte)'-')
                    {
                        continue;
                    }

                    var end = i + 4;
                    while (end < read && buffer[end] != 0)
                    {
                        // Only accept hex digits inside the CAB suffix
                        // so a stray "CAB-" prefix in a random data
                        // block doesn't get mistaken for the manifest
                        // name. The real CAB name is always
                        // hexadecimal.
                        var c = (char)buffer[end];
                        var isHex = (c >= '0' && c <= '9') ||
                                    (c >= 'a' && c <= 'f') ||
                                    (c >= 'A' && c <= 'F');
                        if (!isHex)
                        {
                            break;
                        }

                        end++;
                    }

                    if (end <= i + 4)
                    {
                        continue;
                    }

                    return System.Text.Encoding.ASCII.GetString(buffer, i, end - i);
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        public static System.Threading.Tasks.Task<UnityEngine.AssetBundle> InvokeLoadedBundle(AssetPackRuntimeStore store)
        {
            if (store == null)
            {
                return null;
            }
            var method = _loadedBundleMethod
                         ?? (_loadedBundleMethod = AccessTools.Method(typeof(AssetPackRuntimeStore), "LoadedBundle"));
            if (method == null)
            {
                return null;
            }
            return method.Invoke(store, null) as System.Threading.Tasks.Task<UnityEngine.AssetBundle>;
        }

        public static AssetPackRuntimeStore FindStoreByBasePath(string folderAbsolutePath)
        {
            if (string.IsNullOrWhiteSpace(folderAbsolutePath))
            {
                return null;
            }

            var prefabStore = TryGetSharedPrefabStore();
            if (prefabStore == null)
            {
                return null;
            }

            var storesField = _prefabStoreStoresField
                              ?? (_prefabStoreStoresField =
                                  AccessTools.Field(typeof(Model.Database.PrefabStore), "_stores"));
            if (storesField == null)
            {
                return null;
            }

            var stores = storesField.GetValue(prefabStore) as System.Collections.Generic.IEnumerable<AssetPackRuntimeStore>;
            if (stores == null)
            {
                return null;
            }

            string targetNormalized;
            try
            {
                targetNormalized = System.IO.Path.GetFullPath(folderAbsolutePath);
            }
            catch
            {
                return null;
            }

            foreach (var store in stores)
            {
                if (store == null)
                {
                    continue;
                }
                var storeBase = ResolveBasePath(store);
                if (string.IsNullOrWhiteSpace(storeBase))
                {
                    continue;
                }
                string storeNormalized;
                try
                {
                    storeNormalized = System.IO.Path.GetFullPath(storeBase);
                }
                catch
                {
                    continue;
                }
                if (string.Equals(storeNormalized, targetNormalized, StringComparison.OrdinalIgnoreCase))
                {
                    return store;
                }
            }

            return null;
        }

        private static Model.Database.PrefabStore TryGetSharedPrefabStore()
        {
            // PrefabStore is normally reached via TrainController.Shared.PrefabStore,
            // but TrainController may not yet exist when the asset pack
            // collision patches first fire. Look up reflectively so an
            // early call returns null rather than crashes.
            try
            {
                var trainControllerType = AccessTools.TypeByName("RollingStock.TrainController");
                if (trainControllerType == null)
                {
                    return null;
                }
                var sharedProp = AccessTools.Property(trainControllerType, "Shared");
                var shared = sharedProp?.GetValue(null, null);
                if (shared == null)
                {
                    return null;
                }
                var prefabStoreProp = AccessTools.Property(trainControllerType, "PrefabStore")
                                      ?? AccessTools.Property(shared.GetType(), "PrefabStore");
                return prefabStoreProp?.GetValue(shared, null) as Model.Database.PrefabStore;
            }
            catch
            {
                return null;
            }
        }
    }
}
