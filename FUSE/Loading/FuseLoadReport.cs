using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using FUSE.Authoring.Data;
using FUSE.Infrastructure;
using FUSE.Runtime.Registry;
using Newtonsoft.Json.Linq;
using UI.Common;

namespace FUSE.Loading
{
    internal static class FuseLoadReport
    {
        private static readonly object Sync = new object();
        private static readonly Dictionary<string, UnknownSceneryAsset> UnknownSceneryAssets =
            new Dictionary<string, UnknownSceneryAsset>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<string> Notices = new List<string>();
        private static readonly List<string> GraphPostBindIssues = new List<string>();
        private static readonly List<string> ProgressionTransferSkips = new List<string>();
        private static readonly Dictionary<string, SceneryLoadFailure> SceneryLoadFailures =
            new Dictionary<string, SceneryLoadFailure>(StringComparer.OrdinalIgnoreCase);

        private static string _lastSummary = "FUSE load report has not been generated yet.";
        private static string _lastDetails = "FUSE load report has not been generated yet.";
        private static string _lastJson = "{ \"status\": \"FUSE load report has not been generated yet.\" }";
        private static ReportSnapshot _lastReportSnapshot = null;

        // Captured at the moment of <see cref="PublishMapLoadReport"/>
        // so the on-demand <see cref="GetLastDetailReport"/> /
        // <see cref="GetLastJsonReport"/> rebuild can use the same
        // reason string + pass counts the published summary did. The
        // registries themselves are re-read fresh so any fault
        // recorded after publish (e.g. per-car AddCarInternal
        // finalizer) shows up.
        private static string _lastPublishReason;
        private static int _lastLoadedFromDiskThisPass;
        private static int _lastAppliedToRuntimeThisPass;
        private static bool _hasPublishedAtLeastOnce;

        // Deferred-publish bookkeeping: lifecycle hands us the
        // "reason / loadedCount / appliedCount" tuple while the
        // map-load pipeline is closing out, but the per-car
        // AddCarInternal finalizer (which populates the orphan
        // registry) doesn't run until after HandleSnapshotCars
        // processes the save. Publishing inline at lifecycle time
        // would produce a toast that says "orphans 0" even when 2
        // are about to be recorded. <see cref="ScheduleMapLoadReport"/>
        // stashes the args and a Postfix on HandleSnapshotCars
        // calls <see cref="FlushScheduledMapLoadReport"/> at the
        // right time so the toast / log / cached strings reflect
        // the real registry state.
        private static bool _pendingPublishScheduled;
        private static string _pendingPublishReason;
        private static int _pendingLoadedFromDisk;
        private static int _pendingAppliedToRuntime;

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
                GraphPostBindIssues.Clear();
                ProgressionTransferSkips.Clear();
                SceneryLoadFailures.Clear();
                _lastSummary = "FUSE load report is pending.";
                _lastDetails = "FUSE load report is pending.";
                _lastJson = "{ \"status\": \"FUSE load report is pending.\" }";
                _lastReportSnapshot = null;
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

        /// <summary>
        /// Records a scenery asset whose runtime load faulted (e.g. the asset is
        /// listed in a pack's catalog but missing from its bundle — the game keeps
        /// retrying such loads on every culling-band transition without ever telling
        /// the user which pack is broken). Deduped per asset identifier; returns true
        /// only for the first record so the caller can decide about an immediate
        /// alert. Surfaces via the health report the next time it renders — the
        /// report re-snapshots on demand, so post-load recording needs no republish.
        /// </summary>
        public static bool RecordSceneryLoadFailure(string assetIdentifier, string assetPackIdentifier, string ownerPackageId, string message)
        {
            var key = Normalize(assetIdentifier, "<unknown>");
            lock (Sync)
            {
                if (SceneryLoadFailures.ContainsKey(key))
                {
                    return false;
                }

                SceneryLoadFailures[key] = new SceneryLoadFailure(
                    key,
                    Normalize(assetPackIdentifier, "<unknown>"),
                    Normalize(ownerPackageId, "<unknown>"),
                    Normalize(message, "asset load failed"));
                return true;
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

        public static void RecordGraphPostBindIssue(string packageId, string kind, string objectId, string message)
        {
            var line =
                $"package='{Normalize(packageId, "<unknown>")}' kind='{Normalize(kind, "<unknown>")}' " +
                $"id='{Normalize(objectId, "<unknown>")}' reason='{Normalize(message, "post-bind validation failed")}'";
            lock (Sync)
            {
                if (!GraphPostBindIssues.Contains(line))
                {
                    GraphPostBindIssues.Add(line);
                }
            }
        }

        public static void RecordProgressionTransferSkip(string packageId, string sectionId, string sourceId, string targetId, string reason)
        {
            var line =
                $"package='{Normalize(packageId, "<unknown>")}' section='{Normalize(sectionId, "<unknown>")}' " +
                $"source='{Normalize(sourceId, "<blank>")}' target='{Normalize(targetId, "<blank>")}' " +
                $"reason='{Normalize(reason, "interchange transfer skipped")}'";
            lock (Sync)
            {
                if (!ProgressionTransferSkips.Contains(line))
                {
                    ProgressionTransferSkips.Add(line);
                }
            }
        }

        public static string GetLastDetailReport()
        {
            // The cached <c>_lastDetails</c> was generated at the
            // PublishMapLoadReport moment, which fires during initial
            // load before per-car snapshot processing finishes. Some
            // registries (notably <see cref="FuseSaveCarFaultRegistry"/>)
            // are populated AFTER that point, so a stale cache would
            // miss them. Re-snapshot here so /fuse.report always
            // reflects the current registry state. If we have not yet
            // generated any report this session (rare — only before
            // the first load), fall back to the placeholder.
            var hasPublished = HasGeneratedAReport();
            var registryCount = FuseSaveCarFaultRegistry.Count;
            FuseLog.Info(
                $"FUSE diag GetLastDetailReport invoked: hasPublishedAtLeastOnce={hasPublished} " +
                $"orphanRegistryCount={registryCount}.");

            if (!hasPublished)
            {
                lock (Sync)
                {
                    return _lastDetails;
                }
            }

            var snapshot = CaptureCurrentSnapshot();
            _lastReportSnapshot = snapshot;
            var summary = BuildSummary(snapshot);
            return BuildDetails(snapshot, summary);
        }

        public static string GetLastJsonReport()
        {
            var hasPublished = HasGeneratedAReport();
            var registryCount = FuseSaveCarFaultRegistry.Count;
            FuseLog.Info(
                $"FUSE diag GetLastJsonReport invoked: hasPublishedAtLeastOnce={hasPublished} " +
                $"orphanRegistryCount={registryCount}.");

            if (!hasPublished)
            {
                lock (Sync)
                {
                    return _lastJson;
                }
            }

            var snapshot = CaptureCurrentSnapshot();
            _lastReportSnapshot = snapshot;
            var summary = BuildSummary(snapshot);
            return BuildJson(snapshot, summary);
        }

        public static ReportSnapshot GetLastReportSnapshot()
        {
            if ( _lastReportSnapshot == null )
            {
                return _lastReportSnapshot;
            }
            var snapshot = CaptureCurrentSnapshot();
            _lastReportSnapshot = snapshot;
            return _lastReportSnapshot;
        }

        /// <summary>
        /// Re-snapshots every registry the report exposes without
        /// disturbing the cached "as-published" summary that the
        /// startup toast / log line consumers depend on. Used by the
        /// on-demand console getters so the displayed report always
        /// reflects fault records added after initial publish.
        /// </summary>
        private static ReportSnapshot CaptureCurrentSnapshot()
        {
            string lastReason;
            lock (Sync)
            {
                lastReason = _lastPublishReason ?? "map load";
            }
            return CaptureSnapshot(lastReason, _lastLoadedFromDiskThisPass, _lastAppliedToRuntimeThisPass);
        }

        private static bool HasGeneratedAReport()
        {
            lock (Sync)
            {
                return _hasPublishedAtLeastOnce;
            }
        }

        /// <summary>
        /// Records the lifecycle's intent to publish a map-load
        /// report but defers the actual publish until
        /// <see cref="FlushScheduledMapLoadReport"/> is called. The
        /// hook firing the flush is a Postfix on
        /// <c>TrainController.HandleSnapshotCars</c>, which runs
        /// AFTER every snapshot car has been attempted and the
        /// orphan registry has been populated by the per-car
        /// finalizer. Without this deferral the toast and the log
        /// summary line fire while the orphan registry is still
        /// empty and report <c>orphans 0</c> even when the save
        /// contains broken legacy car instances.
        /// </summary>
        public static void ScheduleMapLoadReport(string reason, int loadedFromDiskThisPass, int appliedToRuntimeThisPass)
        {
            lock (Sync)
            {
                _pendingPublishScheduled = true;
                _pendingPublishReason = reason;
                _pendingLoadedFromDisk = loadedFromDiskThisPass;
                _pendingAppliedToRuntime = appliedToRuntimeThisPass;
            }
        }

        /// <summary>
        /// Publishes the previously-scheduled map-load report, then
        /// clears the pending flag. No-ops when nothing is
        /// scheduled (idempotent — safe to call from multiple
        /// snapshot-cars completion points). The publish runs the
        /// normal path so the toast, log lines, and cached strings
        /// get the orphan-aware data.
        /// </summary>
        public static void FlushScheduledMapLoadReport()
        {
            string reason;
            int loadedFromDisk;
            int appliedToRuntime;
            lock (Sync)
            {
                if (!_pendingPublishScheduled)
                {
                    return;
                }
                reason = _pendingPublishReason;
                loadedFromDisk = _pendingLoadedFromDisk;
                appliedToRuntime = _pendingAppliedToRuntime;
                _pendingPublishScheduled = false;
                _pendingPublishReason = null;
                _pendingLoadedFromDisk = 0;
                _pendingAppliedToRuntime = 0;
            }

            PublishMapLoadReport(reason, loadedFromDisk, appliedToRuntime);
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
                _lastJson = BuildJson(snapshot, summary);
                _lastPublishReason = reason;
                _lastLoadedFromDiskThisPass = loadedFromDiskThisPass;
                _lastAppliedToRuntimeThisPass = appliedToRuntimeThisPass;
                _hasPublishedAtLeastOnce = true;
                _lastReportSnapshot = snapshot;
            }

            if (snapshot.HasProblems)
            {
                FuseLog.Warning(summary);
            }
            else
            {
                FuseLog.Info(summary);
            }

            FuseLog.Info(details);
            PresentToast(summary, snapshot.HasProblems);
        }

        private static ReportSnapshot CaptureSnapshot(string reason, int loadedFromDiskThisPass, int appliedToRuntimeThisPass)
        {
            // Stops may have been refreshed since the last validation pass (graph
            // rebuilds re-run every FUSE stop); revalidate before reading the
            // post-bind issue registry so on-demand report renders stay current.
            // No-ops unless a refresh marked the validator dirty.
            FUSE.Runtime.API.FusePassengerStopValidation.RunIfDirty(reason ?? "report snapshot");

            UnknownSceneryAsset[] unknownScenery;
            string[] notices;
            string[] graphPostBindIssues;
            string[] progressionTransferSkips;
            SceneryLoadFailure[] sceneryLoadFailures;
            lock (Sync)
            {
                unknownScenery = UnknownSceneryAssets.Values
                    .OrderBy(item => item.PackageId, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.SceneryId, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                notices = Notices.ToArray();
                graphPostBindIssues = GraphPostBindIssues.ToArray();
                progressionTransferSkips = ProgressionTransferSkips.ToArray();
                sceneryLoadFailures = SceneryLoadFailures.Values
                    .OrderBy(item => item.AssetPackIdentifier, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.AssetIdentifier, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }

            // Catalog inspection failures live with the asset-pack mount state (built
            // lazily once per mount, so a later map load in the same session would
            // otherwise lose them when Notices is cleared) — fold them in per snapshot.
            notices = notices
                .Concat(FuseAssetPackRegistry.GetCatalogInspectionFailures())
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            var sceneSuppressions = FuseWorldSuppressor.GetActiveScenePathSuppressions().ToArray();
            var trackGroupSuppressions = FuseWorldSuppressor.GetActiveTrackGroupSuppressions().ToArray();
            var areaSuppressions = FuseWorldSuppressor.GetActiveAreaSuppressions().ToArray();

            var loadedPackageIds = FusePackageFaultRegistry.GetLoadedPackageIds().ToArray();
            var appliedPackageIds = FusePackageFaultRegistry.GetAppliedPackageIds().ToArray();
            var skippedPackages = FusePackageFaultRegistry.GetSkippedPackages();
            var disabledPackages = FusePackageFaultRegistry.GetDisabledPackages();
            var faults = FusePackageFaultRegistry.GetFaults().ToArray();
            var conflicts = FuseRegistry.Conflicts.ToArray();
            var legacyConvertedPackageIds = FuseDataPackageDiscovery.GetLegacyConvertedPackageIds().ToArray();
            var orphanedCars = FuseSaveCarFaultRegistry.GetAll().ToArray();

            // Session-cumulative third-party exception observations. One
            // atomic capture (rows + totals under the registry's lock) so the
            // summary line, details section, JSON block, and HasProblems all
            // describe the same instant even while the log hook keeps
            // recording on other threads.
            var modExceptionState = FuseModExceptionRegistry.CaptureReportState();
            var modExceptions = modExceptionState.Mods;
            var modExceptionTotal = modExceptionState.Total;
            var modExceptionUnattributed = modExceptionState.Unattributed;

            return new ReportSnapshot
            {
                Reason = string.IsNullOrWhiteSpace(reason) ? "map load" : reason,
                LoadedFromDiskThisPass = loadedFromDiskThisPass,
                AppliedToRuntimeThisPass = appliedToRuntimeThisPass,
                ResidentDefinitionCount = FuseModLoader.LoadedDefinitionCount,
                LoadedPackageIds = loadedPackageIds,
                AppliedPackageIds = appliedPackageIds,
                SkippedPackages = skippedPackages,
                DisabledPackages = disabledPackages,
                LegacyConvertedPackageIds = legacyConvertedPackageIds,
                Faults = faults,
                Conflicts = conflicts,
                SceneSuppressions = sceneSuppressions,
                TrackGroupSuppressions = trackGroupSuppressions,
                AreaSuppressions = areaSuppressions,
                UnknownSceneryAssets = unknownScenery,
                GraphPostBindIssues = graphPostBindIssues,
                ProgressionTransferSkips = progressionTransferSkips,
                Notices = notices,
                SceneryLoadFailures = sceneryLoadFailures,
                OrphanedCars = orphanedCars,
                ModExceptions = modExceptions,
                ModExceptionTotal = modExceptionTotal,
                ModExceptionUnattributed = modExceptionUnattributed
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
                $"FUSE: {loadedCount} loaded | faults {snapshot.FaultedPackageCount} | " +
                $"conflicts {snapshot.Conflicts.Length} | assets {snapshot.UnknownSceneryAssets.Length} | " +
                $"brokenAssets {snapshot.SceneryLoadFailureCount} | " +
                $"graph {snapshot.GraphPostBindIssues.Length} | transfers {snapshot.ProgressionTransferSkips.Length} | " +
                $"suppressions {suppressionCount} | orphans {snapshot.OrphanedCarCount} | " +
                $"modErrors {snapshot.ModExceptionTotal} | /fuse.report";
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
            sb.AppendLine(
                $"Post-bind: graphIssues={snapshot.GraphPostBindIssues.Length}; " +
                $"progressionTransferSkips={snapshot.ProgressionTransferSkips.Length}.");

            AppendList(sb, "Loaded packages", snapshot.LoadedPackageIds);
            AppendList(sb, "Applied packages", snapshot.AppliedPackageIds);
            AppendList(sb, "Legacy-converted packages", snapshot.LegacyConvertedPackageIds);
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

            sb.AppendLine($"Conflicts recorded: {snapshot.Conflicts.Length} (details: /fuse.conflicts).");

            // Live session counters, not part of the load snapshot: every guard FUSE
            // keeps around broken content, so a pasted report answers "did the guards
            // fire?" without the reporter having to open the Health window at all.
            sb.AppendLine("Runtime guards (session): " + FuseRuntimeGuardCounters.FormatSummary() + ".");

            AppendModExceptions(sb, snapshot);

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

            AppendList(sb, "Graph post-bind issues", snapshot.GraphPostBindIssues);
            AppendList(sb, "Progression transfer skips", snapshot.ProgressionTransferSkips);
            AppendList(sb, "Notices", snapshot.Notices);

            if (snapshot.SceneryLoadFailureCount > 0)
            {
                sb.AppendLine("Scenery assets failing to load at runtime (pack bundle/catalog mismatch):");
                foreach (var item in snapshot.SceneryLoadFailures)
                {
                    sb.AppendLine(
                        $"  {item.AssetIdentifier}: pack='{item.AssetPackIdentifier}' " +
                        $"package='{item.OwnerPackageId}' reason='{item.Message}'");
                }
            }

            if (snapshot.OrphanedCarCount > 0)
            {
                sb.AppendLine($"Orphaned cars (save references prototype FUSE filtered or game cannot resolve): {snapshot.OrphanedCarCount}");
                foreach (var fault in snapshot.OrphanedCars)
                {
                    sb.AppendLine(
                        $"  {fault.DisplayName} (id={fault.CarId}) missingPrototype='{fault.MissingPrototypeId}' " +
                        $"at segment={fault.LocationSegmentId} dist={fault.LocationDistance:F1}");
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Renders the session-cumulative third-party exception observations
        /// (see <see cref="FuseModExceptionRegistry"/>). One row per mod,
        /// worst first — the row itself answers "which mod, how often, since
        /// when, what kind" so a pasted report needs no log spelunking.
        /// Omitted entirely while the registry is idle.
        /// </summary>
        private static void AppendModExceptions(StringBuilder sb, ReportSnapshot snapshot)
        {
            // The registry's snapshot already carries the "(unattributed)" and
            // "(other mods)" sentinel buckets as records of their own, so the
            // per-record rows below are the complete story — no separate
            // unattributed footer, or the bucket would be mentioned twice.
            var records = snapshot.ModExceptions ?? Array.Empty<FuseModExceptionSnapshot>();
            if (records.Length == 0)
            {
                return;
            }

            sb.AppendLine("Third-party mod exceptions observed this session:");
            foreach (var record in records.OrderByDescending(item => item.Count))
            {
                var signatures = record.Signatures;
                var signatureCount = signatures == null ? 0 : signatures.Length;
                var display = string.IsNullOrWhiteSpace(record.DisplayName) ? record.ModId : record.DisplayName;
                var line =
                    $"  {display}: {record.Count} exception(s) over {record.Episodes} episode(s), " +
                    $"{signatureCount} signature(s), " +
                    $"first {FormatSessionTime(record.FirstSeenUtc)} last {FormatSessionTime(record.LastSeenUtc)}";
                if (signatureCount > 0)
                {
                    var top = signatures.OrderByDescending(item => item.Count).First();
                    line += $" — top: {top.ExceptionType} @ {top.TopOwnedFrame}";
                }

                sb.AppendLine(line);
            }
        }

        private static string FormatSessionTime(DateTime timestampUtc)
        {
            // Sortable date + explicit UTC marker: a session spanning midnight
            // must not render a later event as an earlier time-of-day.
            return timestampUtc.ToString("u", CultureInfo.InvariantCulture);
        }

        private static string BuildJson(ReportSnapshot snapshot, string summary)
        {
            var suppressionCount =
                snapshot.SceneSuppressions.Length +
                snapshot.TrackGroupSuppressions.Length +
                snapshot.AreaSuppressions.Length;

            var root = new JObject
            {
                ["summary"] = summary ?? string.Empty,
                ["reason"] = snapshot.Reason ?? string.Empty,
                ["hasProblems"] = snapshot.HasProblems,
                ["counts"] = new JObject
                {
                    ["loadedFromDiskThisPass"] = snapshot.LoadedFromDiskThisPass,
                    ["appliedToRuntimeThisPass"] = snapshot.AppliedToRuntimeThisPass,
                    ["residentDefinitions"] = snapshot.ResidentDefinitionCount,
                    ["loadedPackages"] = snapshot.LoadedPackageIds.Length,
                    ["appliedPackages"] = snapshot.AppliedPackageIds.Length,
                    ["skippedPackages"] = snapshot.SkippedPackages.Count,
                    ["disabledPackages"] = snapshot.DisabledPackages.Count,
                    ["legacyConvertedPackages"] = snapshot.LegacyConvertedPackageIds.Length,
                    ["faultedPackages"] = snapshot.FaultedPackageCount,
                    ["conflicts"] = snapshot.Conflicts.Length,
                    ["unknownSceneryAssets"] = snapshot.UnknownSceneryAssets.Length,
                    ["graphIssues"] = snapshot.GraphPostBindIssues.Length,
                    ["progressionTransferSkips"] = snapshot.ProgressionTransferSkips.Length,
                    ["suppressions"] = suppressionCount,
                    ["notices"] = snapshot.Notices.Length,
                    ["sceneryLoadFailures"] = snapshot.SceneryLoadFailureCount,
                    ["orphanedCars"] = snapshot.OrphanedCarCount
                },
                ["packages"] = new JObject
                {
                    ["loaded"] = ToArray(snapshot.LoadedPackageIds),
                    ["applied"] = ToArray(snapshot.AppliedPackageIds),
                    ["legacyConverted"] = ToArray(snapshot.LegacyConvertedPackageIds),
                    ["skipped"] = ToObject(snapshot.SkippedPackages),
                    ["disabled"] = ToObject(snapshot.DisabledPackages),
                    ["faults"] = new JArray(snapshot.Faults.Select(fault => new JObject
                    {
                        ["packageId"] = fault.PackageId ?? string.Empty,
                        ["stage"] = fault.Stage ?? string.Empty,
                        ["message"] = fault.Message ?? string.Empty
                    }))
                },
                ["conflicts"] = new JArray(snapshot.Conflicts.Select(conflict => new JObject
                {
                    ["kind"] = conflict.Kind.ToString(),
                    ["target"] = conflict.Target ?? string.Empty,
                    ["objectId"] = conflict.Id ?? string.Empty,
                    ["ownerPackageId"] = conflict.OwnerPackageId ?? string.Empty,
                    ["attemptedPackageId"] = conflict.AttemptedPackageId ?? string.Empty,
                    ["resolution"] = conflict.Resolution ?? string.Empty
                })),
                // Live session counters (see BuildDetails); intentionally outside
                // "counts" so they do not read as load-snapshot state.
                ["runtimeGuards"] = new JObject
                {
                    ["guardTotal"] = FuseRuntimeGuardCounters.GuardTotal,
                    ["decalRegistryScrubbed"] = FuseRuntimeGuardCounters.DecalRegistryScrubbed,
                    ["decalVisibilitySuppressed"] = FuseRuntimeGuardCounters.DecalVisibilitySuppressed,
                    ["decalHelperEnableSuppressed"] = FuseRuntimeGuardCounters.DecalHelperEnableSuppressed,
                    ["decalHelperDisableSuppressed"] = FuseRuntimeGuardCounters.DecalHelperDisableSuppressed,
                    ["curveMeshSuppressed"] = FuseRuntimeGuardCounters.CurveMeshSuppressed,
                    ["sceneryCarDecalsDisabled"] = FuseRuntimeGuardCounters.SceneryDecalComponentsDisabled,
                    ["sceneryLoadFailures"] = FuseRuntimeGuardCounters.SceneryLoadFailures,
                    ["flaresSuppressed"] = FuseRuntimeGuardCounters.FlareSuppressed,
                    ["frameSpikes"] = FuseRuntimeGuardCounters.FrameSpikes,
                    ["frameSpikeWorstMs"] = FuseRuntimeGuardCounters.FrameSpikeWorstMs
                },
                // Session-cumulative like runtimeGuards above, sourced from the
                // third-party exception registry snapshot taken with this capture.
                ["modExceptions"] = BuildModExceptionsJson(snapshot),
                ["suppressions"] = new JObject
                {
                    ["scenePaths"] = ToArray(snapshot.SceneSuppressions),
                    ["trackGroups"] = ToArray(snapshot.TrackGroupSuppressions),
                    ["areas"] = ToArray(snapshot.AreaSuppressions)
                },
                ["unknownSceneryAssets"] = new JArray(snapshot.UnknownSceneryAssets.Select(item => new JObject
                {
                    ["packageId"] = item.PackageId ?? string.Empty,
                    ["sceneryId"] = item.SceneryId ?? string.Empty,
                    ["assetIdentifier"] = item.AssetIdentifier ?? string.Empty,
                    ["model"] = item.Model ?? string.Empty
                })),
                ["graphPostBindIssues"] = ToArray(snapshot.GraphPostBindIssues),
                ["progressionTransferSkips"] = ToArray(snapshot.ProgressionTransferSkips),
                ["notices"] = ToArray(snapshot.Notices),
                ["sceneryLoadFailures"] = new JArray((snapshot.SceneryLoadFailures ?? Array.Empty<SceneryLoadFailure>()).Select(item => new JObject
                {
                    ["assetIdentifier"] = item.AssetIdentifier ?? string.Empty,
                    ["assetPackIdentifier"] = item.AssetPackIdentifier ?? string.Empty,
                    ["ownerPackageId"] = item.OwnerPackageId ?? string.Empty,
                    ["message"] = item.Message ?? string.Empty
                })),
                ["orphanedCars"] = new JArray((snapshot.OrphanedCars ?? Array.Empty<FuseSaveCarFault>()).Select(fault => new JObject
                {
                    ["carId"] = fault.CarId ?? string.Empty,
                    ["displayName"] = fault.DisplayName ?? string.Empty,
                    ["reportingMark"] = fault.ReportingMark ?? string.Empty,
                    ["roadNumber"] = fault.RoadNumber ?? string.Empty,
                    ["missingPrototypeId"] = fault.MissingPrototypeId ?? string.Empty,
                    ["locationSegmentId"] = fault.LocationSegmentId ?? string.Empty,
                    ["locationDistance"] = fault.LocationDistance,
                    ["locationEndIsA"] = fault.LocationEndIsA,
                    ["reason"] = fault.Reason ?? string.Empty
                }))
            };

            return root.ToString(Newtonsoft.Json.Formatting.Indented);
        }

        private static JObject BuildModExceptionsJson(ReportSnapshot snapshot)
        {
            var records = snapshot.ModExceptions ?? Array.Empty<FuseModExceptionSnapshot>();
            var mods = new JArray();
            foreach (var record in records.OrderByDescending(item => item.Count))
            {
                var signatures = new JArray();
                if (record.Signatures != null)
                {
                    foreach (var signature in record.Signatures.OrderByDescending(item => item.Count))
                    {
                        signatures.Add(new JObject
                        {
                            ["type"] = signature.ExceptionType ?? string.Empty,
                            ["frame"] = signature.TopOwnedFrame ?? string.Empty,
                            ["count"] = signature.Count,
                            ["episodes"] = signature.Episodes,
                            ["source"] = $"{signature.Source}"
                        });
                    }
                }

                mods.Add(new JObject
                {
                    ["modId"] = record.ModId ?? string.Empty,
                    ["displayName"] = record.DisplayName ?? string.Empty,
                    ["count"] = record.Count,
                    ["episodes"] = record.Episodes,
                    ["firstSeen"] = record.FirstSeenUtc.ToString("o", CultureInfo.InvariantCulture),
                    ["lastSeen"] = record.LastSeenUtc.ToString("o", CultureInfo.InvariantCulture),
                    ["signatures"] = signatures
                });
            }

            return new JObject
            {
                ["total"] = snapshot.ModExceptionTotal,
                ["unattributed"] = snapshot.ModExceptionUnattributed,
                ["mods"] = mods
            };
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

        // Test seams (InternalsVisibleTo): the string/JSON builders stay
        // private because production callers must come through the capture
        // pipeline; tests exercise the composition against a hand-built
        // snapshot without touching the live registries CaptureSnapshot reads.
        internal static string BuildSummaryForTests(ReportSnapshot snapshot) => BuildSummary(snapshot);
        internal static string BuildDetailsForTests(ReportSnapshot snapshot) => BuildDetails(snapshot, BuildSummary(snapshot));
        internal static string BuildJsonForTests(ReportSnapshot snapshot) => BuildJson(snapshot, BuildSummary(snapshot));

        private static void PresentToast(string summary, bool hasProblems)
        {
            try
            {
                Toast.Present(summary, hasProblems ? ToastPosition.Middle : ToastPosition.Bottom);
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE could not display map-load report toast", ex);
            }
        }

        private static string Normalize(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        internal sealed class UnknownSceneryAsset
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

        internal sealed class SceneryLoadFailure
        {
            public SceneryLoadFailure(string assetIdentifier, string assetPackIdentifier, string ownerPackageId, string message)
            {
                AssetIdentifier = assetIdentifier;
                AssetPackIdentifier = assetPackIdentifier;
                OwnerPackageId = ownerPackageId;
                Message = message;
            }

            public string AssetIdentifier { get; }
            public string AssetPackIdentifier { get; }
            public string OwnerPackageId { get; }
            public string Message { get; }
        }

        internal sealed class ReportSnapshot
        {
            public string Reason { get; set; }
            public int LoadedFromDiskThisPass { get; set; }
            public int AppliedToRuntimeThisPass { get; set; }
            public int ResidentDefinitionCount { get; set; }
            public string[] LoadedPackageIds { get; set; }
            public string[] AppliedPackageIds { get; set; }
            public IReadOnlyDictionary<string, string> SkippedPackages { get; set; }
            public IReadOnlyDictionary<string, string> DisabledPackages { get; set; }
            public string[] LegacyConvertedPackageIds { get; set; }
            public FusePackageFault[] Faults { get; set; }
            public FuseRegistryConflict[] Conflicts { get; set; }
            public string[] SceneSuppressions { get; set; }
            public string[] TrackGroupSuppressions { get; set; }
            public string[] AreaSuppressions { get; set; }
            public UnknownSceneryAsset[] UnknownSceneryAssets { get; set; }
            public string[] GraphPostBindIssues { get; set; }
            public string[] ProgressionTransferSkips { get; set; }
            public string[] Notices { get; set; }
            public SceneryLoadFailure[] SceneryLoadFailures { get; set; }
            public FuseSaveCarFault[] OrphanedCars { get; set; }
            public FuseModExceptionSnapshot[] ModExceptions { get; set; }
            public long ModExceptionTotal { get; set; }
            public long ModExceptionUnattributed { get; set; }

            public int FaultedPackageCount =>
                Faults == null
                    ? 0
                    : Faults.Select(fault => fault.PackageId).Distinct(StringComparer.OrdinalIgnoreCase).Count();

            public int OrphanedCarCount => OrphanedCars?.Length ?? 0;

            public int SceneryLoadFailureCount => SceneryLoadFailures?.Length ?? 0;

            public bool HasProblems =>
                FaultedPackageCount > 0 ||
                (Conflicts != null && Conflicts.Length > 0) ||
                (UnknownSceneryAssets != null && UnknownSceneryAssets.Length > 0) ||
                (GraphPostBindIssues != null && GraphPostBindIssues.Length > 0) ||
                (ProgressionTransferSkips != null && ProgressionTransferSkips.Length > 0) ||
                (SkippedPackages != null && SkippedPackages.Any(item => !FusePackageFaultRegistry.IsOptionalSkipReason(item.Value))) ||
                (Notices != null && Notices.Length > 0) ||
                SceneryLoadFailureCount > 0 ||
                OrphanedCarCount > 0 ||
                HasModExceptionProblem;

            // A single one-off third-party exception must not flip the report
            // red, but a per-cycle thrower (world moves, update ticks) crosses
            // these thresholds within seconds of the fault starting.
            public bool HasModExceptionProblem =>
                ModExceptions != null &&
                ModExceptions.Any(record => record.Episodes >= 3 || record.Count >= 10);
        }

        private static JArray ToArray(IEnumerable<string> values)
        {
            return new JArray((values ?? Enumerable.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
        }

        private static JObject ToObject(IReadOnlyDictionary<string, string> values)
        {
            var obj = new JObject();
            if (values == null)
            {
                return obj;
            }

            foreach (var entry in values.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
            {
                obj[entry.Key] = entry.Value ?? string.Empty;
            }

            return obj;
        }
    }
}
