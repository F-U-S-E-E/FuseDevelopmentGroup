using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FUSE.Authoring.Data;
using FUSE.Infrastructure;

namespace FUSE.Loading
{
    /// <summary>
    /// A selectable map registered by a map package. The map id is the owning
    /// package id. Entries whose map folder failed to resolve are kept with a
    /// <see cref="FaultReason"/> so diagnostics can show why a declared map is
    /// not launchable.
    /// </summary>
    public sealed class FuseRegisteredMap
    {
        internal FuseRegisteredMap(
            string mapId,
            string displayName,
            string description,
            string mapFolder,
            string faultReason,
            bool suppressBaseWorld = true,
            IEnumerable<string> progressionIds = null)
        {
            MapId = mapId ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            Description = description ?? string.Empty;
            MapFolder = mapFolder ?? string.Empty;
            FaultReason = faultReason ?? string.Empty;
            SuppressBaseWorld = suppressBaseWorld;
            ProgressionIds = (progressionIds ?? Enumerable.Empty<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public string MapId { get; }
        public string DisplayName { get; }
        public string Description { get; }

        /// <summary>Resolved absolute folder containing Map.json and tiles; empty when faulted.</summary>
        public string MapFolder { get; }

        public string FaultReason { get; }
        public bool IsValid => string.IsNullOrEmpty(FaultReason);
        public bool SuppressBaseWorld { get; }
        public IReadOnlyList<string> ProgressionIds { get; }
    }

    /// <summary>
    /// Registry of maps declared by loaded packages. Populated when definitions
    /// are registered with the loader (independent of runtime apply, so maps
    /// are listable from the main menu before any session exists) and cleared
    /// when their package unloads.
    /// </summary>
    public static class FuseMapPackageRegistry
    {
        private const string MapJsonFileName = "Map.json";

        private static readonly object Sync = new object();
        private static readonly Dictionary<string, FuseRegisteredMap> Maps =
            new Dictionary<string, FuseRegisteredMap>(StringComparer.OrdinalIgnoreCase);

        internal static void RegisterFromDefinition(FuseLoadedMod loaded)
        {
            var definition = loaded?.Definition;
            if (definition == null || string.IsNullOrWhiteSpace(definition.Id))
            {
                return;
            }

            if (definition.Map == null)
            {
                // A replaced definition may have dropped its map declaration.
                Unregister(definition.Id);
                return;
            }

            var progressionIds = definition.Progression?.Progressions?.Keys;
            var entry = BuildEntry(
                definition.Id,
                definition.Name,
                loaded.FolderPath,
                definition.Map,
                progressionIds);
            lock (Sync)
            {
                Maps[entry.MapId] = entry;
            }

            if (entry.IsValid)
            {
                FuseLog.Info($"FUSE registered map package='{entry.MapId}' displayName='{entry.DisplayName}' mapFolder='{entry.MapFolder}'.");
            }
            else
            {
                FuseLog.Warning($"FUSE registered map package='{entry.MapId}' as faulted: {entry.FaultReason}");
            }
        }

        internal static void Unregister(string packageId)
        {
            if (string.IsNullOrWhiteSpace(packageId))
            {
                return;
            }

            lock (Sync)
            {
                Maps.Remove(packageId);
            }
        }

        internal static void Clear()
        {
            lock (Sync)
            {
                Maps.Clear();
            }
        }

        public static IReadOnlyList<FuseRegisteredMap> GetRegisteredMaps()
        {
            lock (Sync)
            {
                return Maps.Values
                    .OrderBy(map => map.MapId, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
        }

        public static bool TryGetMap(string mapId, out FuseRegisteredMap map)
        {
            map = null;
            if (string.IsNullOrWhiteSpace(mapId))
            {
                return false;
            }

            lock (Sync)
            {
                return Maps.TryGetValue(mapId.Trim(), out map);
            }
        }

        internal static FuseRegisteredMap BuildEntry(
            string packageId,
            string packageName,
            string packageFolder,
            FuseMapDeclaration declaration,
            IEnumerable<string> progressionIds = null)
        {
            var displayName = FirstNonBlank(declaration?.DisplayName, packageName, packageId);
            var description = declaration?.Description ?? string.Empty;
            var suppressBaseWorld = declaration?.SuppressBaseWorld ?? true;

            if (string.IsNullOrWhiteSpace(declaration?.MapFolder))
            {
                return new FuseRegisteredMap(
                    packageId,
                    displayName,
                    description,
                    string.Empty,
                    "mapFolder is blank.",
                    suppressBaseWorld,
                    progressionIds);
            }

            if (!TryResolveMapFolder(packageFolder, declaration.MapFolder, out var resolved, out var error))
            {
                return new FuseRegisteredMap(
                    packageId,
                    displayName,
                    description,
                    string.Empty,
                    error,
                    suppressBaseWorld,
                    progressionIds);
            }

            var mapJsonPath = Path.Combine(resolved, MapJsonFileName);
            if (!File.Exists(mapJsonPath))
            {
                return new FuseRegisteredMap(
                    packageId, displayName, description, string.Empty,
                    $"{MapJsonFileName} not found in resolved map folder '{resolved}'.",
                    suppressBaseWorld,
                    progressionIds);
            }

            return new FuseRegisteredMap(
                packageId,
                displayName,
                description,
                resolved,
                null,
                suppressBaseWorld,
                progressionIds);
        }

        internal static bool TryResolveMapFolder(string packageFolder, string mapFolder, out string resolved, out string error)
        {
            resolved = string.Empty;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(mapFolder))
            {
                error = "mapFolder is blank.";
                return false;
            }

            if (Path.IsPathRooted(mapFolder))
            {
                error = $"mapFolder '{mapFolder}' must be package-relative, not rooted.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(packageFolder))
            {
                error = "package folder is unknown, so mapFolder cannot be resolved.";
                return false;
            }

            string packageRoot;
            string combined;
            try
            {
                packageRoot = Path.GetFullPath(packageFolder);
                combined = Path.GetFullPath(Path.Combine(packageRoot, mapFolder));
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                error = $"mapFolder '{mapFolder}' could not be resolved: {ex.Message}";
                return false;
            }

            var rootWithSeparator = packageRoot.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? packageRoot
                : packageRoot + Path.DirectorySeparatorChar;
            if (!combined.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            {
                error = $"mapFolder '{mapFolder}' resolves outside the package folder.";
                return false;
            }

            if (!Directory.Exists(combined))
            {
                error = $"mapFolder '{mapFolder}' does not exist (resolved to '{combined}').";
                return false;
            }

            resolved = combined;
            return true;
        }

        private static string FirstNonBlank(params string[] values)
        {
            for (var index = 0; index < values.Length; index++)
            {
                if (!string.IsNullOrWhiteSpace(values[index]))
                {
                    return values[index].Trim();
                }
            }

            return string.Empty;
        }
    }
}
