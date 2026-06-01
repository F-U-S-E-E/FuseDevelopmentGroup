using FUSE.Converter.Conversion;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FUSE.Tests.Converter
{
    /// <summary>
    /// Pure-unit coverage for LegacyWorldConverter. End-to-end coverage
    /// through FuseLegacyConverter lives in
    /// <see cref="FuseLegacyConverterTests"/>.
    /// </summary>
    public sealed class LegacyWorldConverterTests
    {
        [Fact]
        public void ConvertScenery_prepends_scenery_protocol_for_bare_identifier()
        {
            var legacy = JObject.Parse(
                "{ \"model\": \"oak-tree\", " +
                "\"position\": { \"x\": 1, \"y\": 2, \"z\": 3 }, " +
                "\"scale\": { \"x\": 2, \"y\": 2, \"z\": 2 } }");

            var result = LegacyWorldConverter.ConvertScenery(legacy);

            Assert.Equal("scenery://oak-tree", result.Value<string>("assetIdentifier"));
            Assert.Equal(2.0, result["scale"].Value<double>("x"));
        }

        [Fact]
        public void ConvertScenery_keeps_scheme_qualified_identifier_intact()
        {
            var legacy = JObject.Parse(
                "{ \"prefab\": \"path://custom/tree\", " +
                "\"position\": { \"x\": 0, \"y\": 0, \"z\": 0 } }");

            var result = LegacyWorldConverter.ConvertScenery(legacy);
            Assert.Equal("path://custom/tree", result.Value<string>("assetIdentifier"));
        }

        [Fact]
        public void ConvertScenery_defaults_scale_to_one_when_absent()
        {
            var legacy = JObject.Parse(
                "{ \"model\": \"x\", \"position\": { \"x\": 0, \"y\": 0, \"z\": 0 } }");

            var result = LegacyWorldConverter.ConvertScenery(legacy);
            var scale = result.Value<JObject>("scale");
            Assert.Equal(1.0, scale.Value<double>("x"));
            Assert.Equal(1.0, scale.Value<double>("y"));
            Assert.Equal(1.0, scale.Value<double>("z"));
        }

        [Fact]
        public void ConvertScenery_collects_anchor_span_ids_from_all_aliases()
        {
            var legacy = JObject.Parse(
                "{ \"model\": \"x\", \"position\": { \"x\": 0, \"y\": 0, \"z\": 0 }, " +
                "\"spanIds\": [\"sp-1\", \"sp-2\"] }");

            var result = LegacyWorldConverter.ConvertScenery(legacy);
            var spans = result.Value<JArray>("anchorSpanIds");
            Assert.NotNull(spans);
            Assert.Equal(2, spans.Count);
        }

        [Fact]
        public void ConvertSpliney_maps_FlowyThingBuilder_to_road_by_default()
        {
            var legacy = JObject.Parse(
                "{ \"handler\": \"StrangeCustoms.FlowyThingBuilder\", " +
                "\"points\": [{ \"position\": { \"x\": 0, \"y\": 0, \"z\": 0 } }] }");

            var result = LegacyWorldConverter.ConvertSpliney(legacy);
            Assert.Equal("road", result.Value<string>("type"));
            // FlowyThingBuilder defaults offsetY to -0.1 if absent.
            Assert.Equal(-0.1, result.Value<double>("offsetY"));
        }

        [Fact]
        public void ConvertSpliney_FlowyThingBuilder_with_river_style_becomes_river()
        {
            var legacy = JObject.Parse(
                "{ \"handler\": \"StrangeCustoms.FlowyThingBuilder\", \"style\": \"River\", \"points\": [] }");

            var result = LegacyWorldConverter.ConvertSpliney(legacy);
            Assert.Equal("river", result.Value<string>("type"));
        }

        [Fact]
        public void ConvertSpliney_maps_known_handlers_directly()
        {
            var trestle = LegacyWorldConverter.ConvertSpliney(JObject.Parse(
                "{ \"handler\": \"StrangeCustoms.AutoTrestleBuilder\", \"points\": [] }"));
            Assert.Equal("trestle", trestle.Value<string>("type"));

            var waterfall = LegacyWorldConverter.ConvertSpliney(JObject.Parse(
                "{ \"handler\": \"StrangeCustoms.WaterfallBuilder\", \"points\": [] }"));
            Assert.Equal("waterfall", waterfall.Value<string>("type"));
        }

        [Fact]
        public void ConvertSpliney_unknown_handler_falls_through_to_explicit_type_then_unknown()
        {
            var explicitType = LegacyWorldConverter.ConvertSpliney(JObject.Parse(
                "{ \"handler\": \"SomeOtherHandler\", \"type\": \"custom\", \"points\": [] }"));
            Assert.Equal("custom", explicitType.Value<string>("type"));

            // Unknown handler preservation: the original handler string
            // is stashed in extensions so it can be inspected later.
            Assert.NotNull(explicitType["extensions"]);
            Assert.Equal("SomeOtherHandler", explicitType["extensions"].Value<string>("originalHandler"));

            var unknownType = LegacyWorldConverter.ConvertSpliney(JObject.Parse(
                "{ \"handler\": \"AnotherUnknown\", \"points\": [] }"));
            Assert.Equal("unknown", unknownType.Value<string>("type"));
        }

        [Fact]
        public void ConvertMapLabel_promotes_NN_MPH_text_to_speed_limit_label()
        {
            var legacy = JObject.Parse(
                "{ \"text\": \"45 MPH\", \"position\": { \"x\": 1, \"y\": 2, \"z\": 3 } }");

            var result = LegacyWorldConverter.ConvertMapLabel("label-1", legacy);

            Assert.Equal("45", result.Value<string>("text"));
            Assert.Equal("speedLimit", result.Value<string>("style"));
            Assert.Equal(45, result.Value<int>("speedLimitMph"));
        }

        [Fact]
        public void ConvertMapLabel_keeps_text_when_not_speed_limit_pattern()
        {
            var legacy = JObject.Parse(
                "{ \"text\": \"Roundhouse\", \"position\": { \"x\": 0, \"y\": 0, \"z\": 0 } }");

            var result = LegacyWorldConverter.ConvertMapLabel("rh-label", legacy);

            Assert.Equal("Roundhouse", result.Value<string>("text"));
            Assert.Null(result.Value<string>("style"));
        }

        [Fact]
        public void ConvertMapLabel_falls_back_to_key_when_text_absent()
        {
            var legacy = JObject.Parse("{ \"position\": { \"x\": 0, \"y\": 0, \"z\": 0 } }");
            var result = LegacyWorldConverter.ConvertMapLabel("the-key", legacy);
            Assert.Equal("the-key", result.Value<string>("text"));
        }

        [Fact]
        public void ConvertTelegraphPoleMovements_groups_poles_by_offset()
        {
            var legacy = JObject.Parse(
                "{ \"polesToMove\": [1, 2, 3, 4], " +
                "\"poleMovement\": [[0, 0, 0], [1, 0, 0], [1, 0, 0], [0, 0, 0]] }");

            var result = LegacyWorldConverter.ConvertTelegraphPoleMovements(legacy);

            // Two distinct offsets — (0,0,0) for poles 1+4 and (1,0,0) for 2+3.
            Assert.Equal(2, result.Count);

            var firstGroup = (JObject)result[0];
            var firstIndices = firstGroup.Value<JArray>("poleIndices");
            Assert.Equal(0.0, firstGroup["offset"].Value<double>("x"));
            Assert.Equal(2, firstIndices.Count);
            Assert.Contains(1, firstIndices.Values<int>());
            Assert.Contains(4, firstIndices.Values<int>());

            var secondGroup = (JObject)result[1];
            var secondIndices = secondGroup.Value<JArray>("poleIndices");
            Assert.Equal(1.0, secondGroup["offset"].Value<double>("x"));
            Assert.Equal(2, secondIndices.Count);
        }

        [Fact]
        public void ConvertTelegraphPoleMovements_tolerates_dict_movement_form()
        {
            var legacy = JObject.Parse(
                "{ \"polesToMove\": [5], " +
                "\"poleMovement\": [{ \"x\": 0.5, \"y\": 0, \"z\": 0 }] }");

            var result = LegacyWorldConverter.ConvertTelegraphPoleMovements(legacy);
            Assert.Single(result);
            Assert.Equal(0.5, result[0]["offset"].Value<double>("x"));
        }

        [Fact]
        public void ConvertTelegraphPoleMovements_empty_input_returns_empty_array()
        {
            var legacy = JObject.Parse("{}");
            var result = LegacyWorldConverter.ConvertTelegraphPoleMovements(legacy);
            Assert.Empty(result);
        }
    }
}
