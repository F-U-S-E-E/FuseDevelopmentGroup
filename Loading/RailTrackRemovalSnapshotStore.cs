using System;
using System.Collections.Generic;
using System.Linq;
using RAIL.API;
using RAIL.Data;
using RAIL.Data.Common;
using RAIL.Infrastructure;
using RAIL.Registry;
using Track;
using UnityEngine;

namespace RAIL.Loading
{
    internal static class RailTrackRemovalSnapshotStore
    {
        private const string LossyTrackRestoreWarning =
            "lossy fields not captured: turntable links, private CTC display state, event delegates, runtime caches, " +
            "Bezier/cache internals, generated meshes, collider meshes, and span route caches.";

        private static readonly Dictionary<string, PackageTrackSnapshots> SnapshotsByPackage =
            new Dictionary<string, PackageTrackSnapshots>(StringComparer.OrdinalIgnoreCase);

        public static void CaptureSpanBeforeRemoval(string packageId, string spanId, RailApplyTransaction transaction)
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

            if (!ShouldCaptureBaseTrack(RailClaimKind.Span, spanId, packageId, "track span", transaction))
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
            RailLog.Info($"RAIL captured removable base-game span snapshot package='{packageId}' span='{spanId}'.");
        }

        public static void CaptureSegmentBeforeRemoval(string packageId, string segmentId, RailApplyTransaction transaction)
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

            if (!ShouldCaptureBaseTrack(RailClaimKind.Segment, segmentId, packageId, "track segment", transaction))
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
            RailLog.Info($"RAIL captured removable base-game segment snapshot package='{packageId}' segment='{segmentId}'.");
        }

        public static void CaptureNodeBeforeRemoval(string packageId, string nodeId, RailApplyTransaction transaction)
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

            if (!ShouldCaptureBaseTrack(RailClaimKind.Node, nodeId, packageId, "track node", transaction))
            {
                return;
            }

            var package = GetOrCreatePackage(packageId);
            if (package.Nodes.ContainsKey(nodeId))
            {
                return;
            }

            package.Nodes[nodeId] = SnapshotNode(node);
            RailLog.Info($"RAIL captured removable base-game node snapshot package='{packageId}' node='{nodeId}'.");
        }

        public static void RestorePackage(string packageId)
        {
            if (string.IsNullOrWhiteSpace(packageId) || !SnapshotsByPackage.TryGetValue(packageId, out var package))
            {
                return;
            }

            if (Graph.Shared == null)
            {
                RailLog.Warning($"RAIL cannot restore removed track snapshots for package '{packageId}' because Graph.Shared is not available.");
                SnapshotsByPackage.Remove(packageId);
                return;
            }

            RailLog.Warning(
                $"RAIL restoring removed base-game track snapshots for package '{packageId}' " +
                $"nodes={package.Nodes.Count} segments={package.Segments.Count} spans={package.Spans.Count}; {LossyTrackRestoreWarning}");

            var restoredNodes = 0;
            var restoredSegments = 0;
            var restoredSpans = 0;
            var skipped = new List<string>();

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

            try
            {
                TrackAPI.RebuildGraph();
            }
            catch (Exception ex)
            {
                RailLog.Exception($"RAIL graph rebuild failed after restoring removed track for package '{packageId}'", ex);
            }

            foreach (var item in skipped.Take(40))
            {
                RailLog.Warning($"RAIL track restore skipped package='{packageId}' {item}");
            }

            if (skipped.Count > 40)
            {
                RailLog.Warning($"RAIL track restore skipped package='{packageId}' ... {skipped.Count - 40} more item(s) omitted.");
            }

            RailLog.Info(
                $"RAIL restored removed track snapshots for package '{packageId}' " +
                $"nodes={restoredNodes}/{package.Nodes.Count} segments={restoredSegments}/{package.Segments.Count} " +
                $"spans={restoredSpans}/{package.Spans.Count} skipped={skipped.Count}.");

            SnapshotsByPackage.Remove(packageId);
        }

        public static void ClearPackage(string packageId)
        {
            if (!string.IsNullOrWhiteSpace(packageId))
            {
                SnapshotsByPackage.Remove(packageId);
            }
        }

        public static void ClearAll()
        {
            SnapshotsByPackage.Clear();
        }

        private static void CaptureSegmentsTouchingNode(string packageId, TrackNode node, RailApplyTransaction transaction)
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

        private static void CaptureSpansTouchingSegment(string packageId, string segmentId, RailApplyTransaction transaction)
        {
            foreach (var span in FindSpansReferencingSegment(segmentId))
            {
                CaptureSpanBeforeRemoval(packageId, span.id, transaction);
            }
        }

        private static IEnumerable<TrackSpan> FindSpansReferencingSegment(string segmentId)
        {
            if (string.IsNullOrWhiteSpace(segmentId))
            {
                return Enumerable.Empty<TrackSpan>();
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
                    RailLog.Warning($"RAIL could not inspect span '{span.id}' while snapshotting segment '{segmentId}': {ex.Message}");
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

        private static bool ShouldCaptureBaseTrack(RailClaimKind kind, string id, string packageId, string label, RailApplyTransaction transaction)
        {
            var owner = RailRegistry.GetExclusiveOwner(kind, id);
            if (string.IsNullOrWhiteSpace(owner))
            {
                return true;
            }

            Warn(
                transaction,
                label + " snapshot",
                id,
                $"not snapshotted because it is RAIL-owned by package '{owner}', not base-game track");
            return false;
        }

        private static RailRemovedNodeSnapshot SnapshotNode(TrackNode node)
        {
            return new RailRemovedNodeSnapshot
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

        private static bool TrySnapshotSegment(TrackSegment segment, out RailRemovedSegmentSnapshot snapshot, out string reason)
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

            snapshot = new RailRemovedSegmentSnapshot
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

        private static bool TrySnapshotSpan(TrackSpan span, out RailRemovedSpanSnapshot snapshot, out string reason)
        {
            snapshot = null;
            reason = string.Empty;
            if (span == null)
            {
                reason = "span was null";
                return false;
            }

            RailTrackLocation upper;
            RailTrackLocation lower;
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

            snapshot = new RailRemovedSpanSnapshot
            {
                Id = span.id,
                Upper = upper,
                Lower = lower
            };
            return true;
        }

        private static bool TryMakeTrackLocation(Location? location, out RailTrackLocation definition)
        {
            definition = null;
            if (!location.HasValue || location.Value.segment == null)
            {
                return false;
            }

            definition = new RailTrackLocation
            {
                SegmentId = location.Value.segment.id,
                End = location.Value.end == TrackSegment.End.B ? "B" : "A",
                Distance = location.Value.distance
            };
            return true;
        }

        private static bool TryRestoreNode(RailRemovedNodeSnapshot snapshot, ICollection<string> skipped)
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

        private static bool TryRestoreSegment(RailRemovedSegmentSnapshot snapshot, ICollection<string> skipped)
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

            var definition = new RailSegment
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

        private static bool TryRestoreSpan(RailRemovedSpanSnapshot snapshot, ICollection<string> skipped)
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

            var definition = new RailSpan
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

        private static bool SameEndpoints(TrackSegment segment, RailRemovedSegmentSnapshot snapshot)
        {
            return segment != null &&
                   segment.a != null &&
                   segment.b != null &&
                   string.Equals(segment.a.id, snapshot.StartNodeId, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(segment.b.id, snapshot.EndNodeId, StringComparison.OrdinalIgnoreCase);
        }

        private static bool SpanSegmentsAvailable(RailRemovedSpanSnapshot snapshot)
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
                    RailLog.Warning($"RAIL dropped invalid runtime segment '{segment.id}' before restoring removed base-game track for package '{packageId}'.");
                }
                catch (Exception ex)
                {
                    RailLog.Warning($"RAIL could not drop invalid runtime segment '{segment.id}' before track restore: {ex.Message}");
                }
            }

            foreach (var span in TrackAPI.GetAllSpans().Where(IsInvalidSpan).ToArray())
            {
                if (span == null || string.IsNullOrWhiteSpace(span.id))
                {
                    continue;
                }

                try
                {
                    TrackAPI.RemoveSpan(span.id);
                    RailLog.Warning($"RAIL dropped invalid runtime span '{span.id}' before restoring removed base-game track for package '{packageId}'.");
                }
                catch (Exception ex)
                {
                    RailLog.Warning($"RAIL could not drop invalid runtime span '{span.id}' before track restore: {ex.Message}");
                }
            }
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
                RailLog.Warning($"RAIL treating span '{span.id}' as invalid before track restore because its locations could not be read: {ex.Message}");
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

        private static void Warn(RailApplyTransaction transaction, string kind, string id, string message)
        {
            if (transaction != null)
            {
                transaction.Warning(kind, id, message);
            }

            RailLog.Warning($"RAIL {kind} '{id ?? string.Empty}': {message ?? string.Empty}");
        }

        private sealed class PackageTrackSnapshots
        {
            public Dictionary<string, RailRemovedNodeSnapshot> Nodes { get; } =
                new Dictionary<string, RailRemovedNodeSnapshot>(StringComparer.OrdinalIgnoreCase);

            public Dictionary<string, RailRemovedSegmentSnapshot> Segments { get; } =
                new Dictionary<string, RailRemovedSegmentSnapshot>(StringComparer.OrdinalIgnoreCase);

            public Dictionary<string, RailRemovedSpanSnapshot> Spans { get; } =
                new Dictionary<string, RailRemovedSpanSnapshot>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
