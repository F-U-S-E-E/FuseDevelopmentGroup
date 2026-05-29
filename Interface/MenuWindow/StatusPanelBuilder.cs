using FUSE.API;
using FUSE.Infrastructure;
using FUSE.Loading;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UI.Builder;
using UnityEngine;
using static FUSE.Interface.InterfaceUtils;

namespace FUSE.Interface.MenuWindow
{
    internal struct StatusPanelBuilder
    {
        private enum PageId
        {
            Overview,
            Issues
        }

        private class Page
        {
            public PageId Id { get; }

            public Page(PageId id)
            {
                Id = id;
            }
        }

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

        private static string _lastAction = string.Empty;

        public static void Build(UIPanelBuilder builder, UIState<string> selectedItem)
        {
            if (selectedItem.Value == null)
            {
                selectedItem.Value = "overview";
            }

            List<UIPanelBuilder.ListItem<Page>> list = [];
            list.Add(new UIPanelBuilder.ListItem<Page>("overview", new Page(PageId.Overview), "Status", "Overview"));
            list.Add(new UIPanelBuilder.ListItem<Page>("issues", new Page(PageId.Issues), "Status", "Issues"));

            builder.AddListDetail(list, selectedItem, delegate (UIPanelBuilder builder, Page page)
            {
                if (page == null)
                {
                    builder.AddExpandingVerticalSpacer();
                    builder.AddLabelEmptyState("Select a page");
                    builder.AddExpandingVerticalSpacer();
                }
                else
                {
                    builder.VScrollView(delegate (UIPanelBuilder builder)
                    {
                        switch (page.Id)
                        {
                            case PageId.Overview:
                                BuildOverview(builder);
                                break;
                            case PageId.Issues:
                                BuildIssues(builder);
                                break;
                            default:
                                builder.AddLabel("Unknown page.");
                                break;
                        }
                    }, new RectOffset(0, 4, 0, 0));
                }
            });
        }

        private static void BuildOverview(UIPanelBuilder builder)
        {
            var checklistData = GetStatusChecklistData();

            if (!checklistData.HasValue)
            {
                builder.AddLabel("FUSE report is still pending.");
                return;
            }

            var data = checklistData.Value;

            builder.AddSection("Checklist");
            AddReadinessRow(builder, "Packages", data.FaultCount == 0, $"{data.AppliedPackagesCount}/{data.LoadedPackagesCount} applied", data.FaultCount + " fault(s)");
            AddReadinessRow(builder, "Assets", data.UnknownAssetCount == 0, "0 unknown assets", data.UnknownAssetCount + " unknown");
            AddReadinessRow(builder, "Track Graph", data.GraphIssueCount == 0, "0 graph issues", data.GraphIssueCount + " issue(s)");
            AddReadinessRow(builder, "Progression", data.ProgressionTransferSkipCount == 0, "0 transfer skips", data.ProgressionTransferSkipCount + " skip(s)");
            AddReadinessRow(builder, "Registry", data.ConflictCount == 0, "0 conflicts", data.ConflictCount + " conflict(s)");
            AddReadinessRow(builder, "Notices", data.NoticesCount == 0, "0 notices", data.NoticesCount + " notice(s)");
            builder.Spacer(6f);
        }

        private static void AddReadinessRow(UIPanelBuilder builder, string label, bool ok, string okText, string problemText)
        {
            var value = ok ? "OK | " + okText : "Review | " + problemText;
            builder.AddField(label, value);
        }

        private static StatusChecklistData? GetStatusChecklistData()
        {
            var reportSnapshot = FuseLoadReport.GetLastReportSnapshot();

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

        private static void BuildIssues(UIPanelBuilder builder)
        {
            builder.AddSection("Error Drilldown");

            var reportSnapshot = FuseLoadReport.GetLastReportSnapshot();

            if (reportSnapshot == null)
            {
                builder.AddLabel("FUSE report is still pending.");
                return;
            }

            // TODO: original BuildLogsContent seems to be duplicating a lot of functionality from the status checklist page
            // Check whether we need to actually include this separately since right now
            // it seems like we're just printing the same stats along with an instruction to use /fuse.report

            builder.AddSection("Export");
            builder.HStack(row =>
            {
                row.AddButtonCompact("Copy Health Report", () =>
                {
                    GUIUtility.systemCopyBuffer = FuseLoadReport.GetLastDetailReport();
                    _lastAction = "Copied FUSE health report to clipboard.";
                    builder.Rebuild();
                });
                row.AddButtonCompact("Export JSON", () =>
                {
                    _lastAction = ExportHealthReportJson();
                    builder.Rebuild();
                });
                row.AddButtonCompact("Export Mod Manifest", () =>
                {
                    _lastAction = FuseModSetService.ExportActiveManifest();
                    builder.Rebuild();
                });
            });

            if (!String.IsNullOrEmpty(_lastAction))
            {
                builder.AddLabel(_lastAction);
            }
        }

        private static string ExportHealthReportJson()
        {
            var root = Path.Combine(Application.persistentDataPath, "FUSE");
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, "fuse-health-report.json");
            File.WriteAllText(path, FuseLoadReport.GetLastJsonReport());
            return "Exported FUSE health JSON report: " + path;
        }

        
    }
}
