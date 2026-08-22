using FUSE.Infrastructure;
using System;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UI.Builder;
using UI.Common;
using UnityEngine;
using static FUSE.Interface.InterfaceUtils;

namespace FUSE.Interface.MenuWindow
{
    internal static class LiveDiagnosticsToolPage
    {
        private static readonly string[] LevelFilters =
        {
            "All",
            "Warnings + Errors",
            "Errors"
        };

        private static string _levelFilter = "All";
        private static string _search = string.Empty;
        private static bool _autoRefresh;
        private static bool _hasViewSnapshot;
        private static float _nextViewSnapshotAt;
        private static int _visibleCount;
        private static string _visibleLines = string.Empty;

        public static void Build(UIPanelBuilder builder)
        {
            builder.AddTitle("Live Diagnostics", "");
            builder.AddLabel(
                "Runtime guards and third-party exceptions are diagnostics, not load-health failures. " +
                "They live here so the Status page stays focused on actionable package, asset, graph, progression, and conflict problems.");
            builder.Spacer(8f);

            BuildLogViewer(builder);
            builder.Spacer(12f);
            BuildContainedEvents(builder);
        }

        private static void BuildLogViewer(UIPanelBuilder builder)
        {
            builder.AddSection("FUSE Log");
            builder.AddField("File", string.IsNullOrWhiteSpace(FuseLog.LogFilePath) ? "unavailable" : FuseLog.LogFilePath);
            if (_autoRefresh)
            {
                builder.AddField(
                    "Buffered",
                    () => FuseLiveLogBuffer.Count + " / " + FuseLiveLogBuffer.Capacity + " recent entries",
                    UIPanelBuilder.Frequency.Periodic);
            }
            else
            {
                builder.AddField("Buffered", FuseLiveLogBuffer.Count + " / " + FuseLiveLogBuffer.Capacity + " recent entries");
            }

            var selectedLevel = Math.Max(0, Array.IndexOf(LevelFilters, _levelFilter));
            builder.AddField(
                "Level",
                builder.AddDropdown(LevelFilters.ToList(), selectedLevel, index =>
                {
                    if (index >= 0 && index < LevelFilters.Length)
                    {
                        _levelFilter = LevelFilters[index];
                        InvalidateViewSnapshot();
                        builder.Rebuild();
                    }
                })).Height(32f);

            builder.AddField(
                "Contains",
                builder.AddInputField(_search ?? string.Empty, value => _search = value ?? string.Empty));

            builder.HStack(row =>
            {
                row.AddButtonCompact("Refresh", () =>
                {
                    InvalidateViewSnapshot();
                    builder.Rebuild();
                });
                row.AddButtonCompact(_autoRefresh ? "Pause Auto Refresh" : "Start Auto Refresh", () =>
                {
                    _autoRefresh = !_autoRefresh;
                    InvalidateViewSnapshot();
                    builder.Rebuild();
                });
                row.AddButtonCompact("Clear Filter", () =>
                {
                    _levelFilter = "All";
                    _search = string.Empty;
                    InvalidateViewSnapshot();
                    builder.Rebuild();
                });
            }, 6f).Height(32f);

            builder.HStack(row =>
            {
                row.AddButtonCompact("Copy Visible Log", () =>
                {
                    GUIUtility.systemCopyBuffer = FormatEntries(VisibleEntries());
                    Toast.Present("Copied the visible FUSE log entries.");
                });
                row.AddButtonCompact("Open Log Folder", () =>
                {
                    var directory = string.IsNullOrWhiteSpace(FuseLog.LogFilePath)
                        ? Application.persistentDataPath
                        : Path.GetDirectoryName(FuseLog.LogFilePath);
                    Application.OpenURL(directory);
                    Toast.Present("Opened the FUSE log folder.");
                });
                row.AddButtonCompact(
                    FuseLiveConsole.IsEnabled ? "Stop Live Console" : "Open Live Console",
                    () =>
                    {
                        var result = FuseLiveConsole.IsEnabled
                            ? FuseLiveConsole.Disable()
                            : FuseLiveConsole.Enable();
                        Toast.Present(result);
                        builder.Rebuild();
                    });
            }, 6f).Height(32f);

            builder.AddField(
                "Viewer",
                _autoRefresh
                    ? "Auto refresh is on (once per second)."
                    : "This in-game snapshot refreshes on demand. Open Live Console for a continuously scrolling second-screen view.");

            RefreshViewSnapshot(force: true);
            if (_autoRefresh)
            {
                builder.AddField(
                    "Visible",
                    () =>
                    {
                        RefreshViewSnapshot(force: false);
                        return _visibleCount.ToString();
                    },
                    UIPanelBuilder.Frequency.Periodic);
                var logView = builder.AddLabel(
                    () =>
                    {
                        RefreshViewSnapshot(force: false);
                        return string.IsNullOrWhiteSpace(_visibleLines)
                            ? "No matching FUSE log entries."
                            : _visibleLines;
                    },
                    UIPanelBuilder.Frequency.Periodic);
                ConfigureLogView(logView);
            }
            else
            {
                builder.AddField("Visible", _visibleCount.ToString());
                var logView = builder.AddLabel(
                    string.IsNullOrWhiteSpace(_visibleLines)
                        ? "No matching FUSE log entries."
                        : _visibleLines,
                    text => ConfigureLogText(text));
                logView.Height(1400f);
            }
        }

        private static void BuildContainedEvents(UIPanelBuilder builder)
        {
            builder.AddSection("Contained Runtime Events");
            AddWrappedField(builder, "Compatibility Guards", FuseRuntimeGuardCounters.FormatSummary(), 76f);
            builder.AddField(
                "Native Leak Stacks",
                $"{FuseNativeLeakDiagnostic.ModeLabel} (setting: {(FuseSettings.EnableNativeLeakStackTraces ? "enabled" : "disabled")})");

            var exceptionState = FuseModExceptionRegistry.CaptureReportState();
            builder.AddField("Observed Exceptions", exceptionState.Total.ToString());
            AddWrappedLabel(
                builder,
                exceptionState.Total == 0
                    ? "No third-party exceptions have been observed this session."
                    : "These observations do not change FUSE readiness. Use them to identify repeating mod behavior; attach the health report when reporting a real symptom.",
                48f);

            foreach (var record in exceptionState.Mods.OrderByDescending(item => item.Count))
            {
                var display = string.IsNullOrWhiteSpace(record.DisplayName) ? record.ModId : record.DisplayName;
                var top = record.Signatures == null
                    ? null
                    : record.Signatures.OrderByDescending(item => item.Count).FirstOrDefault();
                var text = record.Count + " occurrence(s) over " + record.Episodes + " episode(s)";
                if (top != null)
                {
                    text += " — " + top.ExceptionType + " @ " + top.TopOwnedFrame;
                }

                AddWrappedField(builder, display, text, 52f);
            }

            builder.HStack(row =>
            {
                row.AddButtonCompact("Copy Diagnostics", () =>
                {
                    GUIUtility.systemCopyBuffer = BuildDiagnosticsText();
                    Toast.Present("Copied FUSE runtime diagnostics.");
                });
                row.AddButtonCompact("Copy Full Health Report", () =>
                {
                    GUIUtility.systemCopyBuffer = FUSE.Loading.FuseLoadReport.GetLastDetailReport();
                    Toast.Present("Copied the full FUSE health report.");
                });
            }, 6f).Height(32f);
        }

        private static FuseLiveLogEntry[] VisibleEntries() =>
            FuseLiveLogBuffer.Snapshot(_levelFilter, _search, 120);

        private static string FormatEntries(FuseLiveLogEntry[] entries)
        {
            if (entries == null || entries.Length == 0)
            {
                return string.Empty;
            }

            var text = new StringBuilder(entries.Length * 96);
            for (var index = 0; index < entries.Length; index++)
            {
                if (index > 0)
                {
                    text.AppendLine();
                }

                text.Append(entries[index].FormatLine());
            }

            return text.ToString();
        }

        private static void InvalidateViewSnapshot()
        {
            _hasViewSnapshot = false;
            _nextViewSnapshotAt = 0f;
        }

        private static void RefreshViewSnapshot(bool force)
        {
            var now = Time.unscaledTime;
            if (!force && _hasViewSnapshot && (!_autoRefresh || now < _nextViewSnapshotAt))
            {
                return;
            }

            var entries = VisibleEntries();
            _visibleCount = entries.Length;
            _visibleLines = FormatEntries(entries);
            _hasViewSnapshot = true;
            _nextViewSnapshotAt = now + 0.9f;
        }

        private static void ConfigureLogView(RectTransform view)
        {
            if (view == null)
            {
                return;
            }

            ConfigureLogText(view.GetComponent<TMP_Text>());
            view.Height(1400f);
        }

        private static void ConfigureLogText(TMP_Text text)
        {
            if (text == null)
            {
                return;
            }

            text.enableWordWrapping = true;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.alignment = TextAlignmentOptions.Left;
        }

        private static string BuildDiagnosticsText()
        {
            var state = FuseModExceptionRegistry.CaptureReportState();
            var text = new StringBuilder();
            text.AppendLine("FUSE Runtime Diagnostics");
            text.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            text.AppendLine("Guards: " + FuseRuntimeGuardCounters.FormatSummary());
            text.AppendLine(state.SummaryLine);
            foreach (var record in state.Mods.OrderByDescending(item => item.Count))
            {
                var display = string.IsNullOrWhiteSpace(record.DisplayName) ? record.ModId : record.DisplayName;
                text.AppendLine($"{display} ({record.ModId}): {record.Count} occurrence(s), {record.Episodes} episode(s)");
                foreach (var signature in (record.Signatures ?? Array.Empty<FuseModExceptionSignatureSnapshot>())
                             .OrderByDescending(item => item.Count))
                {
                    text.AppendLine(
                        $"  {signature.ExceptionType} @ {signature.TopOwnedFrame}: " +
                        $"{signature.Count} occurrence(s), {signature.Episodes} episode(s) — {signature.SampleMessage}");
                }
            }

            return text.ToString().TrimEnd();
        }
    }
}
