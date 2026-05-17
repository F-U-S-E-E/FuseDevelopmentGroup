using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FUSE.Loading;
using UnityModManagerNet;

namespace FUSE.Infrastructure
{
    internal static class FuseUmmInjector
    {
        public const string LegacySupportId = "FUSE-LegacySupport";
        public const string LegacySupportDisplayName = "FUSE Legacy Support";
        public const string LegacySupportAuthor = "FUSE";
        // UMM's ModEntry.Toggleable returns `OnToggle != null || !HasAssembly`, and
        // HasAssembly returns true iff Info.AssemblyName or Info.EntryMethod is non-empty.
        // Setting both to point at the no-op below makes HasAssembly true; leaving OnToggle null
        // therefore makes Toggleable false, which hides the enable/disable toggle in UMM's UI.
        private const string SyntheticAssemblyName = "FUSE.dll";
        private const string SyntheticEntryMethod = "FUSE.Infrastructure.FuseUmmInjector.SyntheticEntryNoop";

        private static readonly FieldInfo ModEntriesField =
            typeof(UnityModManager).GetField("modEntries", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly HashSet<string> InjectedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static bool _ranOnce;

        public static void InjectLegacyEntries(string fuseModEntryPath, string fuseVersion)
        {
            if (!FuseSettings.ShowLegacyModsInUmm)
            {
                if (!_ranOnce)
                {
                    FuseLog.Info("FUSE legacy UMM visibility is disabled in settings; not injecting synthetic mod entries.");
                }
                _ranOnce = true;
                return;
            }

            _ranOnce = true;
            var entries = ModEntriesField?.GetValue(null) as IList;
            if (entries == null)
            {
                FuseLog.Warning("FUSE could not access UnityModManager.modEntries to inject synthetic legacy mod entries; UMM listing will not include legacy data packages.");
                return;
            }

            var supportInjected = TryInjectSupportEntry(entries, fuseModEntryPath, fuseVersion);
            var legacyInjected = TryInjectLegacyPackageEntries(entries);

            FuseLog.Info(
                $"FUSE UMM injection complete: legacySupportInjected={supportInjected} legacyPackagesInjected={legacyInjected} totalEntries={entries.Count}.");
        }

        private static bool TryInjectSupportEntry(IList entries, string fuseModEntryPath, string fuseVersion)
        {
            if (ContainsEntryWithId(entries, LegacySupportId))
            {
                return false;
            }

            var entry = TryCreateModEntry(
                id: LegacySupportId,
                displayName: LegacySupportDisplayName,
                author: LegacySupportAuthor,
                version: string.IsNullOrWhiteSpace(fuseVersion) ? "0.0.0" : fuseVersion,
                requirements: new[] { "FUSE" },
                folderPath: fuseModEntryPath ?? string.Empty);
            if (entry == null)
            {
                return false;
            }

            entries.Add(entry);
            InjectedIds.Add(LegacySupportId);
            FuseLog.Info($"FUSE injected synthetic UMM entry id='{LegacySupportId}' to advertise legacy-mod compatibility.");
            return true;
        }

        private static int TryInjectLegacyPackageEntries(IList entries)
        {
            var modsRoot = FuseDataPackageDiscovery.GetModsRoot();
            if (string.IsNullOrWhiteSpace(modsRoot) || !Directory.Exists(modsRoot))
            {
                FuseLog.Info("FUSE skipped legacy UMM injection because the Mods folder could not be located.");
                return 0;
            }

            string[] folders;
            try
            {
                folders = Directory.GetDirectories(modsRoot)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch (Exception ex)
            {
                FuseLog.Warning($"FUSE could not enumerate '{modsRoot}' to inject legacy UMM entries: {ex.GetBaseException().Message}");
                return 0;
            }

            var injectedCount = 0;
            foreach (var folder in folders)
            {
                if (!FuseLegacyDataConverter.TryReadLegacyManifest(folder, out var manifest))
                {
                    continue;
                }

                if (ContainsEntryForFolder(entries, folder) || ContainsEntryWithId(entries, manifest.PackageId))
                {
                    continue;
                }

                var entry = TryCreateModEntry(
                    id: manifest.PackageId,
                    displayName: string.IsNullOrWhiteSpace(manifest.DisplayName) ? manifest.LegacyId : manifest.DisplayName,
                    author: manifest.Author ?? string.Empty,
                    version: manifest.Version ?? string.Empty,
                    requirements: new[] { LegacySupportId },
                    folderPath: folder);
                if (entry == null)
                {
                    continue;
                }

                entries.Add(entry);
                InjectedIds.Add(manifest.PackageId);
                injectedCount++;
                FuseLog.Info(
                    $"FUSE injected synthetic UMM entry id='{manifest.PackageId}' " +
                    $"displayName='{manifest.DisplayName}' folder='{Path.GetFileName(folder)}'.");
            }

            return injectedCount;
        }

        private static object TryCreateModEntry(
            string id,
            string displayName,
            string author,
            string version,
            string[] requirements,
            string folderPath)
        {
            try
            {
                var info = new UnityModManager.ModInfo
                {
                    Id = id ?? string.Empty,
                    DisplayName = string.IsNullOrWhiteSpace(displayName) ? id : displayName,
                    Author = author ?? string.Empty,
                    Version = version ?? string.Empty,
                    ManagerVersion = string.Empty,
                    GameVersion = string.Empty,
                    Requirements = requirements ?? Array.Empty<string>(),
                    LoadAfter = Array.Empty<string>(),
                    AssemblyName = SyntheticAssemblyName,
                    EntryMethod = SyntheticEntryMethod,
                    HomePage = string.Empty,
                    Repository = string.Empty,
                    ContentType = string.Empty
                };

                var entry = new UnityModManager.ModEntry(info, folderPath ?? string.Empty);
                entry.Enabled = true;
                SetPrivateBool(entry, "mStarted", true);
                SetPrivateBool(entry, "mActive", true);
                SetPrivateBool(entry, "mFirstLoading", false);
                SetPrivateBool(entry, "mErrorOnLoading", false);
                return entry;
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    $"FUSE could not construct synthetic UMM mod entry id='{id}' folder='{folderPath ?? string.Empty}': " +
                    $"{ex.GetBaseException().Message}");
                return null;
            }
        }

        private static bool ContainsEntryWithId(IList entries, string id)
        {
            if (entries == null || string.IsNullOrWhiteSpace(id))
            {
                return false;
            }

            foreach (var entry in entries)
            {
                if (entry == null)
                {
                    continue;
                }

                var info = entry.GetType().GetField("Info", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(entry);
                var entryId = info?.GetType().GetField("Id", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(info) as string;
                if (string.Equals(entryId, id, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsEntryForFolder(IList entries, string folderPath)
        {
            if (entries == null || string.IsNullOrWhiteSpace(folderPath))
            {
                return false;
            }

            var normalized = NormalizePath(folderPath);
            foreach (var entry in entries)
            {
                if (entry == null)
                {
                    continue;
                }

                var pathField = entry.GetType().GetField("Path", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var entryPath = pathField?.GetValue(entry) as string;
                if (string.IsNullOrWhiteSpace(entryPath))
                {
                    continue;
                }

                if (string.Equals(NormalizePath(entryPath), normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
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

        public static bool SyntheticEntryNoop(UnityModManager.ModEntry modEntry)
        {
            FuseLog.Info(
                $"FUSE synthetic UMM entry no-op invoked for id='{modEntry?.Info?.Id ?? "<unknown>"}'; " +
                "FUSE manages legacy data packages internally and the synthetic UMM row carries no assembly.");
            return true;
        }

        private static void SetPrivateBool(object instance, string fieldName, bool value)
        {
            if (instance == null || string.IsNullOrWhiteSpace(fieldName))
            {
                return;
            }

            var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field != null && field.FieldType == typeof(bool))
            {
                field.SetValue(instance, value);
            }
        }
    }
}
