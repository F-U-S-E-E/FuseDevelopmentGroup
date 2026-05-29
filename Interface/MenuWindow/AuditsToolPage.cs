using FUSE.API;
using FUSE.Infrastructure;
using FUSE.Loading;
using FUSE.Registry;
using Model;
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
    internal struct AuditsToolPage
    {
        private sealed class AuditFinding
        {
            public AuditFinding(string severity, string title, string objectId, string detail, string action)
            {
                Severity = string.IsNullOrWhiteSpace(severity) ? "Low" : severity;
                Title = title ?? string.Empty;
                ObjectId = objectId ?? string.Empty;
                Detail = detail ?? string.Empty;
                Action = action ?? string.Empty;
            }

            public string Severity { get; }
            public string Title { get; }
            public string ObjectId { get; }
            public string Detail { get; }
            public string Action { get; }
        }

        public static void Build(UIPanelBuilder builder)
        {
            builder.AddTitle("Diagnostics Report", "");

            builder.AddLabel("This diagnostic report tool allows you to run read-only checks for common Railroader/FUSE failure modes. These do not mutate the world. They only produce actionable diagnostics.");

            var findings = BuildAuditFindings();
            var blocking = findings.Count(finding => finding.Severity == "Critical" || finding.Severity == "High");
            var warnings = findings.Count(finding => finding.Severity == "Medium" || finding.Severity == "Low");

            builder.AddSection("Overview");
            AddValueField(builder, "Findings", findings.Count.ToString());
            AddValueField(builder, "Blocking", blocking.ToString());
            AddValueField(builder, "Warnings", warnings.ToString());

            builder.Spacer(12f);

            builder.HStack(row =>
            {
                row.AddButtonCompact("Run Diagnostics", builder.Rebuild);
                row.AddButtonCompact("Copy Report", () =>
                {
                    GUIUtility.systemCopyBuffer = BuildAuditReport(findings);
                    Toast.Present("Copied FUSE audit report to clipboard.");
                });
                row.AddButtonCompact("Export Report", () =>
                {
                    var message = ExportAuditReport(findings);
                    Toast.Present(message);
                });
            }, 6f).Height(32f);

            builder.Spacer(6f);

            if (findings.Count == 0)
            {
                builder.AddField("Status", builder.AddLabelMarkup("<color=\"green\">No audit findings."));
                builder.Spacer(8f);
                return;
            }

            builder.AddSection("Findings");
            foreach (var finding in findings.Take(30))
            {
                BuildFindingsEntry(builder, finding);
                builder.Spacer(6f);
                builder.AddHRule();
            }

            if (findings.Count > 30)
            {
                AddWrappedField(builder, "More", (findings.Count - 30) + " hidden. Copy or export the report for all findings.", 34f);
            }

            builder.Spacer(8f);
        }

        private static void BuildFindingsEntry(UIPanelBuilder builder, AuditFinding finding)
        {
            builder.AddField("Issue", finding.Title);
            builder.AddField("Severity", builder.AddLabelMarkup(GetSeverityLabelMarkup(finding.Severity)));
            builder.AddField("Object Id", finding.ObjectId);
            builder.AddField("Detail", finding.Detail);
            builder.AddField("Action", finding.Action);
        }

        private static string GetSeverityLabelMarkup(string severity)
        {
            int rank = SeverityRank(severity);
            var color = rank switch
            {
                0 => "red",
                1 => "red",
                2 => "orange",
                3 => "yellow",
                _ => "",
            };
            var prefix = string.Empty;

            if (!string.IsNullOrWhiteSpace(color))
            {
                prefix = $"<color=\"{color}\">";
            }
            return $"{prefix}{severity}";
        }

        private static List<AuditFinding> BuildAuditFindings()
        {
            var findings = new List<AuditFinding>();
            AddHealthReportAuditFindings(findings);
            AddTrackSpanAuditFindings(findings);
            AddIndustryAuditFindings(findings);
            AddLoaderAuditFindings(findings);
            AddPassengerAuditFindings(findings);
            AddSuppressionAuditFindings(findings);
            return findings
                .OrderBy(finding => SeverityRank(finding.Severity))
                .ThenBy(finding => finding.Title, StringComparer.OrdinalIgnoreCase)
                .ThenBy(finding => finding.ObjectId, StringComparer.OrdinalIgnoreCase)
                .Take(300)
                .ToList();
        }

        private static void AddHealthReportAuditFindings(List<AuditFinding> findings)
        {
            var report = LoadReportJson();

            foreach (var fault in report["packages"]?["faults"] as JArray ?? [])
            {
                AddFinding(
                    findings,
                    "Critical",
                    "Package fault",
                    ReadString(fault["packageId"], "(unknown package)"),
                    ReadString(fault["message"], "Package failed during load/apply."),
                    "Open Issues, inspect the package fault stage, then fix the source package or compatibility layer.");
            }

            foreach (var asset in report["unknownSceneryAssets"] as JArray ?? [])
            {
                AddFinding(
                    findings,
                    "High",
                    "Unknown scenery asset",
                    ReadString(asset["sceneryId"], "(unknown scenery)"),
                    $"{ReadString(asset["packageId"], "(unknown package)")} references {ReadString(asset["assetIdentifier"], "(blank asset)")}",
                    "Check asset pack discovery and exact model identifier spelling before changing converter output.");
            }

            foreach (var issue in report["graphPostBindIssues"] as JArray ?? [])
            {
                AddFinding(
                    findings,
                    "High",
                    "Graph post-bind issue",
                    "track graph",
                    issue.ToString(),
                    "Inspect the owning track package and verify deleted/replaced node, segment, and span ids.");
            }

            foreach (var skip in report["progressionTransferSkips"] as JArray ?? [])
            {
                AddFinding(
                    findings,
                    "Medium",
                    "Progression transfer skip",
                    "progression",
                    skip.ToString(),
                    "Verify the referenced industry, load, scene object, or map feature exists after FUSE apply.");
            }

            foreach (var conflict in report["conflicts"] as JArray ?? [])
            {
                AddFinding(
                    findings,
                    "Medium",
                    "Registry conflict",
                    ReadString(conflict["objectId"], "(unknown id)"),
                    $"{ReadString(conflict["ownerPackageId"], "(unknown owner)")} kept over {ReadString(conflict["attemptedPackageId"], "(unknown package)")}",
                    "Confirm whether both packages should layer shared data or whether one needs load-order/removal handling.");
            }
        }

        private static string ReadString(JToken token, string fallback)
        {
            var value = token == null ? string.Empty : token.ToString();
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
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

        private static void AddTrackSpanAuditFindings(List<AuditFinding> findings)
        {
            try
            {
                foreach (var span in TrackAPI.GetAllSpans() ?? [])
                {
                    var id = span?.id ?? "(blank span)";
                    var definition = TrackAPI.GetDefinition(span);
                    if (definition?.Upper == null || definition.Lower == null)
                    {
                        AddFinding(findings, "High", "Invalid track span", id, "Span definition has missing upper/lower location.", "Inspect the source span and repair or remove invalid endpoints.");
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(definition.Upper.SegmentId) ||
                        string.IsNullOrWhiteSpace(definition.Lower.SegmentId))
                    {
                        AddFinding(findings, "High", "Invalid track span", id, "Span endpoint is missing a segment id.", "Repair the span endpoint segment references in the source package.");
                        continue;
                    }

                    if (TrackAPI.GetSegment(definition.Upper.SegmentId) == null ||
                        TrackAPI.GetSegment(definition.Lower.SegmentId) == null)
                    {
                        AddFinding(findings, "High", "Orphaned track span", id, $"References {definition.Upper.SegmentId} / {definition.Lower.SegmentId}.", "Make sure the referenced segments survive final graph merge, or remove this span.");
                    }
                }
            }
            catch (Exception ex)
            {
                AddFinding(findings, "Low", "Track span audit failed", "audit", ex.GetBaseException().Message, "Check FUSE.log for the exception and rerun audits after reload.");
            }
        }

        private static void AddIndustryAuditFindings(List<AuditFinding> findings)
        {
            try
            {
                foreach (var industry in IndustryAPI.GetAllIndustries() ?? [])
                {
                    if (industry == null)
                    {
                        continue;
                    }

                    var id = BlankAs(industry.identifier, industry.name);
                    if (string.IsNullOrWhiteSpace(industry.identifier))
                    {
                        AddFinding(findings, "Medium", "Industry missing identifier", id, GetGameObjectPath(industry.gameObject), "Assign a stable industry identifier or remove the orphan scene object.");
                    }

                    var definition = IndustryAPI.GetDefinition(industry);
                    if (definition == null || definition.Components == null || definition.Components.Count == 0)
                    {
                        AddFinding(findings, "Low", "Industry has no components", id, GetGameObjectPath(industry.gameObject), "Verify whether this is scenery-only, a disabled vanilla industry, or a broken industry component binding.");
                    }
                }
            }
            catch (Exception ex)
            {
                AddFinding(findings, "Low", "Industry audit failed", "audit", ex.GetBaseException().Message, "Check FUSE.log for the exception and rerun audits after reload.");
            }
        }

        private static void AddLoaderAuditFindings(List<AuditFinding> findings)
        {
            try
            {
                foreach (var loader in LoaderAPI.GetAllLoaders() ?? [])
                {
                    if (loader == null)
                    {
                        continue;
                    }

                    var definition = LoaderAPI.GetDefinition(loader);
                    if (definition == null)
                    {
                        AddFinding(findings, "Medium", "Loader missing FUSE definition", loader.name, GetGameObjectPath(loader), "Check whether the loader came from a legacy plugin path that FUSE cannot rehydrate.");
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(definition.IndustryId) && IndustryAPI.GetIndustry(definition.IndustryId) == null)
                    {
                        AddFinding(findings, "High", "Loader industry missing", loader.name, $"industryId={definition.IndustryId}", "Create/restore the referenced industry before loader apply, or update the loader industry id.");
                    }
                }
            }
            catch (Exception ex)
            {
                AddFinding(findings, "Low", "Loader audit failed", "audit", ex.GetBaseException().Message, "Check FUSE.log for the exception and rerun audits after reload.");
            }
        }

        private static void AddPassengerAuditFindings(List<AuditFinding> findings)
        {
            try
            {
                var stationCount = SafeCount(() => StationAPI.GetAllStationAgents().Count());
                var stopCount = SafeCount(() => StationAPI.GetAllPassengerStops().Count());
                if (stationCount > 0 && stopCount == 0)
                {
                    AddFinding(findings, "Critical", "No passenger stops", "passenger system", $"{stationCount} station(s), 0 passenger stop(s).", "Passenger cars need PassengerStop bindings. Check station apply and passenger stop creation.");
                }

                foreach (var station in StationAPI.GetAllStationAgents() ?? Enumerable.Empty<StationAgent>())
                {
                    if (station == null)
                    {
                        continue;
                    }

                    var definition = StationAPI.GetDefinition(station);
                    if (!string.IsNullOrWhiteSpace(definition?.PassengerStopId) &&
                        StationAPI.GetPassengerStop(definition.PassengerStopId) == null)
                    {
                        AddFinding(findings, "High", "Station passenger stop missing", station.name, $"passengerStopId={definition.PassengerStopId}", "Create/restore the passenger stop or update the station passengerStopId.");
                    }
                }
            }
            catch (Exception ex)
            {
                AddFinding(findings, "Low", "Passenger audit failed", "audit", ex.GetBaseException().Message, "Check FUSE.log for the exception and rerun audits after reload.");
            }
        }

        private static void AddSuppressionAuditFindings(List<AuditFinding> findings)
        {
            try
            {
                foreach (var path in FuseRegistry.GetClaimedIds(FuseClaimKind.SuppressedScenePath))
                {
                    var target = FusePrefabResolver.ResolveScenePath(path) ?? GameObject.Find(path);
                    if (target == null)
                    {
                        continue;
                    }

                    var visibleRenderers = target
                        .GetComponentsInChildren<Renderer>(true)
                        .Count(renderer => renderer != null && renderer.enabled && !renderer.forceRenderingOff);
                    if (target.activeInHierarchy && visibleRenderers > 0)
                    {
                        AddFinding(findings, "Medium", "Suppressed scene object still visible", path, $"activeInHierarchy=true visibleRenderers={visibleRenderers}", "Run Advanced > Reload Track/Data or inspect the object; suppression may be missing a child renderer/culler path.");
                    }
                }
            }
            catch (Exception ex)
            {
                AddFinding(findings, "Low", "Suppression audit failed", "audit", ex.GetBaseException().Message, "Check FUSE.log for the exception and rerun audits after reload.");
            }
        }

        private static void AddFinding(List<AuditFinding> findings, string severity, string title, string objectId, string detail, string action)
        {
            findings?.Add(new AuditFinding(severity, title, objectId, detail, action));
        }

        private static int SeverityRank(string severity)
        {
            return severity switch
            {
                "Critical" => 0,
                "High" => 1,
                "Medium" => 2,
                "Low" => 3,
                _ => 4,
            };
        }

        private static string BuildAuditReport(IReadOnlyList<AuditFinding> findings)
        {
            var builder = new StringBuilder();
            builder.AppendLine("FUSE Audit Report");
            builder.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            builder.AppendLine("Findings: " + (findings?.Count ?? 0));
            foreach (var finding in findings ?? [])
            {
                builder.AppendLine();
                builder.AppendLine(finding.Severity + " | " + finding.Title);
                builder.AppendLine("Object: " + finding.ObjectId);
                builder.AppendLine("Detail: " + finding.Detail);
                builder.AppendLine("Action: " + finding.Action);
            }

            return builder.ToString().TrimEnd();
        }

        private static string ExportAuditReport(IReadOnlyList<AuditFinding> findings)
        {
            try
            {
                var root = Path.Combine(Application.persistentDataPath, "FUSE");
                Directory.CreateDirectory(root);
                var path = Path.Combine(root, "fuse-audit-report.json");
                var items = new JArray();
                foreach (var finding in findings ?? [])
                {
                    items.Add(new JObject
                    {
                        ["severity"] = finding.Severity,
                        ["title"] = finding.Title,
                        ["objectId"] = finding.ObjectId,
                        ["detail"] = finding.Detail,
                        ["action"] = finding.Action
                    });
                }

                File.WriteAllText(path, new JObject
                {
                    ["exportedUtc"] = DateTime.UtcNow.ToString("O"),
                    ["count"] = items.Count,
                    ["findings"] = items
                }.ToString(Newtonsoft.Json.Formatting.Indented));
                return "Exported FUSE audit report: " + path;
            }
            catch (Exception e)
            {
                var message = "Failed to export FUSE audit report";
                FuseLog.Exception(message, e);
                return message;
            }
            
        }
    }
}
