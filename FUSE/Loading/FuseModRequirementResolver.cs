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

            var conflictsWith = mixinto?.ConflictsWith?
                .Where(requirement => !string.IsNullOrWhiteSpace(requirement?.Id))
                .ToArray() ?? Array.Empty<FuseModRequirement>();

            if (requirements.Length == 0 && conflictsWith.Length == 0)
            {
                return true;
            }

            var installedMods = CollectInstalledMods();
            var sourceFile = string.IsNullOrWhiteSpace(loaded.DefinitionPath)
                ? mixinto?.SourceFile ?? string.Empty
                : loaded.DefinitionPath;
            foreach (var requirement in requirements)
            {
                if (!TryFindInstalled(requirement.Id, installedMods, out var installed))
                {
                    reason =
                        $"package='{definition?.Id ?? string.Empty}' mixinto dependency missing id='{requirement.Id}' " +
                        $"target='{mixinto?.Target ?? string.Empty}' folder='{loaded.FolderPath}' sourceFile='{sourceFile}' " +
                        "action='Install and enable the named dependency, or correct mixinto.requires in this file.'";
                    return false;
                }

                // Replacement capabilities use FUSE's version line, not the
                // retired package's version line. Once the capability is
                // declared provided, legacy notBefore/notAfter values do not
                // compare meaningfully and must not reject the mixinto.
                if (!installed.IsReplacementCapability &&
                    !VersionSatisfies(definition?.Id, requirement, installed, out reason))
                {
                    reason =
                        $"package='{definition?.Id ?? string.Empty}' mixinto dependency version mismatch id='{requirement.Id}' installedVersion='{installed.Version}' " +
                        $"notBefore='{requirement.NotBefore ?? string.Empty}' notAfter='{requirement.NotAfter ?? string.Empty}' " +
                        $"target='{mixinto?.Target ?? string.Empty}' folder='{loaded.FolderPath}' sourceFile='{sourceFile}' " +
                        "action='Install a compatible dependency version, or correct the version bounds in mixinto.requires.'";
                    return false;
                }
            }

            foreach (var conflict in conflictsWith)
            {
                if (!TryFindInstalled(conflict.Id, installedMods, out var installed))
                {
                    continue;
                }

                var matchesVersion = installed.IsReplacementCapability ||
                    VersionSatisfies(definition?.Id, conflict, installed, out _);
                if (!matchesVersion)
                {
                    continue;
                }

                reason =
                    $"package='{definition?.Id ?? string.Empty}' mixinto conflict matched id='{conflict.Id}' " +
                    $"installedVersion='{installed.Version}' target='{mixinto?.Target ?? string.Empty}' " +
                    $"folder='{loaded.FolderPath}' sourceFile='{sourceFile}' " +
                    "action='This conditional fragment is intentionally inactive while the conflicting package is enabled.'";
                return false;
            }

            reason = $"mixinto requirements satisfied; conflicts clear target='{mixinto?.Target ?? string.Empty}' sourceFile='{sourceFile}'";
            return true;
        }

        internal static Dictionary<string, InstalledMod> CollectInstalledMods()
        {
            var result = new Dictionary<string, InstalledMod>(StringComparer.OrdinalIgnoreCase);
            var fuseVersion = typeof(FuseModRequirementResolver).Assembly.GetName().Version?.ToString() ?? "1.0.0";
            foreach (var capabilityId in FuseReplacementCapabilityCatalog.AdvertisedPackageIds)
            {
                AddInstalled(result, capabilityId, fuseVersion, "FUSE replacement capability", true);
            }

            foreach (var loaded in FuseModLoader.GetLoadedModsInOrder())
            {
                var definition = loaded?.Definition;
                if (definition == null)
                {
                    continue;
                }

                AddInstalled(
                    result,
                    definition.Id,
                    definition.ModVersion,
                    "loaded definition",
                    folderPath: loaded.FolderPath);
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
                var packageId = TryReadPrimaryPackageId(folder);
                if (IsManifestDisabled(folder) ||
                    FuseUmmState.TryGetDisabledReason(folder, packageId, out _) ||
                    !FuseModSetService.IsPackageEnabledByActiveSet(packageId, folder))
                {
                    continue;
                }

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

            AddInstalled(result, Path.GetFileName(folder), string.Empty, "mod folder", folderPath: folder);
            TryReadManifest(infoPath, "Id", "Version", "Info.json", result);
            TryReadManifest(definitionPath, "id", "version", "Definition.json", result);
        }

        private static string TryReadPrimaryPackageId(string folder)
        {
            foreach (var candidate in new[]
                     {
                         new { Path = Path.Combine(folder, "Info.json"), Id = "Id" },
                         new { Path = Path.Combine(folder, "Definition.json"), Id = "id" }
                     })
            {
                if (!File.Exists(candidate.Path))
                {
                    continue;
                }

                try
                {
                    var manifest = FuseLegacyDataConverter.ReadLegacyObject(candidate.Path);
                    var id = ReadString(manifest, candidate.Id, "Id", "id");
                    if (!string.IsNullOrWhiteSpace(id))
                    {
                        return id;
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
                {
                    // The normal manifest reader reports the parse problem. The
                    // folder name remains sufficient for enabled-state checks.
                    FuseLog.Info(
                        $"FUSE dependency scan could not read primary package id from '{candidate.Path}'; " +
                        $"using folder identity instead. reason='{ex.Message}'");
                }
            }

            return Path.GetFileName(folder) ?? string.Empty;
        }

        private static bool IsManifestDisabled(string folder)
        {
            var infoPath = Path.Combine(folder, "Info.json");
            if (!File.Exists(infoPath))
            {
                return false;
            }

            try
            {
                var info = FuseLegacyDataConverter.ReadLegacyObject(infoPath);
                return ReadBoolean(info, "FuseDisabled", false) ||
                       ReadBoolean(info, "Disabled", false) ||
                       ReadBoolean(info, "disabled", false) ||
                       !ReadBoolean(info, "Enabled", true) ||
                       !ReadBoolean(info, "enabled", true);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
            {
                return false;
            }
        }

        private static bool ReadBoolean(JObject manifest, string propertyName, bool defaultValue)
        {
            var token = manifest?[propertyName];
            if (token == null)
            {
                return defaultValue;
            }

            if (token.Type == JTokenType.Boolean)
            {
                return (bool)token;
            }

            return bool.TryParse(token.ToString(), out var value) ? value : defaultValue;
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
                AddInstalled(result, id, version, sourceName, folderPath: Path.GetDirectoryName(path));
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

        private static void AddInstalled(
            IDictionary<string, InstalledMod> result,
            string id,
            string version,
            string source,
            bool isReplacementCapability = false,
            string folderPath = null)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return;
            }

            var normalizedId = id.Trim();
            AddAlias(result, normalizedId, version, source, isReplacementCapability, folderPath);
            if (normalizedId.EndsWith(".FUSE", StringComparison.OrdinalIgnoreCase) ||
                normalizedId.EndsWith(".RAIL", StringComparison.OrdinalIgnoreCase))
            {
                AddAlias(
                    result,
                    normalizedId.Substring(0, normalizedId.Length - 5),
                    version,
                    source,
                    isReplacementCapability,
                    folderPath);
            }
        }

        private static void AddAlias(
            IDictionary<string, InstalledMod> result,
            string id,
            string version,
            string source,
            bool isReplacementCapability,
            string folderPath)
        {
            if (string.IsNullOrWhiteSpace(id) || result.ContainsKey(id))
            {
                return;
            }

            result[id] = new InstalledMod
            {
                Id = id,
                Version = version ?? string.Empty,
                Source = source ?? string.Empty,
                IsReplacementCapability = isReplacementCapability,
                FolderPath = folderPath ?? string.Empty
            };
        }

        internal static bool TryFindInstalled(string id, Dictionary<string, InstalledMod> installedMods, out InstalledMod installed)
        {
            installed = null;
            if (string.IsNullOrWhiteSpace(id) || installedMods == null)
            {
                return false;
            }

            if (installedMods.TryGetValue(id.Trim(), out installed))
            {
                return true;
            }

            if (!FuseReplacementCapabilityCatalog.IsProvided(id))
            {
                return false;
            }

            var fuseVersion = typeof(FuseModRequirementResolver).Assembly.GetName().Version?.ToString() ?? "1.0.0";
            installed = new InstalledMod
            {
                Id = id.Trim(),
                Version = fuseVersion,
                Source = "FUSE replacement capability",
                IsReplacementCapability = true,
                FolderPath = string.Empty
            };
            return true;
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
            public bool IsReplacementCapability { get; set; }
            public string FolderPath { get; set; }
        }
    }
}
