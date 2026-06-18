using System.Collections.Generic;
using FUSE.Converter.Conversion;
using FUSE.Converter.Models;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FUSE.Tests.Converter
{
    /// <summary>
    /// Coverage for ConvertSceneClone, ConvertLabel, ConvertLegacyStart,
    /// ConvertDkwSpliney, and NormalizeTagColor — the deeper world
    /// helpers that landed alongside the industry-component port.
    /// </summary>
    public sealed class LegacyWorldExtrasTests
    {
        // ------------------------------------------------------------------
        // ConvertSceneClone
        // ------------------------------------------------------------------

        [Fact]
        public void ConvertSceneClone_prefixes_bare_source_with_scene_path()
        {
            var legacy = JObject.Parse(
                "{ \"source\": \"oak-tree\", \"position\": { \"x\": 1, \"y\": 2, \"z\": 3 } }");

            var result = LegacyWorldExtras.ConvertSceneClone("clone-1", legacy);

            Assert.Equal("path://scene/oak-tree", result.Value<string>("source"));
            Assert.Equal("clone-1", result.Value<string>("targetPath"));
        }

        [Fact]
        public void ConvertSceneClone_keeps_schemed_source_intact()
        {
            var legacy = JObject.Parse("{ \"instantiateFrom\": \"vanilla://foo\" }");
            var result = LegacyWorldExtras.ConvertSceneClone("c", legacy);
            Assert.Equal("vanilla://foo", result.Value<string>("source"));
        }

        [Fact]
        public void ConvertSceneClone_defaults_local_scale_to_one()
        {
            var result = LegacyWorldExtras.ConvertSceneClone("k", new JObject());
            var scale = result.Value<JObject>("localScale");
            Assert.Equal(1.0, scale.Value<double>("x"));
            Assert.Equal(1.0, scale.Value<double>("y"));
            Assert.Equal(1.0, scale.Value<double>("z"));
        }

        // ------------------------------------------------------------------
        // ConvertLabel
        // ------------------------------------------------------------------

        [Fact]
        public void ConvertLabel_promotes_NN_MPH_to_speed_limit_style()
        {
            var legacy = JObject.Parse("{ \"text\": \"55 MPH\", \"position\": { \"x\": 0, \"y\": 0, \"z\": 0 } }");
            var result = LegacyWorldExtras.ConvertLabel("lbl", legacy);
            Assert.Equal("55", result.Value<string>("text"));
            Assert.Equal("speedLimit", result.Value<string>("style"));
            Assert.Equal(55, result.Value<int>("speedLimitMph"));
        }

        [Fact]
        public void ConvertLabel_keeps_arbitrary_text_intact()
        {
            var result = LegacyWorldExtras.ConvertLabel("lbl", JObject.Parse("{ \"text\": \"Roundhouse\" }"));
            Assert.Equal("Roundhouse", result.Value<string>("text"));
            Assert.Null(result.Value<string>("style"));
        }

        [Fact]
        public void ConvertLabel_falls_back_to_key_when_no_text()
        {
            var result = LegacyWorldExtras.ConvertLabel("the-key", new JObject());
            Assert.Equal("the-key", result.Value<string>("text"));
        }

        // ------------------------------------------------------------------
        // ConvertLegacyStart
        // ------------------------------------------------------------------

        [Fact]
        public void ConvertLegacyStart_emits_spawn_point_and_extension_block()
        {
            var legacy = JObject.Parse(@"{
                ""name"": ""Test Start"",
                ""identifier"": ""test-start"",
                ""spawnPoint"": { ""position"": { ""x"": 10, ""y"": 0, ""z"": 5 }, ""range"": 100 },
                ""initialMoney"": 50000
            }");

            var (spawn, ext) = LegacyWorldExtras.ConvertLegacyStart(legacy);

            Assert.NotNull(spawn);
            Assert.Equal("Test Start", spawn.Value<string>("name"));
            Assert.Equal(10.0, spawn.Value<JObject>("position").Value<double>("x"));
            Assert.Equal(100, spawn.Value<int>("radius"));

            Assert.NotNull(ext);
            Assert.Equal("test-start", ext.Value<string>("identifier"));
            Assert.Equal(50000, ext.Value<int>("initialMoney"));
        }

        [Fact]
        public void ConvertLegacyStart_returns_nulls_when_no_spawn_point()
        {
            var (spawn, ext) = LegacyWorldExtras.ConvertLegacyStart(JObject.Parse("{ \"name\": \"x\" }"));
            Assert.Null(spawn);
            Assert.Null(ext);
        }

        // ------------------------------------------------------------------
        // ConvertDkwSpliney
        // ------------------------------------------------------------------

        [Fact]
        public void ConvertDkwSpliney_returns_false_for_out_of_range_angle()
        {
            var rail = JObject.Parse("{ \"tracks\": { \"nodes\": {}, \"segments\": {} } }");
            var spliney = JObject.Parse(@"{
                ""crossingAngle"": 2,
                ""position"": { ""x"": 0, ""y"": 0, ""z"": 0 },
                ""rotation"": { ""x"": 0, ""y"": 0, ""z"": 0 }
            }");

            Assert.False(LegacyWorldExtras.ConvertDkwSpliney("dkw-1", spliney, rail));
        }

        [Fact]
        public void ConvertDkwSpliney_emits_eight_nodes_and_eight_segments_for_valid_angle()
        {
            var rail = JObject.Parse("{ \"tracks\": { \"nodes\": {}, \"segments\": {} } }");
            var spliney = JObject.Parse(@"{
                ""crossingAngle"": 10,
                ""position"": { ""x"": 0, ""y"": 0, ""z"": 0 },
                ""rotation"": { ""x"": 0, ""y"": 0, ""z"": 0 }
            }");

            var converted = LegacyWorldExtras.ConvertDkwSpliney("dkw-1", spliney, rail);

            Assert.True(converted);
            var nodes = (JObject)((JObject)rail["tracks"])["nodes"];
            var segments = (JObject)((JObject)rail["tracks"])["segments"];
            Assert.Equal(8, nodes.Count);
            Assert.Equal(8, segments.Count);
            // All 8 named suffixes (P1I/P1O/P2I/P2O/P3I/P3O/P4I/P4O) present.
            foreach (var suffix in new[] { "P1I", "P1O", "P2I", "P2O", "P3I", "P3O", "P4I", "P4O" })
            {
                Assert.True(nodes.ContainsKey("Ndkw-1DKW_Node" + suffix));
            }
        }

        [Fact]
        public void ConvertDkwSpliney_flips_diagonal_priorities_for_negative_angle()
        {
            var rail = JObject.Parse("{ \"tracks\": { \"nodes\": {}, \"segments\": {} } }");
            var spliney = JObject.Parse(@"{
                ""crossingAngle"": -10,
                ""position"": { ""x"": 0, ""y"": 0, ""z"": 0 },
                ""rotation"": { ""x"": 0, ""y"": 0, ""z"": 0 }
            }");

            Assert.True(LegacyWorldExtras.ConvertDkwSpliney("dkw-flip", spliney, rail));
            var segments = (JObject)((JObject)rail["tracks"])["segments"];
            // For negative angle, D1 should get priority -1 and D2
            // priority 1 (i.e. flipped versus the +angle case).
            var d1 = (JObject)segments["Sdkw-flipDKW_SegmentD1"];
            var d2 = (JObject)segments["Sdkw-flipDKW_SegmentD2"];
            Assert.Equal(-1, d1.Value<int>("priority"));
            Assert.Equal(1, d2.Value<int>("priority"));
        }

        // ------------------------------------------------------------------
        // NormalizeTagColor
        // ------------------------------------------------------------------

        [Fact]
        public void NormalizeTagColor_passes_through_rgb_and_rgba()
        {
            var rgb = JArray.Parse("[0.5, 0.6, 0.7]");
            var rgba = JArray.Parse("[0.5, 0.6, 0.7, 0.8]");
            Assert.Same(rgb, LegacyWorldExtras.NormalizeTagColor("a", rgb, report: null));
            Assert.Same(rgba, LegacyWorldExtras.NormalizeTagColor("a", rgba, report: null));
        }

        [Fact]
        public void NormalizeTagColor_truncates_oversized_and_reports()
        {
            var input = JArray.Parse("[0.1, 0.2, 0.3, 0.4, 0.5, 0.6]");
            var report = new List<FuseConversionReportEntry>();

            var normalized = (JArray)LegacyWorldExtras.NormalizeTagColor("graham-area", input, report);

            Assert.Equal(3, normalized.Count);
            Assert.Equal(0.1, normalized[0].Value<double>());
            Assert.Contains(report, r => r.Concept == "area-tagColor-overflow");
        }

        [Fact]
        public void NormalizeTagColor_pads_undersized_with_zeros_and_warns()
        {
            var input = JArray.Parse("[0.1]");
            var report = new List<FuseConversionReportEntry>();

            var normalized = (JArray)LegacyWorldExtras.NormalizeTagColor("tiny-area", input, report);

            Assert.Equal(3, normalized.Count);
            Assert.Equal(0.1, normalized[0].Value<double>());
            Assert.Equal(0.0, normalized[1].Value<double>());
            Assert.Equal(0.0, normalized[2].Value<double>());
            Assert.Contains(report, r => r.Concept == "area-tagColor-underflow");
        }

        [Fact]
        public void NormalizeTagColor_passes_non_array_through()
        {
            var nonArray = JToken.Parse("\"red\"");
            Assert.Same(nonArray, LegacyWorldExtras.NormalizeTagColor("a", nonArray, null));
        }
    }
}
