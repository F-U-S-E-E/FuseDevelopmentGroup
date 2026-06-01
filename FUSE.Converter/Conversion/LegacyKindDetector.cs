using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace FUSE.Converter.Conversion
{
    /// <summary>
    /// Detects what kind of legacy package the input folder/file
    /// represents (route, audio, asset pack, map tile, archive,
    /// unknown). Port of <c>detect_kind</c> +
    /// <c>detect_direct_kind</c> from fuse_converter.py.
    /// </summary>
    /// <remarks>
    /// "route" is the everything-else fallback — covers tracks,
    /// industries, scenery, splineys, etc. "audio" is detected by
    /// either a manifest mixinto that targets a known audio table
    /// (whistles/horns/bells/hellsbells), or a JSON file whose
    /// contents look like audio data. "asset" detects FUSE asset
    /// packs by their bundle + Catalog.json + Definitions.json
    /// triple. "map_tile" detects .data files (Map Tiles, AlinasMapMod
    /// shipped per-tile binary data) at the root or under Maps/.
    /// </remarks>
    internal static class LegacyKindDetector
    {
        /// <summary>Top-level kind names matching the Python source.</summary>
        public static class Kinds
        {
            public const string Route = "route";
            public const string Audio = "audio";
            public const string Asset = "asset";
            public const string Archive = "archive";
            public const string Unknown = "unknown";
        }

        private static readonly HashSet<string> JsonManifestNames =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Definition.json", "Info.json" };

        private static readonly HashSet<string> LegacyDataKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "tracks", "areas", "industries", "loads", "turntables",
            "scenery", "splineys", "mandelas", "texts", "simpleGraphs",
            "progression", "progressions", "mapFeatures",
        };

        /// <summary>
        /// Returns the detected kind. When <paramref name="requested"/>
        /// is anything other than "auto", the explicit request takes
        /// precedence (mirrors Python's CLI <c>--kind</c> override).
        /// </summary>
        public static string DetectKind(string sourcePath, string requested)
        {
            if (string.IsNullOrEmpty(sourcePath)) return Kinds.Unknown;

            if (!string.IsNullOrEmpty(requested) && requested != "auto")
            {
                return requested;
            }

            if (File.Exists(sourcePath))
            {
                if (sourcePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    return Kinds.Archive;
                }
                if (sourcePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                    && !JsonManifestNames.Contains(Path.GetFileName(sourcePath)))
                {
                    return DetectsAudioFile(sourcePath) ? Kinds.Audio : Kinds.Route;
                }
                return Kinds.Unknown;
            }

            if (!Directory.Exists(sourcePath)) return Kinds.Unknown;

            if (DetectsAudio(sourcePath) && !DetectsRouteData(sourcePath))
            {
                return Kinds.Audio;
            }
            if (DetectsRouteData(sourcePath)) return Kinds.Route;
            if (FindMapTileSources(sourcePath).Any()) return Kinds.Route;
            if (FindAssetPackSources(sourcePath).Any()) return Kinds.Asset;
            return Kinds.Unknown;
        }

        /// <summary>
        /// Returns true when any non-manifest JSON in
        /// <paramref name="sourcePath"/>'s tree carries one of the
        /// top-level legacy keys (<c>tracks</c>, <c>industries</c>,
        /// ...).
        /// </summary>
        public static bool DetectsRouteData(string sourcePath)
        {
            foreach (var path in IterJsonFiles(sourcePath))
            {
                if (JsonManifestNames.Contains(Path.GetFileName(path))) continue;
                try
                {
                    var data = LegacyJsonReader.ReadJson(path) as JObject;
                    if (data == null) continue;
                    if (data.Properties().Any(p => LegacyDataKeys.Contains(p.Name))) return true;
                }
                catch (Exception) { /* malformed JSON ignored — counted in conversion later */ }
            }
            return false;
        }

        public static bool DetectsAudio(string sourcePath)
        {
            // Audio packages either declare a whistles/horns/bells
            // mixinto in the manifest OR carry a JSON file whose
            // contents look like audio data (clip / layers /
            // indexTimes keys).
            var manifest = ReadManifest(sourcePath);
            if (manifest != null && (manifest["mixintos"] as JObject ?? manifest["Mixintos"] as JObject) is JObject mx)
            {
                foreach (var prop in mx.Properties())
                {
                    var key = (prop.Name ?? string.Empty).ToLowerInvariant();
                    if (key == "whistles" || key == "horns" || key == "bells" || key == "hellsbells") return true;
                }
            }

            foreach (var path in IterJsonFiles(sourcePath))
            {
                if (JsonManifestNames.Contains(Path.GetFileName(path))) continue;
                if (!string.IsNullOrEmpty(DetectAudioJson(path))) return true;
            }
            return false;
        }

        private static bool DetectsAudioFile(string filePath)
        {
            return !string.IsNullOrEmpty(DetectAudioJson(filePath));
        }

        /// <summary>
        /// Returns the audio kind ("horns", "whistles", "bells") for
        /// a single JSON file, or empty string when the file isn't
        /// audio data. Mirrors <c>detect_audio_json</c> exactly.
        /// </summary>
        public static string DetectAudioJson(string path)
        {
            JToken data;
            try
            {
                data = LegacyJsonReader.ReadJson(path);
            }
            catch (Exception)
            {
                return string.Empty;
            }

            if (!(data is JArray arr)) return string.Empty;
            var entries = arr.OfType<JObject>().ToList();
            if (entries.Count == 0) return string.Empty;

            var lowerName = Path.GetFileName(path)?.ToLowerInvariant() ?? string.Empty;
            if (entries.Any(e => e["layers"] is JArray)) return "horns";
            if (entries.Any(e => e["clip"] != null && e["clip"].Type != JTokenType.Null)) return "whistles";
            if (lowerName.IndexOf("hellsbell", StringComparison.Ordinal) >= 0
                || lowerName.IndexOf("bell", StringComparison.Ordinal) >= 0
                || entries.Any(e => e["indexTimes"] != null))
            {
                return "bells";
            }
            return string.Empty;
        }

        // ------------------------------------------------------------------
        // Asset pack detection
        // ------------------------------------------------------------------

        /// <summary>
        /// An asset pack folder contains a "bundle" (file or folder)
        /// plus Catalog.json plus Definitions.json. Case-insensitive
        /// like the Python source.
        /// </summary>
        public static bool IsAssetPackFolder(string folder)
        {
            if (!Directory.Exists(folder)) return false;
            return HasCaseFile(folder, "bundle")
                && HasCaseFile(folder, "Catalog.json")
                && HasCaseFile(folder, "Definitions.json");
        }

        /// <summary>
        /// Enumerates every asset-pack folder under
        /// <paramref name="folder"/>. The root itself is yielded if
        /// it qualifies; otherwise the tree is walked.
        /// </summary>
        public static IEnumerable<string> IterAssetPackFolders(string folder)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) yield break;

            if (IsAssetPackFolder(folder))
            {
                yield return folder;
                yield break;
            }

            IEnumerable<string> children;
            try
            {
                children = Directory.EnumerateDirectories(folder, "*", SearchOption.AllDirectories)
                                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                yield break;
            }

            foreach (var child in children)
            {
                if (IsAssetPackFolder(child)) yield return child;
            }
        }

        /// <summary>
        /// Returns the names of the source-relative roots that
        /// contain asset packs ("." for the source itself, "SCAssetPacks"
        /// for the legacy SC subfolder, or individual subfolder names).
        /// </summary>
        public static List<string> FindAssetPackSources(string source)
        {
            if (!Directory.Exists(source)) return new List<string>();

            if (IsAssetPackFolder(source)) return new List<string> { "." };

            var sources = new List<string>();
            var sc = Path.Combine(source, "SCAssetPacks");
            if (Directory.Exists(sc) && IterAssetPackFolders(sc).Any())
            {
                sources.Add("SCAssetPacks");
            }
            if (HasDirectAssetPackChildren(source))
            {
                sources.Add(".");
            }

            foreach (var child in EnumerateDirectories(source))
            {
                var name = Path.GetFileName(child);
                if (string.Equals(name, "SCAssetPacks", StringComparison.OrdinalIgnoreCase)) continue;
                if (IterAssetPackFolders(child).Any()) sources.Add(name);
            }

            return sources.GroupBy(s => s, StringComparer.OrdinalIgnoreCase)
                          .Select(g => g.First())
                          .ToList();
        }

        private static bool HasDirectAssetPackChildren(string folder)
        {
            return EnumerateDirectories(folder).Any(IsAssetPackFolder);
        }

        // ------------------------------------------------------------------
        // Map tile detection
        // ------------------------------------------------------------------

        /// <summary>
        /// Returns folders containing <c>.data</c> map tile files —
        /// the root itself if it qualifies, plus <c>Maps/</c> +
        /// <c>Maps/&lt;sub&gt;</c> + any sibling folder containing
        /// tiles.
        /// </summary>
        public static List<string> FindMapTileSources(string source)
        {
            var result = new List<string>();
            if (!Directory.Exists(source)) return result;

            if (ContainsTiles(source)) result.Add(source);

            var maps = Path.Combine(source, "Maps");
            if (Directory.Exists(maps))
            {
                if (ContainsTiles(maps)) result.Add(maps);
                foreach (var child in EnumerateDirectories(maps))
                {
                    if (ContainsTiles(child)) result.Add(child);
                }
            }

            foreach (var child in EnumerateDirectories(source))
            {
                var name = Path.GetFileName(child);
                if (string.Equals(name, "Maps", StringComparison.OrdinalIgnoreCase)) continue;
                if (ContainsTiles(child)) result.Add(child);
            }

            // Dedup by full path (case-insensitive).
            return result.GroupBy(p => Path.GetFullPath(p).ToLowerInvariant())
                         .Select(g => g.First())
                         .ToList();
        }

        private static bool ContainsTiles(string folder)
        {
            try
            {
                return Directory.EnumerateFiles(folder, "*.data", SearchOption.TopDirectoryOnly).Any();
            }
            catch (Exception)
            {
                return false;
            }
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private static IEnumerable<string> IterJsonFiles(string source)
        {
            if (string.IsNullOrEmpty(source)) yield break;

            if (File.Exists(source))
            {
                if (source.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                    && !source.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
                {
                    yield return source;
                }
                yield break;
            }

            if (!Directory.Exists(source)) yield break;

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(source, "*.json", SearchOption.AllDirectories);
            }
            catch (Exception)
            {
                yield break;
            }

            foreach (var path in files.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                if (path.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)) continue;
                yield return path;
            }
        }

        private static IEnumerable<string> EnumerateDirectories(string folder)
        {
            try
            {
                return Directory.EnumerateDirectories(folder, "*", SearchOption.TopDirectoryOnly)
                                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return Enumerable.Empty<string>();
            }
        }

        private static bool HasCaseFile(string folder, string name)
        {
            try
            {
                var wanted = name.ToLowerInvariant();
                foreach (var entry in Directory.EnumerateFileSystemEntries(folder, "*", SearchOption.TopDirectoryOnly))
                {
                    if (string.Equals(Path.GetFileName(entry), wanted, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static JObject ReadManifest(string source)
        {
            string folder;
            if (File.Exists(source)) folder = Path.GetDirectoryName(source) ?? string.Empty;
            else folder = source;

            foreach (var name in new[] { "Definition.json", "Info.json" })
            {
                var path = Path.Combine(folder, name);
                if (!File.Exists(path)) continue;
                try
                {
                    return LegacyJsonReader.ReadJson(path) as JObject;
                }
                catch (Exception)
                {
                    return null;
                }
            }
            return null;
        }
    }
}
