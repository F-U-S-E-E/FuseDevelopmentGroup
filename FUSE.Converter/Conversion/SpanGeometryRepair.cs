using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FUSE.Converter.Models;
using Newtonsoft.Json.Linq;

namespace FUSE.Converter.Conversion
{
    /// <summary>
    /// Geometry-repair pass for converted span definitions. Runs after
    /// every fragment in a legacy mod has had its tracks/operations/
    /// world sections translated, so this pass can see the FULL
    /// converted track graph (nodes + segments) and use it to clamp
    /// out-of-range endpoints, swap crossed same-segment endpoints,
    /// and re-align A/B anchors on multi-segment spans by BFS-walking
    /// the converted segment graph.
    /// </summary>
    /// <remarks>
    /// This is a port of the Python <c>repair_package_spans</c> + its
    /// six helpers (<c>_estimate_segment_lengths</c>,
    /// <c>_collect_converted_track_graph</c>,
    /// <c>_clamp_span_endpoint</c>,
    /// <c>_repair_same_segment_span</c>,
    /// <c>_find_segment_path</c>,
    /// <c>_repair_multi_segment_span</c>).
    ///
    /// All mutation is in-place on the supplied fragment documents so
    /// the caller's later "write fragment to disk" loop picks up the
    /// repaired geometry without an extra hand-off.
    /// </remarks>
    internal static class SpanGeometryRepair
    {
        /// <summary>
        /// A single fragment as seen by the repair pass: the source
        /// JSON file name (for attribution in the report) plus the
        /// converted FUSE document. Same shape as the tuple
        /// <see cref="FuseLegacyConverter"/> already keeps, just lifted
        /// into a named record so the pass doesn't depend on its
        /// caller's local tuple shape.
        /// </summary>
        internal readonly struct ConvertedFragment
        {
            public ConvertedFragment(string sourceName, JObject document)
            {
                SourceName = sourceName;
                Document = document;
            }

            public string SourceName { get; }
            public JObject Document { get; }
        }

        /// <summary>
        /// Top-level entry — call once after every fragment has been
        /// converted, before any file gets written.
        /// </summary>
        public static void RepairPackageSpans(IList<ConvertedFragment> fragments, List<FuseConversionReportEntry> report)
        {
            if (fragments == null || fragments.Count == 0)
            {
                return;
            }

            var graph = CollectConvertedTrackGraph(fragments);
            if (graph.Segments.Count == 0)
            {
                return;
            }

            foreach (var fragment in fragments)
            {
                var spans = (fragment.Document?["tracks"] as JObject)?["spans"] as JObject;
                if (spans == null)
                {
                    continue;
                }

                foreach (var property in spans.Properties())
                {
                    var span = property.Value as JObject;
                    if (span == null) continue;

                    var upper = span["upper"] as JObject;
                    var lower = span["lower"] as JObject;
                    var segmentId = upper?.Value<string>("segmentId");
                    if (string.IsNullOrEmpty(segmentId) || lower == null)
                    {
                        continue;
                    }

                    if (segmentId == lower.Value<string>("segmentId"))
                    {
                        double? length = graph.Lengths.TryGetValue(segmentId, out var l) ? l : (double?)null;
                        RepairSameSegmentSpan(property.Name, span, length, fragment.SourceName, report);
                    }
                    else
                    {
                        RepairMultiSegmentSpan(property.Name, span, graph, fragment.SourceName, report);
                    }
                }
            }
        }

        // ------------------------------------------------------------------
        // Graph collection
        // ------------------------------------------------------------------

        /// <summary>
        /// Per-segment metadata the repair pass needs: the segment id
        /// table (to look up startNodeId / endNodeId), the
        /// node-position table (to estimate segment length via vector
        /// distance), the segment length table (computed once,
        /// indexed by segment id), and the segment-neighbor graph
        /// (segments that share a node — used by BFS to walk between
        /// the lower and upper endpoint segments).
        /// </summary>
        internal sealed class TrackGraph
        {
            public Dictionary<string, JObject> Nodes { get; } = new Dictionary<string, JObject>(StringComparer.Ordinal);
            public Dictionary<string, JObject> Segments { get; } = new Dictionary<string, JObject>(StringComparer.Ordinal);
            public Dictionary<string, double> Lengths { get; } = new Dictionary<string, double>(StringComparer.Ordinal);
            public Dictionary<string, HashSet<string>> Neighbors { get; } = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        }

        internal static TrackGraph CollectConvertedTrackGraph(IList<ConvertedFragment> fragments)
        {
            var graph = new TrackGraph();

            // Pass 1 — gather every node + segment into shared
            // dictionaries so we can resolve cross-fragment references
            // (the same legacy package frequently splits its track
            // graph across multiple source files).
            foreach (var fragment in fragments)
            {
                var tracks = fragment.Document?["tracks"] as JObject;
                if (tracks == null) continue;

                var nodes = tracks["nodes"] as JObject;
                if (nodes != null)
                {
                    foreach (var prop in nodes.Properties())
                    {
                        if (prop.Value is JObject nodeObj)
                        {
                            graph.Nodes[prop.Name] = nodeObj;
                        }
                    }
                }

                var segments = tracks["segments"] as JObject;
                if (segments != null)
                {
                    foreach (var prop in segments.Properties())
                    {
                        if (prop.Value is JObject segObj)
                        {
                            graph.Segments[prop.Name] = segObj;
                        }
                    }
                }
            }

            // Pass 2 — for each segment, look up its endpoints' node
            // positions, compute the vector distance between them,
            // stash that as the segment length, and build the
            // node→segments map so we can derive neighbors.
            var nodeToSegments = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var pair in graph.Segments)
            {
                var segmentId = pair.Key;
                var segment = pair.Value;
                var startNode = segment.Value<string>("startNodeId");
                var endNode = segment.Value<string>("endNodeId");

                foreach (var nodeId in new[] { startNode, endNode })
                {
                    if (!string.IsNullOrEmpty(nodeId))
                    {
                        if (!nodeToSegments.TryGetValue(nodeId, out var bucket))
                        {
                            bucket = new HashSet<string>(StringComparer.Ordinal);
                            nodeToSegments[nodeId] = bucket;
                        }
                        bucket.Add(segmentId);
                    }
                }

                graph.Nodes.TryGetValue(startNode ?? string.Empty, out var startNodeObj);
                graph.Nodes.TryGetValue(endNode ?? string.Empty, out var endNodeObj);
                var length = VectorDistance(startNodeObj?["position"], endNodeObj?["position"]);
                if (length.HasValue && length.Value > 0)
                {
                    graph.Lengths[segmentId] = length.Value;
                }

                graph.Neighbors[segmentId] = new HashSet<string>(StringComparer.Ordinal);
            }

            // Pass 3 — two segments are neighbors iff they share a
            // node. Pythonic O(N^2)-per-node, matching the Python
            // reference; node degree is small in practice (≤ 4 for a
            // standard switch).
            foreach (var bucket in nodeToSegments.Values)
            {
                var connected = bucket.ToList();
                for (int i = 0; i < connected.Count; i++)
                {
                    for (int j = i + 1; j < connected.Count; j++)
                    {
                        graph.Neighbors[connected[i]].Add(connected[j]);
                        graph.Neighbors[connected[j]].Add(connected[i]);
                    }
                }
            }

            return graph;
        }

        // ------------------------------------------------------------------
        // Numeric helpers
        // ------------------------------------------------------------------

        internal static double? VectorDistance(JToken a, JToken b)
        {
            if (!(a is JObject ao) || !(b is JObject bo))
            {
                return null;
            }
            var dx = (double)(ao.Value<double?>("x") ?? 0) - (double)(bo.Value<double?>("x") ?? 0);
            var dy = (double)(ao.Value<double?>("y") ?? 0) - (double)(bo.Value<double?>("y") ?? 0);
            var dz = (double)(ao.Value<double?>("z") ?? 0) - (double)(bo.Value<double?>("z") ?? 0);
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        /// <summary>
        /// Computes the signed scalar distance along a segment for a
        /// location, applying the optional normalized→absolute
        /// conversion and the <c>offset</c> additive shift. Mirrors
        /// the Python <c>_location_distance</c>.
        /// </summary>
        internal static double? LocationDistance(JObject location, double? segmentLength)
        {
            if (location == null || !segmentLength.HasValue)
            {
                return null;
            }

            double distance;
            var normalized = location.Value<double?>("normalized");
            if (normalized.HasValue)
            {
                distance = normalized.Value * segmentLength.Value;
            }
            else
            {
                distance = location.Value<double?>("distance") ?? 0.0;
            }

            distance += location.Value<double?>("offset") ?? 0.0;
            return distance;
        }

        /// <summary>
        /// Writes a freshly-clamped distance into a location object,
        /// stripping the normalized + offset fields so the saved
        /// representation is the canonical absolute form.
        /// </summary>
        internal static void SetLocationDistance(JObject location, double distance)
        {
            if (location == null) return;
            var clamped = Math.Max(0.0, distance);
            location["distance"] = Math.Round(clamped, 6);
            location.Remove("normalized");
            location.Remove("offset");
        }

        internal static double? DistanceFromSegmentA(JObject location, double? segmentLength)
        {
            var distance = LocationDistance(location, segmentLength);
            if (distance == null) return null;
            var end = location?.Value<string>("end");
            if (end == "A") return distance;
            if (end == "B") return segmentLength.Value - distance.Value;
            return null;
        }

        internal static bool SameSegmentSpanIsValid(JObject upper, JObject lower, double? segmentLength)
        {
            if (upper == null || lower == null) return false;
            if (upper.Value<string>("end") == lower.Value<string>("end"))
            {
                return false;
            }
            var upperFromA = DistanceFromSegmentA(upper, segmentLength);
            var lowerFromA = DistanceFromSegmentA(lower, segmentLength);
            if (upperFromA == null || lowerFromA == null)
            {
                return true;
            }

            double startSide, endSide;
            if (upper.Value<string>("end") == "A")
            {
                startSide = upperFromA.Value;
                endSide = lowerFromA.Value;
            }
            else
            {
                startSide = lowerFromA.Value;
                endSide = upperFromA.Value;
            }
            return startSide < endSide;
        }

        // ------------------------------------------------------------------
        // Same-segment repair
        // ------------------------------------------------------------------

        internal static bool ClampSpanEndpoint(string spanId, string endpointName, JObject location,
                                                double segmentLength, string sourceName,
                                                List<FuseConversionReportEntry> report)
        {
            var distance = LocationDistance(location, segmentLength);
            if (distance == null)
            {
                return false;
            }
            var clamped = Math.Min(Math.Max(distance.Value, 0.0), segmentLength);
            if (Math.Abs(clamped - distance.Value) <= 0.0001)
            {
                return false;
            }

            SetLocationDistance(location, clamped);
            Report(report, FuseConversionReportLevel.Info, sourceName, "span-repaired",
                $"Span '{spanId}' {endpointName} endpoint on segment '{location.Value<string>("segmentId")}' " +
                $"had distance {distance.Value.ToString("F3", CultureInfo.InvariantCulture)} outside estimated segment length " +
                $"{segmentLength.ToString("F3", CultureInfo.InvariantCulture)}; clamped to " +
                $"{clamped.ToString("F3", CultureInfo.InvariantCulture)}.");
            return true;
        }

        internal static bool RepairSameSegmentSpan(string spanId, JObject span, double? segmentLength,
                                                    string sourceName, List<FuseConversionReportEntry> report)
        {
            if (span == null) return false;
            var upper = span["upper"] as JObject;
            var lower = span["lower"] as JObject;
            if (upper == null || lower == null) return false;

            var upperSeg = upper.Value<string>("segmentId");
            var lowerSeg = lower.Value<string>("segmentId");
            if (string.IsNullOrEmpty(upperSeg) || upperSeg != lowerSeg) return false;

            var upperEnd = upper.Value<string>("end");
            var lowerEnd = lower.Value<string>("end");
            if (upperEnd != "A" && upperEnd != "B") return false;
            if (lowerEnd != "A" && lowerEnd != "B") return false;
            if (!segmentLength.HasValue || segmentLength.Value <= 0) return false;

            bool repaired = false;
            repaired |= ClampSpanEndpoint(spanId, "upper", upper, segmentLength.Value, sourceName, report);
            repaired |= ClampSpanEndpoint(spanId, "lower", lower, segmentLength.Value, sourceName, report);

            if (SameSegmentSpanIsValid(upper, lower, segmentLength))
            {
                return repaired;
            }

            if (upperEnd == lowerEnd)
            {
                Report(report, FuseConversionReportLevel.Warning, sourceName, "span-geometry-crossed",
                    $"Span '{spanId}' on segment '{upperSeg}' has both endpoints anchored to {upperEnd}; " +
                    "FUSE needs opposite-facing endpoints and cannot safely infer the other side.");
                return repaired;
            }

            // Crossed across opposite anchors. Try swapping
            // upper/lower (they may simply be transposed) and keep
            // the swap only if it yields a valid arrangement.
            var swappedUpper = (JObject)lower.DeepClone();
            var swappedLower = (JObject)upper.DeepClone();
            if (SameSegmentSpanIsValid(swappedUpper, swappedLower, segmentLength))
            {
                span["upper"] = swappedUpper;
                span["lower"] = swappedLower;
                Report(report, FuseConversionReportLevel.Info, sourceName, "span-repaired",
                    $"Span '{spanId}' on segment '{upperSeg}' had crossed endpoints; swapped upper/lower.");
                return true;
            }

            Report(report, FuseConversionReportLevel.Warning, sourceName, "span-geometry-crossed",
                $"Span '{spanId}' on segment '{upperSeg}' has crossed endpoints for estimated segment length " +
                $"{segmentLength.Value.ToString("F3", CultureInfo.InvariantCulture)}; converter preserved the original " +
                "endpoints because no safe automatic repair was available.");
            return repaired;
        }

        // ------------------------------------------------------------------
        // Multi-segment repair
        // ------------------------------------------------------------------

        internal static string SharedNode(JObject segmentA, JObject segmentB)
        {
            if (segmentA == null || segmentB == null) return null;
            var aStart = segmentA.Value<string>("startNodeId");
            var aEnd = segmentA.Value<string>("endNodeId");
            var bStart = segmentB.Value<string>("startNodeId");
            var bEnd = segmentB.Value<string>("endNodeId");

            var nodesA = new HashSet<string>(StringComparer.Ordinal);
            if (!string.IsNullOrEmpty(aStart)) nodesA.Add(aStart);
            if (!string.IsNullOrEmpty(aEnd)) nodesA.Add(aEnd);

            var shared = new List<string>();
            if (!string.IsNullOrEmpty(bStart) && nodesA.Contains(bStart)) shared.Add(bStart);
            if (!string.IsNullOrEmpty(bEnd) && nodesA.Contains(bEnd)) shared.Add(bEnd);

            // The Python source only returns a unique shared node;
            // ambiguous (loop-shaped) cases drop to "unknown" and
            // surface as a span-route-unresolved warning later.
            return shared.Count == 1 ? shared[0] : null;
        }

        internal static string OppositeEndForNode(JObject segment, string nodeId)
        {
            if (segment == null || string.IsNullOrEmpty(nodeId)) return null;
            if (segment.Value<string>("startNodeId") == nodeId) return "B";
            if (segment.Value<string>("endNodeId") == nodeId) return "A";
            return null;
        }

        internal static bool FlipLocationEndPreservingPosition(JObject location, string desiredEnd, double? segmentLength)
        {
            if (location == null || (desiredEnd != "A" && desiredEnd != "B")) return false;
            var currentEnd = location.Value<string>("end");
            if (currentEnd == desiredEnd) return false;
            var distance = LocationDistance(location, segmentLength);
            if (distance == null || !segmentLength.HasValue) return false;
            location["end"] = desiredEnd;
            SetLocationDistance(location, segmentLength.Value - distance.Value);
            return true;
        }

        /// <summary>
        /// BFS-finds a chain of segments connecting two segment ids.
        /// Returned path includes both endpoints. Returns null if the
        /// segments aren't in the graph or aren't connected.
        /// </summary>
        internal static List<string> FindSegmentPath(string startSegmentId, string endSegmentId,
                                                     IReadOnlyDictionary<string, HashSet<string>> neighbors)
        {
            if (startSegmentId == endSegmentId)
            {
                return new List<string> { startSegmentId };
            }
            if (!neighbors.ContainsKey(startSegmentId) || !neighbors.ContainsKey(endSegmentId))
            {
                return null;
            }

            var queue = new Queue<(string Current, List<string> Path)>();
            queue.Enqueue((startSegmentId, new List<string> { startSegmentId }));
            var visited = new HashSet<string>(StringComparer.Ordinal) { startSegmentId };

            while (queue.Count > 0)
            {
                var (current, path) = queue.Dequeue();
                // Sort neighbors for deterministic search order — the
                // Python reference does the same so tests can compare
                // paths against a known shape.
                foreach (var neighbor in neighbors[current].OrderBy(n => n, StringComparer.Ordinal))
                {
                    if (visited.Contains(neighbor))
                    {
                        continue;
                    }
                    var nextPath = new List<string>(path) { neighbor };
                    if (neighbor == endSegmentId)
                    {
                        return nextPath;
                    }
                    visited.Add(neighbor);
                    queue.Enqueue((neighbor, nextPath));
                }
            }

            return null;
        }

        internal static bool RepairMultiSegmentSpan(string spanId, JObject span, TrackGraph graph,
                                                    string sourceName, List<FuseConversionReportEntry> report)
        {
            if (span == null) return false;
            var upper = span["upper"] as JObject;
            var lower = span["lower"] as JObject;
            if (upper == null || lower == null) return false;

            var upperSegmentId = upper.Value<string>("segmentId");
            var lowerSegmentId = lower.Value<string>("segmentId");
            if (string.IsNullOrEmpty(upperSegmentId) || string.IsNullOrEmpty(lowerSegmentId)) return false;
            if (upperSegmentId == lowerSegmentId) return false;

            // If either endpoint references a segment that isn't in
            // the converted graph at all, treat it as an external
            // (base-game / cross-mod) dependency and flag it.
            var missing = new List<string>();
            if (!graph.Segments.ContainsKey(upperSegmentId)) missing.Add(upperSegmentId);
            if (!graph.Segments.ContainsKey(lowerSegmentId)) missing.Add(lowerSegmentId);
            if (missing.Count > 0)
            {
                Report(report, FuseConversionReportLevel.Warning, sourceName, "span-external-segment",
                    $"Span '{spanId}' references segment(s) not defined in converted source files: " +
                    $"{string.Join(", ", missing)}. Treating them as external/base-game dependencies.");
                return false;
            }

            var path = FindSegmentPath(lowerSegmentId, upperSegmentId, graph.Neighbors);
            if (path == null || path.Count < 2)
            {
                Report(report, FuseConversionReportLevel.Warning, sourceName, "span-route-unresolved",
                    $"Span '{spanId}' endpoints '{lowerSegmentId}' -> '{upperSegmentId}' are both converted " +
                    "but no connected segment path was found between them. Preserved original anchors.");
                return false;
            }

            // The "lower" end of the span sits on the FIRST segment
            // of the path. Its desired A/B end is the one OPPOSITE
            // the shared node with the second segment — that's the
            // outward-facing anchor that points away from the rest of
            // the path. Symmetric reasoning for the "upper" end.
            var lowerSharedNode = SharedNode(graph.Segments[lowerSegmentId], graph.Segments[path[1]]);
            var upperSharedNode = SharedNode(graph.Segments[upperSegmentId], graph.Segments[path[path.Count - 2]]);
            var desiredLowerEnd = OppositeEndForNode(graph.Segments[lowerSegmentId], lowerSharedNode);
            var desiredUpperEnd = OppositeEndForNode(graph.Segments[upperSegmentId], upperSharedNode);

            if ((desiredLowerEnd != "A" && desiredLowerEnd != "B") ||
                (desiredUpperEnd != "A" && desiredUpperEnd != "B"))
            {
                Report(report, FuseConversionReportLevel.Warning, sourceName, "span-route-unresolved",
                    $"Span '{spanId}' has a connected segment path but the converter could not infer endpoint " +
                    "direction at one side. Preserved original anchors.");
                return false;
            }

            double? lowerLength = graph.Lengths.TryGetValue(lowerSegmentId, out var ll) ? ll : (double?)null;
            double? upperLength = graph.Lengths.TryGetValue(upperSegmentId, out var ul) ? ul : (double?)null;
            if (lowerLength == null || upperLength == null)
            {
                Report(report, FuseConversionReportLevel.Warning, sourceName, "span-route-unresolved",
                    $"Span '{spanId}' needs A/B anchor repair, but one endpoint segment has no estimated length. " +
                    "Preserved original anchors.");
                return false;
            }

            var oldLowerEnd = lower.Value<string>("end");
            var oldUpperEnd = upper.Value<string>("end");
            bool repaired = false;
            repaired |= FlipLocationEndPreservingPosition(lower, desiredLowerEnd, lowerLength);
            repaired |= FlipLocationEndPreservingPosition(upper, desiredUpperEnd, upperLength);

            if (repaired)
            {
                Report(report, FuseConversionReportLevel.Info, sourceName, "span-repaired",
                    $"Span '{spanId}' endpoint anchors were aligned to converted segment topology " +
                    $"path='{string.Join(" -> ", path)}' lowerEnd {oldLowerEnd}->{lower.Value<string>("end")} " +
                    $"upperEnd {oldUpperEnd}->{upper.Value<string>("end")}.");
            }

            return repaired;
        }

        private static void Report(List<FuseConversionReportEntry> report, FuseConversionReportLevel level,
                                    string sourceName, string concept, string message)
        {
            if (report == null) return;
            report.Add(new FuseConversionReportEntry
            {
                Level = level,
                Message = message,
                SourceFile = sourceName ?? string.Empty,
                Concept = concept,
            });
        }
    }
}
