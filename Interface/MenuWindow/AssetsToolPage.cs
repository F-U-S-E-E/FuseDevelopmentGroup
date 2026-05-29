using FUSE.Infrastructure;
using FUSE.Loading;
using Newtonsoft.Json.Linq;
using System;
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
            builder.AddSection("Asset Resolution");
            builder.AddField("Mode", AssetPackModeText());
            builder.AddField("Stores Scanned", diagnostics.StoreFolders.Length.ToString());
            builder.AddField("Runtime Stores", FusePerformanceMetrics.FormatCount("direct asset pack store count"));
            builder.AddField("Unique Asset Keys", diagnostics.UniqueAssetKeys.ToString());
            builder.AddField("Duplicate Keys", diagnostics.DuplicateKeys.Length.ToString());
            builder.AddField("Failed Definitions", diagnostics.FailedDefinitionLoads.Length.ToString());
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
                row.AddButtonCompact("Export Asset Report", () =>
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

            if (!FuseSettings.ShowAdvancedHealthDetails && diagnostics.DuplicateKeys.Length == 0 && diagnostics.FailedDefinitionLoads.Length == 0)
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
                builder.AddSection("Duplicate Asset Keys");
                if (diagnostics.DuplicateKeys.Length == 0)
                {
                    builder.AddField("Status", "None detected");
                }
                else
                {
                    foreach (var duplicate in diagnostics.DuplicateKeys.Take(20))
                    {
                        BuildDuplicateAssetPreview(builder, duplicate);
                        builder.Spacer(4f);
                        builder.AddHRule();
                    }

                    if (diagnostics.DuplicateKeys.Length > 20)
                    {
                        AddWrappedField(
                            builder,
                            "More",
                            (diagnostics.DuplicateKeys.Length - 20) + " hidden. Export Asset Report for all duplicate keys.",
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
            }

            builder.Spacer(8f);
        }

        private static string BuildAssetSummary(FuseAssetPackDiagnostics diagnostics)
        {
            var builder = new StringBuilder();
            builder.AppendLine("FUSE Asset Summary");
            builder.AppendLine("Mode: " + AssetPackModeText());
            builder.AppendLine("Stores scanned: " + (diagnostics.StoreFolders?.Length ?? 0));
            builder.AppendLine("Unique asset keys: " + diagnostics.UniqueAssetKeys);
            builder.AppendLine("Duplicate keys: " + (diagnostics.DuplicateKeys?.Length ?? 0));
            builder.AppendLine("Failed definitions: " + (diagnostics.FailedDefinitionLoads?.Length ?? 0));

            var duplicates = diagnostics.DuplicateKeys ?? Array.Empty<FuseDuplicateAssetKey>();
            if (duplicates.Length > 0)
            {
                builder.AppendLine();
                builder.AppendLine("Duplicate preview:");
                foreach (var duplicate in duplicates.Take(10))
                {
                    builder.AppendLine("- " + BuildDuplicateAssetPreviewString(duplicate));
                }

                if (duplicates.Length > 10)
                {
                    builder.AppendLine("- " + (duplicates.Length - 10) + " more duplicate key(s); export the asset report for the full list.");
                }
            }

            return builder.ToString().TrimEnd();
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
                .Select(source =>
                {
                    var name = string.IsNullOrWhiteSpace(source) ? string.Empty : Path.GetFileName(source);
                    return string.IsNullOrWhiteSpace(name) ? source : name;
                })
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

        private static void BuildDuplicateAssetPreview(UIPanelBuilder builder, FuseDuplicateAssetKey duplicate)
        {
            if (duplicate == null) return;

            GetDuplicateAssetInfo(duplicate, out string winner, out string[] overridden, out string suffix);

            builder.AddField("Asset Key", duplicate.Key);

            if (string.IsNullOrWhiteSpace(winner))
            {
                return;
            }

            builder.AddField("Source Used", winner + suffix);

            if (overridden.Length > 0)
            {
                builder.AddField("Sources Overridden", string.Join(",", overridden) + suffix);
            }
        }

        private static string BuildDuplicateAssetPreviewString(FuseDuplicateAssetKey duplicate)
        {
            if (duplicate == null)
            {
                return string.Empty;
            }

            GetDuplicateAssetInfo(duplicate, out string winner, out string[] overridden, out string suffix);

            return overridden.Length == 0
                ? $"{InsertBreakHints(duplicate.Key)} | winner {winner}{suffix}"
                : $"{InsertBreakHints(duplicate.Key)} | winner {winner} | overridden {string.Join(", ", overridden)}{suffix}";
        }

        private static string ExportAssetDiagnostics(FuseAssetPackDiagnostics diagnostics)
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

                var report = new JObject
                {
                    ["exportedUtc"] = DateTime.UtcNow.ToString("O"),
                    ["mode"] = AssetPackModeText(),
                    ["storesScanned"] = stores.Count,
                    ["uniqueAssetKeys"] = diagnostics.UniqueAssetKeys,
                    ["duplicateKeyCount"] = duplicates.Count,
                    ["failedDefinitionLoadCount"] = failedDefinitions.Count,
                    ["stores"] = stores,
                    ["duplicateKeys"] = duplicates,
                    ["failedDefinitionLoads"] = failedDefinitions
                };

                File.WriteAllText(path, report.ToString(Newtonsoft.Json.Formatting.Indented));
                return "Exported FUSE asset diagnostics: " + path;
            }
            catch (Exception e)
            {
                var message = "Failed to export FUSE asset diagnostics";
                FuseLog.Exception(message, e);
                return message;
            }
        }
    }
}
