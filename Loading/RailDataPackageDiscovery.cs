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

        public static int LoadAllAvailablePackages()
        {
            if (_discoveryComplete)
            {
                return 0;
            }

            var modsRoot = GetModsRoot();
            if (string.IsNullOrWhiteSpace(modsRoot) || !Directory.Exists(modsRoot))
            {
                RailLog.Warning("RAIL could not locate the Unity Mod Manager Mods folder.");
                return 0;
            }

            var packagePaths = DiscoverPackageFolders(modsRoot).ToArray();
            RailLog.Info($"RAIL scanning '{modsRoot}' found {packagePaths.Length} candidate data package(s).");

            var loadedCount = 0;
            foreach (var packagePath in packagePaths)
            {
                try
                {
                    RailModLoader.LoadMod(packagePath);
                    RailLog.Info($"RAIL loaded data package '{Path.GetFileName(packagePath)}'.");
                    loadedCount++;
                }
                catch (Exception ex)
                {
                    RailLog.Exception($"Failed to load RAIL data package '{packagePath}'", ex);
                }
            }

            _discoveryComplete = true;

            if (loadedCount > 0)
            {
                RailLog.Info($"Loaded {loadedCount} RAIL data package(s).");
            }

            return loadedCount;
        }

        public static void ResetDiscovery()
        {
            _discoveryComplete = false;
        }

        public static IEnumerable<string> DiscoverPackageFolders(string modsRoot)
        {
            if (string.IsNullOrWhiteSpace(modsRoot) || !Directory.Exists(modsRoot))
            {
                return Enumerable.Empty<string>();
            }

            return Directory.GetDirectories(modsRoot)
                .Where(IsRailDataPackage)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static bool IsRailDataPackage(string folderPath)
        {
            var infoPath = Path.Combine(folderPath, "Info.json");
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

            var id = (string)info["Id"];
            if (string.Equals(id, "RAIL", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (HasRailDataFile(info["RailDataFile"]) || HasRailDataFile(info["RailDataFiles"]))
            {
                return true;
            }

            return HasRootDefinitionFile(folderPath) && (ContainsRailReference(info["Requirements"]) || ContainsRailReference(info["LoadAfter"]));
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
    }
}
