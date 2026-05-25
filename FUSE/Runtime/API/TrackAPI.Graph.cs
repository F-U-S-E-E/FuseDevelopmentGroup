using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Model.Ops;
using FUSE.Runtime.Cache;
using FUSE.Authoring.Data;
using FUSE.Authoring.Data.Common;
using FUSE.Runtime.Events;
using FUSE.Infrastructure;
using Track;
using Track.Signals;
using UnityEngine;

namespace FUSE.Runtime.API
{
    public static partial class TrackAPI
    {

        public static void SetGroupEnabled(string groupId, bool enabled)
        {
            RequireGraph().SetGroupEnabled(groupId, enabled);
            RequestRebuild();
        }

        /// <summary>
        /// Invalidate the <c>BezierCurve</c> cache on every TrackSegment in
        /// the graph so that the next time <see cref="TrackSegment.Curve"/>
        /// is accessed it rebuilds the bezier against the CURRENT node
        /// transforms.
        ///
        /// Necessary because <c>TrackSegment._curve</c> is populated lazily
        /// from <c>node.transform.localPosition</c> / <c>localRotation</c>
        /// on first access and then held forever — there's no automatic
        /// invalidation when the underlying node transform is mutated.
        /// Vanilla relies on <c>Graph.OnNodeDidChange</c> firing through
        /// <see cref="Track.Graph.InvalidateNode"/> to invalidate the
        /// connected segments' curves, but FUSE's legacy-data pipeline can
        /// land in a state where:
        ///   * The segment is registered (via <see cref="AddSegment"/>)
        ///     against node positions/rotations from the first source
        ///     file that mentioned them, baking those into the curve.
        ///   * A later mixinto file (e.g. Foxy's KaterRepair-migration
        ///     altering the rotation of a Bryson Tweaks switch node)
        ///     moves the node — but if FUSE applies the migration as a
        ///     raw transform write rather than through <see cref="UpdateNode"/>,
        ///     <c>OnNodeDidChange</c> doesn't fire and the segment keeps
        ///     its stale curve.
        ///   * <see cref="Track.Graph.RebuildCollections"/> re-adds every
        ///     segment with <c>invalidateNodes:false</c>, so a manual
        ///     <see cref="RebuildGraph"/> can't recover either.
        ///   * <see cref="Track.TrackObjectManager"/>'s
        ///     <c>SwitchGeometry.Calculate</c> then throws
        ///     "Switch tracks do not intersect" because the two diverging
        ///     rails it computes from the stale curves don't intersect
        ///     within the 1.5 m tolerance, and the switch + every
        ///     connected segment is silently dropped from the mesh build.
        ///     End result: visible rails are missing where the data says
        ///     they should be.
        ///
        /// This method is the wholesale rescue: clear every segment's
        /// cached curve. The next rebuild then computes fresh curves
        /// from current node transforms, the switch geometry calc gets
        /// real intersection points, and meshes are built. Returns the
        /// count of segments touched so callers can include it in a
        /// diagnostic.
        /// </summary>
        public static int InvalidateAllCurves(string reason)
        {
            var graph = Graph.Shared;
            if (graph == null)
            {
                return 0;
            }

            var invalidated = 0;
            foreach (var segment in graph.Segments)
            {
                if (segment == null)
                {
                    continue;
                }

                try
                {
                    segment.InvalidateCurve();
                    invalidated++;
                }
                catch (Exception ex)
                {
                    FuseLog.Warning(
                        $"FUSE could not invalidate curve for segment '{segment.id ?? segment.name ?? "<unknown>"}' " +
                        $"reason='{reason ?? "unspecified"}': {ex.Message}");
                }
            }

            if (invalidated > 0)
            {
                FuseLog.Info(
                    $"FUSE invalidated bezier curves on {invalidated} track segment(s) reason='{reason ?? "unspecified"}'. " +
                    "The next graph rebuild will recompute curves against current node transforms; " +
                    "this clears the stale-curve state that makes SwitchGeometry.Calculate throw on switches " +
                    "whose nodes were repositioned by a later mixinto migration.");
            }

            return invalidated;
        }

        public static int DisableInvalidTrackMarkers(string reason)
        {
            var disabled = 0;
            var markers = UnityEngine.Object.FindObjectsOfType<TrackMarker>(true);
            for (var index = 0; index < markers.Length; index++)
            {
                var marker = markers[index];
                if (marker == null || !marker.enabled)
                {
                    continue;
                }

                if (marker.type == TrackMarkerType.PassengerStop &&
                    marker.GetComponentInParent<FusePassengerStopComponent>() != null)
                {
                    continue;
                }

                Location? location;
                try
                {
                    location = marker.Location;
                }
                catch
                {
                    location = null;
                }

                if (location != null && location.Value.IsValid)
                {
                    continue;
                }

                marker.enabled = false;

                // Don't deactivate the GameObject — that kills every other
                // component on it. TrackMarker is frequently attached to
                // scenery (water columns, diesel fueling stands, coaling
                // towers) whose KeyValueBoolAnimator pipeline relies on the
                // GameObject staying active in the hierarchy. The original
                // implementation called SetActive(false) here as a "tidy up"
                // step, but it had the side effect of breaking every loader
                // animation whose track marker happened to be invalid after
                // a mod's track edits — e.g. 'Water Column Bryson West
                // Siding', 'East Whittier Diesel Fueling Stand'. The
                // KeyValueBoolAnimator inside the same GameObject can't
                // observe its parent KVO if the GameObject is inactive in
                // hierarchy, so the loader never rotates / opens.
                //
                // If a mod genuinely wants to hide a piece of scenery the
                // FuseWorldSuppressor pipeline handles that explicitly
                // (e.g. ALWRoundhouseReplacement suppressing the Bryson
                // turntable roundhouse, Katers.BrysonRepairTrack
                // suppressing the Bryson Water Tower). Marker invalidity
                // alone is not a suppression signal.

                disabled++;
                FuseLog.Info(
                    $"FUSE disabled invalid TrackMarker operation='track marker cleanup' " +
                    $"id='{marker.id ?? string.Empty}' name='{marker.name ?? string.Empty}' " +
                    $"reason='{reason ?? "unspecified"}' (marker behaviour disabled; GameObject kept active so attached scenery animators still work).");
            }

            if (disabled > 0)
            {
                FuseLog.Info($"FUSE disabled {disabled} invalid TrackMarker component(s) reason='{reason ?? "unspecified"}'.");
            }

            return disabled;
        }

        public static int RemoveInvalidTrackSpans(string reason)
        {
            var removed = 0;
            var spans = GetAllSpans().ToArray();
            if (spans.Length == 0)
            {
                return 0;
            }

            BeginBatch();
            try
            {
                for (var index = 0; index < spans.Length; index++)
                {
                    var span = spans[index];
                    if (span == null)
                    {
                        continue;
                    }

                    string detail;
                    if (IsTrackSpanRuntimeUsable(span, out detail))
                    {
                        continue;
                    }

                    RemoveTrackSpanRuntimeObject(span, "track span cleanup", reason, detail);
                    removed++;
                }
            }
            finally
            {
                EndBatch(false);
            }

            if (removed > 0)
            {
                FuseLog.Warning($"FUSE removed {removed} invalid TrackSpan object(s) reason='{reason ?? "unspecified"}'.");
                RequestRebuild();
            }

            return removed;
        }

        public static int ScrubCtcSignalReferences(string reason)
        {
            var scrubbed = 0;
            scrubbed += ScrubAutoSignalBlockReferences(reason);
            scrubbed += ScrubPredicateSignalReferences(reason);

            if (scrubbed > 0)
            {
                FuseLog.Warning($"FUSE scrubbed {scrubbed} stale CTC signal reference(s) reason='{reason ?? "unspecified"}'.");
            }

            return scrubbed;
        }

        private static T CreateGraphChild<T>(Graph graph, string name)
            where T : Component
        {
            var gameObject = new GameObject(name);
            gameObject.transform.SetParent(graph.transform, false);
            return gameObject.AddComponent<T>();
        }

        private static Location MakeLocation(Graph graph, FuseTrackLocation definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            var segment = graph.GetSegment(definition.SegmentId);
            if (segment == null)
            {
                throw new InvalidOperationException($"Track segment '{definition.SegmentId}' was not found.");
            }

            var segmentLength = segment.GetLength();
            var distance = definition.Distance ?? ((definition.Normalized ?? 0f) * segmentLength);
            distance += definition.Offset;
            if (distance < -SpanDistanceTolerance || distance > segmentLength + SpanDistanceTolerance)
            {
                throw new InvalidOperationException(
                    $"Track location on segment '{definition.SegmentId}' is outside the segment length. distance={distance:0.###}, segmentLength={segmentLength:0.###}, end='{definition.End ?? "A"}'.");
            }

            return new Location(
                segment,
                Mathf.Clamp(distance, 0f, segmentLength),
                ParseLocationEnd(definition.End));
        }

        private static void ValidateSpanEndpointPair(string spanId, ref Location upper, ref Location lower)
        {
            if (upper.segment == null || lower.segment == null)
            {
                throw new InvalidOperationException($"Track span '{spanId}' has a null endpoint segment.");
            }

            if (upper.segment != lower.segment)
            {
                return;
            }

            // Resolve both endpoints to a common reference (distance-from-A on
            // this segment) so we can detect and repair crossed/same-direction
            // patterns in one place. Legacy AlinasMapMod tolerated both
            // categories; FUSE used to throw, killing the whole apply phase.
            var length = upper.segment.GetLength();
            var upperFromA = DistanceFromSegmentA(upper);
            var lowerFromA = DistanceFromSegmentA(lower);
            var minPos = Mathf.Min(upperFromA, lowerFromA);
            var maxPos = Mathf.Max(upperFromA, lowerFromA);
            var spanLength = maxPos - minPos;

            if (spanLength <= SpanDistanceTolerance)
            {
                throw new InvalidOperationException(
                    $"Track span '{spanId}' has zero length on segment '{upper.segment.id}'. " +
                    $"upper=({DescribeLocation(upper)}) lower=({DescribeLocation(lower)}).");
            }

            var sameDirection = upper.EndIsA == lower.EndIsA;
            var startSide = upper.EndIsA ? upperFromA : lowerFromA;
            var endSide = upper.EndIsA ? lowerFromA : upperFromA;
            var crossed = !sameDirection && startSide > endSide + SpanDistanceTolerance;

            if (!sameDirection && !crossed)
            {
                return;
            }

            // Re-anchor: A side takes the smaller physical position, B side
            // takes the larger. Preserves both endpoints' physical positions
            // exactly while making the validator's startSide<=endSide invariant
            // hold by construction. Upper/lower roles are reassigned so the
            // larger-position endpoint is "upper" (matches NormalizeUpperLower's
            // expectation downstream).
            var newUpper = new Location(upper.segment, length - maxPos, TrackSegment.End.B);
            var newLower = new Location(upper.segment, minPos, TrackSegment.End.A);
            upper = newUpper;
            lower = newLower;

            var reason = crossed ? "crossed endpoints" : "same-direction endpoints";
            FuseLog.Info(
                $"FUSE auto-repaired track span '{spanId}' on segment '{upper.segment.id}': " +
                $"normalized {reason} to A({minPos:0.###}) <-> B({(length - maxPos):0.###}). " +
                "Legacy AlinasMapMod tolerated this pattern; FUSE re-anchored to keep the package loadable.");
        }

        private static float DistanceFromSegmentA(Location location)
        {
            var length = location.segment.GetLength();
            return location.EndIsA ? location.distance : length - location.distance;
        }

        private static void ValidateSpanRoute(string spanId, TrackSpan span)
        {
            if (span == null || !span.IsValid)
            {
                throw new InvalidOperationException($"Track span '{spanId}' has invalid endpoints.");
            }

            var points = span.GetPoints();
            if (points == null || points.Count < 2 || span.Length <= SpanDistanceTolerance)
            {
                var upper = span.upper.HasValue ? DescribeLocation(span.upper.Value) : "missing";
                var lower = span.lower.HasValue ? DescribeLocation(span.lower.Value) : "missing";
                throw new InvalidOperationException(
                    $"Track span '{spanId}' did not resolve to a valid route. " +
                    $"upper=({upper}) lower=({lower}). Check that upper/lower arrows face each other, " +
                    "distances are inside segment length, and endpoint segments are connected.");
            }
        }

        private static string DescribeLocation(Location location)
        {
            var segment = location.segment;
            if (segment == null)
            {
                return "segment='<null>'";
            }

            var end = location.EndIsA ? "A/Start" : "B/End";
            return $"segment='{segment.id}' end='{end}' distance={location.distance:0.###} segmentLength={segment.GetLength():0.###}";
        }

        private static void RemoveSpansReferencingSegment(TrackSegment segment, string reason)
        {
            if (segment == null)
            {
                return;
            }

            var removed = 0;
            foreach (var span in GetAllSpans().Where(candidate => SpanReferencesSegment(candidate, segment)).ToArray())
            {
                if (span == null)
                {
                    continue;
                }

                RemoveTrackSpanRuntimeObject(
                    span,
                    "track segment dependency cleanup",
                    reason,
                    $"endpoint referenced removed segment '{segment.id ?? string.Empty}'");
                removed++;
            }

            if (removed > 0)
            {
                FuseLog.Warning(
                    $"FUSE removed {removed} TrackSpan object(s) that referenced removed segment '{segment.id ?? string.Empty}' " +
                    $"reason='{reason ?? "unspecified"}'.");
            }
        }

        private static int DisableTrackMarkersReferencingSegment(TrackSegment segment, string reason)
        {
            if (segment == null)
            {
                return 0;
            }

            var disabled = 0;
            foreach (var marker in UnityEngine.Object.FindObjectsOfType<TrackMarker>(true))
            {
                if (marker == null || !MarkerReferencesSegment(marker, segment))
                {
                    continue;
                }

                marker.enabled = false;
                if (marker.gameObject != null && marker.gameObject.activeSelf)
                {
                    marker.gameObject.SetActive(false);
                }

                disabled++;
                FuseLog.Info(
                    $"FUSE disabled TrackMarker operation='track segment dependency cleanup' " +
                    $"id='{marker.id ?? string.Empty}' name='{marker.name ?? string.Empty}' " +
                    $"segment='{segment.id ?? string.Empty}' reason='{reason ?? "unspecified"}'.");
            }

            if (disabled > 0)
            {
                FuseLog.Warning(
                    $"FUSE disabled {disabled} TrackMarker object(s) that referenced removed segment '{segment.id ?? string.Empty}' " +
                    $"reason='{reason ?? "unspecified"}'.");
            }

            return disabled;
        }

        private static void RemoveTrackSpanRuntimeObject(TrackSpan span, string operation, string reason, string detail)
        {
            if (span == null)
            {
                return;
            }

            var id = span.id ?? string.Empty;
            var displayName = span.name ?? string.Empty;
            UnregisterSpanWithGraph(Graph.Shared, id);
            RemoveRuntimeObject(span);
            if (!string.IsNullOrWhiteSpace(id))
            {
                FuseSpanRuntimeIndex.Instance.Remove(id);
                FuseRuntimeDefinitionCache.Remove(FuseDefinitionKind.TrackSpan, id);
                FuseEvents.RaiseSpanRemoved(id);
            }

            RequestRebuild();
            FuseLog.Warning(
                $"FUSE removed TrackSpan operation='{operation ?? "track span cleanup"}' " +
                $"id='{id}' name='{displayName}' reason='{reason ?? "unspecified"}' detail='{detail ?? string.Empty}'.");
        }

        private static bool IsTrackSpanRuntimeUsable(TrackSpan span, out string detail)
        {
            detail = string.Empty;
            if (span == null)
            {
                detail = "span is null";
                return true;
            }

            Location? upper;
            Location? lower;
            try
            {
                upper = span.upper;
                lower = span.lower;
            }
            catch (Exception ex)
            {
                detail = "endpoint location could not be read: " + ex.Message;
                return false;
            }

            if (!upper.HasValue || !lower.HasValue)
            {
                detail = "upper/lower endpoint location is missing";
                return false;
            }

            var upperSegment = upper.Value.segment;
            var lowerSegment = lower.Value.segment;
            if (upperSegment == null || lowerSegment == null)
            {
                detail =
                    $"upper/lower endpoint segment is missing upper='{DescribeSegmentId(upperSegment)}' lower='{DescribeSegmentId(lowerSegment)}'";
                return false;
            }

            if (IsSegmentHiddenByDisabledGroup(upperSegment) || IsSegmentHiddenByDisabledGroup(lowerSegment))
            {
                return true;
            }

            try
            {
                if (!span.IsValid)
                {
                    detail = "span.IsValid returned false";
                    return false;
                }

                var points = span.GetPoints();
                if (points == null || points.Count < 2 || span.Length <= SpanDistanceTolerance)
                {
                    detail =
                        $"span route did not resolve to points upper='{DescribeLocation(upper.Value)}' lower='{DescribeLocation(lower.Value)}'";
                    return false;
                }
            }
            catch (Exception ex)
            {
                detail = "span route validation threw: " + ex.Message;
                return false;
            }

            return true;
        }

        private static bool SpanReferencesSegment(TrackSpan span, TrackSegment segment)
        {
            if (span == null || segment == null)
            {
                return false;
            }

            try
            {
                return LocationReferencesSegment(span.upper, segment) ||
                       LocationReferencesSegment(span.lower, segment);
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    $"FUSE could not inspect span '{span.id ?? string.Empty}' during track dependency cleanup: {ex.Message}");
                return false;
            }
        }

        private static bool MarkerReferencesSegment(TrackMarker marker, TrackSegment segment)
        {
            if (marker == null || segment == null)
            {
                return false;
            }

            try
            {
                return LocationReferencesSegment(marker.Location, segment);
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    $"FUSE could not inspect marker '{marker.id ?? string.Empty}' during track dependency cleanup: {ex.Message}");
                return false;
            }
        }

        private static bool LocationReferencesSegment(Location? location, TrackSegment segment)
        {
            if (!location.HasValue || segment == null)
            {
                return false;
            }

            var locationSegment = location.Value.segment;
            if (locationSegment == null)
            {
                return false;
            }

            if (locationSegment == segment)
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(locationSegment.id) &&
                   !string.IsNullOrWhiteSpace(segment.id) &&
                   string.Equals(locationSegment.id, segment.id, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSegmentHiddenByDisabledGroup(TrackSegment segment)
        {
            if (segment == null || string.IsNullOrWhiteSpace(segment.groupId))
            {
                return false;
            }

            var graph = Graph.Shared;
            return graph == null ||
                   graph.enabledGroupIds == null ||
                   !graph.enabledGroupIds.Contains(segment.groupId);
        }

        private static string DescribeSegmentId(TrackSegment segment)
        {
            return segment != null ? segment.id ?? string.Empty : "<null>";
        }

        private static int ScrubAutoSignalBlockReferences(string reason)
        {
            var scrubbed = 0;
            foreach (var signal in UnityEngine.Object.FindObjectsOfType<CTCAutoSignal>(true))
            {
                if (signal == null || signal.blocks == null || signal.blocks.Count == 0)
                {
                    continue;
                }

                var removed = signal.blocks.RemoveAll(block => !IsLiveUnityComponent(block));
                if (removed <= 0)
                {
                    continue;
                }

                scrubbed += removed;
                FuseLog.Warning(
                    $"FUSE scrubbed stale CTC auto-signal block reference(s) signal='{SafeComponentName(signal)}' " +
                    $"removed={removed} reason='{reason ?? "unspecified"}'.");
            }

            return scrubbed;
        }

        private static int ScrubPredicateSignalReferences(string reason)
        {
            var scrubbed = 0;
            foreach (var signal in UnityEngine.Object.FindObjectsOfType<CTCPredicateSignal>(true))
            {
                if (signal == null || signal.heads == null || signal.heads.Count == 0)
                {
                    continue;
                }

                foreach (var head in signal.heads)
                {
                    if (head == null)
                    {
                        continue;
                    }

                    if (head.nextSignal != null && !IsLiveUnityComponent(head.nextSignal))
                    {
                        head.nextSignal = null;
                        scrubbed++;
                    }

                    if (head.predicates == null)
                    {
                        continue;
                    }

                    foreach (var predicate in head.predicates)
                    {
                        if (predicate?.blocks == null || predicate.blocks.Count == 0)
                        {
                            continue;
                        }

                        scrubbed += predicate.blocks.RemoveAll(block => !IsLiveUnityComponent(block));
                    }
                }
            }

            if (scrubbed > 0)
            {
                FuseLog.Warning(
                    $"FUSE scrubbed stale CTC predicate-signal reference(s) removed={scrubbed} " +
                    $"reason='{reason ?? "unspecified"}'.");
            }

            return scrubbed;
        }

        private static bool IsLiveUnityComponent(Component component)
        {
            if (component == null)
            {
                return false;
            }

            try
            {
                return component.gameObject != null;
            }
            catch
            {
                return false;
            }
        }

        private static string SafeComponentName(Component component)
        {
            if (component == null)
            {
                return string.Empty;
            }

            try
            {
                return component.name ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static void RegisterSpanWithGraph(Graph graph, TrackSpan span)
        {
            if (graph == null || span == null || string.IsNullOrWhiteSpace(span.id))
            {
                return;
            }

            try
            {
                var registered = graph.SpanForId(span.id);
                if (registered == span)
                {
                    return;
                }

                var spans = GraphSpansField?.GetValue(graph) as Dictionary<string, TrackSpan>;
                if (spans != null)
                {
                    spans[span.id] = span;
                    return;
                }

                if (GraphAddSpanMethod == null)
                {
                    if (!_warnedMissingGraphAddSpan)
                    {
                        _warnedMissingGraphAddSpan = true;
                        FuseLog.Warning(
                            "FUSE could not register newly applied track spans with the Railroader graph cache: " +
                            "private Graph.AddSpan method was not found. Spans remain in FUSE runtime index until the next graph rebuild.");
                    }

                    return;
                }

                GraphAddSpanMethod.Invoke(graph, new object[] { span });
            }
            catch (TargetInvocationException ex)
            {
                FuseLog.Warning($"FUSE could not register track span '{span.id}' with Graph cache: {ex.InnerException?.Message ?? ex.Message}");
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE could not register track span '{span.id}' with Graph cache", ex);
            }
        }

        private static void UnregisterNodeWithGraph(Graph graph, string id)
        {
            if (graph == null || string.IsNullOrWhiteSpace(id))
            {
                return;
            }

            try
            {
                (GraphNodesField?.GetValue(graph) as Dictionary<string, TrackNode>)?.Remove(id);
                ClearGraphDerivedCaches(graph);
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE could not unregister track node '{id}' from Graph cache", ex);
            }
        }

        private static void UnregisterSegmentWithGraph(Graph graph, string id)
        {
            if (graph == null || string.IsNullOrWhiteSpace(id))
            {
                return;
            }

            try
            {
                (GraphSegmentsField?.GetValue(graph) as Dictionary<string, TrackSegment>)?.Remove(id);
                ClearGraphDerivedCaches(graph);
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE could not unregister track segment '{id}' from Graph cache", ex);
            }
        }

        private static void UnregisterSpanWithGraph(Graph graph, string id)
        {
            if (graph == null || string.IsNullOrWhiteSpace(id))
            {
                return;
            }

            try
            {
                (GraphSpansField?.GetValue(graph) as Dictionary<string, TrackSpan>)?.Remove(id);
                ClearGraphDerivedCaches(graph);
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE could not unregister track span '{id}' from Graph cache", ex);
            }
        }

        private static void ClearGraphDerivedCaches(Graph graph)
        {
            if (graph == null)
            {
                return;
            }

            foreach (var field in GraphDerivedCacheFields)
            {
                if (field == null)
                {
                    continue;
                }

                var value = field.GetValue(graph);
                if (value == null)
                {
                    continue;
                }

                if (value is IDictionary dictionary)
                {
                    dictionary.Clear();
                    continue;
                }

                var clear = value.GetType().GetMethod("Clear", Type.EmptyTypes);
                clear?.Invoke(value, null);
            }
        }

        private static TrackSpan FindSpanInScene(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            return UnityEngine.Object.FindObjectsOfType<TrackSpan>(true)
                .FirstOrDefault(span => span != null && string.Equals(span.id, id, StringComparison.OrdinalIgnoreCase));
        }

        private static void EnsureTrackSpanGraphChild(Graph graph, TrackSpan span)
        {
            if (graph == null || span == null || span.transform == null || graph.transform == null)
            {
                return;
            }

            try
            {
                if (span.transform.parent != graph.transform)
                {
                    span.transform.SetParent(graph.transform, true);
                }
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE could not move track span '{span.id}' under Graph root", ex);
            }
        }

        private static Graph RequireGraph()
        {
            var graph = Graph.Shared;
            if (graph == null)
            {
                throw new InvalidOperationException("Railroader Graph.Shared is not available.");
            }

            return graph;
        }
    }
}
