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
}
