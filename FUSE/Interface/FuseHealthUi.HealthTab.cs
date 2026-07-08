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

        private void BuildHealthContent(UIPanelBuilder builder)
        {
            var report = LoadReportJson();
            var counts = report["counts"] as JObject ?? new JObject();
            var hasProblems = ReadBool(report["hasProblems"], false);
            var loadedPackages = ReadInt(counts["loadedPackages"]);
            var appliedPackages = ReadInt(counts["appliedPackages"]);
            var faultCount = ReadInt(counts["faultedPackages"]);
            var conflictCount = ReadInt(counts["conflicts"]);
            var unknownAssetCount = ReadInt(counts["unknownSceneryAssets"]);
            var graphIssueCount = ReadInt(counts["graphIssues"]);
            var transferSkipCount = ReadInt(counts["progressionTransferSkips"]);
            var noticeCount = CountArray(report["notices"]);

            builder.FieldLabelWidth = 160f;
            builder.Spacing = 6f;

            builder.AddSection("Stream Readiness");
            AddValueField(builder, "State", hasProblems ? "Needs Attention" : "Ready");
            AddWrappedField(
                builder,
                "Status",
                hasProblems
                    ? "FUSE found items that need review before a clean session."
                    : "Full stack loaded cleanly. No package faults, asset misses, graph issues, or transfer skips are reported.",
                50f);
            AddValueField(builder, "Version", "FUSE " + ReadVersion() + " | Schema " + FuseMigration.CurrentVersion + " | Converter 0.2.0");
            builder.Spacer(6f);

            builder.AddSection("Checklist");
            AddReadinessRow(builder, "Packages", faultCount == 0, $"{appliedPackages}/{loadedPackages} applied", faultCount + " fault(s)");
            AddReadinessRow(builder, "Assets", unknownAssetCount == 0, "0 unknown assets", unknownAssetCount + " unknown");
            AddReadinessRow(builder, "Track Graph", graphIssueCount == 0, "0 graph issues", graphIssueCount + " issue(s)");
            AddReadinessRow(builder, "Progression", transferSkipCount == 0, "0 transfer skips", transferSkipCount + " skip(s)");
            AddReadinessRow(builder, "Registry", conflictCount == 0, "0 conflicts", conflictCount + " conflict(s)");
            AddReadinessRow(builder, "Notices", noticeCount == 0, "0 notices", noticeCount + " notice(s)");
            builder.Spacer(6f);

            var multiplayer = FuseMultiplayerGuard.GetStatus();
            builder.AddSection("Active Profile");
            AddValueField(builder, "Mode", multiplayer.Mode + " | " + multiplayer.Role);
            AddValueField(builder, "Mutation Policy", multiplayer.MutationPolicy);
            AddValueField(builder, "Profile", FuseModSetService.ActiveSetName);
            AddValueField(builder, "Profile Hash", multiplayer.LocalPackageFingerprint);
            AddWrappedField(builder, "Packages", multiplayer.LocalPackageSummary, 38f);
            builder.Spacer(6f);

            builder.AddSection("Load Timing");
            AddValueField(builder, "FUSE Map Load", FusePerformanceMetrics.FormatTiming("map load total"));
            AddValueField(builder, "Runtime Apply", FusePerformanceMetrics.FormatTiming("apply resident definitions"));
            AddWrappedField(builder, "Slowest", FriendlyTimingText(FusePerformanceMetrics.FormatSlowestApplyPackage()), 42f);
            builder.Spacer(6f);

            builder.AddSection("Actions");
            builder.HStack(row =>
            {
                row.AddButtonCompact("Copy Readiness", () =>
                {
                    GUIUtility.systemCopyBuffer = BuildStreamReadinessReport(report);
                    _lastAction = "Copied FUSE readiness report to clipboard.";
                    RebuildWindow();
                });
                row.AddButtonCompact("Open Issues", () => SetPage(Page.Logs));
                row.AddButtonCompact("Advanced", () => SetPage(Page.Advanced));
                row.AddButtonCompact("Refresh", RebuildWindow);
            }, 6f).Height(32f);
            AddWrappedLabel(builder, _lastAction, 34f);
            builder.Spacer(6f);

            builder.AddSection("Active Problems");
            var problemRows = 0;
            problemRows += AddProblemSummary(builder, report, "packages", "faults", "Package Faults", false);
            problemRows += AddProblemSummary(builder, report, null, "conflicts", "Conflicts", false);
            problemRows += AddProblemSummary(builder, report, null, "unknownSceneryAssets", "Unknown Assets", false);
            problemRows += AddProblemSummary(builder, report, null, "graphPostBindIssues", "Graph Issues", false);
            problemRows += AddProblemSummary(builder, report, null, "progressionTransferSkips", "Transfer Skips", false);
            problemRows += AddProblemSummary(builder, report, null, "notices", "Notices", false);
            problemRows += AddProblemSummary(builder, report, null, "sceneryLoadFailures", "Asset Load Failures", false);
            problemRows += AddSaveCarFaultSummaryRow(builder);
            if (problemRows == 0)
            {
                AddValueField(builder, "Status", "None");
            }
            builder.Spacer(8f);

            BuildSaveCarFaultsSection(builder);
        }

        /// <summary>
        /// Adds a single-row entry to "Active Problems" for cars the
        /// save load could not restore. The count is read live from
        /// <see cref="FuseSaveCarFaultRegistry"/>; if zero, no row is
        /// drawn (matching the no-show-zero convention of the
        /// surrounding rows). Returns the number of rows produced so
        /// the caller can sum into the empty-state "Status: None"
        /// path.
        /// </summary>
        private static int AddSaveCarFaultSummaryRow(UIPanelBuilder builder)
        {
            var count = FuseSaveCarFaultRegistry.Count;
            if (count == 0)
            {
                return 0;
            }
            builder.AddField("Orphaned Cars", () => count + " - see below", 0).Height(24f);
            return 1;
        }

        // Per-prototype replacement-target selection persists across
        // the panel rebuild cycle so the user's dropdown choice
        // doesn't reset every time the UI redraws.
        private static readonly Dictionary<string, string> _saveCarFaultReplacementSelection =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Lists every car the save load could not restore, grouped
        /// by the missing prototype identifier so cars that share a
        /// broken type cluster together (and a fix targeting the
        /// type can address them all at once). Empty when the
        /// registry has no entries — silent in that case so the
        /// Health page stays clean. Each group has a picker for a
        /// replacement car type and a button that applies the
        /// replacement to every car in the group, spawning new cars
        /// at the original locations with the original ids /
        /// waybills / properties preserved.
        /// </summary>
        private void BuildSaveCarFaultsSection(UIPanelBuilder builder)
        {
            var faults = FuseSaveCarFaultRegistry.GetAll();
            if (faults.Count == 0)
            {
                return;
            }

            builder.AddSection("Orphaned Cars (this save)");
            AddWrappedLabel(
                builder,
                "These cars were in the save but could not be restored — their car-type definitions " +
                "weren't usable (e.g., the only definition lived in a legacy SCAssetPacks pack whose " +
                "bundle conflicts with the modern pack's bundle, so FUSE filtered it out to prevent " +
                "Unity from refusing the bundle load). Pick a replacement car type per group below " +
                "and FUSE will spawn the car back at its original location with the same id, road " +
                "number, waybill, and load — only the prefab/type changes.",
                52f);
            builder.Spacer(4f);

            // Refresh the available-replacement list every panel
            // rebuild so the picker reflects packs that came in via a
            // late legacy-data converter (the list is cheap to
            // enumerate; this keeps the UI honest about what the game
            // can actually load right now).
            var availableReplacements = FuseSaveCarFaultReplacement.GetAvailablePrototypeIds();

            var byPrototype = faults
                .GroupBy(f => f.MissingPrototypeId, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

            foreach (var group in byPrototype)
            {
                var groupKey = string.IsNullOrEmpty(group.Key) ? "<unknown>" : group.Key;
                var groupList = group.ToList();
                AddValueField(builder, "Type", $"{groupKey} ({groupList.Count})");
                foreach (var fault in groupList.OrderBy(f => f.DisplayName, StringComparer.OrdinalIgnoreCase))
                {
                    AddWrappedField(
                        builder,
                        "  " + fault.DisplayName,
                        $"id={fault.CarId} at segment={fault.LocationSegmentId} dist={fault.LocationDistance:F1}",
                        34f);
                }

                BuildReplacementPickerRow(builder, groupKey, groupList, availableReplacements);
                builder.Spacer(4f);
            }
            builder.Spacer(8f);
        }

        /// <summary>
        /// Renders the replacement controls for one prototype group:
        /// a dropdown of currently-loadable car identifiers and an
        /// Apply button that spawns a replacement for every car in
        /// the group using the selected identifier. Choices persist
        /// across rebuilds via
        /// <see cref="_saveCarFaultReplacementSelection"/>.
        /// </summary>
        private void BuildReplacementPickerRow(
            UIPanelBuilder builder,
            string groupKey,
            List<FuseSaveCarFault> groupFaults,
            string[] availableReplacements)
        {
            if (availableReplacements == null || availableReplacements.Length == 0)
            {
                AddWrappedLabel(
                    builder,
                    "  No replacement car types are currently loadable. Make sure your TOFC Cars (or " +
                    "equivalent) pack is installed at the mod root with the modern definitions.",
                    32f);
                return;
            }

            if (!_saveCarFaultReplacementSelection.TryGetValue(groupKey, out var selected) ||
                Array.IndexOf(availableReplacements, selected) < 0)
            {
                selected = availableReplacements[0];
                _saveCarFaultReplacementSelection[groupKey] = selected;
            }

            // The picker uses a paged-button row instead of a
            // dropdown so the implementation stays simple and the UI
            // works on all UIPanelBuilder shipping in the host. Each
            // button shows one available identifier; clicking sets
            // the selection. Selected identifier is shown bold-ish
            // via a square-bracket marker (no rich-text dep).
            builder.AddField("  Replacement", () =>
            {
                if (_saveCarFaultReplacementSelection.TryGetValue(groupKey, out var current))
                {
                    return current;
                }
                return availableReplacements[0];
            }, 0).Height(24f);

            // Render up to ~6 candidates per row so the user can scan
            // without scrolling forever. For large catalogs we just
            // show the first N alphabetically; refining with a
            // search box can come in a later iteration.
            const int MaxCandidates = 24;
            var candidates = availableReplacements.Take(MaxCandidates).ToArray();
            builder.HStack(row =>
            {
                row.Spacing = 2f;
                foreach (var candidate in candidates)
                {
                    var captured = candidate;
                    var label = string.Equals(captured, selected, StringComparison.Ordinal)
                        ? "[" + captured + "]"
                        : captured;
                    row.AddButtonCompact(label, () =>
                    {
                        _saveCarFaultReplacementSelection[groupKey] = captured;
                        RebuildWindow();
                    });
                }
            }, 4f).Height(28f);

            builder.HStack(row =>
            {
                row.AddButtonCompact(
                    $"Replace {groupFaults.Count} car(s) with '{selected}'",
                    () => ApplyReplacementGroup(groupKey, groupFaults, selected));
            }, 6f).Height(32f);
        }

        private void ApplyReplacementGroup(
            string groupKey,
            List<FuseSaveCarFault> groupFaults,
            string replacementPrototypeId)
        {
            var applied = 0;
            var failed = 0;
            foreach (var fault in groupFaults)
            {
                if (FuseSaveCarFaultReplacement.TryApply(fault, replacementPrototypeId))
                {
                    applied++;
                }
                else
                {
                    failed++;
                }
            }

            _lastAction = failed == 0
                ? $"Replaced {applied} orphaned car(s) of type '{groupKey}' with '{replacementPrototypeId}'."
                : $"Replaced {applied} of {applied + failed} orphaned car(s) of type '{groupKey}' " +
                  $"with '{replacementPrototypeId}'; {failed} failed — see FUSE.log.";
            RebuildWindow();
        }
    }
}
