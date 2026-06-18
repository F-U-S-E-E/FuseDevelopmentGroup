using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FUSE.Authoring.Data;
using FUSE.Infrastructure;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FUSE.Loading
{
    internal static class FuseModRequirementResolver
    {
        private static readonly object WarningLock = new object();
        private static readonly HashSet<string> WarningsEmitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static bool ShouldApply(FuseLoadedMod loaded, out string reason)
        {
            reason = string.Empty;
            var definition = loaded?.Definition;
            var mixinto = definition?.Mixinto;
            var requirements = mixinto?.Requires?
                .Where(requirement => !string.IsNullOrWhiteSpace(requirement?.Id))
                .ToArray() ?? Array.Empty<FuseModRequirement>();

            if (requirements.Length == 0)
            {
                return true;
            }

            var installedMods = CollectInstalledMods();
            foreach (var requirement in requirements)
            {
                if (!TryFindInstalled(requirement.Id, installedMods, out var installed))
                {
                    reason =
                        $"mixinto dependency missing id='{requirement.Id}' " +
                        $"target='{mixinto?.Target ?? string.Empty}' sourceFile='{mixinto?.SourceFile ?? string.Empty}'";
                    return false;
                }

                if (!VersionSatisfies(definition?.Id, requirement, installed, out reason))
                {
                    reason =
                        $"mixinto dependency version mismatch id='{requirement.Id}' installedVersion='{installed.Version}' " +
                        $"notBefore='{requirement.NotBefore ?? string.Empty}' notAfter='{requirement.NotAfter ?? string.Empty}' " +
                        $"target='{mixinto?.Target ?? string.Empty}' sourceFile='{mixinto?.SourceFile ?? string.Empty}'";
                    return false;
                }
            }

            reason = $"mixinto requirements satisfied target='{mixinto?.Target ?? string.Empty}' sourceFile='{mixinto?.SourceFile ?? string.Empty}'";
            return true;
        }

        private static Dictionary<string, InstalledMod> CollectInstalledMods()
        {
            var result = new Dictionary<string, InstalledMod>(StringComparer.OrdinalIgnoreCase);
            AddInstalled(result, "FUSE", "1.0.0", "runtime");

            foreach (var loaded in FuseModLoader.GetLoadedModsInOrder())
            {
                var definition = loaded?.Definition;
                if (definition == null)
                {
                    continue;
                }

                AddInstalled(result, definition.Id, definition.ModVersion, "loaded definition");
                TryReadFolderManifests(loaded.FolderPath, result);
            }

            var modsRoot = FuseDataPackageDiscovery.GetModsRoot();
            if (string.IsNullOrWhiteSpace(modsRoot) || !Directory.Exists(modsRoot))
            {
                return result;
            }

            string[] folders;
            try
            {
                folders = Directory.GetDirectories(modsRoot);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                FuseLog.Exception($"FUSE could not inspect installed mods for mixinto requirements in '{modsRoot}'", ex);
                return result;
            }

            foreach (var folder in folders)
            {
                TryReadFolderManifests(folder, result);
            }

            return result;
        }

        private static void TryReadFolderManifests(string folder, IDictionary<string, InstalledMod> result)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                return;
            }

            var infoPath = Path.Combine(folder, "Info.json");
            var definitionPath = Path.Combine(folder, "Definition.json");
            var hasEnabledManifest = File.Exists(infoPath) || File.Exists(definitionPath);
            if (!hasEnabledManifest)
            {
                return;
            }

            AddInstalled(result, Path.GetFileName(folder), string.Empty, "mod folder");
            TryReadManifest(infoPath, "Id", "Version", "Info.json", result);
            TryReadManifest(definitionPath, "id", "version", "Definition.json", result);
        }

        private static void TryReadManifest(
            string path,
            string idProperty,
            string versionProperty,
            string sourceName,
            IDictionary<string, InstalledMod> result)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return;
            }

            try
            {
                var manifest = FuseLegacyDataConverter.ReadLegacyObject(path);
                var id = ReadString(manifest, idProperty, idProperty.ToLowerInvariant(), idProperty.ToUpperInvariant(), "Id", "id");
                if (string.IsNullOrWhiteSpace(id))
                {
                    id = Path.GetFileName(Path.GetDirectoryName(path));
                }

                var version = ReadString(manifest, versionProperty, versionProperty.ToLowerInvariant(), "Version", "version");
                AddInstalled(result, id, version, sourceName);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
            {
                WarnOnce(
                    $"manifest:{path}",
                    $"FUSE could not parse '{path}' while checking mixinto requirements: {ex.Message}");
            }
        }

        private static string ReadString(JObject manifest, params string[] names)
        {
            if (manifest == null || names == null)
            {
                return string.Empty;
            }

            foreach (var name in names)
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var token = manifest[name];
                if (token == null)
                {
                    continue;
                }

                var value = token.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return string.Empty;
        }

        private static void AddInstalled(IDictionary<string, InstalledMod> result, string id, string version, string source)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return;
            }

            var normalizedId = id.Trim();
            AddAlias(result, normalizedId, version, source);
            if (normalizedId.EndsWith(".FUSE", StringComparison.OrdinalIgnoreCase) ||
                normalizedId.EndsWith(".RAIL", StringComparison.OrdinalIgnoreCase))
            {
                AddAlias(result, normalizedId.Substring(0, normalizedId.Length - 5), version, source);
            }
        }

        private static void AddAlias(IDictionary<string, InstalledMod> result, string id, string version, string source)
        {
            if (string.IsNullOrWhiteSpace(id) || result.ContainsKey(id))
            {
                return;
            }

            result[id] = new InstalledMod
            {
                Id = id,
                Version = version ?? string.Empty,
                Source = source ?? string.Empty
            };
        }

        private static bool TryFindInstalled(string id, Dictionary<string, InstalledMod> installedMods, out InstalledMod installed)
        {
            installed = null;
            return !string.IsNullOrWhiteSpace(id) &&
                   installedMods != null &&
                   installedMods.TryGetValue(id.Trim(), out installed);
        }

        internal static bool VersionSatisfies(
            string packageId,
            FuseModRequirement requirement,
            InstalledMod installed,
            out string reason)
        {
            reason = string.Empty;
            if (requirement == null || installed == null)
            {
                return true;
            }

            var hasMinimum = !string.IsNullOrWhiteSpace(requirement.NotBefore);
            var hasMaximum = !string.IsNullOrWhiteSpace(requirement.NotAfter);
            if (!hasMinimum && !hasMaximum)
            {
                return true;
            }

            if (!TryParseVersion(installed.Version, out var installedVersion))
            {
                WarnOnce(
                    $"{packageId}:{requirement.Id}:installedVersion",
                    $"FUSE mixinto package='{packageId ?? string.Empty}' operation='check mixinto requirement' " +
                    $"id='{requirement.Id}' installedVersion='{installed.Version ?? string.Empty}' could not be parsed; treating installed mod as compatible.");
                return true;
            }

            if (hasMinimum)
            {
                if (TryParseVersion(requirement.NotBefore, out var minimumVersion))
                {
                    if (installedVersion.CompareTo(minimumVersion) < 0)
                    {
                        reason = $"installed version '{installed.Version}' is older than required '{requirement.NotBefore}'";
                        return false;
                    }
                }
                else
                {
                    WarnOnce(
                        $"{packageId}:{requirement.Id}:notBefore:{requirement.NotBefore}",
                        $"FUSE mixinto package='{packageId ?? string.Empty}' operation='check mixinto requirement' " +
                        $"id='{requirement.Id}' notBefore='{requirement.NotBefore}' could not be parsed; ignoring that bound.");
                }
            }

            if (hasMaximum)
            {
                if (TryParseVersion(requirement.NotAfter, out var maximumVersion))
                {
                    if (installedVersion.CompareTo(maximumVersion) > 0)
                    {
                        reason = $"installed version '{installed.Version}' is newer than allowed '{requirement.NotAfter}'";
                        return false;
                    }
                }
                else
                {
                    WarnOnce(
                        $"{packageId}:{requirement.Id}:notAfter:{requirement.NotAfter}",
                        $"FUSE mixinto package='{packageId ?? string.Empty}' operation='check mixinto requirement' " +
                        $"id='{requirement.Id}' notAfter='{requirement.NotAfter}' could not be parsed; ignoring that bound.");
                }
            }

            return true;
        }

        internal static bool TryParseVersion(string value, out Version version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var match = Regex.Match(value.Trim(), @"\d+(\.\d+){0,3}");
            if (!match.Success)
            {
                return false;
            }

            var parts = match.Value.Split('.');
            var numbers = new int[4];
            for (var index = 0; index < numbers.Length; index++)
            {
                numbers[index] = 0;
            }

            for (var index = 0; index < Math.Min(parts.Length, numbers.Length); index++)
            {
                if (!int.TryParse(parts[index], out numbers[index]))
                {
                    return false;
                }
            }

            version = new Version(numbers[0], numbers[1], numbers[2], numbers[3]);
            return true;
        }

        private static void WarnOnce(string key, string message)
        {
            lock (WarningLock)
            {
                if (!WarningsEmitted.Add(key ?? string.Empty))
                {
                    return;
                }
            }

            FuseLog.Warning(message);
        }

        internal sealed class InstalledMod
        {
            public string Id { get; set; }
            public string Version { get; set; }
            public string Source { get; set; }
        }
    }
}
