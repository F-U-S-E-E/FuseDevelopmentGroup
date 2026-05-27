using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FUSE.Converter.Conversion;
using FUSE.Converter.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FUSE.Converter
{
    /// <summary>
    /// First-pass C# port of the Python <c>convert_mod</c> entry point.
    /// Walks a legacy mod folder, classifies each source JSON file,
    /// applies the available converters (tracks today; other sections
    /// land in subsequent commits), and writes the result to
    /// <paramref name="outputFolder"/> as a stamped Info.json plus one
    /// <c>*.fuse.json</c> per source.
    /// </summary>
    /// <remarks>
    /// Caller chooses the output folder explicitly to keep this side
    /// effect free of side-channel "where do conversions go" config.
    /// The editor's Convert button passes a sibling folder
    /// (<c>&lt;input&gt;.FUSE</c>) so the user sees the new mod next
    /// to the original in <c>Mods/</c>.
    /// </remarks>
    internal static class FuseLegacyConverter
    {
        public static FuseConversionResult ConvertMod(string modFolder, string outputFolder)
        {
            var result = new FuseConversionResult
            {
                OutputFolderPath = outputFolder,
            };

            if (string.IsNullOrEmpty(modFolder) || !Directory.Exists(modFolder))
            {
                result.Success = false;
                result.Report.Add(Error("Source folder does not exist", modFolder));
                return result;
            }

            if (string.IsNullOrEmpty(outputFolder))
            {
                result.Success = false;
                result.Report.Add(Error("Output folder path is empty", modFolder));
                return result;
            }

            var manifest = LegacyManifestReader.Read(modFolder);
            result.ModId = manifest.Id;
            result.ModName = manifest.Name;
            result.ModVersion = manifest.Version;
            result.Author = manifest.Author;

            var sourceFiles = FindSourceJsonFiles(modFolder);
            if (sourceFiles.Count == 0)
            {
                result.Success = false;
                result.Report.Add(Error("No legacy JSON source files found in mod folder", modFolder));
                return result;
            }

            try
            {
                Directory.CreateDirectory(outputFolder);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Report.Add(Error("Could not create output folder: " + ex.Message, modFolder));
                return result;
            }

            var fragments = new List<(string SourceName, string OutName, JObject Document)>();
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var orderState = new LegacySourceConverter.OrderState();
            var (mixintoMetadata, _) = LegacyDefinitionConverter.MixintoMetadata(modFolder);
            var declaredInitialGroups = new HashSet<string>(StringComparer.Ordinal);

            foreach (var sourcePath in sourceFiles)
            {
                var sourceName = Path.GetFileName(sourcePath);
                var fragment = MakeUniqueFragmentName(Path.GetFileNameWithoutExtension(sourcePath), usedNames);
                var skeleton = FuseFragmentBuilder.Build(manifest.Id, manifest.Name, manifest.Version, manifest.Author, fragment);

                // Surface mixinto metadata on the fragment so the FUSE
                // loader knows the source file targets an existing
                // runtime object (matches Python `rail["mixinto"]`).
                if (mixintoMetadata.TryGetValue(sourceName.ToLowerInvariant(), out var mx))
                {
                    skeleton["mixinto"] = mx;
                }

                try
                {
                    // Use the tolerant JSON reader — legacy Strange
                    // Customs / Railloader sources often carry JSONC
                    // comments, trailing commas, or are truncated
                    // mid-write. Strict JObject.Parse would reject
                    // them outright and prevent the conversion.
                    var legacy = LegacyJsonReader.ReadJson(sourcePath) as JObject;
                    if (legacy == null)
                    {
                        result.Report.Add(Error($"Source '{sourceName}' did not contain a top-level object.", sourceName));
                        continue;
                    }
                    LegacySourceConverter.ConvertSource(legacy, skeleton, sourceName, orderState, result.Report);
                    LegacySourceConverter.CollectInitiallyEnabledGroups(skeleton, declaredInitialGroups);
                }
                catch (Exception ex)
                {
                    result.Report.Add(Error($"Failed to parse '{sourceName}': {ex.Message}", sourceName));
                    continue;
                }

                fragments.Add((sourceName, fragment + ".fuse.json", skeleton));
            }

            if (fragments.Count == 0)
            {
                result.Success = false;
                result.Report.Add(Error("All source files failed to parse; nothing written.", modFolder));
                return result;
            }

            // After every fragment has been translated, run the
            // geometry-repair pass. This is the only pass that needs
            // visibility across all fragments (the segment graph
            // routinely spans multiple source JSON files) so it lives
            // here in the orchestrator, not in any per-fragment
            // converter. Mutates the fragment documents in place.
            var repairInputs = fragments
                .Select(f => new SpanGeometryRepair.ConvertedFragment(f.SourceName, f.Document))
                .ToList();
            SpanGeometryRepair.RepairPackageSpans(repairInputs, result.Report);

            foreach (var fragment in fragments)
            {
                var outPath = Path.Combine(outputFolder, fragment.OutName);
                try
                {
                    File.WriteAllText(outPath, fragment.Document.ToString(Formatting.Indented));
                    result.WrittenFragments.Add(fragment.OutName);
                    // Per-section counts (port of count_content) drive
                    // both the summary surfaced to the modder and the
                    // rail-data-file weight when sorting fragments in
                    // the FUSE Info.json LoadAfter section.
                    result.FragmentCounts[fragment.OutName] = LegacySourceConverter.CountContent(fragment.Document);
                }
                catch (Exception ex)
                {
                    result.Report.Add(Error($"Failed to write '{fragment.OutName}': {ex.Message}", fragment.SourceName));
                }
            }

            EmitTrackGroupCoverageWarning(declaredInitialGroups, result.Report);

            var legacyLoadAfter = LegacyDefinitionConverter.LegacyLoadAfter(modFolder);
            WriteInfoJson(outputFolder, manifest, result.WrittenFragments, result.Report, legacyLoadAfter);

            // Asset packs + map tiles are file-system payloads that
            // ride alongside the converted *.fuse.json fragments.
            // Detect them by walking the source tree and copy each
            // root into the output folder verbatim — the FUSE
            // loader resolves the .data / bundle / Catalog files at
            // runtime.
            CopyAssociatedAssets(modFolder, outputFolder, result);

            result.Success = result.WrittenFragments.Count > 0;
            return result;
        }

        /// <summary>
        /// Copies asset-pack folders and map-tile data files from
        /// the source legacy mod into the output FUSE folder. Records
        /// per-root counts in the result's FragmentCounts under the
        /// synthesized key "assets" so callers can surface them.
        /// </summary>
        private static void CopyAssociatedAssets(string modFolder, string outputFolder, FuseConversionResult result)
        {
            try
            {
                var assetRoots = LegacyKindDetector.FindAssetPackSources(modFolder);
                if (assetRoots.Count > 0)
                {
                    LegacyAssetCopier.CopyAssetSources(modFolder, outputFolder, assetRoots, result);
                    result.Report.Add(Info(
                        $"Copied {assetRoots.Count} asset-pack source root(s) into the output folder.",
                        sourceFile: string.Empty, concept: "asset-pack-copy"));
                }

                var tileSources = LegacyKindDetector.FindMapTileSources(modFolder);
                if (tileSources.Count > 0)
                {
                    LegacyAssetCopier.CopyMapTiles(modFolder, outputFolder, result);
                    result.Report.Add(Info(
                        $"Copied map tile data from {tileSources.Count} folder(s).",
                        sourceFile: string.Empty, concept: "map-tile-copy"));
                }
            }
            catch (Exception ex)
            {
                result.Report.Add(Warning(
                    "Asset/map-tile copy failed: " + ex.Message,
                    sourceFile: string.Empty, concept: "asset-copy-failed"));
            }
        }

        /// <summary>
        /// Detects the legacy package kind (route / audio / asset /
        /// map_tile / unknown) without performing any conversion.
        /// </summary>
        public static string DetectKind(string sourcePath, string requested = "auto")
        {
            return LegacyKindDetector.DetectKind(sourcePath, requested);
        }

        /// <summary>
        /// Kind-aware dispatcher: routes to <see cref="ConvertMod"/>
        /// for route packages,
        /// <see cref="LegacyAudioConverter.ConvertAudioMod"/> for
        /// audio packages, and produces an empty-success result with
        /// a warning for asset / unknown kinds (the user can copy
        /// asset packs verbatim — that's already what
        /// <see cref="ConvertMod"/> does at the end of the route
        /// path).
        /// </summary>
        public static FuseConversionResult ConvertPackage(string modFolder, string outputFolder, string requestedKind = "auto")
        {
            var kind = DetectKind(modFolder, requestedKind);
            switch (kind)
            {
                case "audio":
                    return LegacyAudioConverter.ConvertAudioMod(modFolder, outputFolder);
                case "asset":
                    return ConvertAssetOnly(modFolder, outputFolder);
                case "route":
                case "unknown":
                default:
                    return ConvertMod(modFolder, outputFolder);
            }
        }

        /// <summary>
        /// Asset-only conversion: just copies asset-pack folders and
        /// writes a FUSE Info.json. Used when the source has no
        /// JSON-side data (no tracks/industries/etc.) but does ship
        /// asset packs (Catalog.json + Definitions.json + bundle).
        /// </summary>
        private static FuseConversionResult ConvertAssetOnly(string modFolder, string outputFolder)
        {
            var result = new FuseConversionResult
            {
                OutputFolderPath = outputFolder,
            };

            var manifest = LegacyManifestReader.Read(modFolder);
            result.ModId = manifest.Id;
            result.ModName = manifest.Name;
            result.ModVersion = manifest.Version;
            result.Author = manifest.Author;

            try
            {
                Directory.CreateDirectory(outputFolder);
                var assetRoots = LegacyKindDetector.FindAssetPackSources(modFolder);
                if (assetRoots.Count > 0)
                {
                    LegacyAssetCopier.CopyAssetSources(modFolder, outputFolder, assetRoots, result);
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
                    ["FuseAssetPacks"] = JArray.FromObject(assetRoots.Count > 0 ? assetRoots : (System.Collections.Generic.IEnumerable<string>)new[] { "." }),
                };
                File.WriteAllText(Path.Combine(outputFolder, "Info.json"), info.ToString(Formatting.Indented));
                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Report.Add(Error("Asset-only conversion failed: " + ex.Message, modFolder));
            }
            return result;
        }

        private static List<string> FindSourceJsonFiles(string modFolder)
        {
            return Directory.GetFiles(modFolder, "*.json", SearchOption.TopDirectoryOnly)
                .Where(p =>
                {
                    var name = Path.GetFileName(p);
                    if (string.Equals(name, "Definition.json", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(name, "Info.json", StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                    if (name.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                    // Signal definitions need a specialised converter
                    // that hasn't been ported yet; skip with a marker
                    // matching the Python source's behaviour.
                    if (name.IndexOf("signal", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return false;
                    }
                    return true;
                })
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string MakeUniqueFragmentName(string baseName, HashSet<string> taken)
        {
            var sanitized = Slug(baseName);
            if (string.IsNullOrEmpty(sanitized))
            {
                sanitized = "fragment";
            }

            if (!taken.Contains(sanitized))
            {
                taken.Add(sanitized);
                return sanitized;
            }

            int index = 2;
            while (true)
            {
                var candidate = sanitized + "-" + index;
                if (!taken.Contains(candidate))
                {
                    taken.Add(candidate);
                    return candidate;
                }
                index++;
            }
        }

        /// <summary>
        /// Emits a single info entry listing the track group ids that
        /// no progression auto-enables. The FUSE runtime transiently
        /// enables them during staged graph apply, so this is purely
        /// informational — port of
        /// <c>_emit_track_group_coverage_warning</c>.
        /// </summary>
        private static void EmitTrackGroupCoverageWarning(HashSet<string> declaredInitialGroups, List<FuseConversionReportEntry> report)
        {
            // Today, group-id collection happens through the
            // _GROUP_IDS_REFERENCED side channel in Python. The C#
            // port doesn't yet maintain that set (every segment
            // converter would have to push into a shared sink). For
            // now we surface the count of declared initial groups so
            // there's at least a stub diagnostic; richer coverage
            // analysis lands when group-id tracking gets ported.
            if (declaredInitialGroups != null && declaredInitialGroups.Count > 0)
            {
                report.Add(Info($"Progression initial-enable scan: {declaredInitialGroups.Count} track group(s) declared as initially enabled.",
                    sourceFile: string.Empty, concept: "track-group-summary"));
            }
        }

        // ApplyTrackSections / ApplyOperationsSections /
        // ApplyWorldSections / ApplyDict / CountContent are removed —
        // their behaviour is now centralized in
        // LegacySourceConverter.ConvertSource + CountContent. The
        // orchestrator loop calls those directly above. See git history
        // for the previous shape if you need it.

        private static void WriteInfoJson(string outputFolder, LegacyManifestReader.LegacyManifest manifest,
                                          List<string> fragments, List<FuseConversionReportEntry> report,
                                          List<string> legacyLoadAfter = null)
        {
            try
            {
                // LoadAfter starts with "FUSE" (the runtime), then
                // every legacy requirement / loadAfter id remapped
                // via LegacyDefinitionConverter.FusePackageId so we
                // wait for any sibling-converted packages.
                var loadAfter = new JArray("FUSE");
                if (legacyLoadAfter != null)
                {
                    foreach (var id in legacyLoadAfter) loadAfter.Add(id);
                }

                var info = new JObject
                {
                    ["$schema"] = ".\\schemas\\umm-info.schema.json",
                    ["Id"] = $"{manifest.Id}.FUSE",
                    ["DisplayName"] = $"{manifest.Name} (FUSE)",
                    ["Author"] = manifest.Author ?? string.Empty,
                    ["Version"] = manifest.Version ?? "1.0.0",
                    ["ManagerVersion"] = "0.27.10",
                    ["Requirements"] = new JArray("FUSE"),
                    ["LoadAfter"] = loadAfter,
                    ["FuseDataFiles"] = JArray.FromObject(fragments),
                };

                File.WriteAllText(Path.Combine(outputFolder, "Info.json"), info.ToString(Formatting.Indented));
            }
            catch (Exception ex)
            {
                report.Add(Error("Failed to write Info.json: " + ex.Message, outputFolder));
            }
        }

        /// <summary>
        /// Port of the Python <c>slug</c>: lower-case alnum + dashes,
        /// collapsed runs, trimmed edges. Used for fragment file
        /// names so the output is predictable across platforms.
        /// </summary>
        private static string Slug(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return string.Empty;
            }

            var lower = raw.ToLowerInvariant().ToCharArray();
            for (int i = 0; i < lower.Length; i++)
            {
                var c = lower[i];
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-')
                {
                    continue;
                }
                lower[i] = '-';
            }

            var collapsed = new System.Text.StringBuilder(lower.Length);
            char? last = null;
            foreach (var c in lower)
            {
                if (c == '-' && last == '-')
                {
                    continue;
                }
                collapsed.Append(c);
                last = c;
            }

            return collapsed.ToString().Trim('-');
        }

        private static FuseConversionReportEntry Info(string message, string sourceFile = "", string concept = "")
        {
            return new FuseConversionReportEntry
            {
                Level = FuseConversionReportLevel.Info,
                Message = message,
                SourceFile = sourceFile,
                Concept = concept,
            };
        }

        private static FuseConversionReportEntry Warning(string message, string sourceFile = "", string concept = "")
        {
            return new FuseConversionReportEntry
            {
                Level = FuseConversionReportLevel.Warning,
                Message = message,
                SourceFile = sourceFile,
                Concept = concept,
            };
        }

        private static FuseConversionReportEntry Error(string message, string sourceFile = "")
        {
            return new FuseConversionReportEntry
            {
                Level = FuseConversionReportLevel.Error,
                Message = message,
                SourceFile = sourceFile,
            };
        }
    }
}
