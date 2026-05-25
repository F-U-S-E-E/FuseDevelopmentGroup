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

        private void BuildLogsContent(UIPanelBuilder builder)
        {
            builder.FieldLabelWidth = 160f;
            builder.Spacing = 6f;

            var report = LoadReportJson();
            builder.AddSection("Error Drilldown");
            AddProblemSummary(builder, report, "packages", "faults", "Package Faults", true);
            AddProblemSummary(builder, report, null, "conflicts", "Conflicts", true);
            AddProblemSummary(builder, report, null, "unknownSceneryAssets", "Unknown Assets", true);
            AddProblemSummary(builder, report, null, "graphPostBindIssues", "Graph Issues", true);
            AddProblemSummary(builder, report, null, "progressionTransferSkips", "Transfer Skips", true);
            AddProblemSummary(builder, report, null, "notices", "Notices", true);
            builder.Spacer(4f);

            builder.AddSection("Export");
            builder.HStack(row =>
            {
                row.AddButtonCompact("Copy Health Report", () =>
                {
                    GUIUtility.systemCopyBuffer = FuseLoadReport.GetLastDetailReport();
                    _lastAction = "Copied FUSE health report to clipboard.";
                    RebuildWindow();
                });
                row.AddButtonCompact("Export JSON", () =>
                {
                    RunAction("export health report", ExportHealthReportJson);
                });
                row.AddButtonCompact("Export Mod Manifest", () =>
                {
                    RunAction("export active mod-set manifest", () => "Exported active mod-set manifest: " + FuseModSetService.ExportActiveManifest());
                });
            }, 6f).Height(32f);
            AddWrappedLabel(builder, _lastAction, 36f);
            builder.Spacer(4f);

            if (FuseSettings.ShowAdvancedHealthDetails || HasReportProblems(report))
            {
                builder.AddSection("Last FUSE Log Lines");
                var lines = ReadLastLogLines(50);
                AddWrappedLabel(builder, lines.Length == 0 ? "No FUSE.log lines available yet." : string.Join("\n", lines), Math.Min(620f, Math.Max(80f, lines.Length * 18f)));
            }
            else
            {
                AddWrappedField(builder, "Log Tail", "Hidden while FUSE is healthy. Enable Advanced Details in Settings to show live log lines.", 44f);
            }
            builder.Spacer(8f);
        }

        private static string[] ReadLastLogLines(int maxLines)
        {
            try
            {
                var path = FuseLog.LogFilePath;
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    return Array.Empty<string>();
                }

                var lines = File.ReadAllLines(path);
                return lines.Skip(Math.Max(0, lines.Length - Math.Max(1, maxLines))).ToArray();
            }
            catch (Exception ex)
            {
                return new[] { "Could not read FUSE.log: " + ex.GetBaseException().Message };
            }
        }
    }
}
