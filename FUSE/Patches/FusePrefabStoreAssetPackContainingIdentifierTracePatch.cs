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
}
