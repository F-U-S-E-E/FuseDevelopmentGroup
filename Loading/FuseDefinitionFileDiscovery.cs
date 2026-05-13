using System;
using System.IO;
using System.Linq;

namespace FUSE.Loading
{
    internal static class FuseDefinitionFileDiscovery
    {
        private static readonly string[] ExcludedFallbackJsonFileNames =
        {
            "Info.json",
            "conversion-report.json",
            "Catalog.json",
            "Definitions.json",
            "Definition.json"
        };

        public static string[] ResolveFallbackDefinitionPaths(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                return Array.Empty<string>();
            }

            var bsonFiles = Directory.GetFiles(folderPath, "*.bson", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (bsonFiles.Length > 0)
            {
                return new[] { bsonFiles[0] };
            }

            var jsonFiles = Directory.GetFiles(folderPath, "*.json", SearchOption.TopDirectoryOnly)
                .Where(IsFallbackDefinitionJsonFile)
                .OrderBy(GetJsonFallbackRank)
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return jsonFiles.Length > 0 ? new[] { jsonFiles[0] } : Array.Empty<string>();
        }

        public static bool HasFallbackDefinitionFile(string folderPath)
        {
            return ResolveFallbackDefinitionPaths(folderPath).Length > 0;
        }

        private static bool IsFallbackDefinitionJsonFile(string path)
        {
            var fileName = Path.GetFileName(path);
            if (string.IsNullOrWhiteSpace(fileName) ||
                !string.Equals(Path.GetExtension(fileName), ".json", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return !ExcludedFallbackJsonFileNames.Any(excluded =>
                string.Equals(fileName, excluded, StringComparison.OrdinalIgnoreCase));
        }

        private static int GetJsonFallbackRank(string path)
        {
            var fileName = Path.GetFileName(path);
            return fileName != null && fileName.EndsWith(".fuse.json", StringComparison.OrdinalIgnoreCase)
                ? 0
                : 1;
        }
    }
}
