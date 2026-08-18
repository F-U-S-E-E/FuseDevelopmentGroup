using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FUSE.Compatibility
{
    /// <summary>
    /// Pure byte-level detection used before retrying a failed legacy UMM entry.
    /// This type deliberately has no Unity or Unity Mod Manager dependency so it
    /// can be exercised safely by the game-free test lane.
    /// </summary>
    internal static class FuseLegacyLoaderReferenceScanner
    {
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
    }

    /// <summary>
    /// Tracks legacy packages whose UMM entry FUSE recovered. The normalized
    /// folder is authoritative because UMM Info.json and legacy Definition.json
    /// can legitimately declare different identifiers for the same package.
    /// </summary>
    internal static class FuseRecoveredPackageRegistry
    {
        private static readonly object Gate = new object();
        private static readonly HashSet<string> FolderPaths =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> PackageIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        internal static bool TryRecord(string folderPath, string packageId)
        {
            var normalizedPath = NormalizeFolderPath(folderPath);
            var normalizedId = NormalizePackageId(packageId);
            lock (Gate)
            {
                var added = false;
                if (!string.IsNullOrEmpty(normalizedPath))
                {
                    added |= FolderPaths.Add(normalizedPath);
                }

                if (!string.IsNullOrEmpty(normalizedId))
                {
                    added |= PackageIds.Add(normalizedId);
                }

                return added;
            }
        }

        internal static bool WasRecovered(string folderPath, string packageId)
        {
            var normalizedPath = NormalizeFolderPath(folderPath);
            var normalizedId = NormalizePackageId(packageId);
            lock (Gate)
            {
                if (!string.IsNullOrEmpty(normalizedPath))
                {
                    if (FolderPaths.Contains(normalizedPath))
                    {
                        return true;
                    }
                }

                return !string.IsNullOrEmpty(normalizedId) && PackageIds.Contains(normalizedId);
            }
        }

        internal static string FolderName(string folderPath)
        {
            var normalizedPath = NormalizeFolderPath(folderPath);
            return string.IsNullOrEmpty(normalizedPath)
                ? string.Empty
                : Path.GetFileName(normalizedPath);
        }

        internal static void Reset()
        {
            lock (Gate)
            {
                FolderPaths.Clear();
                PackageIds.Clear();
            }
        }

        private static string NormalizeFolderPath(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                return string.Empty;
            }

            try
            {
                return Path.GetFullPath(folderPath.Trim())
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return folderPath.Trim()
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
        }

        private static string NormalizePackageId(string packageId)
        {
            return (packageId ?? string.Empty).Trim();
        }
    }
}
