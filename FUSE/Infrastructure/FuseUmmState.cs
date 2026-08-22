using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityModManagerNet;

namespace FUSE.Infrastructure
{
    internal sealed class FuseUmmModInfo
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string FolderName { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public bool Enabled { get; set; }
        public bool IsFuseDataPackage { get; set; }
        public bool IsLegacyConverted { get; set; }
        public string ProfileSource { get; set; } = "UMM-active";
    }

    internal static class FuseUmmState
    {
        private static readonly FieldInfo ModEntriesField =
            typeof(UnityModManager).GetField("modEntries", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        private static bool _loggedInspectionFailure;

        public static IReadOnlyList<FuseUmmModInfo> GetActiveMods(bool includeFuse)
        {
            try
            {
                return EnumerateModEntries()
                    .Where(mod => mod.Enabled)
                    .Where(mod => includeFuse || !IsFuseMod(mod))
                    .OrderBy(mod => mod.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(mod => mod.Id, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch (Exception ex)
            {
                if (!_loggedInspectionFailure)
                {
                    _loggedInspectionFailure = true;
                    FuseLog.Warning($"FUSE could not inspect Unity Mod Manager active mods: {ex.GetBaseException().Message}");
                }

                return Array.Empty<FuseUmmModInfo>();
            }
        }

        public static bool TryGetDisabledReason(string folderPath, string packageId, out string reason)
        {
            reason = string.Empty;
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                return false;
            }

            try
            {
                var entries = ModEntriesField?.GetValue(null) as IEnumerable;
                if (entries == null)
                {
                    return false;
                }

                foreach (var entry in entries)
                {
                    if (entry == null)
                    {
                        continue;
                    }

                    var entryPath = ReadStringMember(entry, "Path");
                    if (!SamePath(entryPath, folderPath))
                    {
                        continue;
                    }

                    var enabled = ReadBooleanMember(entry, "Enabled", true);
                    if (enabled)
                    {
                        return false;
                    }

                    reason = $"disabled in Unity Mod Manager for mod '{SafePackageId(packageId, folderPath)}'";
                    return true;
                }
            }
            catch (Exception ex)
            {
                if (!_loggedInspectionFailure)
                {
                    _loggedInspectionFailure = true;
                    FuseLog.Warning($"FUSE could not inspect Unity Mod Manager disabled state for packages: {ex.GetBaseException().Message}");
                }
            }

            return false;
        }

        /// <summary>
        /// Returns true when Unity Mod Manager already owns an executable package's
        /// active lifecycle. Dual-format mods can also contain a RailLoader
        /// Definition.json; hosting that legacy plugin after UMM has started the
        /// same assembly invokes its startup and Harmony patches twice.
        /// </summary>
        public static bool HasActiveRuntimeEntry(string folderPath, string packageId)
        {
            try
            {
                var entries = ModEntriesField?.GetValue(null) as IEnumerable;
                if (entries == null)
                {
                    return false;
                }

                foreach (var value in entries)
                {
                    if (!(value is UnityModManager.ModEntry entry))
                    {
                        continue;
                    }

                    var infoId = entry.Info?.Id;
                    var samePackage = SamePath(entry.Path, folderPath) ||
                                      (!string.IsNullOrWhiteSpace(packageId) &&
                                       string.Equals(infoId, packageId, StringComparison.OrdinalIgnoreCase));
                    if (!samePackage || string.IsNullOrWhiteSpace(entry.Info?.EntryMethod))
                    {
                        continue;
                    }

                    if (IsActiveRuntimeState(
                        entry.Enabled,
                        entry.Started,
                        entry.Loaded,
                        entry.ErrorOnLoading))
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                if (!_loggedInspectionFailure)
                {
                    _loggedInspectionFailure = true;
                    FuseLog.Warning(
                        $"FUSE could not inspect Unity Mod Manager runtime ownership for packages: " +
                        ex.GetBaseException().Message);
                }
            }

            return false;
        }

        internal static bool IsActiveRuntimeState(
            bool enabled,
            bool started,
            bool loaded,
            bool errorOnLoading)
        {
            return enabled && started && loaded && !errorOnLoading;
        }

        private static IEnumerable<FuseUmmModInfo> EnumerateModEntries()
        {
            var entries = ModEntriesField?.GetValue(null) as IEnumerable;
            if (entries == null)
            {
                yield break;
            }

            foreach (var entry in entries)
            {
                if (entry == null)
                {
                    continue;
                }

                var entryPath = ReadStringMember(entry, "Path");
                var info = ReadObjectMember(entry, "Info");
                var id = ReadStringMember(entry, "Id");
                if (string.IsNullOrWhiteSpace(id))
                {
                    id = ReadStringMember(info, "Id");
                }

                var displayName = ReadStringMember(entry, "DisplayName");
                if (string.IsNullOrWhiteSpace(displayName))
                {
                    displayName = ReadStringMember(info, "DisplayName");
                }

                if (string.IsNullOrWhiteSpace(displayName))
                {
                    displayName = ReadStringMember(info, "Name");
                }

                var folderName = string.IsNullOrWhiteSpace(entryPath) ? string.Empty : Path.GetFileName(NormalizePath(entryPath));
                if (string.IsNullOrWhiteSpace(id))
                {
                    id = folderName;
                }

                if (string.IsNullOrWhiteSpace(displayName))
                {
                    displayName = string.IsNullOrWhiteSpace(id) ? folderName : id;
                }

                var version = ReadStringMember(info, "Version");
                if (string.IsNullOrWhiteSpace(version))
                {
                    version = ReadStringMember(entry, "Version");
                }

                yield return new FuseUmmModInfo
                {
                    Id = id ?? string.Empty,
                    DisplayName = displayName ?? string.Empty,
                    Version = version ?? string.Empty,
                    FolderName = folderName ?? string.Empty,
                    Path = entryPath ?? string.Empty,
                    Enabled = ReadBooleanMember(entry, "Enabled", true)
                };
            }
        }

        private static bool IsFuseMod(FuseUmmModInfo mod)
        {
            if (mod == null)
            {
                return false;
            }

            return string.Equals(mod.Id, "FUSE", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(mod.FolderName, "FUSE", StringComparison.OrdinalIgnoreCase);
        }

        private static object ReadObjectMember(object instance, string name)
        {
            if (instance == null)
            {
                return null;
            }

            var type = instance.GetType();
            var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
            {
                return field.GetValue(instance);
            }

            var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return property?.GetValue(instance, null);
        }

        private static string ReadStringMember(object instance, string name)
        {
            if (instance == null)
            {
                return string.Empty;
            }

            var type = instance.GetType();
            var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null && field.FieldType == typeof(string))
            {
                return field.GetValue(instance) as string;
            }

            var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property == null)
            {
                return string.Empty;
            }

            var value = property.GetValue(instance, null);
            return value == null ? string.Empty : value.ToString();
        }

        private static bool ReadBooleanMember(object instance, string name, bool defaultValue)
        {
            if (instance == null)
            {
                return defaultValue;
            }

            var type = instance.GetType();
            var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null && field.FieldType == typeof(bool))
            {
                return (bool)field.GetValue(instance);
            }

            var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return property != null && property.PropertyType == typeof(bool)
                ? (bool)property.GetValue(instance, null)
                : defaultValue;
        }

        private static bool SamePath(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                return false;
            }

            return string.Equals(NormalizePath(left), NormalizePath(right), StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizePath(string path)
        {
            try
            {
                return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
        }

        private static string SafePackageId(string packageId, string folderPath)
        {
            return string.IsNullOrWhiteSpace(packageId) ? Path.GetFileName(folderPath) : packageId.Trim();
        }
    }
}
