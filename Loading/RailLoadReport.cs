using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RAIL.Infrastructure;
using RAIL.Registry;
using UI.Common;

namespace RAIL.Loading
{
    internal static class RailLoadReport
    {
        private static readonly object Sync = new object();
        private static readonly Dictionary<string, UnknownSceneryAsset> UnknownSceneryAssets =
            new Dictionary<string, UnknownSceneryAsset>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<string> Notices = new List<string>();

        private static string _lastSummary = "RAIL load report has not been generated yet.";
        private static string _lastDetails = "RAIL load report has not been generated yet.";

        public static string LastSummary
        {
            get
            {
                lock (Sync)
                {
                    return _lastSummary;
                }
            }
        }

        public static void ResetMapLoad()
        {
            lock (Sync)
            {
                UnknownSceneryAssets.Clear();
                Notices.Clear();
                _lastSummary = "RAIL load report is pending.";
                _lastDetails = "RAIL load report is pending.";
            }
        }

        public static void RecordUnknownSceneryAsset(string packageId, string sceneryId, string assetIdentifier, string model)
        {
            var normalizedPackageId = Normalize(packageId, "<unknown>");
            var normalizedSceneryId = Normalize(sceneryId, "<unknown>");
            var key = normalizedPackageId + "\0" + normalizedSceneryId;

            lock (Sync)
            {
                if (UnknownSceneryAssets.ContainsKey(key))
                {
                    return;
                }

                UnknownSceneryAssets[key] = new UnknownSceneryAsset(
                    normalizedPackageId,
                    normalizedSceneryId,
                    assetIdentifier ?? string.Empty,
                    model ?? string.Empty);
            }
        }

        public static void RecordNotice(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            lock (Sync)
            {
                if (!Notices.Contains(message))
                {
                    Notices.Add(message);
                }
            }
        }

        public static string GetLastDetailReport()
        {
            lock (Sync)
            {
                return _lastDetails;
            }
        }

        public static void PublishMapLoadReport(string reason, int loadedFromDiskThisPass, int appliedToRuntimeThisPass)
        {
            var snapshot = CaptureSnapshot(reason, loadedFromDiskThisPass, appliedToRuntimeThisPass);
            var summary = BuildSummary(snapshot);
            var details = BuildDetails(snapshot, summary);

            lock (Sync)
            {
                _lastSummary = summary;
                _lastDetails = details;
            }

            if (snapshot.HasProblems)
            {
                RailLog.Warning(summary);
            }
            else
            {
                RailLog.Info(summary);
            }

            RailLog.Info(details);
            PresentToast(summary, snapshot.HasProblems);
        }

        private static ReportSnapshot CaptureSnapshot(string reason, int loadedFromDiskThisPass, int appliedToRuntimeThisPass)
        {
            UnknownSceneryAsset[] unknownScenery;
            string[] notices;
            lock (Sync)
            {
                unknownScenery = UnknownSceneryAssets.Values
                    .OrderBy(item => item.PackageId, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.SceneryId, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                notices = Notices.ToArray();
            }

            var sceneSuppressions = RailWorldSuppressor.GetActiveScenePathSuppressions().ToArray();
            var trackGroupSuppressions = RailWorldSuppressor.GetActiveTrackGroupSuppressions().ToArray();
            var areaSuppressions = RailWorldSuppressor.GetActiveAreaSuppressions().ToArray();

            var loadedPackageIds = RailPackageFaultRegistry.GetLoadedPackageIds().ToArray();
            var appliedPackageIds = RailPackageFaultRegistry.GetAppliedPackageIds().ToArray();
            var skippedPackages = RailPackageFaultRegistry.GetSkippedPackages();
            var disabledPackages = RailPackageFaultRegistry.GetDisabledPackages();
            var faults = RailPackageFaultRegistry.GetFaults().ToArray();
            var conflicts = RailRegistry.Conflicts.ToArray();

            return new ReportSnapshot
            {
                Reason = string.IsNullOrWhiteSpace(reason) ? "map load" : reason,
                LoadedFromDiskThisPass = loadedFromDiskThisPass,
                AppliedToRuntimeThisPass = appliedToRuntimeThisPass,
                ResidentDefinitionCount = RailModLoader.LoadedDefinitionCount,
                LoadedPackageIds = loadedPackageIds,
                AppliedPackageIds = appliedPackageIds,
                SkippedPackages = skippedPackages,
                DisabledPackages = disabledPackages,
                Faults = faults,
                Conflicts = conflicts,
                SceneSuppressions = sceneSuppressions,
                TrackGroupSuppressions = trackGroupSuppressions,
                AreaSuppressions = areaSuppressions,
                UnknownSceneryAssets = unknownScenery,
                Notices = notices
            };
        }

        private static string BuildSummary(ReportSnapshot snapshot)
        {
            var loadedCount = Math.Max(snapshot.ResidentDefinitionCount, snapshot.LoadedPackageIds.Length);
            var suppressionCount =
                snapshot.SceneSuppressions.Length +
                snapshot.TrackGroupSuppressions.Length +
                snapshot.AreaSuppressions.Length;

            return
                $"RAIL: {loadedCount} loaded, {snapshot.FaultedPackageCount} faulted, " +
                $"{snapshot.Conflicts.Length} conflicts, {suppressionCount} suppressions, " +
                $"{snapshot.UnknownSceneryAssets.Length} unknown scenery assets. " +
                "Details: /rail.report /rail.loaded /rail.conflicts";
        }

        private static string BuildDetails(ReportSnapshot snapshot, string summary)
        {
            var sb = new StringBuilder();
            sb.AppendLine(summary);
            sb.AppendLine(
                $"Reason: {snapshot.Reason}; disk-loaded this pass={snapshot.LoadedFromDiskThisPass}; " +
                $"runtime-applied this pass={snapshot.AppliedToRuntimeThisPass}; resident definitions={snapshot.ResidentDefinitionCount}.");
            sb.AppendLine(
                $"Packages: loaded={snapshot.LoadedPackageIds.Length}; applied={snapshot.AppliedPackageIds.Length}; " +
                $"skipped={snapshot.SkippedPackages.Count}; disabled={snapshot.DisabledPackages.Count}; " +
                $"faulted={snapshot.FaultedPackageCount}.");

            AppendList(sb, "Loaded packages", snapshot.LoadedPackageIds);
            AppendList(sb, "Applied packages", snapshot.AppliedPackageIds);
            AppendMap(sb, "Skipped packages", snapshot.SkippedPackages);
            AppendMap(sb, "Disabled packages", snapshot.DisabledPackages);

            if (snapshot.Faults.Length > 0)
            {
                sb.AppendLine("Faults:");
                foreach (var fault in snapshot.Faults)
                {
                    sb.AppendLine($"  {fault.PackageId} [{fault.Stage}] {fault.Message}");
                }
            }

            sb.AppendLine($"Conflicts recorded: {snapshot.Conflicts.Length} (details: /rail.conflicts).");

            sb.AppendLine(
                $"Suppressions active: scenePaths={snapshot.SceneSuppressions.Length}; " +
                $"trackGroups={snapshot.TrackGroupSuppressions.Length}; areas={snapshot.AreaSuppressions.Length}.");
            AppendList(sb, "Suppressed scene paths", snapshot.SceneSuppressions);
            AppendList(sb, "Suppressed track groups", snapshot.TrackGroupSuppressions);
            AppendList(sb, "Suppressed areas", snapshot.AreaSuppressions);

            if (snapshot.UnknownSceneryAssets.Length > 0)
            {
                sb.AppendLine("Scenery skipped because the asset was unknown:");
                foreach (var item in snapshot.UnknownSceneryAssets)
                {
                    sb.AppendLine(
                        $"  {item.PackageId}: {item.SceneryId} " +
                        $"AssetIdentifier='{item.AssetIdentifier}' Model='{item.Model}'");
                }
            }

            AppendList(sb, "Notices", snapshot.Notices);
            return sb.ToString();
        }

        private static void AppendList(StringBuilder sb, string label, IEnumerable<string> values)
        {
            var items = (values ?? Enumerable.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (items.Length == 0)
            {
                return;
            }

            sb.AppendLine(label + ":");
            foreach (var item in items)
            {
                sb.AppendLine("  " + item);
            }
        }

        private static void AppendMap(StringBuilder sb, string label, IReadOnlyDictionary<string, string> values)
        {
            if (values == null || values.Count == 0)
            {
                return;
            }

            sb.AppendLine(label + ":");
            foreach (var entry in values.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine($"  {entry.Key}: {entry.Value}");
            }
        }

        private static void PresentToast(string summary, bool hasProblems)
        {
            try
            {
                Toast.Present(summary, hasProblems ? ToastPosition.Middle : ToastPosition.Bottom);
            }
            catch (Exception ex)
            {
                RailLog.Warning($"RAIL could not display map-load report toast: {ex.Message}");
            }
        }

        private static string Normalize(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        private sealed class UnknownSceneryAsset
        {
            public UnknownSceneryAsset(string packageId, string sceneryId, string assetIdentifier, string model)
            {
                PackageId = packageId;
                SceneryId = sceneryId;
                AssetIdentifier = assetIdentifier;
                Model = model;
            }

            public string PackageId { get; }
            public string SceneryId { get; }
            public string AssetIdentifier { get; }
            public string Model { get; }
        }

        private sealed class ReportSnapshot
        {
            public string Reason { get; set; }
            public int LoadedFromDiskThisPass { get; set; }
            public int AppliedToRuntimeThisPass { get; set; }
            public int ResidentDefinitionCount { get; set; }
            public string[] LoadedPackageIds { get; set; }
            public string[] AppliedPackageIds { get; set; }
            public IReadOnlyDictionary<string, string> SkippedPackages { get; set; }
            public IReadOnlyDictionary<string, string> DisabledPackages { get; set; }
            public RailPackageFault[] Faults { get; set; }
            public RailRegistryConflict[] Conflicts { get; set; }
            public string[] SceneSuppressions { get; set; }
            public string[] TrackGroupSuppressions { get; set; }
            public string[] AreaSuppressions { get; set; }
            public UnknownSceneryAsset[] UnknownSceneryAssets { get; set; }
            public string[] Notices { get; set; }

            public int FaultedPackageCount =>
                Faults == null
                    ? 0
                    : Faults.Select(fault => fault.PackageId).Distinct(StringComparer.OrdinalIgnoreCase).Count();

            public bool HasProblems =>
                FaultedPackageCount > 0 ||
                (Conflicts != null && Conflicts.Length > 0) ||
                (UnknownSceneryAssets != null && UnknownSceneryAssets.Length > 0) ||
                (SkippedPackages != null && SkippedPackages.Count > 0) ||
                (Notices != null && Notices.Length > 0);
        }
    }
}
