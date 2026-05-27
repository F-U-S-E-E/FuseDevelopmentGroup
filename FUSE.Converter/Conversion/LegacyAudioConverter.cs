using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FUSE.Converter.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FUSE.Converter.Conversion
{
    /// <summary>
    /// Port of <c>convert_fuse_audio.py</c>. Converts loose horn /
    /// whistle / bell legacy packs (the Strange Customs convention:
    /// a Definition.json with a <c>mixintos</c> table pointing at
    /// data JSONs and audio clip files) into a FUSE audio package.
    /// </summary>
    /// <remarks>
    /// Asset-pack-style wrappers (where the audio data is already
    /// bundled into a Catalog.json / Definitions.json triple) get
    /// the simpler treatment via <see cref="CopyAssetPackWrapper"/>:
    /// copy everything except Info.json, then write a fresh
    /// FUSE-flavoured Info.json next to it.
    /// </remarks>
    internal static class LegacyAudioConverter
    {
        private static readonly HashSet<string> AudioExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".wav", ".mp3", ".ogg", ".aiff", ".aif",
        };

        private static readonly Regex FileRefPattern =
            new Regex(@"^\s*file\((.+)\)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Converts a single legacy audio package folder. Picks the
        /// right strategy based on whether the source contains
        /// asset-pack-style children: bundles get the wrapper
        /// treatment, loose packs get parsed via the mixinto table.
        /// </summary>
        public static FuseConversionResult ConvertAudioMod(string modFolder, string outputFolder)
        {
            var result = new FuseConversionResult
            {
                OutputFolderPath = outputFolder,
            };

            if (string.IsNullOrEmpty(modFolder) || !Directory.Exists(modFolder))
            {
                result.Success = false;
                result.Report.Add(Report(FuseConversionReportLevel.Error,
                    "Source folder does not exist", modFolder, concept: "audio-source-missing"));
                return result;
            }

            try
            {
                Directory.CreateDirectory(outputFolder);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Report.Add(Report(FuseConversionReportLevel.Error,
                    "Could not create output folder: " + ex.Message, modFolder, "audio-output-create"));
                return result;
            }

            if (HasAssetPackChildren(modFolder))
            {
                CopyAssetPackWrapper(modFolder, outputFolder, result);
            }
            else
            {
                ConvertLoosePackage(modFolder, outputFolder, result);
            }

            return result;
        }

        // ------------------------------------------------------------------
        // Asset-pack wrapper
        // ------------------------------------------------------------------

        /// <summary>
        /// Wraps an already-bundled audio asset pack by copying the
        /// folder verbatim (sans Info.json) and writing a FUSE-side
        /// Info.json that declares the asset-pack root.
        /// </summary>
        public static void CopyAssetPackWrapper(string source, string outputFolder, FuseConversionResult result)
        {
            var manifest = LegacyManifestReader.Read(source);
            result.ModId = manifest.Id;
            result.ModName = manifest.Name;
            result.ModVersion = manifest.Version;
            result.Author = manifest.Author;

            try
            {
                CopyDirectoryExcludingInfo(source, outputFolder);
            }
            catch (Exception ex)
            {
                result.Report.Add(Report(FuseConversionReportLevel.Error,
                    "Failed to copy asset-pack contents: " + ex.Message, source, "audio-asset-pack-copy"));
                return;
            }

            var info = new JObject
            {
                ["$schema"] = ".\\schemas\\umm-info.schema.json",
                ["Id"] = manifest.Id + ".FUSE",
                ["DisplayName"] = manifest.Name + " (FUSE)",
                ["Author"] = manifest.Author ?? string.Empty,
                ["Version"] = manifest.Version ?? "1.0.0",
                ["ManagerVersion"] = "0.27.10",
                ["Requirements"] = new JArray("FUSE"),
                ["LoadAfter"] = new JArray("FUSE"),
                ["FuseAssetPacks"] = new JArray("."),
            };

            try
            {
                File.WriteAllText(Path.Combine(outputFolder, "Info.json"),
                    info.ToString(Formatting.Indented));
                result.Success = true;
                result.Report.Add(Report(FuseConversionReportLevel.Info,
                    "Wrapped asset-pack audio package.", source, "audio-asset-pack-wrap"));
            }
            catch (Exception ex)
            {
                result.Report.Add(Report(FuseConversionReportLevel.Error,
                    "Failed to write Info.json: " + ex.Message, outputFolder, "audio-info-write"));
            }
        }

        // ------------------------------------------------------------------
        // Loose package (mixinto-driven)
        // ------------------------------------------------------------------

        /// <summary>
        /// Converts a legacy "loose" audio package: Definition.json
        /// declares mixintos pointing at JSON files containing the
        /// whistle/horn/bell entries. We read each one, copy the
        /// referenced audio clips into Audio/&lt;kind&gt;/, and emit
        /// a FUSE-shape audio.fuse.json.
        /// </summary>
        public static void ConvertLoosePackage(string source, string outputFolder, FuseConversionResult result)
        {
            var manifest = LegacyManifestReader.Read(source);
            result.ModId = manifest.Id;
            result.ModName = manifest.Name;
            result.ModVersion = manifest.Version;
            result.Author = manifest.Author;

            JObject definition = null;
            var definitionPath = Path.Combine(source, "Definition.json");
            if (!File.Exists(definitionPath)) definitionPath = Path.Combine(source, "definition.json");
            if (File.Exists(definitionPath))
            {
                try { definition = LegacyJsonReader.ReadJson(definitionPath) as JObject; }
                catch (Exception) { definition = null; }
            }

            var mixintos = (definition?["mixintos"] as JObject) ?? (definition?["Mixintos"] as JObject) ?? new JObject();
            var rail = BuildAudioSkeleton(manifest.Id, manifest.Name, manifest.Version, manifest.Author);

            int totalEntries = 0;
            foreach (var prop in mixintos.Properties())
            {
                var sourceFilePath = ResolveSourceFile(source, prop.Value);
                if (string.IsNullOrEmpty(sourceFilePath) || !File.Exists(sourceFilePath))
                {
                    result.Report.Add(Report(FuseConversionReportLevel.Warning,
                        $"Missing mixinto file for '{prop.Name}': {prop.Value}",
                        source, "audio-mixinto-missing"));
                    continue;
                }

                var lower = (prop.Name ?? string.Empty).ToLowerInvariant();
                if (lower == "whistles") totalEntries += ConvertWhistles(source, outputFolder, manifest.Id, sourceFilePath, rail, result);
                else if (lower == "horns") totalEntries += ConvertHorns(source, outputFolder, manifest.Id, sourceFilePath, rail, result);
                else if (lower == "bells" || lower == "hellsbells") totalEntries += ConvertBells(source, outputFolder, manifest.Id, sourceFilePath, rail, result);
            }

            if (totalEntries == 0)
            {
                result.Report.Add(Report(FuseConversionReportLevel.Warning,
                    "No horn/whistle/bell entries found in this package.",
                    source, "audio-no-entries"));
                return;
            }

            try
            {
                File.WriteAllText(Path.Combine(outputFolder, "audio.fuse.json"),
                    rail.ToString(Formatting.Indented));
                result.WrittenFragments.Add("audio.fuse.json");

                var audio = (JObject)rail["audio"];
                result.FragmentCounts["audio.fuse.json"] = new Dictionary<string, int>(StringComparer.Ordinal)
                {
                    ["audio.whistles"] = ((JObject)audio["whistles"]).Count,
                    ["audio.horns"] = ((JObject)audio["horns"]).Count,
                    ["audio.bells"] = ((JObject)audio["bells"]).Count,
                };

                var info = new JObject
                {
                    ["$schema"] = ".\\schemas\\umm-info.schema.json",
                    ["Id"] = manifest.Id + ".FUSE",
                    ["DisplayName"] = manifest.Name + " (FUSE Audio)",
                    ["Author"] = manifest.Author ?? string.Empty,
                    ["Version"] = manifest.Version ?? "1.0.0",
                    ["ManagerVersion"] = "0.27.10",
                    ["Requirements"] = new JArray("FUSE"),
                    ["LoadAfter"] = new JArray("FUSE"),
                    ["FuseDataFiles"] = new JArray("audio.fuse.json"),
                };
                File.WriteAllText(Path.Combine(outputFolder, "Info.json"),
                    info.ToString(Formatting.Indented));
                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Report.Add(Report(FuseConversionReportLevel.Error,
                    "Failed to write audio fragment / Info.json: " + ex.Message,
                    outputFolder, "audio-write-failed"));
            }
        }

        // ------------------------------------------------------------------
        // Per-kind converters
        // ------------------------------------------------------------------

        private static int ConvertWhistles(string source, string output, string modId, string path,
                                            JObject rail, FuseConversionResult result)
        {
            var entries = ReadJsonArray(path);
            var whistles = (JObject)((JObject)rail["audio"])["whistles"];
            int count = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                if (!(entries[i] is JObject item)) continue;
                var name = item.Value<string>("name") ?? "Whistle " + (i + 1);
                var entryId = $"{modId}.whistle.{Slug(name, (i + 1).ToString(CultureInfo.InvariantCulture))}";
                var entry = new JObject
                {
                    ["name"] = name,
                    ["clip"] = CopyAudio(source, output, item["clip"], "whistles", result),
                };
                if (item["model"] is JObject model)
                {
                    entry["model"] = new JObject
                    {
                        ["assetPackIdentifier"] = model.Value<string>("assetPackIdentifier") ?? string.Empty,
                        ["assetIdentifier"] = model.Value<string>("assetIdentifier") ?? string.Empty,
                    };
                }
                whistles[entryId] = entry;
                count++;
            }
            return count;
        }

        private static int ConvertHorns(string source, string output, string modId, string path,
                                          JObject rail, FuseConversionResult result)
        {
            var entries = ReadJsonArray(path);
            var horns = (JObject)((JObject)rail["audio"])["horns"];
            int count = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                if (!(entries[i] is JObject item)) continue;
                var name = item.Value<string>("name") ?? "Horn " + (i + 1);
                var entryId = $"{modId}.horn.{Slug(name, (i + 1).ToString(CultureInfo.InvariantCulture))}";

                var layers = new JArray();
                if (item["layers"] is JArray legacyLayers)
                {
                    foreach (var layer in legacyLayers.OfType<JObject>())
                    {
                        var keyframes = new JArray();
                        if (layer["keyframes"] is JArray legacyKeyframes)
                        {
                            foreach (var kf in legacyKeyframes.OfType<JObject>())
                            {
                                keyframes.Add(new JObject
                                {
                                    ["t"] = kf.Value<double?>("t") ?? 0.0,
                                    ["value"] = kf.Value<double?>("value") ?? 0.0,
                                });
                            }
                        }
                        layers.Add(new JObject
                        {
                            ["file"] = CopyAudio(source, output, layer["file"], "horns", result),
                            ["keyframes"] = keyframes,
                        });
                    }
                }

                horns[entryId] = new JObject
                {
                    ["name"] = name,
                    ["layers"] = layers,
                };
                count++;
            }
            return count;
        }

        private static int ConvertBells(string source, string output, string modId, string path,
                                          JObject rail, FuseConversionResult result)
        {
            var entries = ReadJsonArray(path);
            var bells = (JObject)((JObject)rail["audio"])["bells"];
            int count = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                if (!(entries[i] is JObject item)) continue;
                var name = item.Value<string>("name") ?? "Bell " + (i + 1);
                var entryId = $"{modId}.bell.{Slug(name, (i + 1).ToString(CultureInfo.InvariantCulture))}";

                var indexTimes = new JArray();
                if (item["indexTimes"] is JArray rawTimes)
                {
                    foreach (var t in rawTimes)
                    {
                        var v = t.Value<double?>();
                        if (v.HasValue) indexTimes.Add(v.Value);
                    }
                }

                bells[entryId] = new JObject
                {
                    ["name"] = name,
                    ["file"] = CopyAudio(source, output, item["file"], "bells", result),
                    ["indexTimes"] = indexTimes,
                };
                count++;
            }
            return count;
        }

        // ------------------------------------------------------------------
        // Audio copy + skeleton + helpers
        // ------------------------------------------------------------------

        private static string CopyAudio(string sourceRoot, string outputRoot, JToken specToken, string kind,
                                         FuseConversionResult result)
        {
            var spec = specToken?.Type == JTokenType.String ? specToken.Value<string>() : null;
            if (string.IsNullOrEmpty(spec)) return string.Empty;

            var srcPath = ResolveSourceFile(sourceRoot, specToken);
            if (string.IsNullOrEmpty(srcPath) || !File.Exists(srcPath))
            {
                result.Report.Add(Report(FuseConversionReportLevel.Warning,
                    $"Missing audio file: {spec}", sourceRoot, "audio-clip-missing"));
                return FileRefValue(spec);
            }

            var extension = Path.GetExtension(srcPath);
            if (!AudioExtensions.Contains(extension))
            {
                result.Report.Add(Report(FuseConversionReportLevel.Warning,
                    $"Unsupported audio extension: {srcPath}", srcPath, "audio-ext-unsupported"));
            }

            var destFolder = Path.Combine(outputRoot, "Audio", kind);
            Directory.CreateDirectory(destFolder);
            var destPath = Path.Combine(destFolder, Path.GetFileName(srcPath));
            try
            {
                File.Copy(srcPath, destPath, overwrite: true);
            }
            catch (Exception ex)
            {
                result.Report.Add(Report(FuseConversionReportLevel.Warning,
                    $"Failed to copy audio file '{srcPath}': {ex.Message}", srcPath, "audio-copy-failed"));
            }
            // Return a forward-slash relative path so the FUSE loader
            // can resolve it consistently across platforms.
            return ("Audio/" + kind + "/" + Path.GetFileName(srcPath)).Replace('\\', '/');
        }

        public static JObject BuildAudioSkeleton(string modId, string modName, string modVersion, string author)
        {
            var rail = FuseFragmentBuilder.Build(modId, modName, modVersion, author, "audio");
            // The audio skeleton extends the base with an audio
            // section (whistles/horns/bells dicts) plus the
            // suppressBase arrays the audio variant uses.
            rail["id"] = $"{modId}.FUSE.Audio";
            rail["name"] = $"{modName} (FUSE Audio)";
            rail["audio"] = new JObject
            {
                ["whistles"] = new JObject(),
                ["horns"] = new JObject(),
                ["bells"] = new JObject(),
            };
            var world = (JObject)rail["world"];
            world["suppressBaseScenePaths"] = new JArray();
            world["suppressBaseTrackGroups"] = new JArray();
            world["suppressBaseAreas"] = new JArray();
            return rail;
        }

        public static string FileRefValue(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var match = FileRefPattern.Match(value);
            var inner = match.Success ? match.Groups[1].Value.Trim() : value.Trim();
            return inner.Trim('"').Trim('\'');
        }

        private static string ResolveSourceFile(string sourceRoot, JToken specToken)
        {
            var spec = specToken?.Type == JTokenType.String ? specToken.Value<string>() : null;
            var refValue = FileRefValue(spec);
            if (string.IsNullOrEmpty(refValue)) return string.Empty;
            if (Path.IsPathRooted(refValue))
            {
                return Path.GetFullPath(refValue);
            }
            return Path.GetFullPath(Path.Combine(sourceRoot ?? string.Empty, refValue));
        }

        private static JArray ReadJsonArray(string path)
        {
            try
            {
                return LegacyJsonReader.ReadJson(path) as JArray ?? new JArray();
            }
            catch (Exception)
            {
                return new JArray();
            }
        }

        public static bool HasAssetPackChildren(string source)
        {
            if (!Directory.Exists(source)) return false;
            foreach (var child in Directory.EnumerateDirectories(source))
            {
                // Audio asset packs use a capitalised "Bundle"
                // (folder) plus the standard Catalog.json /
                // Definitions.json triple — Python checks via the
                // file-existence pattern here, not the lower-case
                // file-existence helper used elsewhere.
                if (File.Exists(Path.Combine(child, "Bundle"))
                    || Directory.Exists(Path.Combine(child, "Bundle")))
                {
                    if (File.Exists(Path.Combine(child, "Catalog.json"))
                        && File.Exists(Path.Combine(child, "Definitions.json")))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static void CopyDirectoryExcludingInfo(string source, string dest)
        {
            Directory.CreateDirectory(dest);
            foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            {
                var rel = MakeRelative(source, dir);
                Directory.CreateDirectory(Path.Combine(dest, rel));
            }
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                if (string.Equals(Path.GetFileName(file), "Info.json", StringComparison.OrdinalIgnoreCase)) continue;
                var rel = MakeRelative(source, file);
                var target = Path.Combine(dest, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(target) ?? dest);
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

        public static string Slug(string raw, string fallback)
        {
            if (string.IsNullOrEmpty(raw)) return fallback;
            var sanitized = Regex.Replace(raw, "[^A-Za-z0-9]+", "-").Trim('-').ToLowerInvariant();
            return string.IsNullOrEmpty(sanitized) ? fallback : sanitized;
        }

        private static FuseConversionReportEntry Report(FuseConversionReportLevel level, string message,
                                                          string sourceFile, string concept)
        {
            return new FuseConversionReportEntry
            {
                Level = level,
                Message = message,
                SourceFile = sourceFile ?? string.Empty,
                Concept = concept,
            };
        }
    }
}
