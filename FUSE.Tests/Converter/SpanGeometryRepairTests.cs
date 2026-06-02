using System.Collections.Generic;
using System.Linq;
using FUSE.Converter.Conversion;
using FUSE.Converter.Models;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FUSE.Tests.Converter
{
    /// <summary>
    /// Coverage for the cross-fragment geometry-repair pass:
    /// clamp/swap on same-segment spans, anchor flipping on
    /// multi-segment spans, and segment-graph BFS pathfinding.
    /// </summary>
    public sealed class SpanGeometryRepairTests
    {
        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private static SpanGeometryRepair.ConvertedFragment MakeFragment(string name, JObject tracks)
        {
            var document = new JObject
            {
                ["tracks"] = tracks ?? new JObject(),
            };
            return new SpanGeometryRepair.ConvertedFragment(name, document);
        }

        private static JObject Node(double x, double y, double z)
        {
            return new JObject
            {
                ["position"] = new JObject { ["x"] = x, ["y"] = y, ["z"] = z },
            };
        }

        private static JObject Segment(string startNodeId, string endNodeId)
        {
            return new JObject
            {
                ["startNodeId"] = startNodeId,
                ["endNodeId"] = endNodeId,
            };
        }

        private static JObject Location(string segmentId, string end, double distance)
        {
            return new JObject
            {
                ["segmentId"] = segmentId,
                ["end"] = end,
                ["distance"] = distance,
            };
        }

        // ------------------------------------------------------------------
        // ClampSpanEndpoint
        // ------------------------------------------------------------------

        [Fact]
        public void ClampSpanEndpoint_no_op_when_distance_inside_segment()
        {
            var location = Location("s", "A", 5.0);
            var report = new List<FuseConversionReportEntry>();

            var changed = SpanGeometryRepair.ClampSpanEndpoint(
                "sp", "upper", location, segmentLength: 10.0, sourceName: "f.json", report: report);

            Assert.False(changed);
            Assert.Empty(report);
            Assert.Equal(5.0, location.Value<double>("distance"));
        }

        [Fact]
        public void ClampSpanEndpoint_clamps_negative_distance_and_reports()
        {
            var location = Location("s", "A", -3.0);
            var report = new List<FuseConversionReportEntry>();

            var changed = SpanGeometryRepair.ClampSpanEndpoint(
                "sp", "upper", location, 10.0, "f.json", report);

            Assert.True(changed);
            Assert.Equal(0.0, location.Value<double>("distance"));
            Assert.Contains(report, r =>
                r.Level == FuseConversionReportLevel.Info &&
                r.Concept == "span-repaired" &&
                r.Message.Contains("upper") &&
                r.Message.Contains("clamped"));
        }

        [Fact]
        public void ClampSpanEndpoint_clamps_overlength_distance_and_strips_normalized()
        {
            var location = new JObject
            {
                ["segmentId"] = "s",
                ["end"] = "A",
                ["normalized"] = 2.0,    // overflows the [0,1] range
                ["offset"] = 0.5,
            };
            var report = new List<FuseConversionReportEntry>();

            var changed = SpanGeometryRepair.ClampSpanEndpoint("sp", "upper", location, 10.0, "f.json", report);

            Assert.True(changed);
            Assert.Equal(10.0, location.Value<double>("distance"));
            // normalized + offset should be removed by SetLocationDistance.
            Assert.Null(location["normalized"]);
            Assert.Null(location["offset"]);
        }

        // ------------------------------------------------------------------
        // RepairSameSegmentSpan
        // ------------------------------------------------------------------

        [Fact]
        public void RepairSameSegmentSpan_warns_no_safe_repair_when_opposite_ends_still_crossed()
        {
            // upper anchored to A but sitting at 9m (near B); lower
            // anchored to B but also sitting at 9m (near A): the
            // physical positions cross. The validity predicate
            // (dist-from-A: upper=9, lower=10-9=1, requires 9<1)
            // fails, AND swapping the upper/lower roles is symmetric
            // (so the swap branch can never repair this geometry —
            // matches the Python reference). Expect the warning, no
            // data mutation.
            var span = new JObject
            {
                ["upper"] = Location("s", "A", 9.0),
                ["lower"] = Location("s", "B", 9.0),
            };
            var report = new List<FuseConversionReportEntry>();

            SpanGeometryRepair.RepairSameSegmentSpan(
                "sp", span, segmentLength: 10.0, sourceName: "f.json", report: report);

            Assert.Contains(report, r =>
                r.Level == FuseConversionReportLevel.Warning &&
                r.Concept == "span-geometry-crossed" &&
                r.Message.Contains("no safe automatic repair"));
            // Original endpoints preserved.
            Assert.Equal("A", span["upper"].Value<string>("end"));
            Assert.Equal("B", span["lower"].Value<string>("end"));
        }

        [Fact]
        public void RepairSameSegmentSpan_warns_when_both_endpoints_anchored_same_end_post_clamp()
        {
            // Both at A — even after clamp, can't be repaired.
            var span = new JObject
            {
                ["upper"] = Location("s", "A", 5.0),
                ["lower"] = Location("s", "A", 8.0),
            };
            var report = new List<FuseConversionReportEntry>();

            SpanGeometryRepair.RepairSameSegmentSpan("sp", span, 10.0, "f.json", report);

            Assert.Contains(report, r =>
                r.Level == FuseConversionReportLevel.Warning &&
                r.Concept == "span-geometry-crossed" &&
                r.Message.Contains("both endpoints anchored"));
        }

        [Fact]
        public void RepairSameSegmentSpan_returns_false_when_segment_length_missing()
        {
            var span = new JObject
            {
                ["upper"] = Location("s", "A", 5.0),
                ["lower"] = Location("s", "B", 5.0),
            };
            var report = new List<FuseConversionReportEntry>();

            var repaired = SpanGeometryRepair.RepairSameSegmentSpan(
                "sp", span, segmentLength: null, sourceName: "f.json", report: report);

            Assert.False(repaired);
            Assert.Empty(report);
        }

        [Fact]
        public void RepairSameSegmentSpan_well_ordered_endpoints_no_repair()
        {
            // upper A:2 + lower B:2 on a 10m segment: dist_from_A(upper)=2,
            // dist_from_A(lower)=8 → 2 < 8 → valid.
            var span = new JObject
            {
                ["upper"] = Location("s", "A", 2.0),
                ["lower"] = Location("s", "B", 2.0),
            };
            var report = new List<FuseConversionReportEntry>();

            var repaired = SpanGeometryRepair.RepairSameSegmentSpan("sp", span, 10.0, "f.json", report);

            Assert.False(repaired);
            Assert.Empty(report);
        }

        // ------------------------------------------------------------------
        // FindSegmentPath
        // ------------------------------------------------------------------

        [Fact]
        public void FindSegmentPath_returns_single_element_when_endpoints_equal()
        {
            var neighbors = new Dictionary<string, HashSet<string>>
            {
                ["s1"] = new HashSet<string>(),
            };

            var path = SpanGeometryRepair.FindSegmentPath("s1", "s1", neighbors);

            Assert.NotNull(path);
            Assert.Single(path);
            Assert.Equal("s1", path[0]);
        }

        [Fact]
        public void FindSegmentPath_walks_chain_via_BFS()
        {
            // s1 — s2 — s3, asking for s1→s3.
            var neighbors = new Dictionary<string, HashSet<string>>
            {
                ["s1"] = new HashSet<string> { "s2" },
                ["s2"] = new HashSet<string> { "s1", "s3" },
                ["s3"] = new HashSet<string> { "s2" },
            };

            var path = SpanGeometryRepair.FindSegmentPath("s1", "s3", neighbors);

            Assert.Equal(new[] { "s1", "s2", "s3" }, path);
        }

        [Fact]
        public void FindSegmentPath_returns_null_when_disconnected()
        {
            var neighbors = new Dictionary<string, HashSet<string>>
            {
                ["s1"] = new HashSet<string>(),
                ["s2"] = new HashSet<string>(),
            };

            Assert.Null(SpanGeometryRepair.FindSegmentPath("s1", "s2", neighbors));
        }

        [Fact]
        public void FindSegmentPath_returns_null_when_endpoint_unknown()
        {
            var neighbors = new Dictionary<string, HashSet<string>>
            {
                ["s1"] = new HashSet<string>(),
            };

            Assert.Null(SpanGeometryRepair.FindSegmentPath("s1", "s99", neighbors));
        }

        // ------------------------------------------------------------------
        // Shared-node + opposite-end helpers
        // ------------------------------------------------------------------

        [Fact]
        public void SharedNode_returns_the_unique_shared_node()
        {
            var a = Segment("n1", "n2");
            var b = Segment("n2", "n3");
            Assert.Equal("n2", SpanGeometryRepair.SharedNode(a, b));
        }

        [Fact]
        public void SharedNode_returns_null_when_ambiguous()
        {
            // A loop where both nodes are shared.
            var a = Segment("n1", "n2");
            var b = Segment("n1", "n2");
            Assert.Null(SpanGeometryRepair.SharedNode(a, b));
        }

        [Fact]
        public void OppositeEndForNode_matches_start_to_B_end_to_A()
        {
            var segment = Segment("n1", "n2");
            Assert.Equal("B", SpanGeometryRepair.OppositeEndForNode(segment, "n1"));
            Assert.Equal("A", SpanGeometryRepair.OppositeEndForNode(segment, "n2"));
            Assert.Null(SpanGeometryRepair.OppositeEndForNode(segment, "unrelated-node"));
        }

        // ------------------------------------------------------------------
        // CollectConvertedTrackGraph
        // ------------------------------------------------------------------

        [Fact]
        public void CollectConvertedTrackGraph_estimates_lengths_from_node_positions()
        {
            var tracks = new JObject
            {
                ["nodes"] = new JObject
                {
                    ["n1"] = Node(0, 0, 0),
                    ["n2"] = Node(3, 0, 4),    // distance 5 from n1
                },
                ["segments"] = new JObject
                {
                    ["s1"] = Segment("n1", "n2"),
                },
            };

            var graph = SpanGeometryRepair.CollectConvertedTrackGraph(
                new List<SpanGeometryRepair.ConvertedFragment> { MakeFragment("a.json", tracks) });

            Assert.True(graph.Lengths.ContainsKey("s1"));
            Assert.Equal(5.0, graph.Lengths["s1"], precision: 6);
        }

        [Fact]
        public void CollectConvertedTrackGraph_builds_segment_neighbors_via_shared_nodes()
        {
            var tracks = new JObject
            {
                ["nodes"] = new JObject
                {
                    ["n1"] = Node(0, 0, 0),
                    ["n2"] = Node(1, 0, 0),
                    ["n3"] = Node(2, 0, 0),
                },
                ["segments"] = new JObject
                {
                    ["s1"] = Segment("n1", "n2"),
                    ["s2"] = Segment("n2", "n3"),
                },
            };

            var graph = SpanGeometryRepair.CollectConvertedTrackGraph(
                new List<SpanGeometryRepair.ConvertedFragment> { MakeFragment("a.json", tracks) });

            Assert.Contains("s2", graph.Neighbors["s1"]);
            Assert.Contains("s1", graph.Neighbors["s2"]);
        }

        [Fact]
        public void CollectConvertedTrackGraph_merges_across_fragments()
        {
            // Nodes in fragment a, segments in fragment b — both must
            // land in the same graph so cross-file references resolve.
            var a = new JObject
            {
                ["nodes"] = new JObject
                {
                    ["n1"] = Node(0, 0, 0),
                    ["n2"] = Node(0, 0, 10),
                },
            };
            var b = new JObject
            {
                ["segments"] = new JObject
                {
                    ["s1"] = Segment("n1", "n2"),
                },
            };

            var graph = SpanGeometryRepair.CollectConvertedTrackGraph(new List<SpanGeometryRepair.ConvertedFragment>
            {
                MakeFragment("a.json", a),
                MakeFragment("b.json", b),
            });

            Assert.True(graph.Segments.ContainsKey("s1"));
            Assert.Equal(10.0, graph.Lengths["s1"], precision: 6);
        }

        // ------------------------------------------------------------------
        // RepairMultiSegmentSpan
        // ------------------------------------------------------------------

        [Fact]
        public void RepairMultiSegmentSpan_flips_anchors_along_two_segment_chain()
        {
            // s1: n1—n2, s2: n2—n3. Span lower on s1 starts at A=0,
            // upper on s2 starts at A=0. Both anchored to A pre-repair.
            // The pass should flip lower's anchor to A (shared node n2,
            // opposite is "B" of s1 actually). Let me reason about
            // _opposite_end_for_node:
            //   segment startNodeId == nodeId → "B"
            //   segment endNodeId == nodeId   → "A"
            // For s1 with start=n1 end=n2 and shared=n2: opposite is "A"
            //   (because n2 is the end, so the OPPOSITE end is A).
            // Wait, that's not what we want — A is the start. Re-reading:
            //   "endNodeId == nodeId → A" means: when the shared node is
            //   the END of the segment, the OPPOSITE physical end of the
            //   segment is the START which is anchored to A.
            // So desiredLowerEnd for s1 (shared=n2, n2 is endNodeId)
            // = "A". For s2 (shared=n2, n2 is startNodeId): "B".
            var tracks = new JObject
            {
                ["nodes"] = new JObject
                {
                    ["n1"] = Node(0, 0, 0),
                    ["n2"] = Node(0, 0, 10),
                    ["n3"] = Node(0, 0, 20),
                },
                ["segments"] = new JObject
                {
                    ["s1"] = Segment("n1", "n2"),
                    ["s2"] = Segment("n2", "n3"),
                },
                ["spans"] = new JObject
                {
                    ["sp1"] = new JObject
                    {
                        ["upper"] = Location("s2", "A", 3.0),   // s2 wants B end → flip
                        ["lower"] = Location("s1", "B", 7.0),   // s1 wants A end → flip
                    },
                },
            };

            var report = new List<FuseConversionReportEntry>();
            SpanGeometryRepair.RepairPackageSpans(
                new List<SpanGeometryRepair.ConvertedFragment> { MakeFragment("a.json", tracks) },
                report);

            var upper = (JObject)((JObject)((JObject)tracks["spans"])["sp1"])["upper"];
            var lower = (JObject)((JObject)((JObject)tracks["spans"])["sp1"])["lower"];

            Assert.Equal("B", upper.Value<string>("end"));
            Assert.Equal("A", lower.Value<string>("end"));
            // FlipLocation preserves position: distance from the
            // SAME physical point of the segment, just measured from
            // the other anchor. Original lower was B:7 on a 10m
            // segment → distance from A = 10-7 = 3.
            Assert.Equal(3.0, lower.Value<double>("distance"), precision: 6);
            Assert.Equal(7.0, upper.Value<double>("distance"), precision: 6);
            Assert.Contains(report, r =>
                r.Concept == "span-repaired" &&
                r.Message.Contains("aligned to converted segment topology"));
        }

        [Fact]
        public void RepairMultiSegmentSpan_warns_external_segment()
        {
            // s1 exists but s99 doesn't — the foreign reference should
            // surface as span-external-segment.
            var tracks = new JObject
            {
                ["nodes"] = new JObject
                {
                    ["n1"] = Node(0, 0, 0),
                    ["n2"] = Node(0, 0, 10),
                },
                ["segments"] = new JObject
                {
                    ["s1"] = Segment("n1", "n2"),
                },
                ["spans"] = new JObject
                {
                    ["sp-ext"] = new JObject
                    {
                        ["upper"] = Location("s99", "A", 3.0),
                        ["lower"] = Location("s1", "A", 3.0),
                    },
                },
            };

            var report = new List<FuseConversionReportEntry>();
            SpanGeometryRepair.RepairPackageSpans(
                new List<SpanGeometryRepair.ConvertedFragment> { MakeFragment("a.json", tracks) },
                report);

            Assert.Contains(report, r =>
                r.Concept == "span-external-segment" &&
                r.Message.Contains("s99"));
        }

        [Fact]
        public void RepairMultiSegmentSpan_warns_when_segments_disconnected()
        {
            // Two segments share no nodes — repair should flag
            // span-route-unresolved rather than guess.
            var tracks = new JObject
            {
                ["nodes"] = new JObject
                {
                    ["n1"] = Node(0, 0, 0),
                    ["n2"] = Node(0, 0, 5),
                    ["n3"] = Node(100, 0, 0),
                    ["n4"] = Node(100, 0, 5),
                },
                ["segments"] = new JObject
                {
                    ["s1"] = Segment("n1", "n2"),
                    ["s2"] = Segment("n3", "n4"),
                },
                ["spans"] = new JObject
                {
                    ["sp"] = new JObject
                    {
                        ["upper"] = Location("s2", "A", 2.0),
                        ["lower"] = Location("s1", "A", 2.0),
                    },
                },
            };

            var report = new List<FuseConversionReportEntry>();
            SpanGeometryRepair.RepairPackageSpans(
                new List<SpanGeometryRepair.ConvertedFragment> { MakeFragment("a.json", tracks) },
                report);

            Assert.Contains(report, r =>
                r.Concept == "span-route-unresolved" &&
                r.Message.Contains("no connected segment path"));
        }

        // ------------------------------------------------------------------
        // Top-level: RepairPackageSpans
        // ------------------------------------------------------------------

        [Fact]
        public void RepairPackageSpans_handles_empty_input_quietly()
        {
            var report = new List<FuseConversionReportEntry>();
            SpanGeometryRepair.RepairPackageSpans(new List<SpanGeometryRepair.ConvertedFragment>(), report);
            Assert.Empty(report);
        }

        [Fact]
        public void RepairPackageSpans_tolerates_missing_tracks_section()
        {
            // Fragments with no tracks at all (operations-only sources)
            // should be a no-op rather than crashing.
            var fragments = new List<SpanGeometryRepair.ConvertedFragment>
            {
                new SpanGeometryRepair.ConvertedFragment("ops.json", new JObject()),
            };
            var report = new List<FuseConversionReportEntry>();

            SpanGeometryRepair.RepairPackageSpans(fragments, report);
            Assert.Empty(report);
        }

        [Fact]
        public void RepairPackageSpans_skips_spans_without_segment_id()
        {
            // A malformed span with no segmentId on upper should be
            // skipped rather than throwing.
            var tracks = new JObject
            {
                ["nodes"] = new JObject(),
                ["segments"] = new JObject
                {
                    ["s1"] = Segment("n1", "n2"),
                },
                ["spans"] = new JObject
                {
                    ["sp"] = new JObject
                    {
                        ["upper"] = new JObject { ["end"] = "A", ["distance"] = 1.0 },
                        ["lower"] = Location("s1", "B", 1.0),
                    },
                },
            };

            var report = new List<FuseConversionReportEntry>();
            SpanGeometryRepair.RepairPackageSpans(
                new List<SpanGeometryRepair.ConvertedFragment> { MakeFragment("a.json", tracks) },
                report);

            // No crash, no report entries about this span.
            Assert.DoesNotContain(report, r => r.Message.Contains("sp"));
        }
    }
}
