using System;
using System.Collections.Generic;
using System.Linq;
using FUSE.Runtime.API;
using FUSE.Authoring.Data;
using FUSE.Authoring.Data.Common;
using FUSE.Infrastructure;
using FUSE.Runtime.Registry;
using Track;
using UnityEngine;

namespace FUSE.Loading
{
    internal static class FuseTrackRemovalSnapshotStore
    {
        private const string LossyTrackRestoreWarning =
            "lossy fields not captured: turntable links, private CTC display state, event delegates, runtime caches, " +
            "Bezier/cache internals, generated meshes, collider meshes, and span route caches.";

        private static readonly Dictionary<string, PackageTrackSnapshots> SnapshotsByPackage =
            new Dictionary<string, PackageTrackSnapshots>(StringComparer.OrdinalIgnoreCase);

        // Capture-batch span index. Every segment (and node->segment cascade)
        // capture needs "which spans reference this segment", which the live
        // path answers with a full FindObjectsOfType<TrackSpan> scene scan per
        // segment — ~800 scans on a removal-heavy map load. Inside a capture
        // batch the first lookup builds one segment-id -> spans index from a
        // single scan and every later lookup is a dictionary hit. The index is
        // batch-scoped only: live-reload single-package paths never open a
        // batch and keep the always-fresh scan.
        //
        // Thread-safety: these statics carry no synchronization by design. The
        // whole capture/removal path runs on the Unity main thread (driven
        // synchronously from the map-load apply pipeline, which calls
        // FindObjectsOfType — itself main-thread-only), so Begin/EndCaptureBatch
        // and the lazy build below cannot race. Do not call them off the main
        // thread.
        private static bool _captureBatchActive;
        private static Dictionary<string, List<TrackSpan>> _captureBatchSpansBySegment;

        public static void BeginCaptureBatch()
        {
            _captureBatchActive = true;
            _captureBatchSpansBySegment = null;
        }

        public static void EndCaptureBatch()
        {
            _captureBatchActive = false;
            _captureBatchSpansBySegment = null;
        }

        public static void CaptureSpanBeforeRemoval(string packageId, string spanId, FuseApplyTransaction transaction)
        {
            if (string.IsNullOrWhiteSpace(packageId) || string.IsNullOrWhiteSpace(spanId))
            {
                return;
            }

            var span = TrackAPI.GetSpan(spanId);
            if (span == null)
            {
                return;
            }

            if (!ShouldCaptureBaseTrack(FuseClaimKind.Span, spanId, packageId, "track span", transaction))
            {
                return;
            }

            var package = GetOrCreatePackage(packageId);
            if (package.Spans.ContainsKey(spanId))
            {
                return;
            }

            if (!TrySnapshotSpan(span, out var snapshot, out var reason))
            {
                Warn(transaction, "track span snapshot", spanId, reason);
                return;
            }

            package.Spans[spanId] = snapshot;
            if (FuseSettings.VerboseApplyReportDetails)
            {
                FuseLog.Info(
                    $"FUSE captured removable base-game track snapshot package='{packageId}' operation='capture-track-removal-snapshot' " +
                    $"kind='track span' id='{spanId}'.");
            }
        }

        public static void CaptureSegmentBeforeRemoval(string packageId, string segmentId, FuseApplyTransaction transaction)
        {
            if (string.IsNullOrWhiteSpace(packageId) || string.IsNullOrWhiteSpace(segmentId))
            {
                return;
            }

            var segment = TrackAPI.GetSegment(segmentId);
            if (segment == null)
            {
                return;
            }

            CaptureSpansTouchingSegment(packageId, segmentId, transaction);

            if (!ShouldCaptureBaseTrack(FuseClaimKind.Segment, segmentId, packageId, "track segment", transaction))
            {
                return;
            }

            var package = GetOrCreatePackage(packageId);
            if (package.Segments.ContainsKey(segmentId))
            {
                return;
            }

            if (!TrySnapshotSegment(segment, out var snapshot, out var reason))
            {
                Warn(transaction, "track segment snapshot", segmentId, reason);
                return;
            }

            package.Segments[segmentId] = snapshot;
            if (FuseSettings.VerboseApplyReportDetails)
            {
                FuseLog.Info(
                    $"FUSE captured removable base-game track snapshot package='{packageId}' operation='capture-track-removal-snapshot' " +
                    $"kind='track segment' id='{segmentId}'.");
            }
        }

        public static void CaptureNodeBeforeRemoval(string packageId, string nodeId, FuseApplyTransaction transaction)
        {
            if (string.IsNullOrWhiteSpace(packageId) || string.IsNullOrWhiteSpace(nodeId))
            {
                return;
            }

            var node = TrackAPI.GetNode(nodeId);
            if (node == null)
            {
                return;
            }

            CaptureSegmentsTouchingNode(packageId, node, transaction);

            if (!ShouldCaptureBaseTrack(FuseClaimKind.Node, nodeId, packageId, "track node", transaction))
            {
                return;
            }

            var package = GetOrCreatePackage(packageId);
            if (package.Nodes.ContainsKey(nodeId))
            {
                return;
            }

            package.Nodes[nodeId] = SnapshotNode(node);
            if (FuseSettings.VerboseApplyReportDetails)
            {
                FuseLog.Info(
                    $"FUSE captured removable base-game track snapshot package='{packageId}' operation='capture-track-removal-snapshot' " +
                    $"kind='track node' id='{nodeId}'.");
            }
        }

        public static void RestorePackage(string packageId)
        {
            if (string.IsNullOrWhiteSpace(packageId) || !SnapshotsByPackage.TryGetValue(packageId, out var package))
            {
                return;
            }

            if (Graph.Shared == null)
            {
                FuseLog.Warning(
                    $"FUSE track restore skipped package='{packageId}' operation='restore-track-removal-snapshot' " +
                    "phase='restore' kind='track snapshots' id='<package>' reason='Graph.Shared is null'.");
                SnapshotsByPackage.Remove(packageId);
                return;
            }

            FuseLog.Warning(
                $"FUSE restoring removed base-game track snapshots package='{packageId}' operation='restore-track-removal-snapshot' " +
                $"phase='restore' kind='track snapshots' id='<package>' nodes={package.Nodes.Count} " +
                $"segments={package.Segments.Count} spans={package.Spans.Count} warning='{LossyTrackRestoreWarning}'");

            var restoredNodes = 0;
            var restoredSegments = 0;
            var restoredSpans = 0;
            var skipped = new List<string>();

            // Outer batch folds the previously-separate nodes/segments and spans
            // rebuilds into a single graph rebuild at the end. The two phases
            // each used to call TrackAPI.RebuildGraph() directly, which doubled
            // the cost of every package snapshot restore.
            TrackAPI.BeginBatch();
            try
            {
                TrackAPI.BeginBatch();
                try
                {
                    DropInvalidRuntimeTrackBeforeRestore(packageId);

                    foreach (var snapshot in package.Nodes.Values)
                    {
                        if (TryRestoreNode(snapshot, skipped))
                        {
                            restoredNodes++;
                        }
                    }

                    foreach (var snapshot in package.Segments.Values)
                    {
                        if (TryRestoreSegment(snapshot, skipped))
                        {
                            restoredSegments++;
                        }
                    }
                }
                finally
                {
                    TrackAPI.EndBatch(false);
                }

                TryRebuildGraphAfterRestore(packageId, "nodes and segments");

                TrackAPI.BeginBatch();
                try
                {
                    foreach (var snapshot in package.Spans.Values)
                    {
                        if (TryRestoreSpan(snapshot, skipped))
                        {
                            restoredSpans++;
                        }
                    }
                }
                finally
                {
                    TrackAPI.EndBatch(false);
                }

                TryRebuildGraphAfterRestore(packageId, "spans");
            }
            finally
            {
                TrackAPI.EndBatch(true);
            }

            foreach (var item in skipped.Take(40))
            {
                FuseLog.Warning($"FUSE track restore skipped package='{packageId}' operation='restore-track-removal-snapshot' {item}");
            }

            if (skipped.Count > 40)
            {
                FuseLog.Warning(
                    $"FUSE track restore skipped package='{packageId}' operation='restore-track-removal-snapshot' " +
                    $"phase='restore' kind='track snapshots' id='<package>' reason='{skipped.Count - 40} more item(s) omitted'.");
            }

            FuseLog.Info(
                $"FUSE restored removed track snapshots package='{packageId}' operation='restore-track-removal-snapshot' " +
                $"phase='restore' kind='track snapshots' id='<package>' nodes={restoredNodes}/{package.Nodes.Count} " +
                $"segments={restoredSegments}/{package.Segments.Count} spans={restoredSpans}/{package.Spans.Count} skipped={skipped.Count}.");

            SnapshotsByPackage.Remove(packageId);
        }

        private static void TryRebuildGraphAfterRestore(string packageId, string phase)
        {
            try
            {
                // RequestRebuild defers when the caller has an outer batch open,
                // so two sequential restore phases now share a single rebuild.
                TrackAPI.RequestRebuild();
            }
            catch (Exception ex)
            {
                FuseLog.Exception(
                    $"FUSE graph rebuild failed package='{packageId}' operation='restore-track-removal-snapshot' " +
                    $"phase='{phase ?? string.Empty}' kind='graph' id='<graph>'",
                    ex);
            }
        }

        public static bool ClearPackage(string packageId)
        {
            if (string.IsNullOrWhiteSpace(packageId))
            {
                return false;
            }

            return SnapshotsByPackage.Remove(packageId);
        }

        public static void ClearAll()
        {
            SnapshotsByPackage.Clear();
        }

        private static void CaptureSegmentsTouchingNode(string packageId, TrackNode node, FuseApplyTransaction transaction)
        {
            var graph = Graph.Shared;
            if (graph == null || node == null)
            {
                return;
            }

            TrackSegment[] connected;
            try
            {
                connected = graph.SegmentsConnectedTo(node)
                    .Where(segment => segment != null && !string.IsNullOrWhiteSpace(segment.id))
                    .ToArray();
            }
            catch (Exception ex)
            {
                Warn(transaction, "track node snapshot", node != null ? node.id : string.Empty, $"failed to inspect connected segments: {ex.Message}");
                return;
            }

            foreach (var segment in connected)
            {
                CaptureSegmentBeforeRemoval(packageId, segment.id, transaction);
            }
        }

        private static void CaptureSpansTouchingSegment(string packageId, string segmentId, FuseApplyTransaction transaction)
        {
            foreach (var span in FindSpansReferencingSegment(segmentId, packageId))
            {
                CaptureSpanBeforeRemoval(packageId, span.id, transaction);
            }
        }

        private static IEnumerable<TrackSpan> FindSpansReferencingSegment(string segmentId, string packageId)
        {
            if (string.IsNullOrWhiteSpace(segmentId))
            {
                return Enumerable.Empty<TrackSpan>();
            }

            if (_captureBatchActive)
            {
                var index = _captureBatchSpansBySegment ??
                            (_captureBatchSpansBySegment = BuildSpansBySegmentIndex(packageId));
                // The null filter mirrors the live scan below (which also skips
                // null spans) and guards the caller's span.id access: index
                // entries are non-null at build, but a span an earlier removal in
                // this same batch destroyed could read back null here.
                return index.TryGetValue(segmentId, out var spans)
                    ? spans.Where(span => span != null)
                    : Enumerable.Empty<TrackSpan>();
            }

            var matches = new List<TrackSpan>();
            foreach (var span in UnityEngine.Object.FindObjectsOfType<TrackSpan>(true))
            {
                if (span == null || string.IsNullOrWhiteSpace(span.id))
                {
                    continue;
                }

                try
                {
                    if (LocationReferencesSegment(span.upper, segmentId) ||
                        LocationReferencesSegment(span.lower, segmentId))
                    {
                        matches.Add(span);
                    }
                }
                catch (Exception ex)
                {
                    FuseLog.Warning(
                        $"FUSE could not inspect span package='{packageId ?? "<unknown>"}' operation='capture-track-removal-snapshot' " +
                        $"phase='snapshot' kind='track span' id='{span.id}' segment='{segmentId}' reason='{ex.Message}'.");
                }
            }

            return matches;
        }

        private static bool LocationReferencesSegment(Location? location, string segmentId)
        {
            return location.HasValue &&
                   location.Value.segment != null &&
                   string.Equals(location.Value.segment.id, segmentId, StringComparison.OrdinalIgnoreCase);
        }

        private static Dictionary<string, List<TrackSpan>> BuildSpansBySegmentIndex(string packageId)
        {
            var index = new Dictionary<string, List<TrackSpan>>(StringComparer.OrdinalIgnoreCase);
            foreach (var span in UnityEngine.Object.FindObjectsOfType<TrackSpan>(true))
            {
                if (span == null || string.IsNullOrWhiteSpace(span.id))
                {
                    continue;
                }

                try
                {
                    Location? upper = span.upper;
                    Location? lower = span.lower;
                    var upperId = upper.HasValue && upper.Value.segment != null ? upper.Value.segment.id : null;
                    var lowerId = lower.HasValue && lower.Value.segment != null ? lower.Value.segment.id : null;
                    AddSpanToIndex(index, upperId, span);
                    if (!string.Equals(lowerId, upperId, StringComparison.OrdinalIgnoreCase))
                    {
                        AddSpanToIndex(index, lowerId, span);
                    }
                }
                catch (Exception ex)
                {
                    FuseLog.Warning(
                        $"FUSE could not inspect span package='{packageId ?? "<unknown>"}' operation='capture-track-removal-snapshot' " +
                        $"phase='snapshot' kind='track span' id='{span.id}' segment='<index build>' reason='{ex.Message}'.");
                }
            }

            return index;
        }

        private static void AddSpanToIndex(Dictionary<string, List<TrackSpan>> index, string segmentId, TrackSpan span)
        {
            if (string.IsNullOrWhiteSpace(segmentId))
            {
                return;
            }

            if (!index.TryGetValue(segmentId, out var spans))
            {
                spans = new List<TrackSpan>();
                index[segmentId] = spans;
            }

            spans.Add(span);
        }

        private static bool ShouldCaptureBaseTrack(FuseClaimKind kind, string id, string packageId, string label, FuseApplyTransaction transaction)
        {
            var owner = FuseRegistry.GetExclusiveOwner(kind, id);
            if (string.IsNullOrWhiteSpace(owner))
            {
                return true;
            }

            Warn(
                transaction,
                label + " snapshot",
                id,
                $"not snapshotted because it is FUSE-owned by package '{owner}', not base-game track");
            return false;
        }

        private static FuseRemovedNodeSnapshot SnapshotNode(TrackNode node)
        {
            return new FuseRemovedNodeSnapshot
            {
                Id = node.id,
                Position = node.transform.localPosition,
                Rotation = node.transform.localEulerAngles,
                FlipSwitchStand = node.flipSwitchStand,
                IsThrown = node.isThrown,
                IsCtcSwitch = node.IsCTCSwitch,
                IsCtcSwitchUnlocked = node.IsCTCSwitchUnlocked
            };
        }

        private static bool TrySnapshotSegment(TrackSegment segment, out FuseRemovedSegmentSnapshot snapshot, out string reason)
        {
            snapshot = null;
            reason = string.Empty;
            if (segment == null)
            {
                reason = "segment was null";
                return false;
            }

            if (segment.a == null || segment.b == null)
            {
                reason = "segment endpoints were missing";
                return false;
            }

            snapshot = new FuseRemovedSegmentSnapshot
            {
                Id = segment.id,
                StartNodeId = segment.a.id,
                EndNodeId = segment.b.id,
                Style = segment.style.ToString(),
                TrackClass = segment.trackClass == TrackClass.Mainline ? "main" : segment.trackClass.ToString(),
                GroupId = segment.groupId,
                Priority = segment.priority,
                SpeedLimit = segment.speedLimit,
                Available = segment.Available,
                GroupEnabled = segment.GroupEnabled,
                Length = SafeGetSegmentLength(segment),
                StartNodePosition = segment.a.transform.localPosition,
                StartNodeRotation = segment.a.transform.localEulerAngles,
                EndNodePosition = segment.b.transform.localPosition,
                EndNodeRotation = segment.b.transform.localEulerAngles
            };
            return true;
        }

        private static bool TrySnapshotSpan(TrackSpan span, out FuseRemovedSpanSnapshot snapshot, out string reason)
        {
            snapshot = null;
            reason = string.Empty;
            if (span == null)
            {
                reason = "span was null";
                return false;
            }

            FuseTrackLocation upper;
            FuseTrackLocation lower;
            try
            {
                if (!TryMakeTrackLocation(span.upper, out upper) || !TryMakeTrackLocation(span.lower, out lower))
                {
                    reason = "span upper/lower location was missing or invalid";
                    return false;
                }
            }
            catch (Exception ex)
            {
                reason = $"span upper/lower location could not be read: {ex.Message}";
                return false;
            }

            snapshot = new FuseRemovedSpanSnapshot
            {
                Id = span.id,
                Upper = upper,
                Lower = lower
            };
            return true;
        }

        private static bool TryMakeTrackLocation(Location? location, out FuseTrackLocation definition)
        {
            definition = null;
            if (!location.HasValue || location.Value.segment == null)
            {
                return false;
            }

            definition = new FuseTrackLocation
            {
                SegmentId = location.Value.segment.id,
                End = location.Value.end == TrackSegment.End.B ? "B" : "A",
                Distance = location.Value.distance
            };
            return true;
        }

        private static bool TryRestoreNode(FuseRemovedNodeSnapshot snapshot, List<string> skipped)
        {
            if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.Id))
            {
                skipped.Add("node '<empty>' reason='snapshot missing id'");
                return false;
            }

            try
            {
                var existing = TrackAPI.GetNode(snapshot.Id);
                if (existing == null)
                {
                    TrackAPI.AddNode(snapshot.Id, snapshot.Position, snapshot.Rotation, snapshot.FlipSwitchStand);
                }
                else
                {
                    TrackAPI.UpdateNode(snapshot.Id, snapshot.Position, snapshot.Rotation, snapshot.FlipSwitchStand);
                }

                var restored = TrackAPI.GetNode(snapshot.Id);
                if (restored != null)
                {
                    restored.IsCTCSwitch = snapshot.IsCtcSwitch;
                    restored.IsCTCSwitchUnlocked = snapshot.IsCtcSwitchUnlocked;
                    restored.isThrown = snapshot.IsThrown;
                }

                return true;
            }
            catch (Exception ex)
            {
                skipped.Add($"node '{snapshot.Id}' reason='{ex.Message}'");
                return false;
            }
        }

        private static bool TryRestoreSegment(FuseRemovedSegmentSnapshot snapshot, List<string> skipped)
        {
            if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.Id))
            {
                skipped.Add("segment '<empty>' reason='snapshot missing id'");
                return false;
            }

            if (TrackAPI.GetNode(snapshot.StartNodeId) == null || TrackAPI.GetNode(snapshot.EndNodeId) == null)
            {
                skipped.Add(
                    $"segment '{snapshot.Id}' reason='missing endpoint(s) start={snapshot.StartNodeId ?? string.Empty} end={snapshot.EndNodeId ?? string.Empty}'");
                return false;
            }

            var definition = new FuseSegment
            {
                StartNodeId = snapshot.StartNodeId,
                EndNodeId = snapshot.EndNodeId,
                Style = snapshot.Style,
                TrackClass = snapshot.TrackClass,
                SpeedLimit = snapshot.SpeedLimit,
                Priority = snapshot.Priority,
                GroupId = snapshot.GroupId
            };

            try
            {
                var existing = TrackAPI.GetSegment(snapshot.Id);
                if (existing == null)
                {
                    TrackAPI.AddSegment(snapshot.Id, definition);
                }
                else if (!SameEndpoints(existing, snapshot))
                {
                    TrackAPI.RemoveSegment(snapshot.Id);
                    TrackAPI.AddSegment(snapshot.Id, definition);
                }
                else
                {
                    TrackAPI.UpdateSegment(snapshot.Id, definition);
                }

                var restored = TrackAPI.GetSegment(snapshot.Id);
                if (restored != null)
                {
                    restored.Available = snapshot.Available;
                    restored.GroupEnabled = snapshot.GroupEnabled;
                }

                return true;
            }
            catch (Exception ex)
            {
                skipped.Add($"segment '{snapshot.Id}' reason='{ex.Message}'");
                return false;
            }
        }

        private static bool TryRestoreSpan(FuseRemovedSpanSnapshot snapshot, List<string> skipped)
        {
            if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.Id))
            {
                skipped.Add("span '<empty>' reason='snapshot missing id'");
                return false;
            }

            if (!SpanSegmentsAvailable(snapshot))
            {
                skipped.Add($"span '{snapshot.Id}' reason='missing upper/lower segment'");
                return false;
            }

            var definition = new FuseSpan
            {
                Upper = snapshot.Upper,
                Lower = snapshot.Lower,
                Normalize = false
            };

            try
            {
                if (TrackAPI.GetSpan(snapshot.Id) == null)
                {
                    TrackAPI.AddSpan(snapshot.Id, definition);
                }
                else
                {
                    TrackAPI.UpdateSpan(snapshot.Id, definition);
                }

                return true;
            }
            catch (Exception ex)
            {
                skipped.Add($"span '{snapshot.Id}' reason='{ex.Message}'");
                return false;
            }
        }

        private static bool SameEndpoints(TrackSegment segment, FuseRemovedSegmentSnapshot snapshot)
        {
            return segment != null &&
                   segment.a != null &&
                   segment.b != null &&
                   string.Equals(segment.a.id, snapshot.StartNodeId, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(segment.b.id, snapshot.EndNodeId, StringComparison.OrdinalIgnoreCase);
        }

        private static bool SpanSegmentsAvailable(FuseRemovedSpanSnapshot snapshot)
        {
            return snapshot?.Upper != null &&
                   snapshot.Lower != null &&
                   TrackAPI.GetSegment(snapshot.Upper.SegmentId) != null &&
                   TrackAPI.GetSegment(snapshot.Lower.SegmentId) != null;
        }

        private static void DropInvalidRuntimeTrackBeforeRestore(string packageId)
        {
            foreach (var segment in TrackAPI.GetAllSegments().Where(segment => segment == null || segment.a == null || segment.b == null).ToArray())
            {
                if (segment == null || string.IsNullOrWhiteSpace(segment.id))
                {
                    continue;
                }

                try
                {
                    TrackAPI.RemoveSegment(segment.id);
                    FuseLog.Warning(
                        $"FUSE dropped invalid runtime segment package='{packageId}' operation='restore-track-removal-snapshot' " +
                        $"phase='pre-restore-cleanup' kind='track segment' id='{segment.id}' reason='missing endpoint'.");
                }
                catch (Exception ex)
                {
                    FuseLog.Warning(
                        $"FUSE could not drop invalid runtime segment package='{packageId}' operation='restore-track-removal-snapshot' " +
                        $"phase='pre-restore-cleanup' kind='track segment' id='{segment.id}' reason='{ex.Message}'.");
                }
            }

            // Do NOT prune spans here. During reload/restore, spans can appear invalid simply
            // because their referenced segment is temporarily absent while graph edits are being
            // composed. Dropping them here destroys station/industry/passenger spans that become
            // valid again after the final graph rebuild. Span validation belongs after the staged
            // graph commit, not inside snapshot restore.
        }

        private static bool IsInvalidSpan(TrackSpan span)
        {
            if (span == null)
            {
                return false;
            }

            try
            {
                var upper = span.upper;
                var lower = span.lower;
                return !upper.HasValue ||
                       !lower.HasValue ||
                       upper.Value.segment == null ||
                       lower.Value.segment == null;
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    $"FUSE treating span as invalid package='<unknown>' operation='restore-track-removal-snapshot' " +
                    $"phase='pre-restore-cleanup' kind='track span' id='{span.id}' reason='{ex.Message}'.");
                return true;
            }
        }

        private static float SafeGetSegmentLength(TrackSegment segment)
        {
            try
            {
                return segment != null ? segment.GetLength() : 0f;
            }
            catch
            {
                return 0f;
            }
        }

        private static PackageTrackSnapshots GetOrCreatePackage(string packageId)
        {
            if (!SnapshotsByPackage.TryGetValue(packageId, out var package))
            {
                package = new PackageTrackSnapshots();
                SnapshotsByPackage[packageId] = package;
            }

            return package;
        }

        private static void Warn(FuseApplyTransaction transaction, string kind, string id, string message)
        {
            if (transaction != null)
            {
                transaction.Warning(kind, id, message);
            }

            var packageId = transaction?.Report?.DefinitionId ?? "<unknown>";
            var phase = transaction?.CurrentPhase ?? "snapshot";
            FuseLog.Warning(
                $"FUSE track snapshot warning package='{packageId}' operation='track-removal-snapshot' " +
                $"phase='{phase}' kind='{kind ?? string.Empty}' id='{id ?? string.Empty}' reason='{message ?? string.Empty}'.");
        }

        private sealed class PackageTrackSnapshots
        {
            public Dictionary<string, FuseRemovedNodeSnapshot> Nodes { get; } =
                new Dictionary<string, FuseRemovedNodeSnapshot>(StringComparer.OrdinalIgnoreCase);

            public Dictionary<string, FuseRemovedSegmentSnapshot> Segments { get; } =
                new Dictionary<string, FuseRemovedSegmentSnapshot>(StringComparer.OrdinalIgnoreCase);

            public Dictionary<string, FuseRemovedSpanSnapshot> Spans { get; } =
                new Dictionary<string, FuseRemovedSpanSnapshot>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
