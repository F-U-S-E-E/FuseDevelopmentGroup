using System;
using System.Collections;
using System.IO;
using System.Reflection;
using FUSE.Infrastructure;
using UnityModManagerNet;

namespace FUSE.Compatibility
{
    /// <summary>
    /// Recovers dual-format mods whose UMM entry was loaded before FUSE and
    /// failed while resolving a legacy Railloader assembly. UMM leaves the
    /// rejected assembly assigned to the entry, so an ordinary Load retry
    /// immediately returns the cached failure. FUSE clears that failed state
    /// and asks UMM to use its normal uniquely-renamed reload path after the
    /// legacy assembly resolver has been installed.
    /// </summary>
    internal static class FuseLegacyUmmRecovery
    {
        private static readonly FieldInfo ModEntriesField =
            typeof(UnityModManager).GetField("modEntries", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo AssemblyField =
            typeof(UnityModManager.ModEntry).GetField("mAssembly", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo FirstLoadingField =
            typeof(UnityModManager.ModEntry).GetField("mFirstLoading", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo ErrorOnLoadingField =
            typeof(UnityModManager.ModEntry).GetField("mErrorOnLoading", BindingFlags.Instance | BindingFlags.NonPublic);
        internal static int RecoverFailedEntries()
        {
            if (ModEntriesField == null || AssemblyField == null || FirstLoadingField == null || ErrorOnLoadingField == null)
            {
                FuseLog.Warning("FUSE legacy UMM recovery is unavailable because this Unity Mod Manager build has an unsupported ModEntry layout.");
                return 0;
            }

            var entries = ModEntriesField.GetValue(null) as IEnumerable;
            if (entries == null)
            {
                return 0;
            }

            var recovered = 0;
            foreach (var value in entries)
            {
                if (!(value is UnityModManager.ModEntry entry) || !IsRecoveryCandidate(entry))
                {
                    continue;
                }

                var id = string.IsNullOrWhiteSpace(entry.Info?.Id)
                    ? FuseRecoveredPackageRegistry.FolderName(entry.Path)
                    : entry.Info.Id;
                var previousAssembly = AssemblyField.GetValue(entry);
                var previousErrorOnLoading = ErrorOnLoadingField.GetValue(entry);
                var previousFirstLoading = FirstLoadingField.GetValue(entry);
                try
                {
                    // The failed Assembly.LoadFrom result is still attached to
                    // the entry. Clear it and force UMM's reload branch, which
                    // gives the replacement assembly a unique identity and
                    // avoids the CLR's cached TypeLoadException.
                    AssemblyField.SetValue(entry, null);
                    ErrorOnLoadingField.SetValue(entry, false);
                    FirstLoadingField.SetValue(entry, false);

                    if (!entry.Load() || entry.ErrorOnLoading || !entry.Loaded)
                    {
                        RestoreEntryState(
                            entry,
                            previousAssembly,
                            previousErrorOnLoading,
                            previousFirstLoading);
                        FuseLog.Warning(
                            $"FUSE could not recover legacy UMM mod '{id}' after installing compatibility shims; " +
                            "the mod remains inactive for this session.");
                        continue;
                    }

                    TryRecordRecovered(entry.Path, id);
                    recovered++;
                    FuseLog.Info(
                        $"FUSE recovered legacy UMM mod '{id}' after its early Railloader assembly-resolution failure. " +
                        "Its UMM entry is now active; FUSE will not host its legacy plugin class a second time.");
                }
                catch (Exception ex)
                {
                    RestoreEntryState(
                        entry,
                        previousAssembly,
                        previousErrorOnLoading,
                        previousFirstLoading);
                    FuseLog.Exception($"FUSE legacy UMM recovery failed for '{id}'", ex);
                }
            }

            return recovered;
        }

        internal static bool WasRecovered(string folderPath, string packageId)
        {
            return FuseRecoveredPackageRegistry.WasRecovered(folderPath, packageId);
        }

        internal static bool TryRecordRecovered(string folderPath, string packageId)
        {
            return FuseRecoveredPackageRegistry.TryRecord(folderPath, packageId);
        }

        internal static void Reset()
        {
            FuseRecoveredPackageRegistry.Reset();
        }

        private static bool IsRecoveryCandidate(UnityModManager.ModEntry entry)
        {
            if (entry == null ||
                !entry.Enabled ||
                !entry.ErrorOnLoading ||
                entry.Started ||
                !entry.HasAssembly ||
                entry.Assembly == null ||
                string.IsNullOrWhiteSpace(entry.Info?.EntryMethod) ||
                string.Equals(entry.Info?.Id, "FUSE", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var assemblyPath = Path.Combine(entry.Path ?? string.Empty, entry.Info.AssemblyName ?? string.Empty);
            try
            {
                return File.Exists(assemblyPath) &&
                       FuseLegacyLoaderReferenceScanner.ReferencesLegacyLoader(File.ReadAllBytes(assemblyPath));
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    $"FUSE could not inspect failed UMM assembly '{assemblyPath}' for legacy references: " +
                    ex.GetBaseException().Message);
                return false;
            }
        }

        private static void RestoreEntryState(
            UnityModManager.ModEntry entry,
            object assembly,
            object errorOnLoading,
            object firstLoading)
        {
            AssemblyField.SetValue(entry, assembly);
            ErrorOnLoadingField.SetValue(entry, errorOnLoading);
            FirstLoadingField.SetValue(entry, firstLoading);
        }
    }
}
