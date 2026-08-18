using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
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
        private static readonly HashSet<string> RecoveredPackageKeys =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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

                var id = entry.Info?.Id ?? Path.GetFileName(entry.Path);
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
                    FuseLog.Exception($"FUSE legacy UMM recovery failed for '{id}'", ex);
                }
            }

            return recovered;
        }

        internal static bool WasRecovered(string folderPath, string packageId)
        {
            return RecoveredPackageKeys.Contains(BuildPackageKey(folderPath, packageId));
        }

        internal static bool TryRecordRecovered(string folderPath, string packageId)
        {
            return RecoveredPackageKeys.Add(BuildPackageKey(folderPath, packageId));
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
                return File.Exists(assemblyPath) && ReferencesLegacyLoader(File.ReadAllBytes(assemblyPath));
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    $"FUSE could not inspect failed UMM assembly '{assemblyPath}' for legacy references: " +
                    ex.GetBaseException().Message);
                return false;
            }
        }

        internal static bool ReferencesLegacyLoader(byte[] assemblyBytes)
        {
            return ContainsAscii(assemblyBytes, "Railloader.Interchange") ||
                   ContainsAscii(assemblyBytes, "StrangeCustoms");
        }

        private static bool ContainsAscii(byte[] source, string value)
        {
            if (source == null || source.Length == 0 || string.IsNullOrEmpty(value))
            {
                return false;
            }

            var pattern = Encoding.ASCII.GetBytes(value);
            for (var offset = 0; offset <= source.Length - pattern.Length; offset++)
            {
                var matched = true;
                for (var index = 0; index < pattern.Length; index++)
                {
                    if (source[offset + index] == pattern[index])
                    {
                        continue;
                    }

                    matched = false;
                    break;
                }

                if (matched)
                {
                    return true;
                }
            }

            return false;
        }

        private static string BuildPackageKey(string folderPath, string packageId)
        {
            string normalizedPath;
            try
            {
                normalizedPath = Path.GetFullPath(folderPath ?? string.Empty)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                normalizedPath = (folderPath ?? string.Empty).Trim();
            }

            return normalizedPath + "|" + (packageId ?? string.Empty).Trim();
        }
    }
}
