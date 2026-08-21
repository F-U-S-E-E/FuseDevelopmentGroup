using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FUSE.Infrastructure;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FUSE.Loading
{
    /// <summary>
    /// Builds the dependency view for every installed package, including
    /// equipment and asset-only packages that are not FUSE data packages.
    /// Network access deliberately lives in the installer; the in-game UI
    /// consumes only manifests on disk and the installer's offline cache.
    /// </summary>
    internal static class FuseInstalledDependencyCatalog
    {
        internal const string MetadataDirectoryName = ".fuse-metadata";
        internal const string MetadataFileName = "dependencies.json";

        private static readonly Regex UmmVersionedRequirement = new Regex(
            @"^(?<id>.+)-(?<version>\d+\.\d+\.\d+(?:\.\d+)?)(?:[^\d].*)?$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex NexusPageIdentity = new Regex(
            @"https?://(?:www\.)?nexusmods\.com/(?:games/)?(?<game>[^/]+)/mods/(?<mod>\d+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        internal static IReadOnlyList<FuseInstalledPackageDependencySnapshot> DiscoverInstalledPackages()
        {
            var packages = DiscoverInstalledPackages(
                FuseDataPackageDiscovery.GetModsRoot(),
                FuseDataPackageDiscovery.GetPackageManifestSnapshots());
            foreach (var package in packages)
            {
                if (package == null || string.IsNullOrWhiteSpace(package.FolderPath))
                {
                    continue;
                }

                package.Disabled = package.Disabled ||
                    FuseUmmState.TryGetDisabledReason(package.FolderPath, package.Id, out _) ||
                    !FuseModSetService.IsPackageEnabledByActiveSet(package.Id, package.FolderPath);
            }

            return packages;
        }

        internal static IReadOnlyList<FuseInstalledPackageDependencySnapshot> DiscoverInstalledPackages(
            string modsRoot,
            IReadOnlyList<FusePackageManifestSnapshot> fusePackages)
        {
            var packages = new List<FuseInstalledPackageDependencySnapshot>();
            if (!string.IsNullOrWhiteSpace(modsRoot) && Directory.Exists(modsRoot))
            {
                string[] folders;
                try
                {
                    folders = Directory.GetDirectories(modsRoot);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    FuseLog.Exception($"FUSE could not inspect installed packages for the dependency graph in '{modsRoot}'", ex);
                    folders = Array.Empty<string>();
                }

                foreach (var folder in folders)
                {
                    if (string.Equals(Path.GetFileName(folder), MetadataDirectoryName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var package = ReadInstalledFolder(folder);
                    if (package != null)
                    {
                        packages.Add(package);
                    }
                }

                MergeOfflineMetadata(Path.Combine(modsRoot, MetadataDirectoryName, MetadataFileName), packages);
            }

            MergeFuseSnapshots(fusePackages, packages);
            return packages
                .Where(package => package != null && !string.IsNullOrWhiteSpace(package.Id))
                .OrderBy(package => package.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(package => package.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static FuseInstalledPackageDependencySnapshot ReadInstalledFolder(string folder)
        {
            var folderName = Path.GetFileName(folder) ?? string.Empty;
            var package = new FuseInstalledPackageDependencySnapshot
            {
                Id = folderName,
                DisplayName = folderName,
                FolderName = folderName,
                FolderPath = folder,
                Category = "Mod",
                ManifestSource = "folder"
            };
            var foundManifest = false;

            var infoPath = Path.Combine(folder, "Info.json");
            if (File.Exists(infoPath))
            {
                foundManifest = true;
                TryMergeManifest(infoPath, package, isUmmInfo: true);
            }

            var definitionPath = Path.Combine(folder, "Definition.json");
            if (File.Exists(definitionPath))
            {
                foundManifest = true;
                TryMergeManifest(definitionPath, package, isUmmInfo: false);
            }

            if (!foundManifest)
            {
                return null;
            }

            ClassifyPackage(package);
            return package;
        }

        private static void TryMergeManifest(
            string path,
            FuseInstalledPackageDependencySnapshot package,
            bool isUmmInfo)
        {
            JObject manifest;
            try
            {
                manifest = FuseLegacyDataConverter.ReadLegacyObject(path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
            {
                package.AddFault($"{Path.GetFileName(path)} could not be read: {ex.GetBaseException().Message}");
                return;
            }

            var id = ReadString(manifest, "Id", "id");
            var name = ReadString(manifest, "DisplayName", "displayName", "Name", "name");
            var version = ReadString(manifest, "Version", "version");
            var homepage = ReadString(manifest, "Homepage", "homepage", "Website", "website", "url");
            if (!string.IsNullOrWhiteSpace(id) &&
                (isUmmInfo || string.Equals(package.Id, package.FolderName, StringComparison.OrdinalIgnoreCase)))
            {
                package.Id = id;
            }

            if (!string.IsNullOrWhiteSpace(name) &&
                (isUmmInfo || string.Equals(package.DisplayName, package.FolderName, StringComparison.OrdinalIgnoreCase)))
            {
                package.DisplayName = name;
            }

            if (!string.IsNullOrWhiteSpace(version) &&
                (isUmmInfo || string.IsNullOrWhiteSpace(package.Version)))
            {
                package.Version = version;
            }

            package.Disabled = package.Disabled ||
                ReadBoolean(manifest, "FuseDisabled", false) ||
                ReadBoolean(manifest, "Disabled", false) ||
                ReadBoolean(manifest, "disabled", false) ||
                !ReadBoolean(manifest, "Enabled", true) ||
                !ReadBoolean(manifest, "enabled", true);
            ApplyNexusIdentity(package, homepage);

            var source = isUmmInfo ? "UMM Info.json" : "legacy Definition.json";
            if (isUmmInfo)
            {
                package.ManifestSource = "UMM Info.json";
                package.IsCodePlugin = !string.IsNullOrWhiteSpace(ReadString(manifest, "EntryMethod", "entryMethod")) ||
                                       !string.IsNullOrWhiteSpace(ReadString(manifest, "AssemblyName", "assemblyName"));
                AddEdges(package.Requirements, manifest["Requirements"] ?? manifest["requirements"], source, parseUmmVersion: true);
                AddEdges(package.Requirements, manifest["FuseRequires"], "FUSE Info.json", parseUmmVersion: false);
                AddEdges(package.LoadAfter, manifest["LoadAfter"], source, parseUmmVersion: false);
                AddEdges(package.LoadAfter, manifest["FuseLoadAfter"], "FUSE Info.json", parseUmmVersion: false);
                AddEdges(package.LoadBefore, manifest["FuseLoadBefore"], "FUSE Info.json", parseUmmVersion: false);
            }
            else
            {
                if (string.Equals(package.ManifestSource, "folder", StringComparison.OrdinalIgnoreCase))
                {
                    package.ManifestSource = source;
                }

                package.IsLegacy = true;
                AddEdges(package.Requirements, manifest["requires"] ?? manifest["Requires"], source, parseUmmVersion: false);
                AddEdges(package.LoadAfter, manifest["loadAfter"] ?? manifest["LoadAfter"], source, parseUmmVersion: false);
                AddEdges(package.LoadBefore, manifest["loadBefore"] ?? manifest["LoadBefore"], source, parseUmmVersion: false);
            }
        }

        private static void ClassifyPackage(FuseInstalledPackageDependencySnapshot package)
        {
            if (package.IsLegacy)
            {
                package.Category = "Legacy package";
            }
            else if (package.IsCodePlugin)
            {
                package.Category = "Code plugin";
            }

            if (!package.HasEdges)
            {
                return;
            }

            string[] definitionFiles;
            try
            {
                definitionFiles = Directory.GetFiles(package.FolderPath, "Definitions.json", SearchOption.AllDirectories);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                package.AddFault($"AssetLoader metadata could not be inspected: {ex.GetBaseException().Message}");
                return;
            }

            if (definitionFiles.Length == 0)
            {
                return;
            }

            package.Category = "Asset package";
            package.HasAssetLoaderMetadata = true;
            foreach (var path in definitionFiles)
            {
                try
                {
                    var text = File.ReadAllText(path);
                    if (ContainsEquipmentKind(text))
                    {
                        package.Category = "Equipment";
                        return;
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    package.AddFault($"AssetLoader metadata '{path}' could not be read: {ex.GetBaseException().Message}");
                }
            }
        }

        private static bool ContainsEquipmentKind(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            return Regex.IsMatch(
                text,
                "\\\"kind\\\"\\s*:\\s*\\\"(?:Car|DieselLocomotive|SteamLocomotive|Locomotive|Tender)\\\"",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static void AddEdges(
            ICollection<FuseInstalledDependencyEdge> destination,
            JToken token,
            string source,
            bool parseUmmVersion)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return;
            }

            IEnumerable<JToken> entries = token.Type == JTokenType.Array
                ? token.Children()
                : new[] { token };
            foreach (var entry in entries)
            {
                var edge = ReadEdge(entry, source, parseUmmVersion);
                AddOrReplaceEdge(destination, edge);
            }
        }

        private static FuseInstalledDependencyEdge ReadEdge(JToken token, string source, bool parseUmmVersion)
        {
            if (token == null)
            {
                return null;
            }

            string id;
            string notBefore = string.Empty;
            string notAfter = string.Empty;
            var displayName = string.Empty;
            var nexusModId = string.Empty;
            if (token.Type == JTokenType.Object)
            {
                id = ReadString((JObject)token, "Id", "id", "packageId");
                notBefore = ReadString((JObject)token, "NotBefore", "notBefore", "MinimumVersion", "minimumVersion");
                notAfter = ReadString((JObject)token, "NotAfter", "notAfter", "MaximumVersion", "maximumVersion");
                displayName = ReadString((JObject)token, "DisplayName", "displayName", "name");
                nexusModId = ReadString((JObject)token, "NexusModId", "nexusModId", "modId");
            }
            else
            {
                id = token.Type == JTokenType.String ? ((string)token)?.Trim() : token.ToString().Trim();
                if (parseUmmVersion && !string.IsNullOrWhiteSpace(id))
                {
                    var match = UmmVersionedRequirement.Match(id);
                    if (match.Success)
                    {
                        id = match.Groups["id"].Value.Trim();
                        notBefore = match.Groups["version"].Value.Trim();
                    }
                }
            }

            return string.IsNullOrWhiteSpace(id)
                ? null
                : new FuseInstalledDependencyEdge
                {
                    Id = id.Trim(),
                    DisplayName = displayName,
                    NotBefore = notBefore,
                    NotAfter = notAfter,
                    Source = source ?? string.Empty,
                    NexusModId = nexusModId
                };
        }

        private static void AddOrReplaceEdge(
            ICollection<FuseInstalledDependencyEdge> destination,
            FuseInstalledDependencyEdge edge)
        {
            if (destination == null || edge == null || string.IsNullOrWhiteSpace(edge.Id))
            {
                return;
            }

            var existing = destination.FirstOrDefault(candidate =>
                FuseDeclaredPackageRelationship.SamePackageId(candidate?.Id, edge.Id));
            if (existing == null)
            {
                destination.Add(edge);
                return;
            }

            if (SourcePriority(edge.Source) <= SourcePriority(existing.Source))
            {
                return;
            }

            destination.Remove(existing);
            destination.Add(edge);
        }

        private static int SourcePriority(string source)
        {
            if (source?.IndexOf("FUSE Info", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 40;
            }

            if (source?.IndexOf("UMM Info", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 30;
            }

            if (source?.IndexOf("Definition", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 20;
            }

            return source?.IndexOf("Nexus", StringComparison.OrdinalIgnoreCase) >= 0 ? 10 : 0;
        }

        private static void MergeOfflineMetadata(
            string path,
            ICollection<FuseInstalledPackageDependencySnapshot> packages)
        {
            if (!File.Exists(path))
            {
                return;
            }

            JObject root;
            try
            {
                root = FuseLegacyDataConverter.ReadLegacyObject(path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
            {
                FuseLog.Exception($"FUSE could not read the offline dependency metadata cache '{path}'", ex);
                return;
            }

            foreach (var token in (root["packages"] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
            {
                var id = ReadString(token, "id", "Id");
                var folderName = ReadString(token, "folder", "folderName");
                var package = packages.FirstOrDefault(candidate =>
                    (!string.IsNullOrWhiteSpace(folderName) && string.Equals(candidate.FolderName, folderName, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(id) && FuseDeclaredPackageRelationship.SamePackageId(candidate.Id, id)));
                if (package == null)
                {
                    // A stale cache entry for an uninstalled mod must never
                    // masquerade as an installed dependency provider.
                    continue;
                }

                package.HasOfflineMetadata = true;
                var source = token["source"] as JObject;
                package.NexusUrl = ReadString(token, "nexusUrl", "url");
                if (string.IsNullOrWhiteSpace(package.NexusUrl))
                {
                    package.NexusUrl = ReadString(source, "url");
                }
                package.NexusGameDomain = ReadString(source, "gameDomain", "game");
                package.NexusModId = ReadString(source, "gameScopedModId", "nexusModId", "modId");
                ApplyNexusIdentity(package, package.NexusUrl);
                var packageSourceIsNexus = string.Equals(ReadString(source, "kind"), "nexus", StringComparison.OrdinalIgnoreCase);
                // Nexus is only a fallback for manifests that provide no hard
                // dependency list. If an author adds local requirements after
                // an older install, stale cached edges must not remain active.
                if (package.Requirements.Count == 0)
                {
                    AddEdges(package.Requirements, NexusOnly(token["requirements"], packageSourceIsNexus), "Nexus API cache", parseUmmVersion: false);
                }
                AddEdges(package.LoadAfter, NexusOnly(token["loadAfter"], packageSourceIsNexus), "Nexus API cache", parseUmmVersion: false);
                AddEdges(package.LoadBefore, NexusOnly(token["loadBefore"], packageSourceIsNexus), "Nexus API cache", parseUmmVersion: false);
                ClassifyPackage(package);
            }
        }

        private static JToken NexusOnly(JToken token, bool packageSourceIsNexus)
        {
            if (token == null || packageSourceIsNexus)
            {
                return token;
            }

            IEnumerable<JToken> items = token.Type == JTokenType.Array
                ? token.Children()
                : new[] { token };
            return new JArray(items.Where(item =>
            {
                if (!(item is JObject obj))
                {
                    return false;
                }

                var source = ReadString(obj, "source");
                var id = ReadString(obj, "id", "Id");
                return string.Equals(source, "nexus", StringComparison.OrdinalIgnoreCase) ||
                       id.StartsWith("nexus:", StringComparison.OrdinalIgnoreCase);
            }));
        }

        private static void MergeFuseSnapshots(
            IReadOnlyList<FusePackageManifestSnapshot> fusePackages,
            List<FuseInstalledPackageDependencySnapshot> packages)
        {
            foreach (var snapshot in fusePackages ?? Array.Empty<FusePackageManifestSnapshot>())
            {
                var package = packages.FirstOrDefault(candidate =>
                    FuseDeclaredPackageRelationship.SamePackageId(candidate.Id, snapshot.Id) ||
                    (!string.IsNullOrWhiteSpace(snapshot.FolderPath) &&
                     string.Equals(candidate.FolderPath, snapshot.FolderPath, StringComparison.OrdinalIgnoreCase)));
                if (package == null)
                {
                    package = new FuseInstalledPackageDependencySnapshot
                    {
                        Id = snapshot.Id,
                        DisplayName = snapshot.DisplayName,
                        Version = snapshot.Version,
                        FolderName = snapshot.FolderName,
                        FolderPath = snapshot.FolderPath
                    };
                    packages.Add(package);
                }

                package.Id = snapshot.Id;
                package.DisplayName = string.IsNullOrWhiteSpace(snapshot.DisplayName) ? snapshot.Id : snapshot.DisplayName;
                package.Version = snapshot.Version ?? package.Version;
                package.Disabled = snapshot.Disabled;
                package.IsFuseDataPackage = true;
                package.IsLegacy = snapshot.IsLegacyConverted;
                package.Category = snapshot.IsLegacyConverted ? "Legacy data package" : "FUSE data package";
                package.ManifestSource = snapshot.IsLegacyConverted ? "legacy Definition.json" : "FUSE Info.json";
                foreach (var dependency in snapshot.RequiredPackageIds ?? Array.Empty<string>())
                {
                    AddOrReplaceEdge(package.Requirements, new FuseInstalledDependencyEdge
                    {
                        Id = dependency,
                        Source = package.ManifestSource
                    });
                }

                foreach (var dependency in snapshot.LoadAfter ?? Array.Empty<string>())
                {
                    AddOrReplaceEdge(package.LoadAfter, new FuseInstalledDependencyEdge
                    {
                        Id = dependency,
                        Source = package.ManifestSource
                    });
                }

                foreach (var dependency in snapshot.LoadBefore ?? Array.Empty<string>())
                {
                    AddOrReplaceEdge(package.LoadBefore, new FuseInstalledDependencyEdge
                    {
                        Id = dependency,
                        Source = package.ManifestSource
                    });
                }

                foreach (var fault in snapshot.Faults ?? Array.Empty<string>())
                {
                    package.AddFault(fault);
                }
            }
        }

        internal static FuseInstalledPackageDependencySnapshot FindInstalled(
            IEnumerable<FuseInstalledPackageDependencySnapshot> packages,
            string dependencyId)
        {
            ParseNexusDependencyId(dependencyId, out var nexusGame, out var nexusModId);
            return (packages ?? Enumerable.Empty<FuseInstalledPackageDependencySnapshot>())
                .FirstOrDefault(candidate =>
                    FuseDeclaredPackageRelationship.SamePackageId(candidate?.Id, dependencyId) ||
                    string.Equals(candidate?.FolderName, dependencyId, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrWhiteSpace(nexusModId) &&
                     string.Equals(candidate?.NexusModId, nexusModId, StringComparison.OrdinalIgnoreCase) &&
                     (string.IsNullOrWhiteSpace(nexusGame) ||
                      string.Equals(candidate?.NexusGameDomain, nexusGame, StringComparison.OrdinalIgnoreCase))));
        }

        private static void ApplyNexusIdentity(FuseInstalledPackageDependencySnapshot package, string url)
        {
            if (package == null || string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            var match = NexusPageIdentity.Match(url);
            if (!match.Success)
            {
                return;
            }

            package.NexusUrl = url;
            if (string.IsNullOrWhiteSpace(package.NexusGameDomain))
            {
                package.NexusGameDomain = match.Groups["game"].Value;
            }

            if (string.IsNullOrWhiteSpace(package.NexusModId))
            {
                package.NexusModId = match.Groups["mod"].Value;
            }
        }

        private static void ParseNexusDependencyId(string dependencyId, out string game, out string modId)
        {
            game = string.Empty;
            modId = string.Empty;
            var parts = (dependencyId ?? string.Empty).Split(':');
            if (parts.Length != 3 || !string.Equals(parts[0], "nexus", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            game = parts[1].Trim();
            modId = parts[2].Trim();
        }

        internal static bool VersionSatisfies(
            string installedVersion,
            string notBefore,
            string notAfter,
            out bool versionReadable)
        {
            versionReadable = TryReadVersion(installedVersion, out var installed);
            if (!versionReadable)
            {
                return string.IsNullOrWhiteSpace(notBefore) && string.IsNullOrWhiteSpace(notAfter);
            }

            if (!string.IsNullOrWhiteSpace(notBefore) &&
                TryReadVersion(notBefore, out var minimum) &&
                installed < minimum)
            {
                return false;
            }

            return string.IsNullOrWhiteSpace(notAfter) ||
                   !TryReadVersion(notAfter, out var maximum) ||
                   installed <= maximum;
        }

        private static bool TryReadVersion(string value, out Version version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var match = Regex.Match(value, @"\d+(?:\.\d+){0,3}", RegexOptions.CultureInvariant);
            if (!match.Success)
            {
                return false;
            }

            var parts = match.Value.Split('.').Take(4).ToList();
            while (parts.Count < 2)
            {
                parts.Add("0");
            }

            return Version.TryParse(string.Join(".", parts), out version);
        }

        private static string ReadString(JObject obj, params string[] names)
        {
            if (obj == null)
            {
                return string.Empty;
            }

            foreach (var name in names ?? Array.Empty<string>())
            {
                var token = obj[name];
                if (token == null || token.Type == JTokenType.Null)
                {
                    continue;
                }

                var value = token.Type == JTokenType.String ? (string)token : token.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }

            return string.Empty;
        }

        private static bool ReadBoolean(JObject obj, string name, bool defaultValue)
        {
            var token = obj?[name];
            if (token == null || token.Type == JTokenType.Null)
            {
                return defaultValue;
            }

            if (token.Type == JTokenType.Boolean)
            {
                return (bool)token;
            }

            return bool.TryParse(token.ToString(), out var value) ? value : defaultValue;
        }
    }

    internal sealed class FuseInstalledPackageDependencySnapshot
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string FolderName { get; set; } = string.Empty;
        public string FolderPath { get; set; } = string.Empty;
        public string Category { get; set; } = "Mod";
        public string ManifestSource { get; set; } = string.Empty;
        public string NexusUrl { get; set; } = string.Empty;
        public string NexusGameDomain { get; set; } = string.Empty;
        public string NexusModId { get; set; } = string.Empty;
        public bool Disabled { get; set; }
        public bool IsLegacy { get; set; }
        public bool IsCodePlugin { get; set; }
        public bool IsFuseDataPackage { get; set; }
        public bool HasAssetLoaderMetadata { get; set; }
        public bool HasOfflineMetadata { get; set; }
        public List<FuseInstalledDependencyEdge> Requirements { get; } = new List<FuseInstalledDependencyEdge>();
        public List<FuseInstalledDependencyEdge> LoadAfter { get; } = new List<FuseInstalledDependencyEdge>();
        public List<FuseInstalledDependencyEdge> LoadBefore { get; } = new List<FuseInstalledDependencyEdge>();
        public List<string> Faults { get; } = new List<string>();
        public bool HasEdges => Requirements.Count > 0 || LoadAfter.Count > 0 || LoadBefore.Count > 0;

        internal void AddFault(string fault)
        {
            if (!string.IsNullOrWhiteSpace(fault) && !Faults.Contains(fault, StringComparer.OrdinalIgnoreCase))
            {
                Faults.Add(fault);
            }
        }
    }

    internal sealed class FuseInstalledDependencyEdge
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string NotBefore { get; set; } = string.Empty;
        public string NotAfter { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string NexusModId { get; set; } = string.Empty;
    }
}
