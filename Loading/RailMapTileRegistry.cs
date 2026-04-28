using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Map.Runtime;
using RAIL.Data;
using RAIL.Infrastructure;
using RAIL.Serialization;
using UnityEngine;

namespace RAIL.Loading
{
    internal static class RailMapTileRegistry
    {
        private static readonly object Sync = new object();
        private static readonly FieldInfo DescriptorsField = typeof(MapStore).GetField("_descriptors", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo StoreField = typeof(MapManager).GetField("_store", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly Dictionary<string, RegisteredTileSource> Sources = new Dictionary<string, RegisteredTileSource>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<Vector2Int, string> ActiveTilePaths = new Dictionary<Vector2Int, string>();

        private static string _activeDirectoryName = string.Empty;

        internal static void RefreshFromAvailablePackages()
        {
            lock (Sync)
            {
                Sources.Clear();
                ActiveTilePaths.Clear();
                _activeDirectoryName = string.Empty;

                var modsRoot = RailDataPackageDiscovery.GetModsRoot();
                if (string.IsNullOrWhiteSpace(modsRoot) || !Directory.Exists(modsRoot))
                {
                    return;
                }

                foreach (var packagePath in RailDataPackageDiscovery.DiscoverPackageFolders(modsRoot))
                {
                    try
                    {
                        var definitionPath = RailModLoader.ResolveDefinitionPath(packagePath);
                        var definition = RailSerializer.Load(definitionPath);
                        RegisterTileSourcesUnsafe(definition?.Id ?? Path.GetFileName(packagePath), packagePath, definition?.World);
                    }
                    catch (Exception ex)
                    {
                        RailLog.Exception($"Failed to inspect map tile definitions in '{packagePath}'", ex);
                    }
                }
            }
        }

        internal static void RegisterTileSources(string modId, string modFolder, RailWorldDefinition world)
        {
            lock (Sync)
            {
                RemoveTileSourcesUnsafe(modId);
                RegisterTileSourcesUnsafe(modId, modFolder, world);
            }

            MountForActiveMapIfLoaded();
        }

        internal static void UnregisterTileSources(string modId)
        {
            lock (Sync)
            {
                RemoveTileSourcesUnsafe(modId);
            }
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
                    RailLog.Warning("RAIL could not access MapStore tile descriptors.");
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
                        RailLog.Warning($"RAIL map tile folder was not found: {source.ResolvedFolder}");
                        continue;
                    }

                    foreach (var tilePath in Directory.GetFiles(source.ResolvedFolder, "*.data", SearchOption.TopDirectoryOnly))
                    {
                        if (!TryParseTilePosition(tilePath, out var tilePosition))
                        {
                            continue;
                        }

                        descriptors[tilePosition] = new TileDescriptor(tilePosition, TileDescriptorStatus.Real);
                        ActiveTilePaths[tilePosition] = tilePath;
                        mountedCount++;
                    }
                }

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
            }
        }

        internal static void ClearAll()
        {
            lock (Sync)
            {
                Sources.Clear();
                ActiveTilePaths.Clear();
                _activeDirectoryName = string.Empty;
            }
        }

        private static void MountForActiveMapIfLoaded()
        {
            var mapManager = MapManager.Instance;
            if (mapManager == null)
            {
                return;
            }

            var store = StoreField?.GetValue(mapManager) as MapStore;
            if (store == null)
            {
                return;
            }

            var directoryName = mapManager.directoryName;
            try
            {
                var mountedCount = MountIntoStore(store, directoryName);
                if (mountedCount > 0)
                {
                    RailLog.Info($"Mounted {mountedCount} RAIL map tile(s) for '{directoryName}'.");
                }
            }
            catch (Exception ex)
            {
                RailLog.Exception("RAIL failed to mount map tiles for the active map.", ex);
            }
        }

        private static void RegisterTileSourcesUnsafe(string modId, string modFolder, RailWorldDefinition world)
        {
            if (world?.MapTiles == null || world.MapTiles.Count == 0)
            {
                return;
            }

            foreach (var tileSource in world.MapTiles)
            {
                if (tileSource.Value == null)
                {
                    continue;
                }

                var resolvedFolder = ResolveSourceFolder(modFolder, tileSource.Value.SourceFolder);
                if (string.IsNullOrWhiteSpace(resolvedFolder))
                {
                    RailLog.Warning($"Skipping map tile source '{tileSource.Key}' in '{modId}' because its sourceFolder could not be resolved.");
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
            }
        }

        private static void RemoveTileSourcesUnsafe(string modId)
        {
            if (string.IsNullOrWhiteSpace(modId))
            {
                return;
            }

            var keys = Sources.Keys
                .Where(key => key.StartsWith(modId + "::", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            for (var index = 0; index < keys.Length; index++)
            {
                Sources.Remove(keys[index]);
            }
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
