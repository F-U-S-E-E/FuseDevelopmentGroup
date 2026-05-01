using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RAIL.Infrastructure;
using Newtonsoft.Json.Linq;

namespace RAIL.Loading
{
    public static class RailDataPackageDiscovery
    {
        private static bool _discoveryComplete;
        private static bool _definitionsLoadedFromDisk;
        private static string[] _discoveredPackageFolders = Array.Empty<string>();
        private static RailPackageManifest[] _discoveredPackageManifests = Array.Empty<RailPackageManifest>();

        public static int LoadAllAvailablePackages()
        {
            return LoadPackagesFromDisk(false);
        }

        public static IReadOnlyList<string> DiscoverPackagesOnce()
        {
            if (_discoveryComplete)
            {
                RailLog.Info($"RAIL package discovery already completed with {_discoveredPackageFolders.Length} candidate data package(s).");
                return _discoveredPackageFolders;
            }

            var modsRoot = GetModsRoot();
            if (string.IsNullOrWhiteSpace(modsRoot) || !Directory.Exists(modsRoot))
            {
                RailLog.Warning("RAIL could not locate the Unity Mod Manager Mods folder.");
                _discoveredPackageFolders = Array.Empty<string>();
                _discoveryComplete = true;
                return _discoveredPackageFolders;
            }

            try
            {
                _discoveredPackageManifests = DiscoverPackageManifests(modsRoot).ToArray();
                _discoveredPackageFolders = _discoveredPackageManifests.Select(manifest => manifest.FolderPath).ToArray();
            }
            catch (Exception ex)
            {
                RailLog.Exception($"RAIL failed while discovering data packages in '{modsRoot}'", ex);
                _discoveredPackageFolders = Array.Empty<string>();
                _discoveredPackageManifests = Array.Empty<RailPackageManifest>();
            }

            _discoveryComplete = true;
            RailLog.Info($"RAIL discovered {_discoveredPackageFolders.Length} candidate data package(s) in '{modsRoot}'.");
            for (var index = 0; index < _discoveredPackageFolders.Length; index++)
            {
                var manifest = index < _discoveredPackageManifests.Length ? _discoveredPackageManifests[index] : null;
                var status = manifest == null
                    ? "unknown"
                    : manifest.Disabled
                        ? "disabled"
                        : manifest.HasBlockingFaults
                            ? "faulted"
                            : "ready";
                RailLog.Info($"RAIL discovered package[{index}] '{Path.GetFileName(_discoveredPackageFolders[index])}' id='{manifest?.Id ?? string.Empty}' status='{status}' priority={manifest?.Priority ?? 0} path='{_discoveredPackageFolders[index]}'.");
            }

            return _discoveredPackageFolders;
        }

        public static IReadOnlyList<string> RefreshDiscoveredPackages()
        {
            ResetDiscovery();
            return DiscoverPackagesOnce();
        }

        public static int LoadPackagesFromDisk(bool forceReload)
        {
            if (_definitionsLoadedFromDisk && !forceReload)
            {
                RailLog.Info($"RAIL package disk load skipped because {RailModLoader.LoadedDefinitionCount} definition(s) are already loaded.");
                RailPackageFaultRegistry.LogFinalReport("disk load skipped", RailModLoader.LoadedDefinitionCount);
                return 0;
            }

            if (forceReload)
            {
                RailLog.Info("RAIL forced package disk reload requested; using existing discovery cache.");
                RailModLoader.UnloadAll(resetDiscovery: false);
                RailPackageFaultRegistry.Reset();
                _definitionsLoadedFromDisk = false;
            }

            var packagePaths = DiscoverPackagesOnce();
            if (packagePaths.Count == 0)
            {
                _definitionsLoadedFromDisk = true;
                RailLog.Info("RAIL loaded 0 package definition(s) from disk because discovery found no packages.");
                RailPackageFaultRegistry.LogFinalReport("disk load", RailModLoader.LoadedDefinitionCount);
                return 0;
            }

            try
            {
                RailAssetPackRegistry.MountAllAvailableAssetPacks();
            }
            catch (Exception ex)
            {
                RailLog.Exception("RAIL asset pack mount failed before package disk load; continuing with definition loading.", ex);
            }

            var loadedCount = 0;
            var manifests = _discoveredPackageManifests.Length > 0
                ? _discoveredPackageManifests
                : packagePaths.Select(path => new RailPackageManifest
                {
                    Id = Path.GetFileName(path),
                    FolderPath = path
                }).ToArray();

            foreach (var manifest in manifests)
            {
                var packagePath = manifest.FolderPath;
                if (manifest.Disabled)
                {
                    RailPackageFaultRegistry.MarkDisabled(manifest.Id, manifest.DisabledReason);
                    RailPackageFaultRegistry.MarkSkipped(manifest.Id, manifest.DisabledReason);
                    RailLog.Warning($"RAIL skipped disabled data package '{manifest.Id}' path='{packagePath}' reason='{manifest.DisabledReason}'.");
                    continue;
                }

                if (manifest.HasBlockingFaults)
                {
                    foreach (var fault in manifest.Faults)
                    {
                        RailPackageFaultRegistry.RecordFault(manifest.Id, "dependency/load-order", fault);
                    }

                    RailPackageFaultRegistry.MarkSkipped(manifest.Id, "dependency/load-order fault");
                    RailLog.Warning($"RAIL skipped faulted data package '{manifest.Id}' path='{packagePath}' faultCount={manifest.Faults.Count}.");
                    continue;
                }

                try
                {
                    RailModLoader.LoadMod(packagePath);
                    RailPackageFaultRegistry.MarkLoadedFromDisk(manifest.Id);
                    RailLog.Info($"RAIL loaded package '{Path.GetFileName(packagePath)}' id='{manifest.Id}' from disk into resident definitions. Runtime apply has not run in this step.");
                    loadedCount++;
                }
                catch (Exception ex)
                {
                    RailPackageFaultRegistry.RecordFault(manifest.Id, "deserialization", ex.Message, ex);
                    RailPackageFaultRegistry.MarkSkipped(manifest.Id, "deserialization failed");
                    RailLog.Exception($"Failed to deserialize RAIL data package '{packagePath}' from disk", ex);
                }
            }

            _definitionsLoadedFromDisk = true;
            RailLog.Info($"RAIL loaded {loadedCount} data package folder(s) from disk; {RailModLoader.LoadedDefinitionCount} resident definition(s). Runtime apply is separate.");
            RailPackageFaultRegistry.LogFinalReport("disk load", RailModLoader.LoadedDefinitionCount);
            return loadedCount;
        }

        public static int ApplyLoadedPackages(string reason)
        {
            var appliedCount = RailModLoader.ApplyLoadedDefinitions(reason);
            RailLog.Info($"RAIL applied {appliedCount} resident definition(s) to runtime for '{reason ?? "unspecified"}'.");
            RailPackageFaultRegistry.LogFinalReport(reason ?? "runtime apply", RailModLoader.LoadedDefinitionCount);
            return appliedCount;
        }

        public static int ReapplyLoadedPackages(string reason)
        {
            return ApplyLoadedPackages(reason);
        }

        public static int LoadAndApplyAvailablePackages(string reason, bool forceReload = false)
        {
            var loadedCount = LoadPackagesFromDisk(forceReload);
            var appliedCount = ApplyLoadedPackages(reason);
            RailLog.Info($"RAIL load/apply complete for '{reason ?? "unspecified"}': loadedFromDisk={loadedCount}, appliedToRuntime={appliedCount}.");
            return appliedCount;
        }

        public static void ReloadPackagesFromDisk()
        {
            LoadPackagesFromDisk(true);
        }

        public static void ResetDiscovery()
        {
            if (!_discoveryComplete && !_definitionsLoadedFromDisk && _discoveredPackageFolders.Length == 0)
            {
                return;
            }

            _discoveryComplete = false;
            _definitionsLoadedFromDisk = false;
            _discoveredPackageFolders = Array.Empty<string>();
            _discoveredPackageManifests = Array.Empty<RailPackageManifest>();
            RailPackageFaultRegistry.Reset();
            RailLog.Info("RAIL package discovery state reset.");
        }

        public static IEnumerable<string> DiscoverPackageFolders(string modsRoot)
        {
            return DiscoverPackageManifests(modsRoot)
                .Select(manifest => manifest.FolderPath)
                .ToArray();
        }

        private static IEnumerable<RailPackageManifest> DiscoverPackageManifests(string modsRoot)
        {
            if (string.IsNullOrWhiteSpace(modsRoot) || !Directory.Exists(modsRoot))
            {
                return Enumerable.Empty<RailPackageManifest>();
            }

            var manifests = new List<RailPackageManifest>();
            string[] packagePaths;
            try
            {
                packagePaths = Directory.GetDirectories(modsRoot)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch (Exception ex)
            {
                RailLog.Exception($"RAIL could not enumerate Mods folder '{modsRoot}' for data packages", ex);
                return Enumerable.Empty<RailPackageManifest>();
            }

            foreach (var packagePath in packagePaths)
            {
                if (TryReadRailPackageManifest(packagePath, out var manifest))
                {
                    manifests.Add(manifest);
                }
            }

            return SortPackages(manifests)
                .ToArray();
        }

        private static bool IsRailDataPackage(string folderPath)
        {
            return TryReadRailPackageManifest(folderPath, out _);
        }

        private static bool TryReadRailPackageManifest(string folderPath, out RailPackageManifest manifest)
        {
            var infoPath = Path.Combine(folderPath, "Info.json");
            manifest = null;
            if (!File.Exists(infoPath))
            {
                return false;
            }

            JObject info;
            try
            {
                info = JObject.Parse(File.ReadAllText(infoPath));
            }
            catch (Exception ex)
            {
                RailLog.Warning($"RAIL ignored package '{folderPath}' because Info.json could not be parsed: {ex.Message}");
                return false;
            }

            var id = ((string)info["Id"] ?? Path.GetFileName(folderPath)).Trim();
            if (string.Equals(id, "RAIL", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var isDataPackage = HasRailDataFile(info["RailDataFile"]) ||
                HasRailDataFile(info["RailDataFiles"]) ||
                (HasRootDefinitionFile(folderPath) && (ContainsRailReference(info["Requirements"]) || ContainsRailReference(info["LoadAfter"])));
            if (!isDataPackage)
            {
                return false;
            }

            manifest = new RailPackageManifest
            {
                Id = string.IsNullOrWhiteSpace(id) ? Path.GetFileName(folderPath) : id,
                FolderPath = folderPath,
                Priority = ReadPriority(info["RailLoadPriority"]),
                LoadAfter = ReadDependencyIds(info["RailLoadAfter"]),
                LoadBefore = ReadDependencyIds(info["RailLoadBefore"]),
                Disabled = ReadDisabled(info),
                DisabledReason = ReadDisabledReason(info)
            };
            return true;
        }

        private static IReadOnlyList<RailPackageManifest> SortPackages(IReadOnlyList<RailPackageManifest> packages)
        {
            var fallbackOrder = packages
                .OrderBy(package => package.Priority)
                .ThenBy(package => package.Id, StringComparer.OrdinalIgnoreCase)
                .ThenBy(package => package.FolderPath, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (fallbackOrder.Length <= 1)
            {
                return fallbackOrder;
            }

            var byId = new Dictionary<string, RailPackageManifest>(StringComparer.OrdinalIgnoreCase);
            foreach (var package in fallbackOrder)
            {
                if (!byId.ContainsKey(package.Id))
                {
                    byId.Add(package.Id, package);
                    continue;
                }

                var message = $"Package id '{package.Id}' appears more than once; dependency ordering will target the first matching package.";
                package.Faults.Add(message);
                RailLog.Warning($"RAIL {message}");
            }

            var outgoing = fallbackOrder.ToDictionary(package => package, _ => new HashSet<RailPackageManifest>());
            var incomingCount = fallbackOrder.ToDictionary(package => package, _ => 0);

            foreach (var package in fallbackOrder)
            {
                foreach (var dependencyId in package.LoadAfter)
                {
                    if (byId.TryGetValue(dependencyId, out var dependency))
                    {
                        if (dependency.Disabled)
                        {
                            var message = $"Package '{package.Id}' declares RailLoadAfter '{dependencyId}', but that package is disabled.";
                            package.Faults.Add(message);
                            RailLog.Warning($"RAIL {message}");
                            continue;
                        }

                        AddPackageOrderEdge(dependency, package, outgoing, incomingCount);
                    }
                    else
                    {
                        var message = $"Package '{package.Id}' declares RailLoadAfter '{dependencyId}', but no matching RAIL data package was discovered.";
                        package.Faults.Add(message);
                        RailLog.Warning($"RAIL {message}");
                    }
                }

                foreach (var dependencyId in package.LoadBefore)
                {
                    if (byId.TryGetValue(dependencyId, out var dependency))
                    {
                        if (dependency.Disabled)
                        {
                            var message = $"Package '{package.Id}' declares RailLoadBefore '{dependencyId}', but that package is disabled.";
                            package.Faults.Add(message);
                            RailLog.Warning($"RAIL {message}");
                            continue;
                        }

                        AddPackageOrderEdge(package, dependency, outgoing, incomingCount);
                    }
                    else
                    {
                        var message = $"Package '{package.Id}' declares RailLoadBefore '{dependencyId}', but no matching RAIL data package was discovered.";
                        package.Faults.Add(message);
                        RailLog.Warning($"RAIL {message}");
                    }
                }
            }

            var result = new List<RailPackageManifest>(fallbackOrder.Length);
            var ready = fallbackOrder.Where(package => incomingCount[package] == 0).ToList();
            SortReadyPackages(ready);
            while (ready.Count > 0)
            {
                var package = ready[0];
                ready.RemoveAt(0);
                result.Add(package);

                foreach (var after in outgoing[package].OrderBy(candidate => candidate.Priority).ThenBy(candidate => candidate.Id, StringComparer.OrdinalIgnoreCase))
                {
                    incomingCount[after]--;
                    if (incomingCount[after] == 0)
                    {
                        ready.Add(after);
                    }
                }

                SortReadyPackages(ready);
            }

            if (result.Count != fallbackOrder.Length)
            {
                var cycle = fallbackOrder.Where(package => !result.Contains(package)).ToArray();
                var cycleIds = string.Join(", ", cycle.Select(package => package.Id).ToArray());
                RailLog.Warning($"RAIL package load-order cycle detected among: {cycleIds}. Appending those packages by priority/name fallback order as faulted packages.");
                foreach (var package in cycle)
                {
                    package.Faults.Add($"Load-order cycle detected among: {cycleIds}.");
                }

                result.AddRange(cycle);
            }

            RailLog.Info("RAIL package load order: " + string.Join(", ", result.Select(package => $"{package.Id}(priority={package.Priority})").ToArray()));
            return result;
        }

        private static void AddPackageOrderEdge(
            RailPackageManifest before,
            RailPackageManifest after,
            IDictionary<RailPackageManifest, HashSet<RailPackageManifest>> outgoing,
            IDictionary<RailPackageManifest, int> incomingCount)
        {
            if (before == null || after == null || ReferenceEquals(before, after))
            {
                return;
            }

            if (outgoing[before].Add(after))
            {
                incomingCount[after]++;
            }
        }

        private static void SortReadyPackages(List<RailPackageManifest> ready)
        {
            ready.Sort((left, right) =>
            {
                var priorityCompare = left.Priority.CompareTo(right.Priority);
                if (priorityCompare != 0)
                {
                    return priorityCompare;
                }

                var idCompare = string.Compare(left.Id, right.Id, StringComparison.OrdinalIgnoreCase);
                return idCompare != 0
                    ? idCompare
                    : string.Compare(left.FolderPath, right.FolderPath, StringComparison.OrdinalIgnoreCase);
            });
        }

        private static int ReadPriority(JToken token)
        {
            if (token == null)
            {
                return 0;
            }

            if (token.Type == JTokenType.Integer)
            {
                return (int)token;
            }

            return int.TryParse(token.ToString(), out var priority) ? priority : 0;
        }

        private static bool ReadDisabled(JObject info)
        {
            if (info == null)
            {
                return false;
            }

            if (ReadBooleanFlag(info["RailDisabled"], false) ||
                ReadBooleanFlag(info["Disabled"], false) ||
                ReadBooleanFlag(info["disabled"], false))
            {
                return true;
            }

            return !ReadBooleanFlag(info["Enabled"], true) ||
                   !ReadBooleanFlag(info["enabled"], true);
        }

        private static string ReadDisabledReason(JObject info)
        {
            if (info == null)
            {
                return "disabled by package manifest";
            }

            var reason =
                (string)info["RailDisabledReason"] ??
                (string)info["DisabledReason"] ??
                (string)info["disabledReason"];
            return string.IsNullOrWhiteSpace(reason) ? "disabled by package manifest" : reason.Trim();
        }

        private static bool ReadBooleanFlag(JToken token, bool defaultValue)
        {
            if (token == null)
            {
                return defaultValue;
            }

            if (token.Type == JTokenType.Boolean)
            {
                return (bool)token;
            }

            var value = token.ToString();
            if (bool.TryParse(value, out var boolean))
            {
                return boolean;
            }

            if (int.TryParse(value, out var integer))
            {
                return integer != 0;
            }

            return defaultValue;
        }

        private static string[] ReadDependencyIds(JToken token)
        {
            if (token == null)
            {
                return Array.Empty<string>();
            }

            var result = new List<string>();
            AddDependencyId(token, result);
            return result
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static void AddDependencyId(JToken token, ICollection<string> result)
        {
            if (token == null)
            {
                return;
            }

            if (token.Type == JTokenType.Array)
            {
                foreach (var child in token.Children())
                {
                    AddDependencyId(child, result);
                }

                return;
            }

            if (token.Type == JTokenType.Object)
            {
                var objectId = (string)token["Id"] ?? (string)token["id"];
                if (!string.IsNullOrWhiteSpace(objectId))
                {
                    result.Add(objectId.Trim());
                }

                return;
            }

            var value = (string)token;
            if (!string.IsNullOrWhiteSpace(value))
            {
                result.Add(value.Trim());
            }
        }

        private static bool HasRailDataFile(JToken token)
        {
            if (token == null)
            {
                return false;
            }

            if (token.Type == JTokenType.String)
            {
                return !string.IsNullOrWhiteSpace((string)token);
            }

            if (token.Type != JTokenType.Array)
            {
                return false;
            }

            foreach (var item in token.Children())
            {
                if (item.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string)item))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasRootDefinitionFile(string folderPath)
        {
            try
            {
                if (Directory.GetFiles(folderPath, "*.bson", SearchOption.TopDirectoryOnly).Length > 0)
                {
                    return true;
                }

                return Directory.GetFiles(folderPath, "*.json", SearchOption.TopDirectoryOnly)
                    .Any(path => !string.Equals(Path.GetFileName(path), "Info.json", StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                RailLog.Warning($"RAIL could not inspect package root definition files in '{folderPath}': {ex.Message}");
                return false;
            }
        }

        private static bool ContainsRailReference(JToken token)
        {
            if (token == null)
            {
                return false;
            }

            if (token.Type == JTokenType.String)
            {
                return string.Equals((string)token, "RAIL", StringComparison.OrdinalIgnoreCase);
            }

            if (token.Type != JTokenType.Array)
            {
                return false;
            }

            foreach (var item in token.Children())
            {
                if (item.Type == JTokenType.String && string.Equals((string)item, "RAIL", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (item.Type == JTokenType.Object)
                {
                    var itemId = (string)item["Id"] ?? (string)item["id"];
                    if (string.Equals(itemId, "RAIL", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        internal static string GetModsRoot()
        {
            var candidates = new[]
            {
                RailPlugin.ModEntry?.Path,
                AppDomain.CurrentDomain.BaseDirectory
            };

            for (var index = 0; index < candidates.Length; index++)
            {
                var resolved = TryResolveModsRootFromPath(candidates[index]);
                if (!string.IsNullOrWhiteSpace(resolved))
                {
                    return resolved;
                }
            }

            var directFallback = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Mods");
            return LooksLikeModsRoot(directFallback) ? directFallback : null;
        }

        private static string TryResolveModsRootFromPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            var directory = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return null;
            }

            for (var current = new DirectoryInfo(directory); current != null; current = current.Parent)
            {
                if (LooksLikeModsRoot(current.FullName))
                {
                    return current.FullName;
                }

                var childMods = Path.Combine(current.FullName, "Mods");
                if (LooksLikeModsRoot(childMods))
                {
                    return childMods;
                }
            }

            return null;
        }

        private static bool LooksLikeModsRoot(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                return false;
            }

            if (!string.Equals(Path.GetFileName(path), "Mods", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            try
            {
                return Directory.GetDirectories(path)
                    .Any(child => File.Exists(Path.Combine(child, "Info.json")));
            }
            catch (Exception ex)
            {
                RailLog.Warning($"RAIL could not inspect potential Mods root '{path}': {ex.Message}");
                return false;
            }
        }

        private sealed class RailPackageManifest
        {
            public string Id { get; set; }
            public string FolderPath { get; set; }
            public int Priority { get; set; }
            public string[] LoadAfter { get; set; } = Array.Empty<string>();
            public string[] LoadBefore { get; set; } = Array.Empty<string>();
            public bool Disabled { get; set; }
            public string DisabledReason { get; set; } = string.Empty;
            public List<string> Faults { get; } = new List<string>();
            public bool HasBlockingFaults => Faults.Count > 0;
        }
    }
}
