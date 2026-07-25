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
        internal const bool SyntheticEntriesAreActive = false;
        internal const bool SyntheticLegacyPackagesAreEnabled = true;
        // Synthetic rows have no real assembly. Leaving Info.AssemblyName / Info.EntryMethod
        // empty makes ModEntry.HasAssembly return false; combined with pre-setting mStarted,
        // ModEntry.Loaded short-circuits to true so UMM's Load() returns at its first line
        // without trying to resolve a DLL beside the legacy mod folder.

        private static readonly FieldInfo ModEntriesField =
            typeof(UnityModManager).GetField("modEntries", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly string[] LegacySupportRequirements = { "FUSE" };

        private static readonly HashSet<string> InjectedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static bool _ranOnce;
        private static string _pendingFuseModEntryPath;
        private static string _pendingFuseVersion;
        private static bool _injectionPending;

        // Defers UMM modEntries mutation until after UnityModManager._Start's foreach
        // finishes; mutating the list mid-iteration throws InvalidOperationException
        // from List<T>.Enumerator and aborts the rest of UMM's mod loading.
        public static void ScheduleInjection(string fuseModEntryPath, string fuseVersion)
        {
            _pendingFuseModEntryPath = fuseModEntryPath;
            _pendingFuseVersion = fuseVersion;
            _injectionPending = true;
        }

        public static void FlushPendingInjection()
        {
            if (!_injectionPending)
            {
                return;
            }

            _injectionPending = false;
            var path = _pendingFuseModEntryPath;
            var version = _pendingFuseVersion;
            _pendingFuseModEntryPath = null;
            _pendingFuseVersion = null;
            InjectLegacyEntries(path, version);

            // The flush runs after UMM's _Start finished loading every real mod
            // entry, i.e. the moment the UMM mod population is complete — drop
            // the exception-attribution cache so its next lazy rebuild sees
            // them all.
            FuseModAttributionMap.Invalidate();
        }

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
                requirements: LegacySupportRequirements,
                folderPath: fuseModEntryPath ?? string.Empty,
                enabled: true);
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
                    folderPath: folder,
                    enabled: SyntheticLegacyPackagesAreEnabled);
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

        private static UnityModManagerNet.UnityModManager.ModEntry TryCreateModEntry(
            string id,
            string displayName,
            string author,
            string version,
            string[] requirements,
            string folderPath,
            bool enabled)
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
                    AssemblyName = string.Empty,
                    EntryMethod = string.Empty,
                    HomePage = string.Empty,
                    Repository = string.Empty,
                    ContentType = string.Empty
                };

                var entry = new UnityModManager.ModEntry(info, folderPath ?? string.Empty);
                // Keep the row enabled so UMM does not persist a false value for this package id;
                // that persisted state could later disable a real UMM mod installed under the
                // same id. Package rows are still inactive metadata (below), so UMM dispatches no
                // frame callbacks. If AssetLoader inspects enabled rows, FUSE's physical-path
                // store reuse prevents it from mounting the same folder a second time.
                entry.Enabled = enabled;
                SetPrivateBool(entry, "mStarted", true);
                // These rows are display metadata, not executable UMM mods. Keeping
                // them inactive prevents UMM from dispatching Update/FixedUpdate/
                // LateUpdate/hotkey work to every synthetic package when visibility
                // is explicitly enabled.
                SetPrivateBool(entry, "mActive", SyntheticEntriesAreActive);
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
