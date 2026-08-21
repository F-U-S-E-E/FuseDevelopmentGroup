using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AssetPack.Runtime;
using Model.Definition;
using Model.Database;
using Newtonsoft.Json.Linq;
using FUSE.Infrastructure;

namespace FUSE.Loading
{
    internal static partial class FuseAssetPackRegistry
    {
        private const string FuseDefinitionOverridesProperty = "FuseDefinitionOverrides";

        private static readonly object LegacyDefinitionOverrideLock = new object();
        private static Dictionary<string, FuseLegacyDefinitionOverrideRegistration>
            LegacyDefinitionOverridesByStore =
                new Dictionary<string, FuseLegacyDefinitionOverrideRegistration>(StringComparer.Ordinal);
        private static string[] LegacyDefinitionOverrideIssues = Array.Empty<string>();
        private static readonly HashSet<string> LoggedLegacyDefinitionOverrideLoads =
            new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> LoggedLegacyDefinitionOverrideFailures =
            new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Rebuilds the FUSE-owned equivalent of AssetLoader's definitions-only
        /// child-folder routing. A folder containing Catalog.json remains a normal
        /// store. An immediate child containing only Definitions.json targets the
        /// existing store whose identifier equals the child folder name.
        /// Native packages can opt into nested or differently named files through
        /// Info.json/FuseDefinitionOverrides.
        /// </summary>
        private static void RefreshLegacyDefinitionOverrides()
        {
            var candidates = new List<FuseLegacyDefinitionOverrideRegistration>();
            var issues = new List<string>();
            var modsRoot = FuseDataPackageDiscovery.GetModsRoot();

            if (!string.IsNullOrWhiteSpace(modsRoot) && Directory.Exists(modsRoot))
            {
                string[] packagePaths;
                try
                {
                    packagePaths = Directory.GetDirectories(modsRoot)
                        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                }
                catch (Exception ex)
                {
                    packagePaths = Array.Empty<string>();
                    issues.Add(
                        $"Asset definition override discovery could not enumerate '{modsRoot}': " +
                        ex.GetBaseException().Message);
                }

                foreach (var packagePath in packagePaths)
                {
                    if (!ShouldInspectPackage(packagePath))
                    {
                        continue;
                    }

                    try
                    {
                        candidates.AddRange(
                            DiscoverDefinitionOverrideCandidatesForPackage(packagePath, out var packageIssues));
                        issues.AddRange(packageIssues);
                    }
                    catch (Exception ex)
                    {
                        issues.Add(
                            $"Asset definition override discovery failed for package '{packagePath}': " +
                            ex.GetBaseException().Message);
                    }
                }
            }

            var winners = SelectLegacyDefinitionOverrides(candidates, out var selectionIssues);
            issues.AddRange(selectionIssues);

            lock (LegacyDefinitionOverrideLock)
            {
                LegacyDefinitionOverridesByStore = winners;
                LegacyDefinitionOverrideIssues = issues
                    .Where(issue => !string.IsNullOrWhiteSpace(issue))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                LoggedLegacyDefinitionOverrideLoads.Clear();
                LoggedLegacyDefinitionOverrideFailures.Clear();
            }

            if (winners.Count > 0)
            {
                FuseLog.Info(
                    $"FUSE discovered {winners.Count} AssetLoader-compatible definition override(s); " +
                    $"issues={issues.Count}.");
            }
        }

        internal static FuseLegacyDefinitionOverrideRegistration[]
            DiscoverDefinitionOverrideCandidatesForPackage(
                string packagePath,
                out string[] issues)
        {
            var result = new List<FuseLegacyDefinitionOverrideRegistration>();
            var issueList = new List<string>();
            if (string.IsNullOrWhiteSpace(packagePath) || !Directory.Exists(packagePath))
            {
                issues = Array.Empty<string>();
                return Array.Empty<FuseLegacyDefinitionOverrideRegistration>();
            }

            var packageRoot = Path.GetFullPath(packagePath);
            var packageId = TryReadPackageId(packageRoot) ?? Path.GetFileName(packageRoot);
            var infoPath = Path.Combine(packageRoot, "Info.json");
            if (!File.Exists(infoPath))
            {
                var lowerInfoPath = Path.Combine(packageRoot, "info.json");
                if (File.Exists(lowerInfoPath))
                {
                    infoPath = lowerInfoPath;
                }
            }

            if (File.Exists(infoPath))
            {
                try
                {
                    var info = FuseLegacyDataConverter.ReadLegacyObject(infoPath);
                    AppendExplicitDefinitionOverrideCandidates(
                        packageRoot,
                        packageId,
                        info[FuseDefinitionOverridesProperty],
                        result,
                        issueList);
                }
                catch (Exception ex)
                {
                    issueList.Add(
                        $"Package '{packageId}' could not parse '{infoPath}' while reading " +
                        $"{FuseDefinitionOverridesProperty}: {ex.GetBaseException().Message}");
                }
            }

            // Preserve AssetLoader 1.0.1's implicit convention exactly: only
            // immediate child folders, only when Catalog.json is absent.
            foreach (var child in Directory.GetDirectories(packageRoot)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                var definitionsPath = Path.Combine(child, "Definitions.json");
                if (File.Exists(Path.Combine(child, "Catalog.json")) ||
                    !File.Exists(definitionsPath))
                {
                    continue;
                }

                result.Add(new FuseLegacyDefinitionOverrideRegistration
                {
                    StoreIdentifier = Path.GetFileName(child),
                    DefinitionsPath = Path.GetFullPath(definitionsPath),
                    PackageId = packageId,
                    PackagePath = packageRoot,
                    Explicit = false
                });
            }

            issues = issueList.ToArray();
            return result
                .GroupBy(
                    item => item.StoreIdentifier + "\0" + item.DefinitionsPath,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderByDescending(item => item.Explicit).First())
                .ToArray();
        }

        private static void AppendExplicitDefinitionOverrideCandidates(
            string packageRoot,
            string packageId,
            JToken token,
            ICollection<FuseLegacyDefinitionOverrideRegistration> result,
            ICollection<string> issues)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return;
            }

            if (token.Type == JTokenType.Array)
            {
                foreach (var item in token.Children())
                {
                    AppendExplicitDefinitionOverrideCandidates(
                        packageRoot,
                        packageId,
                        item,
                        result,
                        issues);
                }
                return;
            }

            string storeIdentifier = null;
            string relativePath = null;
            if (token.Type == JTokenType.String)
            {
                relativePath = (string)token;
            }
            else if (token is JObject obj)
            {
                storeIdentifier =
                    (string)obj["StoreIdentifier"] ??
                    (string)obj["storeIdentifier"] ??
                    (string)obj["Identifier"] ??
                    (string)obj["identifier"];
                relativePath =
                    (string)obj["Path"] ??
                    (string)obj["path"] ??
                    (string)obj["DefinitionsPath"] ??
                    (string)obj["definitionsPath"];
            }

            if (string.IsNullOrWhiteSpace(relativePath))
            {
                issues.Add(
                    $"Package '{packageId}' has a {FuseDefinitionOverridesProperty} entry " +
                    "without a non-empty Path.");
                return;
            }

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(Path.Combine(packageRoot, relativePath.Trim()));
                if (Directory.Exists(fullPath))
                {
                    fullPath = Path.Combine(fullPath, "Definitions.json");
                }
            }
            catch (Exception ex)
            {
                issues.Add(
                    $"Package '{packageId}' has invalid definition override path " +
                    $"'{relativePath}': {ex.GetBaseException().Message}");
                return;
            }

            if (!IsPathWithinPackage(packageRoot, fullPath))
            {
                issues.Add(
                    $"Package '{packageId}' definition override path '{relativePath}' " +
                    "escapes the package folder and was ignored.");
                return;
            }

            if (!File.Exists(fullPath))
            {
                issues.Add(
                    $"Package '{packageId}' definition override file was not found: '{fullPath}'.");
                return;
            }

            if (string.IsNullOrWhiteSpace(storeIdentifier))
            {
                storeIdentifier = Path.GetFileName(Path.GetDirectoryName(fullPath));
            }

            if (string.IsNullOrWhiteSpace(storeIdentifier))
            {
                issues.Add(
                    $"Package '{packageId}' could not infer a target store for '{relativePath}'.");
                return;
            }

            result.Add(new FuseLegacyDefinitionOverrideRegistration
            {
                StoreIdentifier = storeIdentifier.Trim(),
                DefinitionsPath = fullPath,
                PackageId = packageId,
                PackagePath = packageRoot,
                Explicit = true
            });
        }

        private static bool IsPathWithinPackage(string packageRoot, string candidatePath)
        {
            var normalizedRoot = Path.GetFullPath(packageRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            var normalizedCandidate = Path.GetFullPath(candidatePath);
            return normalizedCandidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
        }

        internal static Dictionary<string, FuseLegacyDefinitionOverrideRegistration>
            SelectLegacyDefinitionOverrides(
                IEnumerable<FuseLegacyDefinitionOverrideRegistration> candidates,
                out string[] issues)
        {
            var winners = new Dictionary<string, FuseLegacyDefinitionOverrideRegistration>(
                StringComparer.Ordinal);
            var issueList = new List<string>();

            foreach (var candidate in (candidates ??
                    Enumerable.Empty<FuseLegacyDefinitionOverrideRegistration>())
                .Where(item => item != null &&
                               !string.IsNullOrWhiteSpace(item.StoreIdentifier) &&
                               !string.IsNullOrWhiteSpace(item.DefinitionsPath))
                .OrderByDescending(item => item.Explicit)
                .ThenBy(item => item.PackagePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.DefinitionsPath, StringComparer.OrdinalIgnoreCase))
            {
                if (!winners.TryGetValue(candidate.StoreIdentifier, out var existing))
                {
                    winners[candidate.StoreIdentifier] = candidate;
                    continue;
                }

                if (string.Equals(
                        existing.DefinitionsPath,
                        candidate.DefinitionsPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                issueList.Add(
                    $"Multiple packages target asset store '{candidate.StoreIdentifier}'. " +
                    $"FUSE selected '{existing.DefinitionsPath}' from '{existing.PackageId}' and " +
                    $"ignored '{candidate.DefinitionsPath}' from '{candidate.PackageId}'.");
            }

            issues = issueList.ToArray();
            return winners;
        }

        private static void ValidateLegacyDefinitionOverrideTargets(PrefabStore prefabStore)
        {
            HashSet<string> storeIdentifiers;
            try
            {
                storeIdentifiers = new HashSet<string>(StringComparer.Ordinal);
                if (PrefabStoreStoresField?.GetValue(prefabStore) is System.Collections.IEnumerable stores)
                {
                    foreach (var item in stores)
                    {
                        if (item is AssetPackRuntimeStore store &&
                            !string.IsNullOrWhiteSpace(store.Identifier))
                        {
                            storeIdentifiers.Add(store.Identifier);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AddLegacyDefinitionOverrideIssue(
                    "FUSE could not validate asset definition override targets: " +
                    ex.GetBaseException().Message);
                return;
            }

            foreach (var registration in GetLegacyDefinitionOverrides())
            {
                if (!storeIdentifiers.Contains(registration.StoreIdentifier))
                {
                    AddLegacyDefinitionOverrideIssue(
                        $"Package '{registration.PackageId}' supplies definitions for asset store " +
                        $"'{registration.StoreIdentifier}', but no store with that exact identifier is installed.");
                }
            }
        }

        private static int InvalidateLegacyDefinitionOverrideTargetContainers(PrefabStore prefabStore)
        {
            var targets = new HashSet<string>(
                GetLegacyDefinitionOverrides().Select(item => item.StoreIdentifier),
                StringComparer.Ordinal);
            if (targets.Count == 0 || prefabStore == null || PrefabStoreStoresField == null)
            {
                return 0;
            }

            var invalidated = 0;
            try
            {
                if (PrefabStoreStoresField.GetValue(prefabStore) is System.Collections.IEnumerable stores)
                {
                    foreach (var item in stores)
                    {
                        if (item is AssetPackRuntimeStore store && targets.Contains(store.Identifier))
                        {
                            RuntimeStoreContainerField?.SetValue(store, null);
                            invalidated++;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AddLegacyDefinitionOverrideIssue(
                    "FUSE could not invalidate original containers before applying asset definition overrides: " +
                    ex.GetBaseException().Message);
            }

            return invalidated;
        }

        internal static bool TryLoadLegacyDefinitionOverrideContainer(
            AssetPackRuntimeStore store,
            out Container container)
        {
            container = null;
            if (store == null || string.IsNullOrWhiteSpace(store.Identifier))
            {
                return false;
            }

            FuseLegacyDefinitionOverrideRegistration registration;
            lock (LegacyDefinitionOverrideLock)
            {
                if (!LegacyDefinitionOverridesByStore.TryGetValue(
                        store.Identifier,
                        out registration))
                {
                    return false;
                }
            }

            try
            {
                var cached = RuntimeStoreContainerField?.GetValue(store) as Container;
                if (cached != null)
                {
                    container = cached;
                    return true;
                }

                var sourceText = File.ReadAllText(registration.DefinitionsPath);
                var droppedByKind = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                container = LoadResilientDirectContainer(
                    sourceText,
                    store.Identifier,
                    droppedByKind);
                if (container == null)
                {
                    throw new InvalidDataException("Definitions deserialized to null.");
                }

                SanitizeDeserializedDirectContainer(container);
                RuntimeStoreContainerField?.SetValue(store, container);
                RecordMissingComponentKinds(store.Identifier, droppedByKind.Keys);

                lock (LegacyDefinitionOverrideLock)
                {
                    if (LoggedLegacyDefinitionOverrideLoads.Add(store.Identifier))
                    {
                        var dropped = droppedByKind.Count == 0
                            ? string.Empty
                            : " Unbindable components dropped: " + string.Join(
                                ", ",
                                droppedByKind.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                                    .Select(item => item.Key + "=" + item.Value)) + ".";
                        FuseLog.Info(
                            $"FUSE applied AssetLoader-compatible definitions for store " +
                            $"'{store.Identifier}' from '{registration.DefinitionsPath}'.{dropped}");
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                var message =
                    $"Package '{registration.PackageId}' could not apply asset definitions " +
                    $"for store '{store.Identifier}' from '{registration.DefinitionsPath}': " +
                    $"{ex.GetBaseException().GetType().Name}: {ex.GetBaseException().Message}. " +
                    "FUSE kept the store's original definitions so other equipment and menus remain usable.";
                AddLegacyDefinitionOverrideIssue(message);

                lock (LegacyDefinitionOverrideLock)
                {
                    if (LoggedLegacyDefinitionOverrideFailures.Add(store.Identifier))
                    {
                        FuseLog.Warning(message);
                    }
                }

                container = null;
                return false;
            }
        }

        private static void AddLegacyDefinitionOverrideIssue(string issue)
        {
            if (string.IsNullOrWhiteSpace(issue))
            {
                return;
            }

            lock (LegacyDefinitionOverrideLock)
            {
                if (!LegacyDefinitionOverrideIssues.Contains(issue, StringComparer.Ordinal))
                {
                    LegacyDefinitionOverrideIssues = LegacyDefinitionOverrideIssues
                        .Concat(new[] { issue })
                        .ToArray();
                }
            }
        }

        internal static FuseLegacyDefinitionOverrideRegistration[] GetLegacyDefinitionOverrides()
        {
            lock (LegacyDefinitionOverrideLock)
            {
                return LegacyDefinitionOverridesByStore.Values
                    .OrderBy(item => item.StoreIdentifier, StringComparer.Ordinal)
                    .ToArray();
            }
        }

        internal static string[] GetLegacyDefinitionOverrideIssues()
        {
            lock (LegacyDefinitionOverrideLock)
            {
                return LegacyDefinitionOverrideIssues.ToArray();
            }
        }

        private static void ResetLegacyDefinitionOverrides()
        {
            lock (LegacyDefinitionOverrideLock)
            {
                LegacyDefinitionOverridesByStore =
                    new Dictionary<string, FuseLegacyDefinitionOverrideRegistration>(StringComparer.Ordinal);
                LegacyDefinitionOverrideIssues = Array.Empty<string>();
                LoggedLegacyDefinitionOverrideLoads.Clear();
                LoggedLegacyDefinitionOverrideFailures.Clear();
            }
        }
    }

    internal sealed class FuseLegacyDefinitionOverrideRegistration
    {
        public string StoreIdentifier { get; set; }
        public string DefinitionsPath { get; set; }
        public string PackageId { get; set; }
        public string PackagePath { get; set; }
        public bool Explicit { get; set; }
    }
}
