using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using FUSE.Runtime.Events;
using FUSE.Loading;
using Newtonsoft.Json;
using UnityEngine;

namespace FUSE.Infrastructure
{
    internal static class FuseModSetService
    {
        private static readonly object Sync = new object();
        private static FuseModSetStore _store;
        private static string _lastStatus = "No mod set is loaded. All UMM-active and FUSE-managed packages are enabled.";

        public static string LastStatus
        {
            get
            {
                lock (Sync)
                {
                    return _lastStatus;
                }
            }
        }

        public static string ActiveSetId
        {
            get
            {
                lock (Sync)
                {
                    EnsureLoaded();
                    return _store.ActiveSetId ?? string.Empty;
                }
            }
        }

        public static string ActiveSetName
        {
            get
            {
                lock (Sync)
                {
                    EnsureLoaded();
                    var set = FindSet(_store.ActiveSetId);
                    return set == null ? "None - all available packages enabled" : set.Name;
                }
            }
        }

        public static IReadOnlyList<FuseModSet> GetSets()
        {
            lock (Sync)
            {
                EnsureLoaded();
                return _store.Sets
                    .OrderBy(set => set.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(set => set.Clone())
                    .ToArray();
            }
        }

        public static IReadOnlyList<FuseUmmModInfo> GetVisibleUmmMods()
        {
            return FuseUmmState.GetActiveMods(includeFuse: false)
                .OrderBy(mod => mod.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(mod => mod.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public static IReadOnlyList<FuseUmmModInfo> GetVisibleProfileMods()
        {
            return MergeVisibleProfileMods(
                FuseUmmState.GetActiveMods(includeFuse: false),
                FuseDataPackageDiscovery.GetPackageManifestSnapshots());
        }

        internal static IReadOnlyList<FuseUmmModInfo> MergeVisibleProfileMods(
            IEnumerable<FuseUmmModInfo> activeUmmMods,
            IEnumerable<FusePackageManifestSnapshot> dataPackages)
        {
            var result = (activeUmmMods ?? Enumerable.Empty<FuseUmmModInfo>())
                .Where(mod => mod != null)
                .Select(mod => new FuseUmmModInfo
                {
                    Id = mod.Id ?? string.Empty,
                    DisplayName = mod.DisplayName ?? string.Empty,
                    Version = mod.Version ?? string.Empty,
                    FolderName = mod.FolderName ?? string.Empty,
                    Path = mod.Path ?? string.Empty,
                    Enabled = mod.Enabled,
                    IsFuseDataPackage = mod.IsFuseDataPackage,
                    IsLegacyConverted = mod.IsLegacyConverted,
                    ProfileSource = string.IsNullOrWhiteSpace(mod.ProfileSource) ? "UMM-active" : mod.ProfileSource
                })
                .ToList();

            foreach (var package in dataPackages ?? Enumerable.Empty<FusePackageManifestSnapshot>())
            {
                if (package == null ||
                    string.Equals(package.Id, "FUSE", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(package.FolderName, "FUSE", StringComparison.OrdinalIgnoreCase) ||
                    (package.Disabled && !IsDisabledByFuseProfile(package.DisabledReason)))
                {
                    continue;
                }

                var existing = result.FirstOrDefault(mod =>
                    FuseDeclaredPackageRelationship.SamePackageId(mod.Id, package.Id) ||
                    (!string.IsNullOrWhiteSpace(package.FolderName) &&
                     string.Equals(mod.FolderName, package.FolderName, StringComparison.OrdinalIgnoreCase)));
                var source = package.IsLegacyConverted ? "legacy converted data" : "FUSE data";
                if (existing != null)
                {
                    existing.IsFuseDataPackage = true;
                    existing.IsLegacyConverted = existing.IsLegacyConverted || package.IsLegacyConverted;
                    if (existing.ProfileSource.IndexOf(source, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        existing.ProfileSource += " + " + source;
                    }

                    if (string.IsNullOrWhiteSpace(existing.DisplayName))
                    {
                        existing.DisplayName = package.DisplayName;
                    }

                    if (string.IsNullOrWhiteSpace(existing.Version))
                    {
                        existing.Version = package.Version;
                    }

                    continue;
                }

                result.Add(new FuseUmmModInfo
                {
                    Id = package.Id ?? string.Empty,
                    DisplayName = string.IsNullOrWhiteSpace(package.DisplayName) ? package.Id : package.DisplayName,
                    Version = package.Version ?? string.Empty,
                    FolderName = package.FolderName ?? string.Empty,
                    Path = package.FolderPath ?? string.Empty,
                    Enabled = !package.Disabled,
                    IsFuseDataPackage = true,
                    IsLegacyConverted = package.IsLegacyConverted,
                    ProfileSource = source
                });
            }

            return result
                .Where(mod => !string.IsNullOrWhiteSpace(mod.Id) || !string.IsNullOrWhiteSpace(mod.FolderName))
                .OrderBy(mod => mod.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(mod => mod.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static bool IsDisabledByFuseProfile(string reason)
        {
            return !string.IsNullOrWhiteSpace(reason) &&
                   reason.IndexOf("disabled by active FUSE mod set", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static IReadOnlyList<FuseUmmModInfo> GetEnabledVisibleProfileMods()
        {
            return GetVisibleProfileMods()
                .Where(IsModEnabledInActiveSet)
                .OrderBy(mod => mod.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(mod => mod.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public static string GetActiveSetFingerprint()
        {
            var mods = GetEnabledVisibleProfileMods();
            var input = string.Join(
                "\n",
                mods.Select(mod => $"{mod.Id}|{mod.Version}|{mod.FolderName}").ToArray());
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
                return BitConverter.ToString(bytes).Replace("-", string.Empty).Substring(0, 12);
            }
        }

        public static string GetActiveSetPackageSummary()
        {
            lock (Sync)
            {
                EnsureLoaded();
                var set = FindSet(_store.ActiveSetId);
                return GetSetPackageSummary(set);
            }
        }

        public static string GetSetPackageSummary(FuseModSet set)
        {
            var visible = GetVisibleProfileMods();
            var enabled = visible.Count(m => IsModEnabledInSet(set, m));
            return $"{enabled}/{visible.Count} available package(s) enabled by FUSE profile";
        }

        public static string ExportActiveManifest()
        {
            lock (Sync)
            {
                EnsureLoaded();
                var path = Path.Combine(Path.GetDirectoryName(GetStorePath()) ?? Application.persistentDataPath, "active-mod-set-manifest.json");
                var mods = GetEnabledVisibleProfileMods()
                    .Select(mod => new
                    {
                        mod.Id,
                        mod.DisplayName,
                        mod.Version,
                        mod.FolderName,
                        mod.ProfileSource
                    })
                    .ToArray();
                var manifest = new
                {
                    exportedUtc = DateTime.UtcNow.ToString("O"),
                    activeSet = ActiveSetName,
                    fingerprint = GetActiveSetFingerprint(),
                    policy = "UMM-disabled mods are excluded before FUSE profile filtering; non-UMM FUSE and converted legacy data packages remain profile-selectable.",
                    mods
                };
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? string.Empty);
                File.WriteAllText(path, JsonConvert.SerializeObject(manifest, Formatting.Indented));
                _lastStatus = $"Exported active mod-set manifest to '{path}'.";
                return path;
            }
        }

        public static FuseModSet CreateSetFromCurrentActiveMods()
        {
            lock (Sync)
            {
                EnsureLoaded();
                var set = CreateSetFromCurrentActiveModsNoLock();
                Save();
                _lastStatus = $"Created and selected mod set '{set.Name}'. It applies on the next package reload or map load.";
                FuseEvents.RaiseModSetAdded(set.Id);
                return set.Clone();
            }
        }

        public static bool SetActive(string setId)
        {
            lock (Sync)
            {
                EnsureLoaded();
                var set = FindSet(setId);
                if (set == null)
                {
                    _lastStatus = $"Could not select mod set '{setId}' because it no longer exists.";
                    return false;
                }

                _store.ActiveSetId = set.Id;
                Save();
                _lastStatus = $"Selected mod set '{set.Name}'. It applies on the next package reload or map load.";
                return true;
            }
        }

        public static void ClearActiveSet()
        {
            lock (Sync)
            {
                EnsureLoaded();
                _store.ActiveSetId = string.Empty;
                Save();
                _lastStatus = "No mod set is loaded. All UMM-active and FUSE-managed packages are enabled.";
            }
        }

        public static bool DeleteSet(string setId)
        {
            lock (Sync)
            {
                EnsureLoaded();
                var set = FindSet(setId);
                if (set == null)
                {
                    return false;
                }

                _store.Sets.Remove(set);
                if (string.Equals(_store.ActiveSetId, set.Id, StringComparison.OrdinalIgnoreCase))
                {
                    _store.ActiveSetId = string.Empty;
                }

                Save();
                _lastStatus = $"Deleted mod set '{set.Name}'.";
                FuseEvents.RaiseModSetRemoved(setId);
                return true;
            }
        }

        public static bool ToggleModInActiveSet(FuseUmmModInfo mod)
        {
            lock (Sync)
            {
                EnsureLoaded();
                var set = FindSet(_store.ActiveSetId);
                if (set == null)
                {
                    set = CreateSetFromCurrentActiveModsNoLock();
                }
                return ToggleModInSet(mod, set);
            }
        }

        public static bool ToggleModInSet(FuseUmmModInfo mod, FuseModSet set)
        {
            if (mod == null || set == null)
            {
                return false;
            }

            lock (Sync)
            {
                EnsureLoaded();
                var storedSet = FindSet(set.Id);
                if (storedSet == null)
                {
                    _lastStatus = $"Could not update mod set '{set.Name}' because it no longer exists.";
                    return false;
                }

                var nowEnabled = ToggleModMembership(storedSet, mod);
                if (!ReferenceEquals(storedSet, set))
                {
                    set.EnabledModIds = storedSet.EnabledModIds.ToArray();
                    set.EnabledFolderNames = storedSet.EnabledFolderNames.ToArray();
                    set.UpdatedUtc = storedSet.UpdatedUtc;
                }

                _lastStatus = nowEnabled
                    ? $"Turned on '{mod.DisplayName}' in mod set '{storedSet.Name}'."
                    : $"Turned off '{mod.DisplayName}' in mod set '{storedSet.Name}'.";
                Save();
                return true;
            }
        }

        internal static bool ToggleModMembership(FuseModSet set, FuseUmmModInfo mod)
        {
            if (set == null || mod == null)
            {
                return false;
            }

            var ids = ToMutableSet(set.EnabledModIds);
            var folders = ToMutableSet(set.EnabledFolderNames);
            var currentlyEnabled = IsModEnabledInSet(set, mod);
            if (currentlyEnabled)
            {
                ids.Remove(mod.Id ?? string.Empty);
                folders.Remove(mod.FolderName ?? string.Empty);
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(mod.Id))
                {
                    ids.Add(mod.Id);
                }

                if (!string.IsNullOrWhiteSpace(mod.FolderName))
                {
                    folders.Add(mod.FolderName);
                }
            }

            set.EnabledModIds = ids.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();
            set.EnabledFolderNames = folders.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();
            set.UpdatedUtc = DateTime.UtcNow.ToString("O");
            return !currentlyEnabled;
        }

        public static bool HasActiveSet
        {
            get
            {
                lock (Sync)
                {
                    EnsureLoaded();
                    return FindSet(_store.ActiveSetId) != null;
                }
            }
        }

        public static bool IsModEnabledInActiveSet(FuseUmmModInfo mod)
        {
            lock (Sync)
            {
                EnsureLoaded();
                var set = FindSet(_store.ActiveSetId);
                return set == null || IsModEnabledInSet(set, mod);
            }
        }

        public static bool IsPackageEnabledByActiveSet(string packageId, string folderPath)
        {
            lock (Sync)
            {
                EnsureLoaded();
                var set = FindSet(_store.ActiveSetId);
                if (set == null)
                {
                    return true;
                }

                return IsPackageEnabledInSet(set, packageId, folderPath);
            }
        }

        internal static bool IsPackageEnabledInSet(FuseModSet set, string packageId, string folderPath)
        {
            return set == null || IsPackageMatched(set, packageId, folderPath);
        }

        public static string GetPackageDisabledReason(string packageId, string folderPath)
        {
            lock (Sync)
            {
                EnsureLoaded();
                var set = FindSet(_store.ActiveSetId);
                var name = set == null ? "unknown" : set.Name;
                var id = string.IsNullOrWhiteSpace(packageId) ? Path.GetFileName(folderPath) : packageId;
                return $"disabled by active FUSE mod set '{name}' for package '{id}'";
            }
        }

        public static string GetStorePath()
        {
            var root = Path.Combine(Application.persistentDataPath, "FUSE");
            return Path.Combine(root, "mod-sets.json");
        }

        private static void EnsureLoaded()
        {
            if (_store != null)
            {
                return;
            }

            var path = GetStorePath();
            try
            {
                if (File.Exists(path))
                {
                    _store = JsonConvert.DeserializeObject<FuseModSetStore>(File.ReadAllText(path)) ?? new FuseModSetStore();
                }
                else
                {
                    _store = new FuseModSetStore();
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
            {
                FuseLog.Exception($"FUSE could not read mod-set store '{path}'", ex);
                _store = new FuseModSetStore();
            }

            _store.Sets = _store.Sets ?? new List<FuseModSet>();
        }

        private static void Save()
        {
            var path = GetStorePath();
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? string.Empty);
            File.WriteAllText(path, JsonConvert.SerializeObject(_store, Formatting.Indented));
            FuseDataPackageDiscovery.ResetDiscovery();
            FuseAssetPackRegistry.Reset();
        }

        private static FuseModSet FindSet(string setId)
        {
            return string.IsNullOrWhiteSpace(setId)
                ? null
                : _store.Sets.FirstOrDefault(set => string.Equals(set.Id, setId, StringComparison.OrdinalIgnoreCase));
        }

        private static FuseModSet CreateSetFromCurrentActiveModsNoLock()
        {
            var mods = GetVisibleProfileMods().ToArray();
            var now = DateTime.UtcNow.ToString("O");
            var set = CreateSetDefinition(
                "set-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"),
                NextSetName(),
                mods,
                now);

            _store.Sets.Add(set);
            _store.ActiveSetId = set.Id;
            return set;
        }

        internal static FuseModSet CreateSetDefinition(
            string id,
            string name,
            IEnumerable<FuseUmmModInfo> mods,
            string timestamp)
        {
            var candidates = (mods ?? Enumerable.Empty<FuseUmmModInfo>())
                .Where(mod => mod != null)
                .ToArray();
            return new FuseModSet
            {
                Id = id ?? string.Empty,
                Name = name ?? string.Empty,
                EnabledModIds = candidates.Select(mod => mod.Id)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                EnabledFolderNames = candidates.Select(mod => mod.FolderName)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                CreatedUtc = timestamp ?? string.Empty,
                UpdatedUtc = timestamp ?? string.Empty
            };
        }

        private static string NextSetName()
        {
            var index = 1;
            while (_store.Sets.Any(set => string.Equals(set.Name, "Server Set " + index, StringComparison.OrdinalIgnoreCase)))
            {
                index++;
            }

            return "Server Set " + index;
        }

        private static HashSet<string> ToMutableSet(IEnumerable<string> values)
        {
            return new HashSet<string>(
                values?.Where(value => !string.IsNullOrWhiteSpace(value)) ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);
        }

        internal static bool IsModEnabledInSet(FuseModSet set, FuseUmmModInfo mod)
        {
            if (set == null || mod == null)
            {
                return true;
            }

            return Contains(set.EnabledModIds, mod.Id) ||
                   Contains(set.EnabledFolderNames, mod.FolderName);
        }

        private static bool IsPackageMatched(FuseModSet set, string packageId, string folderPath)
        {
            var folderName = string.IsNullOrWhiteSpace(folderPath) ? string.Empty : Path.GetFileName(folderPath);
            return Contains(set.EnabledModIds, packageId) ||
                   Contains(set.EnabledFolderNames, folderName);
        }

        private static bool Contains(IEnumerable<string> values, string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   values != null &&
                   values.Any(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase));
        }
    }

    internal sealed class FuseModSet
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string[] EnabledModIds { get; set; } = Array.Empty<string>();
        public string[] EnabledFolderNames { get; set; } = Array.Empty<string>();
        public string CreatedUtc { get; set; } = string.Empty;
        public string UpdatedUtc { get; set; } = string.Empty;

        public FuseModSet Clone()
        {
            return new FuseModSet
            {
                Id = Id,
                Name = Name,
                EnabledModIds = EnabledModIds?.ToArray() ?? Array.Empty<string>(),
                EnabledFolderNames = EnabledFolderNames?.ToArray() ?? Array.Empty<string>(),
                CreatedUtc = CreatedUtc,
                UpdatedUtc = UpdatedUtc
            };
        }
    }

    internal sealed class FuseModSetStore
    {
        public string ActiveSetId { get; set; } = string.Empty;
        public List<FuseModSet> Sets { get; set; } = new List<FuseModSet>();
    }
}
