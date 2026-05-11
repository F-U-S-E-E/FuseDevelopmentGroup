using System;
using System.IO;

namespace FUSE.Infrastructure
{
    internal static class FusePathSafety
    {
        public static bool TryResolvePackageRelativePath(string packageRoot, string relativePath, out string fullPath)
        {
            fullPath = string.Empty;
            if (string.IsNullOrWhiteSpace(packageRoot) || string.IsNullOrWhiteSpace(relativePath))
            {
                return false;
            }

            var trimmed = relativePath.Trim();
            if (Path.IsPathRooted(trimmed))
            {
                return false;
            }

            try
            {
                var root = Path.GetFullPath(packageRoot);
                var candidate = Path.GetFullPath(Path.Combine(root, trimmed));
                if (!IsUnderRoot(root, candidate))
                {
                    return false;
                }

                fullPath = candidate;
                return true;
            }
            catch
            {
                fullPath = string.Empty;
                return false;
            }
        }

        public static bool IsUnderRoot(string rootPath, string candidatePath)
        {
            if (string.IsNullOrWhiteSpace(rootPath) || string.IsNullOrWhiteSpace(candidatePath))
            {
                return false;
            }

            try
            {
                var root = Path.GetFullPath(rootPath);
                var candidate = Path.GetFullPath(candidatePath);
                if (string.Equals(root, candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                var rootWithSeparator = AppendDirectorySeparatorChar(root);
                return candidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static string AppendDirectorySeparatorChar(string path)
        {
            if (string.IsNullOrEmpty(path) ||
                path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ||
                path.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            {
                return path;
            }

            return path + Path.DirectorySeparatorChar;
        }
    }
}
