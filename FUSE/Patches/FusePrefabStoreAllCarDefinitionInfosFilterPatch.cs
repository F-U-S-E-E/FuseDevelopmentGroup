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
        private static MethodInfo TargetMethod()
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
}
