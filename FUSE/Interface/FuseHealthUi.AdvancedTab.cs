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

        private void BuildAdvancedContent(UIPanelBuilder builder)
        {
            builder.FieldLabelWidth = 170f;
            builder.Spacing = 6f;

            builder.AddSection("Developer Workbench");
            AddWrappedField(
                builder,
                "Mode",
                "Advanced tools are for live Unity/Railroader inspection, cache rebuilds, and FUSE compatibility debugging. They are intentionally separated from the stream-ready status pages.",
                58f);
            AddSettingToggle(
                builder,
                "Advanced Details",
                FuseSettings.ShowAdvancedHealthDetails ? "enabled" : "disabled",
                FuseSettings.ShowAdvancedHealthDetails ? "Hide" : "Show",
                () =>
                {
                    FuseSettings.SetShowAdvancedHealthDetails(!FuseSettings.ShowAdvancedHealthDetails);
                    RebuildWindow();
                });
            builder.Spacer(4f);

            builder.AddSection("Unity Runtime");
            AddUnityRuntimeFields(builder);
            builder.Spacer(4f);

            builder.AddSection("Railroader Runtime");
            AddRailroaderRuntimeFields(builder);
            builder.Spacer(4f);

            builder.AddSection("Object Finder");
            BuildAdvancedObjectFinder(builder);
            builder.Spacer(4f);

            builder.AddSection("FUSE Registry");
            AddValueField(builder, "Exclusive Claims", FUSE.Runtime.Registry.FuseRegistry.ExclusiveClaimCount.ToString());
            AddValueField(builder, "Shared Claims", FUSE.Runtime.Registry.FuseRegistry.SharedClaimCount.ToString());
            AddValueField(builder, "Conflicts", FUSE.Runtime.Registry.FuseRegistry.Conflicts.Count.ToString());
            AddValueField(builder, "Asset Stores", FusePerformanceMetrics.FormatCount("direct asset pack store count"));
            builder.Spacer(4f);

            builder.AddSection("Runtime Actions");
            builder.HStack(row =>
            {
                row.AddButtonCompact("Reload Track/Data", () =>
                {
                    RunAction("reload track and data", () =>
                    {
                        var applied = FuseRuntimeReloadService.ReloadTrackAndData("FUSE advanced page reload track/data");
                        return $"Reload Track/Data complete. Applied {applied} resident definition(s).";
                    });
                });
                row.AddButtonCompact("Reload Terrain", () =>
                {
                    RunAction("reload terrain", () =>
                        FuseRuntimeReloadService.ReloadTerrain("FUSE advanced page reload terrain")
                            ? "Reload Terrain complete."
                            : "Reload Terrain skipped or failed. See FUSE.log.");
                });
                row.AddButtonCompact("Rebuild Caches", () =>
                {
                    RunAction("rebuild caches", () =>
                    {
                        FuseCacheRegistry.RebuildAll();
                        return "Rebuilt FUSE runtime caches.";
                    });
                });
            }, 6f).Height(32f);
            builder.HStack(row =>
            {
                row.AddButtonCompact("Copy Runtime Snapshot", () =>
                {
                    GUIUtility.systemCopyBuffer = BuildRuntimeSnapshotText();
                    _lastAction = "Copied FUSE runtime snapshot to clipboard.";
                    RebuildWindow();
                });
                row.AddButtonCompact("Export Debug Bundle", () =>
                {
                    RunAction("export debug bundle", ExportDebugBundle);
                });
                row.AddButtonCompact("Refresh", RebuildWindow);
            }, 6f).Height(32f);
            AddWrappedLabel(builder, _lastAction, 36f);
            builder.Spacer(4f);

            builder.AddSection("Debug Overlays");
            AddSettingToggle(
                builder,
                "Track Probe",
                FuseSettings.ShowTrackDebugOverlay ? "enabled on hover" : "disabled",
                FuseSettings.ShowTrackDebugOverlay ? "Disable" : "Enable",
                () =>
                {
                    FuseSettings.SetShowTrackDebugOverlay(!FuseSettings.ShowTrackDebugOverlay);
                    RebuildWindow();
                });
            AddSettingToggle(
                builder,
                "Track Span Paths",
                FuseSettings.ShowTrackDebugSpanPaths
                    ? (FuseSettings.ShowTrackDebugOverlay ? "shown in overlay" : "shown when track probe is on")
                    : "hidden",
                FuseSettings.ShowTrackDebugSpanPaths ? "Hide" : "Show",
                () =>
                {
                    FuseSettings.SetShowTrackDebugSpanPaths(!FuseSettings.ShowTrackDebugSpanPaths);
                    RebuildWindow();
                });
            AddSettingToggle(
                builder,
                "Scenery Probe",
                FuseSettings.ShowSceneryDebugOverlay ? "enabled on hover" : "disabled",
                FuseSettings.ShowSceneryDebugOverlay ? "Disable" : "Enable",
                () =>
                {
                    FuseSettings.SetShowSceneryDebugOverlay(!FuseSettings.ShowSceneryDebugOverlay);
                    RebuildWindow();
                });
            AddSettingToggle(
                builder,
                "Scenery Details",
                FuseSettings.ShowSceneryDebugAdvanced
                    ? (FuseSettings.ShowSceneryDebugOverlay ? "shown in overlay" : "shown when scenery probe is on")
                    : "hidden",
                FuseSettings.ShowSceneryDebugAdvanced ? "Hide" : "Show",
                () =>
                {
                    FuseSettings.SetShowSceneryDebugAdvanced(!FuseSettings.ShowSceneryDebugAdvanced);
                    RebuildWindow();
                });
            AddSettingToggle(
                builder,
                "World Labels",
                FuseSettings.ShowWorldLabelsOverlay ? "color-coded labels on every visible entity" : "disabled",
                FuseSettings.ShowWorldLabelsOverlay ? "Disable" : "Enable",
                () =>
                {
                    FuseSettings.SetShowWorldLabelsOverlay(!FuseSettings.ShowWorldLabelsOverlay);
                    RebuildWindow();
                });
            if (FuseSettings.ShowWorldLabelsOverlay)
            {
                AddSettingToggle(
                    builder,
                    "  Labels: Scenery",
                    FuseSettings.WorldLabelsShowScenery ? "shown (orange=FUSE, gray=vanilla)" : "hidden",
                    FuseSettings.WorldLabelsShowScenery ? "Hide" : "Show",
                    () =>
                    {
                        FuseSettings.SetWorldLabelsShowScenery(!FuseSettings.WorldLabelsShowScenery);
                        RebuildWindow();
                    });
                AddSettingToggle(
                    builder,
                    "  Labels: Scene Clones",
                    FuseSettings.WorldLabelsShowSceneClones ? "shown (cyan)" : "hidden",
                    FuseSettings.WorldLabelsShowSceneClones ? "Hide" : "Show",
                    () =>
                    {
                        FuseSettings.SetWorldLabelsShowSceneClones(!FuseSettings.WorldLabelsShowSceneClones);
                        RebuildWindow();
                    });
                AddSettingToggle(
                    builder,
                    "  Labels: Industries",
                    FuseSettings.WorldLabelsShowIndustries ? "shown (pink)" : "hidden",
                    FuseSettings.WorldLabelsShowIndustries ? "Hide" : "Show",
                    () =>
                    {
                        FuseSettings.SetWorldLabelsShowIndustries(!FuseSettings.WorldLabelsShowIndustries);
                        RebuildWindow();
                    });
                AddSettingToggle(
                    builder,
                    "  Labels: Track Nodes",
                    FuseSettings.WorldLabelsShowTrackNodes ? "shown (green) — dense" : "hidden",
                    FuseSettings.WorldLabelsShowTrackNodes ? "Hide" : "Show",
                    () =>
                    {
                        FuseSettings.SetWorldLabelsShowTrackNodes(!FuseSettings.WorldLabelsShowTrackNodes);
                        RebuildWindow();
                    });
                AddSettingToggle(
                    builder,
                    "  Labels: Track Segments",
                    FuseSettings.WorldLabelsShowTrackSegments ? "shown (yellow) — dense" : "hidden",
                    FuseSettings.WorldLabelsShowTrackSegments ? "Hide" : "Show",
                    () =>
                    {
                        FuseSettings.SetWorldLabelsShowTrackSegments(!FuseSettings.WorldLabelsShowTrackSegments);
                        RebuildWindow();
                    });
            }
            builder.Spacer(4f);

            builder.AddSection("Performance Diagnostics");
            AddSettingToggle(
                builder,
                "Scenery Cull Log",
                FuseSettings.EnableSceneryCullingDiagnostics ? "logging all scenery (FUSE + vanilla) load/unload to FUSE.log" : "disabled",
                FuseSettings.EnableSceneryCullingDiagnostics ? "Disable" : "Enable",
                () =>
                {
                    FuseSettings.SetEnableSceneryCullingDiagnostics(!FuseSettings.EnableSceneryCullingDiagnostics);
                    RebuildWindow();
                });
            AddWrappedField(
                builder,
                "Usage",
                "Toggle on, teleport into the heavy scene, then off. Each scenery load/unload flip is written to FUSE.log as 'scenery-cull' (fuse=true|false). Hot, repeating flips on the same object during the test point at culling churn (issue #76).",
                58f);
            builder.Spacer(4f);

            builder.AddSection("Scenery Load Benchmark");
            AddWrappedField(
                builder,
                "How",
                "Reproducible culling/streaming tests (issue #76). CORRIDOR teleports between Bryson and Sylva a few times, then drives the camera up and down the track between them at a set pace. SWEEP A/B is the quick local test (oscillates across the cull boundary at your current view). The DEBOUNCE A/B (Corridor/Sweep A/B) toggles culling hysteresis off vs on (churn). The THROTTLE A/B toggles the per-frame load cap off vs on (batch-load stall) — compare minFps and maxLoadMs. Be in the overview camera. Each run appends a summary to FUSE-scenery-benchmark.json and writes a per-frame CSV (FUSE-bench-*.csv: FPS, object counts, churn, defer/release, load latency, memory); live progress prints to FUSE.log.",
                150f);
            builder.HStack(row =>
            {
                row.AddButtonCompact("Run Corridor", () => RunAction("run corridor benchmark", FuseSceneryBenchmark.RunCorridor));
                row.AddButtonCompact("Corridor A/B", () => RunAction("run corridor debounce A/B benchmark", FuseSceneryBenchmark.RunCorridorAb));
                row.AddButtonCompact("Sweep A/B", () => RunAction("run sweep debounce A/B benchmark", FuseSceneryBenchmark.RunSweepAb));
            }, 6f).Height(32f);
            builder.HStack(row =>
            {
                row.AddButtonCompact("Corridor Throttle A/B", () => RunAction("run corridor throttle A/B benchmark", FuseSceneryBenchmark.RunCorridorThrottleAb));
                row.AddButtonCompact("Sweep Throttle A/B", () => RunAction("run sweep throttle A/B benchmark", FuseSceneryBenchmark.RunSweepThrottleAb));
                row.AddButtonCompact("Refresh", RebuildWindow);
            }, 6f).Height(32f);
            _lastBenchmarkStatus = FuseSceneryBenchmark.Status;
            AddWrappedLabel(builder, "Benchmark: " + _lastBenchmarkStatus, 48f);
            builder.Spacer(4f);

            builder.AddSection("Experimental");
            AddSettingToggle(
                builder,
                "Early Suppression",
                FuseSettings.EnableExperimentalEarlyScenePathSuppression ? "enabled next map load" : "disabled",
                FuseSettings.EnableExperimentalEarlyScenePathSuppression ? "Disable" : "Enable",
                () =>
                {
                    FuseSettings.SetEnableExperimentalEarlyScenePathSuppression(!FuseSettings.EnableExperimentalEarlyScenePathSuppression);
                    RebuildWindow();
                });
            AddWrappedField(
                builder,
                "Inspector Roadmap",
                "Next step: add a safe scene/object inspector inspired by UnityRuntimeEditor, scoped to Railroader objects, FUSE claims, component health, and non-destructive property probes.",
                58f);
            builder.Spacer(8f);
        }

        private void BuildAdvancedObjectFinder(UIPanelBuilder builder)
        {
            AddWrappedField(
                builder,
                "Scope",
                "Read-only search across FUSE runtime indexes and loaded Unity scene objects. Use ids, names, or scene path fragments.",
                48f);
            builder.AddField(
                "Search",
                builder.AddInputField(_advancedSearchTerm ?? string.Empty, value =>
                {
                    _advancedSearchTerm = value ?? string.Empty;
                })).Height(32f);
            builder.HStack(row =>
            {
                row.AddButtonCompact("Run Search", RebuildWindow);
                row.AddButtonCompact("Clear", () =>
                {
                    _advancedSearchTerm = string.Empty;
                    RebuildWindow();
                });
                row.AddButtonCompact("Copy Results", () =>
                {
                    GUIUtility.systemCopyBuffer = BuildObjectSearchReport(_advancedSearchTerm);
                    _lastAction = "Copied FUSE object search results to clipboard.";
                    RebuildWindow();
                });
            }, 6f).Height(32f);

            var term = (_advancedSearchTerm ?? string.Empty).Trim();
            if (term.Length < 2)
            {
                AddWrappedField(builder, "Results", "Enter at least 2 characters, then Run Search.", 34f);
                return;
            }

            var results = BuildObjectSearchResults(term, 35);
            if (results.Count == 0)
            {
                AddWrappedField(builder, "Results", "No matching runtime or scene objects.", 34f);
                return;
            }

            AddValueField(builder, "Matches", results.Count.ToString());
            foreach (var result in results.Take(18))
            {
                AddWrappedLabel(builder, InsertBreakHints(result), 38f);
            }

            if (results.Count > 18)
            {
                AddWrappedField(builder, "More", (results.Count - 18) + " hidden. Copy Results for a longer report.", 34f);
            }
        }

        private static List<string> BuildObjectSearchResults(string rawTerm, int limit)
        {
            var results = new List<string>();
            var term = (rawTerm ?? string.Empty).Trim();
            if (term.Length < 2)
            {
                return results;
            }

            limit = Math.Max(1, limit);
            AddRuntimeIndexMatches(results, "Track Node", FuseNodeRuntimeIndex.Instance, term, limit);
            AddRuntimeIndexMatches(results, "Track Segment", FuseSegmentRuntimeIndex.Instance, term, limit);
            AddRuntimeIndexMatches(results, "Track Span", FuseSpanRuntimeIndex.Instance, term, limit);
            AddRuntimeIndexMatches(results, "Area", FuseAreaRuntimeIndex.Instance, term, limit);
            AddRuntimeIndexMatches(results, "Load", FuseLoadRuntimeIndex.Instance, term, limit);
            AddRuntimeIndexMatches(results, "Industry", FuseIndustryRuntimeIndex.Instance, term, limit);
            AddRuntimeIndexMatches(results, "Industry Component", FuseIndustryComponentRuntimeIndex.Instance, term, limit);
            AddRuntimeIndexMatches(results, "Loader", FuseLoaderRuntimeIndex.Instance, term, limit);
            AddRuntimeIndexMatches(results, "Station", FuseStationRuntimeIndex.Instance, term, limit);
            AddRuntimeIndexMatches(results, "Scenery", FuseSceneryRuntimeIndex.Instance, term, limit);
            AddRuntimeIndexMatches(results, "Spliney", FuseSplineyRuntimeIndex.Instance, term, limit);
            AddRuntimeIndexMatches(results, "Map Label", FuseMapLabelRuntimeIndex.Instance, term, limit);
            AddRuntimeIndexMatches(results, "Progression", FuseProgressionRuntimeIndex.Instance, term, limit);
            AddRuntimeIndexMatches(results, "Map Feature", FuseMapFeatureRuntimeIndex.Instance, term, limit);
            AddSceneObjectMatches(results, term, limit);
            return results;
        }

        private static void AddRuntimeIndexMatches<TCache>(
            List<string> results,
            string kind,
            FuseRuntimeIndex<TCache> index,
            string term,
            int limit)
            where TCache : FuseRuntimeIndex<TCache>
        {
            if (results == null || results.Count >= limit || index == null)
            {
                return;
            }

            foreach (var id in index.Ids.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            {
                if (results.Count >= limit)
                {
                    return;
                }

                var runtime = index[id];
                var detail = FormatRuntimeObject(runtime);
                if (!MatchesSearch(id, term) && !MatchesSearch(detail, term))
                {
                    continue;
                }

                results.Add($"{kind} | {id} | {detail}");
            }
        }

        private static void AddSceneObjectMatches(List<string> results, string term, int limit)
        {
            if (results == null || results.Count >= limit)
            {
                return;
            }

            GameObject[] objects;
            try
            {
                objects = Resources.FindObjectsOfTypeAll<GameObject>();
            }
            catch
            {
                return;
            }

            foreach (var gameObject in objects
                         .Where(IsLoadedSceneObject)
                         .OrderBy(GetGameObjectPath, StringComparer.OrdinalIgnoreCase))
            {
                if (results.Count >= limit)
                {
                    return;
                }

                var path = GetGameObjectPath(gameObject);
                if (!MatchesSearch(gameObject.name, term) && !MatchesSearch(path, term))
                {
                    continue;
                }

                results.Add($"Scene Object | {path} | active={gameObject.activeInHierarchy} | components={FormatComponentList(gameObject)}");
            }
        }

        private static string BuildObjectSearchReport(string rawTerm)
        {
            var term = (rawTerm ?? string.Empty).Trim();
            var builder = new StringBuilder();
            builder.AppendLine("FUSE Object Search");
            builder.AppendLine("Search: " + (term.Length == 0 ? "(blank)" : term));
            builder.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            if (term.Length < 2)
            {
                builder.AppendLine("Enter at least 2 characters before searching.");
                return builder.ToString().TrimEnd();
            }

            var results = BuildObjectSearchResults(term, 200);
            builder.AppendLine("Matches: " + results.Count);
            foreach (var result in results)
            {
                builder.AppendLine("- " + result);
            }

            return builder.ToString().TrimEnd();
        }
    }
}
