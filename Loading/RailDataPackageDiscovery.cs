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
        private static bool _packagesLoadedFromDisk;
        private static string[] _discoveredPackageFolders = Array.Empty<string>();

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

            RailAssetPackRegistry.MountAllAvailableAssetPacks();

            _discoveredPackageFolders = DiscoverPackageFolders(modsRoot).ToArray();
            _discoveryComplete = true;
            RailLog.Info($"RAIL discovered {_discoveredPackageFolders.Length} candidate data package(s) in '{modsRoot}'.");
            return _discoveredPackageFolders;
        }

        public static int LoadPackagesFromDisk(bool forceReload)
        {
            if (_packagesLoadedFromDisk && !forceReload)
            {
                RailLog.Info($"RAIL package disk load skipped because {RailModLoader.LoadedDefinitionCount} definition(s) are already loaded.");
                return 0;
            }

            if (forceReload)
            {
                RailLog.Info("RAIL forced package disk reload requested.");
                RailModLoader.UnloadAll(resetDiscovery: false);
                _packagesLoadedFromDisk = false;
            }

            var packagePaths = DiscoverPackagesOnce();
            if (packagePaths.Count == 0)
            {
                _packagesLoadedFromDisk = true;
                return 0;
            }

            var loadedCount = 0;
            foreach (var packagePath in packagePaths)
            {
                try
                {
                    RailModLoader.LoadMod(packagePath);
                    RailLog.Info($"RAIL loaded-from-disk data package '{Path.GetFileName(packagePath)}'.");
                    loadedCount++;
                }
                catch (Exception ex)
                {
                    RailLog.Exception($"Failed to load RAIL data package '{packagePath}'", ex);
                }
            }

            _packagesLoadedFromDisk = true;
            RailLog.Info($"RAIL loaded {loadedCount} data package folder(s) from disk; {RailModLoader.LoadedDefinitionCount} definition(s) are resident.");
            return loadedCount;
        }

        public static int ReapplyLoadedPackages(string reason)
        {
            var reappliedCount = RailModLoader.ReapplyLoadedDefinitions(reason);
            RailLog.Info($"RAIL reapplied {reappliedCount} loaded definition(s) to runtime for '{reason ?? "unspecified"}'.");
            return reappliedCount;
        }

        public static void ReloadPackagesFromDisk()
        {
            LoadPackagesFromDisk(true);
        }

        public static void ResetDiscovery()
        {
            _discoveryComplete = false;
            _packagesLoadedFromDisk = false;
            _discoveredPackageFolders = Array.Empty<string>();
        }

        public static IEnumerable<string> DiscoverPackageFolders(string modsRoot)
        {
            if (string.IsNullOrWhiteSpace(modsRoot) || !Directory.Exists(modsRoot))
            {
                return Enumerable.Empty<string>();
            }

            var manifests = new List<RailPackageManifest>();
            foreach (var packagePath in Directory.GetDirectories(modsRoot).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                if (TryReadRailPackageManifest(packagePath, out var manifest))
                {
                    manifests.Add(manifest);
                }
            }

            return SortPackages(manifests)
                .Select(manifest => manifest.FolderPath)
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
            catch
            {
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
                LoadBefore = ReadDependencyIds(info["RailLoadBefore"])
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

                RailLog.Warning($"RAIL package id '{package.Id}' appears more than once; dependency ordering will target the first matching package.");
            }

            var outgoing = fallbackOrder.ToDictionary(package => package, _ => new HashSet<RailPackageManifest>());
            var incomingCount = fallbackOrder.ToDictionary(package => package, _ => 0);

            foreach (var package in fallbackOrder)
            {
                foreach (var dependencyId in package.LoadAfter)
                {
                    if (byId.TryGetValue(dependencyId, out var dependency))
                    {
                        AddPackageOrderEdge(dependency, package, outgoing, incomingCount);
                    }
                    else
                    {
                        RailLog.Warning($"RAIL package '{package.Id}' declares RailLoadAfter '{dependencyId}', but no matching RAIL data package was discovered.");
                    }
                }

                foreach (var dependencyId in package.LoadBefore)
                {
                    if (byId.TryGetValue(dependencyId, out var dependency))
                    {
                        AddPackageOrderEdge(package, dependency, outgoing, incomingCount);
                    }
                    else
                    {
                        RailLog.Warning($"RAIL package '{package.Id}' declares RailLoadBefore '{dependencyId}', but no matching RAIL data package was discovered.");
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
                RailLog.Warning($"RAIL package load-order cycle detected among: {string.Join(", ", cycle.Select(package => package.Id).ToArray())}. Appending those packages by priority/name fallback order.");
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
            if (Directory.GetFiles(folderPath, "*.bson", SearchOption.TopDirectoryOnly).Length > 0)
            {
                return true;
            }

            return Directory.GetFiles(folderPath, "*.json", SearchOption.TopDirectoryOnly)
                .Any(path => !string.Equals(Path.GetFileName(path), "Info.json", StringComparison.OrdinalIgnoreCase));
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
            catch
            {
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
        }
    }
}
