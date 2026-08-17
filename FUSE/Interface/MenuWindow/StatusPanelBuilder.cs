using FUSE.Infrastructure;
using FUSE.Loading;
using FUSE.Runtime.Lifecycle;
using FUSE.Authoring.Migrations;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;
using System.Text;
using UI.Builder;
using UI.Common;
using UnityEngine;

namespace FUSE.Interface.MenuWindow
{
    internal struct StatusPanelBuilder
    {
        private struct StatusChecklistData
        {
            public bool HasProblems;
            public int LoadedPackagesCount;
            public int AppliedPackagesCount;
            public int FaultCount;
            public int ConflictCount;
            public int UnknownAssetCount;
            public int GraphIssueCount;
            public int ProgressionTransferSkipCount;
            public int NoticesCount;
        }

        public static void Build(UIPanelBuilder builder)
        {
            // The status page can grow substantially when runtime guards or
            // third-party mods report problems. Give the complete page its own
            // viewport so diagnostics never spill into the action rows or out
            // of the window.
            builder.VScrollView(BuildScrollableContent, new RectOffset(0, 8, 0, 0));
        }

        private static void BuildScrollableContent(UIPanelBuilder builder)
        {
            builder.AddTitle("FUSE Status", "");

            var reportSnapshot = FuseLoadReport.GetLastReportSnapshot();
            var checklistData = GetStatusChecklistData(reportSnapshot);

            if (!checklistData.HasValue)
            {
                builder.AddLabel("FUSE report is still pending.");
                return;
            }
            var data = checklistData.Value;

            builder.AddSection("Overview");

            builder.AddField("Status",
                builder.AddLabelMarkup(data.HasProblems
                ? "<color=\"red\">Needs Attention</color> - FUSE found items that need review before a clean session."
                : "<color=\"green\">OK</color> - Full stack loaded cleanly. No package faults, asset misses, graph issues, or transfer skips are reported."));

            builder.AddField("Version", "FUSE " + ReadVersion() + " - Schema " + FuseMigration.CurrentVersion + " - Converter 0.2.0");

            AddUpdateNotice(builder);

            builder.AddSection("Checklist");
            AddReadinessRow(builder, "Packages", data.FaultCount == 0, $"{data.AppliedPackagesCount}/{data.LoadedPackagesCount} applied", data.FaultCount + " fault(s)");
            AddReadinessRow(builder, "Assets", data.UnknownAssetCount == 0, "0 unknown assets", data.UnknownAssetCount + " unknown");
            AddReadinessRow(builder, "Track Graph", data.GraphIssueCount == 0, "0 graph issues", data.GraphIssueCount + " issue(s)");
            AddReadinessRow(builder, "Progression", data.ProgressionTransferSkipCount == 0, "0 transfer skips", data.ProgressionTransferSkipCount + " skip(s)");
            AddReadinessRow(builder, "Registry", data.ConflictCount == 0, "0 conflicts", data.ConflictCount + " conflict(s)");
            AddReadinessRow(builder, "Notices", data.NoticesCount == 0, "0 notices", data.NoticesCount + " notice(s)");
            // Live session counters, not snapshot state.
            AddReadinessRow(
                builder,
                "Guards",
                FuseRuntimeGuardCounters.AllIdle,
                "idle",
                FuseRuntimeGuardCounters.GuardTotal + " contained event(s)");
            // Session-cumulative third-party exception observations — same
            // live-counter semantics as Guards, sourced from the exception
            // registry rather than the load snapshot. One atomic capture here
            // (rows + totals + summary line under the registry's lock), reused
            // by the readiness row and the breakdown section below so a
            // concurrent log event can never render contradictory rows.
            var modExceptionState = FuseModExceptionRegistry.CaptureReportState();
            var modExceptions = modExceptionState.Mods;
            AddReadinessRow(
                builder,
                "Mod Health",
                modExceptionState.Total == 0,
                "0 exceptions observed",
                $"{modExceptionState.Total} exception(s) across {modExceptions.Length} mod(s)");
            builder.Spacer(6f);

            // Full per-guard breakdown (this window is the only UI surface, so
            // the counters must be readable here, not just in copied reports).
            builder.AddSection("Runtime Guards");
            InterfaceUtils.AddWrappedLabel(builder, FuseRuntimeGuardCounters.FormatSummary(), 76f);
            builder.AddField(
                "Native leak stacks",
                $"{FuseNativeLeakDiagnostic.ModeLabel} (FUSE setting: {(FuseSettings.EnableNativeLeakStackTraces ? "enabled" : "disabled")})");
            InterfaceUtils.AddWrappedLabel(
                builder,
                FuseRuntimeGuardCounters.AllIdle
                    ? "All idle — no broken content needed containing this session."
                    : "Non-zero counters are content problems FUSE is containing; offenders are named in FUSE.log and the health report.",
                48f);
            builder.Spacer(6f);

            // Per-mod breakdown for the third-party exception registry,
            // mirroring the Runtime Guards treatment above (this window is
            // the only UI surface, so the observations must be readable
            // here, not just in copied reports).
            builder.AddSection("Mod Health");
            builder.AddLabel(modExceptionState.SummaryLine);
            if (modExceptions.Length > 0)
            {
                foreach (var record in modExceptions.OrderByDescending(item => item.Count).Take(5))
                {
                    var display = string.IsNullOrWhiteSpace(record.DisplayName) ? record.ModId : record.DisplayName;
                    InterfaceUtils.AddWrappedField(builder, display, DescribeModExceptionRecord(record), 52f);
                }

                if (modExceptions.Length > 5)
                {
                    InterfaceUtils.AddWrappedLabel(
                        builder,
                        $"...and {modExceptions.Length - 5} more mod(s) — full list in the health report.",
                        28f);
                }
            }

            InterfaceUtils.AddWrappedLabel(
                builder,
                modExceptionState.Total == 0
                    ? "All idle — no third-party mod exceptions were observed this session."
                    : "Non-zero counts are third-party mod faults FUSE observed or contained; offenders are named in FUSE.log and the health report.",
                48f);
            builder.Spacer(6f);

            builder.AddSection("Actions");
            builder.HStack(row =>
            {
                row.AddButtonCompact("Copy Readiness", () =>
                {
                    var report = LoadReportJson();
                    GUIUtility.systemCopyBuffer = BuildStreamReadinessReport(report);
                    Toast.Present("Copied FUSE readiness report to clipboard.");
                    builder.Rebuild();
                });
                row.AddButtonCompact("Copy Health Report", () =>
                {
                    GUIUtility.systemCopyBuffer = FuseLoadReport.GetLastDetailReport();
                    Toast.Present("Copied FUSE health report to clipboard.");
                    builder.Rebuild();
                });
            }, 6f).Height(32f);

            builder.AddSection("Export");
            builder.HStack(row =>
            {
                row.AddButtonCompact("Export and open JSON", () =>
                {
                    var message = ExportHealthReportJson(openFolder: true);
                    Toast.Present(message);
                    builder.Rebuild();
                });
                row.AddButtonCompact("Export and open Mod Manifest", () =>
                {
                    var message = ExportActiveModManifest(openFolder: true);
                    Toast.Present(message);
                    builder.Rebuild();
                });
            }, 6f).Height(32f);
        }

        // Renders the "an update is available" row when the startup check found a
        // newer stable release. Silent otherwise (up to date, disabled, offline,
        // or still checking), so the Status page never nags a current install.
        private static void AddUpdateNotice(UIPanelBuilder builder)
        {
            if (!FuseVersionCheck.UpdateAvailable)
            {
                return;
            }

            var where = FuseInstallSource.DescribeChannel(FuseVersionCheck.Channel);
            builder.AddField(
                "Update",
                builder.AddLabelMarkup(
                    $"<color=\"yellow\">Available</color> - FUSE {FuseVersionCheck.LatestVersionText} is out " +
                    $"(you have {FuseVersionCheck.CurrentVersionText})."));
            builder.HStack(row =>
            {
                row.AddButtonCompact($"Get it from {where}", () =>
                {
                    Application.OpenURL(FuseVersionCheck.ResolveUpdateUrl());
                    Toast.Present($"Opening the FUSE {where} download page in your browser.");
                });
            }, 6f).Height(32f);
        }

        private static string ReadVersion()
        {
            try
            {
                var infoPath = Path.Combine(FusePlugin.ModEntry?.Path ?? string.Empty, "Info.json");
                if (!File.Exists(infoPath))
                {
                    return "unknown";
                }

                var info = JObject.Parse(File.ReadAllText(infoPath));
                return ReadString(info["Version"], "unknown");
            }
            catch
            {
                return "unknown";
            }
        }

        private static string ReadString(JToken token, string fallback)
        {
            var value = token == null ? string.Empty : token.ToString();
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        private static int ReadInt(JToken token)
        {
            return token != null && int.TryParse(token.ToString(), out var value) ? value : 0;
        }

        private static bool ReadBool(JToken token, bool fallback)
        {
            return token != null && bool.TryParse(token.ToString(), out var value) ? value : fallback;
        }

        /// <summary>
        /// One-line per-mod value for the Mod Health breakdown: counts plus
        /// the mod's top signature (by count), matching the per-mod row the
        /// health report renders so the two surfaces read the same.
        /// </summary>
        private static string DescribeModExceptionRecord(FuseModExceptionSnapshot record)
        {
            var text = $"{record.Count} exception(s) over {record.Episodes} episode(s)";
            var signatures = record.Signatures;
            if (signatures != null && signatures.Length > 0)
            {
                var top = signatures.OrderByDescending(item => item.Count).First();
                text += $" — top: {top.ExceptionType} @ {top.TopOwnedFrame}";
            }

            return text;
        }

        private static void AddReadinessRow(UIPanelBuilder builder, string label, bool ok, string okText, string problemText)
        {
            var value = ok
                ? $"<color=\"green\">OK</color> - {okText}"
                : $"<color=\"red\">Review</color> - {problemText}";
            builder.AddField(label, builder.AddLabelMarkup(value));
        }

        private static StatusChecklistData? GetStatusChecklistData(FuseLoadReport.ReportSnapshot reportSnapshot)
        {
            if (reportSnapshot == null)
            {
                return null;
            }
            else
            {
                return new StatusChecklistData
                {
                    HasProblems = reportSnapshot.HasProblems,
                    AppliedPackagesCount = reportSnapshot.AppliedPackageIds.Length,
                    ConflictCount = reportSnapshot.Conflicts.Length,
                    FaultCount = reportSnapshot.Faults.Length,
                    GraphIssueCount = reportSnapshot.GraphPostBindIssues.Length,
                    LoadedPackagesCount = reportSnapshot.LoadedPackageIds.Length,
                    NoticesCount = reportSnapshot.Notices.Length,
                    ProgressionTransferSkipCount = reportSnapshot.ProgressionTransferSkips.Length,
                    UnknownAssetCount = reportSnapshot.UnknownSceneryAssets.Length
                };
            }
        }

        private static JObject LoadReportJson()
        {
            try
            {
                return JObject.Parse(FuseLoadReport.GetLastJsonReport());
            }
            catch
            {
                return new JObject
                {
                    ["summary"] = FuseLoadReport.LastSummary,
                    ["hasProblems"] = false,
                    ["counts"] = new JObject()
                };
            }
        }

        private static string BuildStreamReadinessReport(JObject report)
        {
            var counts = report?["counts"] as JObject ?? new JObject();
            var builder = new StringBuilder();
            builder.AppendLine("FUSE Readiness");
            builder.AppendLine("State: " + (ReadBool(report?["hasProblems"], false) ? "Needs Attention" : "Ready"));
            builder.AppendLine("Summary: " + ReadString(report?["summary"], FuseLoadReport.LastSummary));
            builder.AppendLine("Version: FUSE " + ReadVersion() + " | Schema " + FuseMigration.CurrentVersion + " | Converter 0.2.0");
            builder.AppendLine("Profile: " + FuseModSetService.ActiveSetName);
            builder.AppendLine("Profile Hash: " + FuseModSetService.GetActiveSetFingerprint());
            builder.AppendLine("Loaded Packages: " + ReadInt(counts["loadedPackages"]));
            builder.AppendLine("Applied Packages: " + ReadInt(counts["appliedPackages"]));
            builder.AppendLine("Faults: " + ReadInt(counts["faultedPackages"]));
            builder.AppendLine("Conflicts: " + ReadInt(counts["conflicts"]));
            builder.AppendLine("Unknown Assets: " + ReadInt(counts["unknownSceneryAssets"]));
            builder.AppendLine("Graph Issues: " + ReadInt(counts["graphIssues"]));
            builder.AppendLine("Transfer Skips: " + ReadInt(counts["progressionTransferSkips"]));
            builder.AppendLine("Suppressions: " + ReadInt(counts["suppressions"]));
            builder.AppendLine("Runtime Guards: " + FuseRuntimeGuardCounters.FormatSummary());
            builder.AppendLine(
                "Native Leak Detection: " + FuseNativeLeakDiagnostic.ModeLabel +
                " (FUSE stack setting " + (FuseSettings.EnableNativeLeakStackTraces ? "enabled" : "disabled") + ")");
            builder.AppendLine("Map Load: " + FusePerformanceMetrics.FormatTiming("map load total"));
            builder.AppendLine("Runtime Apply: " + FusePerformanceMetrics.FormatTiming("apply resident definitions"));
            return builder.ToString().TrimEnd();
        }

        private static string ExportHealthReportJson(bool openFolder = true)
        {
            try
            {
                var root = Path.Combine(Application.persistentDataPath, "FUSE");
                Directory.CreateDirectory(root);
                var path = Path.Combine(root, "fuse-health-report.json");
                File.WriteAllText(path, FuseLoadReport.GetLastJsonReport());
                if (openFolder)
                {
                    string directoryPath = Path.GetDirectoryName(path);
                    Application.OpenURL(directoryPath);
                }
                return "Exported FUSE health report";
            }
            catch (Exception e)
            {
                var message = "Failed to export FUSE health report";
                FuseLog.Exception(message, e);
                return message;
            }
        }

        private static string ExportActiveModManifest(bool openFolder = true)
        {
            try
            {
                var path = FuseModSetService.ExportActiveManifest();
                if (openFolder)
                {
                    string directoryPath = Path.GetDirectoryName(path);
                    Application.OpenURL(directoryPath);
                }
                return "Exported FUSE active mod profile manifest";
            }
            catch (Exception e)
            {
                var message = "Failed to export FUSE active mod profile manifest";
                FuseLog.Exception(message, e);
                return message;
            }
        }
    }
}
