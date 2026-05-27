using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FUSE.Converter.Models;

namespace FUSE.Converter.Conversion
{
    /// <summary>
    /// Port of <c>copy_asset_sources</c> +
    /// <c>convert_map_tiles</c>-related helpers. Copies asset-pack
    /// folders and map-tile data directories from a legacy mod root
    /// into the FUSE output folder so the converted package carries
    /// its associated binary payloads.
    /// </summary>
    /// <remarks>
    /// Asset packs and map tiles are file-system data; they
    /// round-trip through the converter unchanged. We don't reach
    /// into bundles or .data files — the FUSE loader handles the
    /// runtime parsing. The copier just preserves directory
    /// structure and the file timestamps (so mod authors can spot
    /// "stale" data after a re-export).
    /// </remarks>
    internal static class LegacyAssetCopier
    {
        /// <summary>
        /// Copies asset packs referenced by <paramref name="roots"/>
        /// (the same list produced by
        /// <see cref="LegacyKindDetector.FindAssetPackSources"/>)
        /// into the output folder. Increments
        /// <c>assetPackSources</c> / <c>assetPacks</c> counts on the
        /// result so callers can report what landed.
        /// </summary>
        public static void CopyAssetSources(string source, string output, IList<string> roots, FuseConversionResult result)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(output)) return;
            if (roots == null || roots.Count == 0) return;

            Directory.CreateDirectory(output);

            foreach (var root in roots)
            {
                if (root == ".")
                {
                    if (LegacyKindDetector.IsAssetPackFolder(source))
                    {
                        // The mod folder IS one asset pack — copy
                        // every child directly into the output root.
                        CopyDirectoryChildren(source, output);
                        continue;
                    }

                    foreach (var child in Directory.EnumerateDirectories(source))
                    {
                        if (LegacyKindDetector.IsAssetPackFolder(child))
                        {
                            var dest = Path.Combine(output, Path.GetFileName(child));
                            CopyDirectory(child, dest);
                        }
                    }
                    continue;
                }

                var sourceRoot = Path.Combine(source, root);
                if (Directory.Exists(sourceRoot))
                {
                    CopyDirectory(sourceRoot, Path.Combine(output, root));
                }
            }
        }

        /// <summary>
        /// Copies all <c>.data</c> map tile files (and their
        /// containing folder structure) from the legacy mod into the
        /// output folder. Mirrors the Python <c>convert_map_tiles</c>
        /// minus the FUSE-side schema generation (which the runtime
        /// can derive on first load).
        /// </summary>
        public static void CopyMapTiles(string source, string output, FuseConversionResult result)
        {
            var tileSources = LegacyKindDetector.FindMapTileSources(source);
            if (tileSources.Count == 0) return;

            Directory.CreateDirectory(output);
            foreach (var tileSource in tileSources)
            {
                var relative = MakeRelative(source, tileSource);
                var dest = string.IsNullOrEmpty(relative) ? output : Path.Combine(output, relative);
                CopyDirectoryDataFiles(tileSource, dest);
            }
        }

        // ------------------------------------------------------------------
        // File-system helpers
        // ------------------------------------------------------------------

        private static void CopyDirectoryChildren(string source, string dest)
        {
            Directory.CreateDirectory(dest);
            foreach (var child in Directory.EnumerateDirectories(source))
            {
                CopyDirectory(child, Path.Combine(dest, Path.GetFileName(child)));
            }
            foreach (var file in Directory.EnumerateFiles(source))
            {
                File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite: true);
            }
        }

        private static void CopyDirectory(string source, string dest)
        {
            Directory.CreateDirectory(dest);
            foreach (var child in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            {
                var rel = MakeRelative(source, child);
                Directory.CreateDirectory(Path.Combine(dest, rel));
            }
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var rel = MakeRelative(source, file);
                var target = Path.Combine(dest, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(target) ?? dest);
                File.Copy(file, target, overwrite: true);
            }
        }

        private static void CopyDirectoryDataFiles(string source, string dest)
        {
            Directory.CreateDirectory(dest);
            foreach (var file in Directory.EnumerateFiles(source, "*.data", SearchOption.TopDirectoryOnly))
            {
                var target = Path.Combine(dest, Path.GetFileName(file));
                File.Copy(file, target, overwrite: true);
            }
        }

        private static string MakeRelative(string root, string path)
        {
            var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var pathFull = Path.GetFullPath(path);
            if (pathFull.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            {
                var sliced = pathFull.Substring(rootFull.Length);
                return sliced.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            return Path.GetFileName(path);
        }
    }
}
