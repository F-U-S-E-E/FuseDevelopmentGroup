using System.Collections.Generic;
using FUSE.Converter.Conversion;
using FUSE.Converter.Models;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FUSE.Tests.Converter
{
    /// <summary>
    /// Pure-unit coverage for ConvertSpan / ConvertLocation /
    /// NormalizeEnd, plus the simple crossed-endpoint validator that
    /// runs at conversion time (the general crossed case needs
    /// segment lengths and lives in the deferred geometry-repair
    /// pass).
    /// </summary>
    public sealed class LegacySpanConverterTests
    {
        [Theory]
        [InlineData("a", "A")]
        [InlineData("A", "A")]
        [InlineData("Start", "A")]
        [InlineData("start", "A")]
        [InlineData("b", "B")]
        [InlineData("B", "B")]
        [InlineData("End", "B")]
        [InlineData("end", "B")]
        public void NormalizeEnd_collapses_legacy_aliases(string input, string expected)
        {
            Assert.Equal(expected, LegacyTrackConverter.NormalizeEnd(input));
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        public void NormalizeEnd_returns_null_for_missing_input(string input)
        {
            Assert.Null(LegacyTrackConverter.NormalizeEnd(input));
        }

        [Fact]
        public void NormalizeEnd_passes_through_unknown_value()
        {
            // The Python source preserves the original token so the
            // FUSE loader's validator can flag it; mirror that.
            Assert.Equal("middle", LegacyTrackConverter.NormalizeEnd("middle"));
        }

        [Fact]
        public void ConvertLocation_reads_distance_form()
        {
            var legacy = JObject.Parse(
                "{ \"segmentId\": \"seg-a\", \"end\": \"A\", \"distance\": 12.5 }");

            var result = LegacyTrackConverter.ConvertLocation(legacy);

            Assert.Equal("seg-a", result.Value<string>("segmentId"));
            Assert.Equal("A", result.Value<string>("end"));
            Assert.Equal(12.5, result.Value<double>("distance"));
            Assert.Null(result["normalized"]);
        }

        [Fact]
        public void ConvertLocation_reads_normalized_form()
        {
            var legacy = JObject.Parse(
                "{ \"segmentId\": \"seg-a\", \"end\": \"B\", \"normalized\": 0.75 }");

            var result = LegacyTrackConverter.ConvertLocation(legacy);

            Assert.Equal("B", result.Value<string>("end"));
            Assert.Equal(0.75, result.Value<double>("normalized"));
            Assert.Null(result["distance"]);
        }

        [Fact]
        public void ConvertLocation_preserves_offset_when_specified()
        {
            var legacy = JObject.Parse(
                "{ \"segmentId\": \"s\", \"end\": \"A\", \"distance\": 1, \"offset\": 0.25 }");

            var result = LegacyTrackConverter.ConvertLocation(legacy);
            Assert.Equal(0.25, result.Value<double>("offset"));
        }

        [Fact]
        public void ConvertLocation_returns_safe_default_for_non_object()
        {
            var result = LegacyTrackConverter.ConvertLocation(null);

            Assert.Equal(string.Empty, result.Value<string>("segmentId"));
            Assert.Equal(0.0, result.Value<double>("distance"));
            Assert.Equal("A", result.Value<string>("end"));
        }

        [Fact]
        public void ConvertLocation_supports_legacy_segment_aliases()
        {
            // SegmentID, segment (no Id suffix) etc all map to segmentId.
            var legacy = JObject.Parse("{ \"SegmentId\": \"my-seg\" }");
            var result = LegacyTrackConverter.ConvertLocation(legacy);
            Assert.Equal("my-seg", result.Value<string>("segmentId"));
        }

        [Fact]
        public void ConvertSpan_translates_upper_and_lower()
        {
            var legacy = JObject.Parse(
                "{ \"upper\": { \"segmentId\": \"s\", \"end\": \"B\", \"distance\": 10 }, " +
                "\"lower\": { \"segmentId\": \"s\", \"end\": \"B\", \"distance\": 30 } }");

            var result = LegacyTrackConverter.ConvertSpan("sp-1", legacy);

            Assert.Equal("s", result["upper"].Value<string>("segmentId"));
            Assert.Equal(10.0, result["upper"].Value<double>("distance"));
            Assert.Equal(30.0, result["lower"].Value<double>("distance"));
        }

        [Fact]
        public void ConvertSpan_warns_on_crossed_endpoints_anchored_to_A()
        {
            // Both endpoints at A but upper.distance < lower.distance.
            // Upper should sit FARTHER from anchor A, so this is crossed.
            var legacy = JObject.Parse(
                "{ \"upper\": { \"segmentId\": \"s\", \"end\": \"A\", \"distance\": 5 }, " +
                "\"lower\": { \"segmentId\": \"s\", \"end\": \"A\", \"distance\": 10 } }");

            var report = new List<FuseConversionReportEntry>();
            LegacyTrackConverter.ConvertSpan("sp-crossed", legacy, report, "test.json");

            Assert.Contains(report, r =>
                r.Level == FuseConversionReportLevel.Warning &&
                r.Concept == "span-geometry-crossed" &&
                r.Message.Contains("sp-crossed"));
        }

        [Fact]
        public void ConvertSpan_warns_on_crossed_endpoints_anchored_to_B()
        {
            // Both at B but upper.distance > lower.distance: upper should
            // be closer to anchor A (i.e. farther FROM B), so distance-B
            // for upper must be < distance-B for lower.
            var legacy = JObject.Parse(
                "{ \"upper\": { \"segmentId\": \"s\", \"end\": \"B\", \"distance\": 30 }, " +
                "\"lower\": { \"segmentId\": \"s\", \"end\": \"B\", \"distance\": 5 } }");

            var report = new List<FuseConversionReportEntry>();
            LegacyTrackConverter.ConvertSpan("sp-crossed-b", legacy, report, "test.json");

            Assert.Contains(report, r =>
                r.Level == FuseConversionReportLevel.Warning &&
                r.Concept == "span-geometry-crossed" &&
                r.Message.Contains("sp-crossed-b"));
        }

        [Fact]
        public void ConvertSpan_does_not_warn_when_endpoints_on_different_ends()
        {
            // Endpoints on different anchors — the general crossed case
            // needs segment length; this validator stays silent and
            // lets the FUSE preflight catch it later.
            var legacy = JObject.Parse(
                "{ \"upper\": { \"segmentId\": \"s\", \"end\": \"A\", \"distance\": 5 }, " +
                "\"lower\": { \"segmentId\": \"s\", \"end\": \"B\", \"distance\": 5 } }");

            var report = new List<FuseConversionReportEntry>();
            LegacyTrackConverter.ConvertSpan("sp-different-ends", legacy, report, "test.json");

            Assert.DoesNotContain(report, r => r.Concept == "span-geometry-crossed");
        }

        [Fact]
        public void ConvertSpan_does_not_warn_when_endpoints_on_different_segments()
        {
            // The simple validator only fires on same-segment crossings.
            var legacy = JObject.Parse(
                "{ \"upper\": { \"segmentId\": \"s1\", \"end\": \"A\", \"distance\": 5 }, " +
                "\"lower\": { \"segmentId\": \"s2\", \"end\": \"A\", \"distance\": 10 } }");

            var report = new List<FuseConversionReportEntry>();
            LegacyTrackConverter.ConvertSpan("sp-multi-segment", legacy, report, "test.json");

            Assert.DoesNotContain(report, r => r.Concept == "span-geometry-crossed");
        }

        [Fact]
        public void ConvertSpan_well_ordered_endpoints_do_not_warn()
        {
            // upper.distance > lower.distance with both at A — correct
            // ordering (upper farther from anchor A).
            var legacy = JObject.Parse(
                "{ \"upper\": { \"segmentId\": \"s\", \"end\": \"A\", \"distance\": 20 }, " +
                "\"lower\": { \"segmentId\": \"s\", \"end\": \"A\", \"distance\": 5 } }");

            var report = new List<FuseConversionReportEntry>();
            LegacyTrackConverter.ConvertSpan("sp-ok", legacy, report, "test.json");

            Assert.DoesNotContain(report, r => r.Concept == "span-geometry-crossed");
        }

        [Fact]
        public void ConvertSpan_null_report_does_not_crash_validator()
        {
            // Crossed but no report — the validator should still run
            // (no throw) and just not record anything.
            var legacy = JObject.Parse(
                "{ \"upper\": { \"segmentId\": \"s\", \"end\": \"A\", \"distance\": 5 }, " +
                "\"lower\": { \"segmentId\": \"s\", \"end\": \"A\", \"distance\": 10 } }");

            var result = LegacyTrackConverter.ConvertSpan("sp-crossed-no-report", legacy, report: null);
            Assert.NotNull(result);
        }
    }
}
