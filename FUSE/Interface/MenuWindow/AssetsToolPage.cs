using FUSE.Infrastructure;
using FUSE.Loading;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UI.Builder;
using UI.Common;
using UnityEngine;
using static FUSE.Interface.InterfaceUtils;

namespace FUSE.Interface.MenuWindow
{
    internal struct AssetsToolPage
    {
        public static void Build(UIPanelBuilder builder)
        {
            builder.AddTitle("Assets Report", "");

            builder.AddLabel("This tool shows asset information and a list of any duplicate asset keys.");

            var diagnostics = FuseAssetPackRegistry.GetDiagnostics();
            var duplicateGroups = GroupDuplicateAssets(diagnostics.DuplicateKeys);
            builder.AddSection("Asset Resolution");
            builder.AddField("Mode", AssetPackModeText());
            builder.AddField("Stores Scanned", diagnostics.StoreFolders.Length.ToString());
            builder.AddField("Runtime Stores", FusePerformanceMetrics.FormatCount("direct asset pack store count"));
            builder.AddField("Unique Asset Keys", diagnostics.UniqueAssetKeys.ToString());
            builder.AddField("Overlapping Asset Keys", diagnostics.DuplicateKeys.Length.ToString());
            builder.AddField("Source Overlap Groups", duplicateGroups.Length.ToString());
            builder.AddField("Identical Copies", diagnostics.DuplicateKeys.Count(item => !item.DefinitionsDiffer).ToString());
            builder.AddField("Different Definitions", diagnostics.DuplicateKeys.Count(item => item.DefinitionsDiffer).ToString());
            builder.AddField("Failed Definitions", diagnostics.FailedDefinitionLoads.Length.ToString());
            builder.AddField("Definition Overrides", diagnostics.LegacyDefinitionOverrides.Length.ToString());
            builder.AddField("Override Issues", diagnostics.LegacyDefinitionOverrideIssues.Length.ToString());
            builder.AddField("Last Direct Mount", FusePerformanceMetrics.FormatTiming("direct asset pack stores"));
            AddWrappedField(
                builder,
                "Duplicates",
                diagnostics.DuplicateKeys.Length == 0
                    ? "None detected."
                    : "Overlap diagnostics, not automatic errors. Export the report to see every duplicate key and source.",
                44f);

            builder.Spacer(8f);

            builder.HStack(row =>
            {
                row.AddButtonCompact("Export and open Asset Report", () =>
                {
                    var message = ExportAssetDiagnostics(diagnostics);
                    Toast.Present(message);
                });
                row.AddButtonCompact("Copy Asset Summary", () =>
                {
                    GUIUtility.systemCopyBuffer = BuildAssetSummary(diagnostics);
                    Toast.Present("Copied FUSE asset summary to clipboard.");
                });
            }, 6f).Height(32f);

            builder.Spacer(4f);

            if (!FuseSettings.ShowAdvancedHealthDetails && diagnostics.DuplicateKeys.Length == 0 &&
                diagnostics.FailedDefinitionLoads.Length == 0 && diagnostics.LegacyDefinitionOverrideIssues.Length == 0)
            {
                builder.AddField("Status", "No asset issues detected");
            }
            else if (!FuseSettings.ShowAdvancedHealthDetails)
            {
                AddWrappedField(
                    builder,
                    "Details",
                    "Enable Advanced Details in Settings to view duplicate winners, overridden sources, and store paths inside this panel.",
                    48f);
            }
            else
            {
                builder.AddSection("Asset Source Overlaps");
                if (diagnostics.DuplicateKeys.Length == 0)
                {
                    builder.AddField("Status", "None detected");
                }
                else
                {
                    foreach (var group in duplicateGroups.Take(20))
                    {
                        BuildDuplicateAssetGroupPreview(builder, group);
                        builder.Spacer(4f);
                        builder.AddHRule();
                    }

                    if (duplicateGroups.Length > 20)
                    {
                        AddWrappedField(
                            builder,
                            "More",
                            (duplicateGroups.Length - 20) + " source group(s) hidden. Export Asset Report for every overlapping key.",
                            34f);
                    }
                }

                builder.Spacer(4f);
                builder.AddSection("Asset Stores");
                foreach (var folder in diagnostics.StoreFolders.Take(40))
                {
                    AddWrappedLabel(builder, InsertBreakHints(Path.GetFileName(folder)), 26f);
                }

                if (diagnostics.StoreFolders.Length > 40)
                {
                    AddWrappedField(
                        builder,
                        "More",
                        (diagnostics.StoreFolders.Length - 40) + " hidden. Export Asset Report for all store paths.",
                        34f);
                }

                builder.Spacer(4f);
                builder.AddSection("Definition Overrides");
                if (diagnostics.LegacyDefinitionOverrides.Length == 0)
                {
                    builder.AddField("Status", "None detected");
                }
                else
                {
                    foreach (var definitionOverride in diagnostics.LegacyDefinitionOverrides.Take(20))
                    {
                        AddWrappedField(
                            builder,
                            definitionOverride.StoreIdentifier,
                            definitionOverride.PackageId + " | " + definitionOverride.DefinitionsPath,
                            44f);
                    }
                }

                foreach (var issue in diagnostics.LegacyDefinitionOverrideIssues.Take(20))
                {
                    AddWrappedField(builder, "Override Issue", issue, 58f);
                }
            }

            builder.Spacer(8f);
        }

        internal static string BuildAssetSummary(FuseAssetPackDiagnostics diagnostics)
        {
            diagnostics = diagnostics ?? new FuseAssetPackDiagnostics();
            var duplicateGroups = GroupDuplicateAssets(diagnostics.DuplicateKeys);
            var builder = new StringBuilder();
            builder.AppendLine("FUSE Asset Summary");
            builder.AppendLine("Mode: " + AssetPackModeText());
            builder.AppendLine("Stores scanned: " + (diagnostics.StoreFolders?.Length ?? 0));
            builder.AppendLine("Unique asset keys: " + diagnostics.UniqueAssetKeys);
            builder.AppendLine("Overlapping asset keys: " + (diagnostics.DuplicateKeys?.Length ?? 0));
            builder.AppendLine("Source overlap groups: " + duplicateGroups.Length);
            builder.AppendLine("Identical duplicate definitions: " + (diagnostics.DuplicateKeys?.Count(item => !item.DefinitionsDiffer) ?? 0));
            builder.AppendLine("Different duplicate definitions: " + (diagnostics.DuplicateKeys?.Count(item => item.DefinitionsDiffer) ?? 0));
            builder.AppendLine("Failed definitions: " + (diagnostics.FailedDefinitionLoads?.Length ?? 0));
            builder.AppendLine("Definition overrides: " + (diagnostics.LegacyDefinitionOverrides?.Length ?? 0));
            builder.AppendLine("Definition override issues: " + (diagnostics.LegacyDefinitionOverrideIssues?.Length ?? 0));

            if (duplicateGroups.Length > 0)
            {
                builder.AppendLine();
                builder.AppendLine("Overlap group preview:");
                foreach (var group in duplicateGroups.Take(10))
                {
                    builder.AppendLine("- " + BuildDuplicateAssetGroupPreviewString(group));
                }

                if (duplicateGroups.Length > 10)
                {
                    builder.AppendLine("- " + (duplicateGroups.Length - 10) + " more source overlap group(s); export the asset report for every key.");
                }
            }

            return builder.ToString().TrimEnd();
        }

        internal static FuseDuplicateAssetGroup[] GroupDuplicateAssets(
            IEnumerable<FuseDuplicateAssetKey> duplicates)
        {
            var grouped = new Dictionary<string, FuseDuplicateAssetGroup>(StringComparer.Ordinal);
            foreach (var duplicate in duplicates ?? Enumerable.Empty<FuseDuplicateAssetKey>())
            {
                if (duplicate == null || string.IsNullOrWhiteSpace(duplicate.Key))
                {
                    continue;
                }

                var sources = (duplicate.Sources ?? Array.Empty<string>())
                    .Where(source => !string.IsNullOrWhiteSpace(source))
                    .ToArray();
                var identity = (duplicate.DefinitionsDiffer ? "different" : "identical") +
                               "\u001e" + string.Join("\u001f", sources);
                if (!grouped.TryGetValue(identity, out var group))
                {
                    group = new FuseDuplicateAssetGroup
                    {
                        DefinitionsDiffer = duplicate.DefinitionsDiffer,
                        Sources = sources
                    };
                    grouped.Add(identity, group);
                }

                group.Keys.Add(duplicate.Key);
            }

            return grouped.Values
                .OrderByDescending(group => group.DefinitionsDiffer)
                .ThenByDescending(group => group.Keys.Count)
                .ThenBy(group => group.Keys.FirstOrDefault() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static string AssetPackModeText()
        {
            if (FuseSettings.MirrorAssetPacksToLocalLow)
            {
                return "LocalLow mirror fallback";
            }

            return "Direct stores";
        }

        private static void GetDuplicateAssetInfo(FuseDuplicateAssetKey duplicateAssetKey, out string winner, out string[] overridden, out string suffix)
        {
            winner = string.Empty;
            overridden = [];
            suffix = string.Empty;

            if (duplicateAssetKey == null)
            {
                return;
            }

            var sources = duplicateAssetKey.Sources ?? [];
            var preview = sources
                .Where(source => !string.IsNullOrWhiteSpace(source))
                .ToArray();

            if (preview.Length == 0)
            {
                return;
            }

            winner = preview[0];
            overridden = preview.Skip(1).ToArray();

            if (overridden.Length == 0)
            {
                suffix = sources.Length > preview.Length ? " +" + (sources.Length - preview.Length) + " more" : string.Empty;
            }
        }

        private static void BuildDuplicateAssetGroupPreview(UIPanelBuilder builder, FuseDuplicateAssetGroup group)
        {
            if (group == null)
            {
                return;
            }

            GetDuplicateAssetInfo(group.Sources, out string winner, out string[] overridden, out string suffix);

            builder.AddField("Affected Keys", group.Keys.Count.ToString());
            builder.AddField("Definition Match", group.DefinitionsDiffer ? "Different definitions (review)" : "Identical copies");

            if (string.IsNullOrWhiteSpace(winner))
            {
                return;
            }

            builder.AddField("Source Used", winner + suffix);

            if (overridden.Length > 0)
            {
                builder.AddField("Sources Overridden", string.Join(",", overridden) + suffix);
            }

            AddWrappedField(
                builder,
                "Example Keys",
                string.Join(", ", group.Keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase).Take(4)),
                44f);
            AddWrappedField(
                builder,
                "Impact",
                group.DefinitionsDiffer
                    ? "Definitions differ; FUSE uses the listed winner, so model or behavior can change with source order."
                    : "Definitions are identical; this is redundant installed content, not a load failure.",
                44f);
        }

        private static string BuildDuplicateAssetGroupPreviewString(FuseDuplicateAssetGroup group)
        {
            if (group == null)
            {
                return string.Empty;
            }

            GetDuplicateAssetInfo(group.Sources, out string winner, out string[] overridden, out string suffix);
            var examples = string.Join(", ", group.Keys
                .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .Select(InsertBreakHints));
            var impact = group.DefinitionsDiffer
                ? "different definitions; winner controls behavior"
                : "identical copies; no behavior change";

            return overridden.Length == 0
                ? $"{group.Keys.Count} key(s) | winner {winner}{suffix} | {impact} | examples {examples}"
                : $"{group.Keys.Count} key(s) | winner {winner} | overridden {string.Join(", ", overridden)}{suffix} | {impact} | examples {examples}";
        }

        private static void GetDuplicateAssetInfo(
            string[] sources,
            out string winner,
            out string[] overridden,
            out string suffix)
        {
            GetDuplicateAssetInfo(
                new FuseDuplicateAssetKey { Sources = sources ?? Array.Empty<string>() },
                out winner,
                out overridden,
                out suffix);
        }

        private static string ExportAssetDiagnostics(FuseAssetPackDiagnostics diagnostics, bool openFolder = true)
        {
            try
            {
                var root = Path.Combine(Application.persistentDataPath, "FUSE");
                Directory.CreateDirectory(root);
                var path = Path.Combine(root, "fuse-asset-diagnostics.json");

                var stores = new JArray();
                foreach (var folder in diagnostics.StoreFolders ?? Array.Empty<string>())
                {
                    stores.Add(folder);
                }

                var duplicates = new JArray();
                foreach (var duplicate in diagnostics.DuplicateKeys ?? Array.Empty<FuseDuplicateAssetKey>())
                {
                    var sources = new JArray();
                    foreach (var source in duplicate.Sources ?? Array.Empty<string>())
                    {
                        sources.Add(source);
                    }

                    duplicates.Add(new JObject
                    {
                        ["key"] = duplicate.Key ?? string.Empty,
                        ["definitionsDiffer"] = duplicate.DefinitionsDiffer,
                        ["sourceCount"] = sources.Count,
                        ["winner"] = sources.Count > 0 ? sources[0] : string.Empty,
                        ["overridden"] = new JArray(sources.Skip(1)),
                        ["sources"] = sources
                    });
                }

                var failedDefinitions = new JArray();
                foreach (var failure in diagnostics.FailedDefinitionLoads ?? Array.Empty<string>())
                {
                    failedDefinitions.Add(failure);
                }

                var definitionOverrides = new JArray();
                foreach (var definitionOverride in diagnostics.LegacyDefinitionOverrides ??
                         Array.Empty<FuseLegacyDefinitionOverrideRegistration>())
                {
                    definitionOverrides.Add(new JObject
                    {
                        ["storeIdentifier"] = definitionOverride.StoreIdentifier ?? string.Empty,
                        ["definitionsPath"] = definitionOverride.DefinitionsPath ?? string.Empty,
                        ["packageId"] = definitionOverride.PackageId ?? string.Empty,
                        ["packagePath"] = definitionOverride.PackagePath ?? string.Empty,
                        ["explicit"] = definitionOverride.Explicit
                    });
                }

                var definitionOverrideIssues = new JArray();
                foreach (var issue in diagnostics.LegacyDefinitionOverrideIssues ?? Array.Empty<string>())
                {
                    definitionOverrideIssues.Add(issue);
                }

                var report = new JObject
                {
                    ["exportedUtc"] = DateTime.UtcNow.ToString("O"),
                    ["mode"] = AssetPackModeText(),
                    ["storesScanned"] = stores.Count,
                    ["uniqueAssetKeys"] = diagnostics.UniqueAssetKeys,
                    ["duplicateKeyCount"] = duplicates.Count,
                    ["failedDefinitionLoadCount"] = failedDefinitions.Count,
                    ["definitionOverrideCount"] = definitionOverrides.Count,
                    ["definitionOverrideIssueCount"] = definitionOverrideIssues.Count,
                    ["stores"] = stores,
                    ["duplicateKeys"] = duplicates,
                    ["failedDefinitionLoads"] = failedDefinitions,
                    ["definitionOverrides"] = definitionOverrides,
                    ["definitionOverrideIssues"] = definitionOverrideIssues
                };

                File.WriteAllText(path, report.ToString(Newtonsoft.Json.Formatting.Indented));

                if (openFolder)
                {
                    string directoryPath = Path.GetDirectoryName(path);
                    Application.OpenURL(directoryPath);
                }

                return "Exported FUSE asset diagnostics";
            }
            catch (Exception e)
            {
                var message = "Failed to export FUSE asset diagnostics";
                FuseLog.Exception(message, e);
                return message;
            }
        }
    }

    internal sealed class FuseDuplicateAssetGroup
    {
        internal bool DefinitionsDiffer { get; set; }

        internal string[] Sources { get; set; } = Array.Empty<string>();

        internal List<string> Keys { get; } = new List<string>();
    }
}
