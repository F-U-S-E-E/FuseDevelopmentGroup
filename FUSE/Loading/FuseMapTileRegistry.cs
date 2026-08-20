using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Map.Runtime;
using FUSE.Authoring.Data;
using FUSE.Infrastructure;
using UnityEngine;

namespace FUSE.Loading
{
    internal static class FuseMapTileRegistry
    {
        private static readonly object Sync = new object();
        private static readonly FieldInfo DescriptorsField = typeof(MapStore).GetField("_descriptors", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo StoreField = typeof(MapManager).GetField("_store", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly Dictionary<string, RegisteredTileSource> Sources = new Dictionary<string, RegisteredTileSource>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<Vector2Int, string> ActiveTilePaths = new Dictionary<Vector2Int, string>();

        private static string _activeDirectoryName = string.Empty;
        private static int _sourceVersion;
        private static int _mountedSourceVersion = -1;

        internal static void RegisterTileSources(string modId, string modFolder, FuseWorldDefinition world)
        {
            lock (Sync)
            {
                var removed = RemoveTileSourcesUnsafe(modId);
                var added = RegisterTileSourcesUnsafe(modId, modFolder, world);
                if (removed > 0 || added > 0)
                {
                    _sourceVersion++;
                }
            }
        }

        internal static void UnregisterTileSources(string modId)
        {
            lock (Sync)
            {
                if (RemoveTileSourcesUnsafe(modId) > 0)
                {
                    _sourceVersion++;
                }
            }
        }

        internal static bool RegisterTileSource(
            string modId,
            string modFolder,
            string sourceId,
            FuseMapTileSource tileSource)
        {
            if (string.IsNullOrWhiteSpace(modId)
                || string.IsNullOrWhiteSpace(sourceId)
                || tileSource == null)
            {
                return false;
            }

            string resolvedFolder;
            try
            {
                resolvedFolder = ResolveSourceFolder(
                    modFolder,
                    tileSource.SourceFolder);
            }
            catch (Exception ex)
            {
                FuseLog.Exception(
                    $"Skipping map tile source '{sourceId}' in '{modId}' "
                    + "because its sourceFolder threw while resolving",
                    ex);
                return false;
            }

            if (string.IsNullOrWhiteSpace(resolvedFolder))
            {
                FuseLog.Warning(
                    $"Skipping map tile source '{sourceId}' in '{modId}' "
                    + "because its sourceFolder could not be resolved.");
                return false;
            }

            lock (Sync)
            {
                var key = BuildSourceKey(modId, sourceId);
                Sources[key] = new RegisteredTileSource
                {
                    SourceKey = key,
                    ModId = modId,
                    DirectoryName = NormalizeDirectoryName(
                        tileSource.Directory),
                    ResolvedFolder = resolvedFolder,
                    Priority = tileSource.Priority
                };
                _sourceVersion++;
            }

            return true;
        }

        internal static int MountIntoStore(MapStore store, string directoryName)
        {
            if (store == null)
            {
                return 0;
            }

            var normalizedDirectory = NormalizeDirectoryName(directoryName);
            lock (Sync)
            {
                ActiveTilePaths.Clear();
                _activeDirectoryName = normalizedDirectory;

                var descriptors = DescriptorsField?.GetValue(store) as IDictionary<Vector2Int, TileDescriptor>;
                if (descriptors == null)
                {
                    FuseLog.Warning("FUSE could not access MapStore tile descriptors.");
                    return 0;
                }

                var mountedCount = 0;
                foreach (var source in Sources.Values
                    .Where(source => string.Equals(source.DirectoryName, normalizedDirectory, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(source => source.Priority)
                    .ThenBy(source => source.SourceKey, StringComparer.OrdinalIgnoreCase))
                {
                    if (!Directory.Exists(source.ResolvedFolder))
                    {
                        FuseLog.Warning($"FUSE map tile folder was not found: {source.ResolvedFolder}");
                        continue;
                    }

                    string[] tilePaths;
                    try
                    {
                        tilePaths = Directory.GetFiles(source.ResolvedFolder, "*.data", SearchOption.TopDirectoryOnly);
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                        FuseLog.Exception($"FUSE could not enumerate map tiles in '{source.ResolvedFolder}'", ex);
                        continue;
                    }

                    foreach (var tilePath in tilePaths)
                    {
                        if (!TryParseTilePosition(tilePath, out var tilePosition))
                        {
                            FuseLog.Warning($"FUSE ignored map tile with unexpected filename '{tilePath}'.");
                            continue;
                        }

                        descriptors[tilePosition] = new TileDescriptor(tilePosition, TileDescriptorStatus.Real);
                        ActiveTilePaths[tilePosition] = tilePath;
                        mountedCount++;
                    }
                }

                _mountedSourceVersion = _sourceVersion;
                return mountedCount;
            }
        }

        internal static bool TryGetMountedTilePath(Vector2Int tilePosition, out string tilePath)
        {
            lock (Sync)
            {
                return ActiveTilePaths.TryGetValue(tilePosition, out tilePath);
            }
        }

        internal static void ClearActiveTilePaths()
        {
            lock (Sync)
            {
                ActiveTilePaths.Clear();
                _activeDirectoryName = string.Empty;
                _mountedSourceVersion = -1;
            }
        }

        internal static void ClearAll()
        {
            lock (Sync)
            {
                Sources.Clear();
                ActiveTilePaths.Clear();
                _activeDirectoryName = string.Empty;
                _sourceVersion++;
                _mountedSourceVersion = -1;
            }
        }

        internal static int MountForActiveMapIfLoaded(string reason)
        {
            var mapManager = MapManager.Instance;
            if (mapManager == null)
            {
                return 0;
            }

            var store = StoreField?.GetValue(mapManager) as MapStore;
            if (store == null)
            {
                return 0;
            }

            var directoryName = mapManager.directoryName;
            lock (Sync)
            {
                if (_mountedSourceVersion == _sourceVersion &&
                    string.Equals(_activeDirectoryName, NormalizeDirectoryName(directoryName), StringComparison.OrdinalIgnoreCase))
                {
                    return 0;
                }
            }

            try
            {
                var mountedCount = MountIntoStore(store, directoryName);
                if (mountedCount > 0)
                {
                    FuseLog.Info(
                        $"Mounted {mountedCount} FUSE map tile(s) for '{directoryName}' " +
                        $"reason='{reason ?? "unspecified"}'.");
                }

                return mountedCount;
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE failed to mount map tiles for the active map.", ex);
                return 0;
            }
        }

        private static int RegisterTileSourcesUnsafe(string modId, string modFolder, FuseWorldDefinition world)
        {
            if (world?.MapTiles == null || world.MapTiles.Count == 0)
            {
                return 0;
            }

            var added = 0;
            foreach (var tileSource in world.MapTiles)
            {
                if (tileSource.Value == null)
                {
                    continue;
                }

                string resolvedFolder;
                try
                {
                    resolvedFolder = ResolveSourceFolder(modFolder, tileSource.Value.SourceFolder);
                }
                catch (Exception ex)
                {
                    FuseLog.Exception($"Skipping map tile source '{tileSource.Key}' in '{modId}' because its sourceFolder threw while resolving", ex);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(resolvedFolder))
                {
                    FuseLog.Warning($"Skipping map tile source '{tileSource.Key}' in '{modId}' because its sourceFolder could not be resolved.");
                    continue;
                }

                var key = BuildSourceKey(modId, tileSource.Key);
                Sources[key] = new RegisteredTileSource
                {
                    SourceKey = key,
                    ModId = modId ?? string.Empty,
                    DirectoryName = NormalizeDirectoryName(tileSource.Value.Directory),
                    ResolvedFolder = resolvedFolder,
                    Priority = tileSource.Value.Priority
                };
                added++;
            }

            return added;
        }

        private static int RemoveTileSourcesUnsafe(string modId)
        {
            if (string.IsNullOrWhiteSpace(modId))
            {
                return 0;
            }

            var keys = Sources.Keys
                .Where(key => key.StartsWith(modId + "::", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            for (var index = 0; index < keys.Length; index++)
            {
                Sources.Remove(keys[index]);
            }

            return keys.Length;
        }

        private static string ResolveSourceFolder(string modFolder, string sourceFolder)
        {
            if (string.IsNullOrWhiteSpace(sourceFolder))
            {
                return string.Empty;
            }

            if (Path.IsPathRooted(sourceFolder))
            {
                return Path.GetFullPath(sourceFolder);
            }

            if (string.IsNullOrWhiteSpace(modFolder))
            {
                return string.Empty;
            }

            var packageRoot = Path.GetFullPath(modFolder);
            var resolved = Path.GetFullPath(Path.Combine(packageRoot, sourceFolder));
            return resolved.StartsWith(packageRoot, StringComparison.OrdinalIgnoreCase)
                ? resolved
                : string.Empty;
        }

        private static bool TryParseTilePosition(string tilePath, out Vector2Int tilePosition)
        {
            tilePosition = default(Vector2Int);

            var parts = Path.GetFileNameWithoutExtension(tilePath).Split('_');
            if (parts.Length != 3 || !string.Equals(parts[0], "tile", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!int.TryParse(parts[1], out var x) || !int.TryParse(parts[2], out var y))
            {
                return false;
            }

            tilePosition = new Vector2Int(x, y);
            return true;
        }

        private static string BuildSourceKey(string modId, string sourceId)
        {
            return (modId ?? string.Empty) + "::" + (sourceId ?? string.Empty);
        }

        private static string NormalizeDirectoryName(string directoryName)
        {
            return (directoryName ?? string.Empty).Trim();
        }

        private sealed class RegisteredTileSource
        {
            public string SourceKey { get; set; }
            public string ModId { get; set; }
            public string DirectoryName { get; set; }
            public string ResolvedFolder { get; set; }
            public int Priority { get; set; }
        }
    }
}
