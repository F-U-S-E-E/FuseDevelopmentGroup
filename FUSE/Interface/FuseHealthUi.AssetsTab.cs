using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FUSE.Runtime.API;
using FUSE.Runtime.Cache;
using FUSE.Authoring.Data;
using FUSE.Infrastructure;
using FUSE.Runtime.Lifecycle;
using FUSE.Loading;
using FUSE.Authoring.Migrations;
using FUSE.Runtime.Registry;
using Model;
using Model.Ops;
using Newtonsoft.Json.Linq;
using Railloader;
using TMPro;
using Track;
using UI;
using UI.Builder;
using UI.Common;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace FUSE.Interface
{
    internal sealed partial class FuseHealthUi : MonoBehaviour
    {

        private void BuildAssetsContent(UIPanelBuilder builder)
        {
            builder.FieldLabelWidth = 170f;
            builder.Spacing = 6f;

            var diagnostics = FuseAssetPackRegistry.GetDiagnostics();
            builder.AddSection("Asset Resolution");
            AddValueField(builder, "Mode", AssetPackModeText());
            AddValueField(builder, "Stores Scanned", diagnostics.StoreFolders.Length.ToString());
            AddValueField(builder, "Runtime Stores", FusePerformanceMetrics.FormatCount("direct asset pack store count"));
            AddValueField(builder, "Unique Asset Keys", diagnostics.UniqueAssetKeys.ToString());
            AddValueField(builder, "Duplicate Keys", diagnostics.DuplicateKeys.Length.ToString());
            AddValueField(builder, "Failed Definitions", diagnostics.FailedDefinitionLoads.Length.ToString());
            AddValueField(builder, "Last Direct Mount", FusePerformanceMetrics.FormatTiming("direct asset pack stores"));
            AddWrappedField(
                builder,
                "Duplicates",
                diagnostics.DuplicateKeys.Length == 0
                    ? "None detected."
                    : "Overlap diagnostics, not automatic errors. Export the report to see every duplicate key and source.",
                44f);
            builder.HStack(row =>
            {
                row.AddButtonCompact("Export Asset Report", () =>
                {
                    RunAction("export asset diagnostics", () => ExportAssetDiagnostics(diagnostics));
                });
                row.AddButtonCompact("Copy Asset Summary", () =>
                {
                    GUIUtility.systemCopyBuffer = BuildAssetSummary(diagnostics);
                    _lastAction = "Copied FUSE asset summary to clipboard.";
                    RebuildWindow();
                });
            }, 6f).Height(32f);
            AddWrappedLabel(builder, _lastAction, 34f);
            builder.Spacer(4f);

            if (!FuseSettings.ShowAdvancedHealthDetails && diagnostics.DuplicateKeys.Length == 0 && diagnostics.FailedDefinitionLoads.Length == 0)
            {
                AddValueField(builder, "Status", "No asset issues detected");
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
                    AddValueField(builder, "Status", "None detected");
                }
                else
                {
                    foreach (var duplicate in diagnostics.DuplicateKeys.Take(20))
                    {
                        AddWrappedLabel(builder, BuildDuplicateAssetPreview(duplicate), 52f);
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

        private static string ExportAssetDiagnostics(FuseAssetPackDiagnostics diagnostics)
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
                    builder.AppendLine("- " + BuildDuplicateAssetPreview(duplicate));
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

        private static string BuildDuplicateAssetPreview(FuseDuplicateAssetKey duplicate)
        {
            if (duplicate == null)
            {
                return string.Empty;
            }

            var sources = duplicate.Sources ?? Array.Empty<string>();
            var preview = sources
                .Take(3)
                .Select(source =>
                {
                    var name = string.IsNullOrWhiteSpace(source) ? string.Empty : Path.GetFileName(source);
                    return string.IsNullOrWhiteSpace(name) ? source : name;
                })
                .Where(source => !string.IsNullOrWhiteSpace(source))
                .ToArray();

            var suffix = sources.Length > preview.Length ? " +" + (sources.Length - preview.Length) + " more" : string.Empty;
            if (preview.Length == 0)
            {
                return InsertBreakHints(duplicate.Key);
            }

            var winner = preview[0];
            var overridden = preview.Skip(1).ToArray();
            return overridden.Length == 0
                ? $"{InsertBreakHints(duplicate.Key)} | winner {winner}{suffix}"
                : $"{InsertBreakHints(duplicate.Key)} | winner {winner} | overridden {string.Join(", ", overridden)}{suffix}";
        }

        private static string FriendlyTimingText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            return InsertBreakHints(value
                .Replace("__merged-graph-rebuild__", "merged graph rebuild")
                .Replace("__merged-world-suppressions__", "merged suppressions")
                .Replace("merged-single-graph-rebuild", "single graph rebuild")
                .Replace("apply-resident-definitions", "runtime apply"));
        }

        private static string BuildSlowestDetailText()
        {
            var package = FriendlyTimingText(FusePerformanceMetrics.FormatSlowestApplyPackage());
            var phase = FriendlyTimingText(FusePerformanceMetrics.FormatSlowestApplyPhase());
            if (string.IsNullOrWhiteSpace(phase))
            {
                return "No timing sample yet.";
            }

            if (NormalizeTimingName(package).StartsWith("merged graph rebuild", StringComparison.OrdinalIgnoreCase) &&
                NormalizeTimingName(phase).Contains("single graph rebuild"))
            {
                return "single graph rebuild inside merged graph rebuild";
            }

            return phase;
        }

        private static string NormalizeTimingName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var paren = value.IndexOf('(');
            return (paren >= 0 ? value.Substring(0, paren) : value).Trim();
        }
    }
}
