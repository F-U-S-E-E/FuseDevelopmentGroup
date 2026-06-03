using System.Collections.Generic;
using FUSE.Converter.Conversion;
using FUSE.Converter.Models;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FUSE.Tests.Converter
{
    /// <summary>
    /// Coverage for the deep industry-component port —
    /// type inference, partial detection, custom-field bucketing,
    /// sub-id generation, spanless passenger-stop flagging.
    /// </summary>
    public sealed class LegacyIndustryComponentConverterTests
    {
        // ------------------------------------------------------------------
        // NormalizeComponentType
        // ------------------------------------------------------------------

        [Theory]
        [InlineData("Model.Ops.IndustryLoader", "loader")]
        [InlineData("model.opsnew.IndustryUnloader", "unloader")]
        [InlineData("alinasmapmod.paxStationComponent", "passengerStop")]
        [InlineData("captiveConversionLoader", "ConfusingSupplements.IndustryComponents.CaptiveConversionLoader")]
        [InlineData("Pay4Resource", "ConfusingSupplements.IndustryComponents.Pay4Resource")]
        public void NormalizeComponentType_maps_known_aliases(string input, string expected)
        {
            Assert.Equal(expected, LegacyIndustryComponentConverter.NormalizeComponentType(input));
        }

        [Fact]
        public void NormalizeComponentType_passes_unknown_through()
        {
            Assert.Equal("MyCustomThing", LegacyIndustryComponentConverter.NormalizeComponentType("MyCustomThing"));
        }

        [Fact]
        public void NormalizeComponentType_returns_empty_for_null_or_blank()
        {
            Assert.Equal("", LegacyIndustryComponentConverter.NormalizeComponentType(null));
            Assert.Equal("", LegacyIndustryComponentConverter.NormalizeComponentType("   "));
        }

        // ------------------------------------------------------------------
        // Shape probes
        // ------------------------------------------------------------------

        [Fact]
        public void HasLegacyLoadOperationShape_detects_canonical_load_keys()
        {
            Assert.True(LegacyIndustryComponentConverter.HasLegacyLoadOperationShape(JObject.Parse("{ \"loadId\": \"oil\" }")));
            Assert.True(LegacyIndustryComponentConverter.HasLegacyLoadOperationShape(JObject.Parse("{ \"maxStorage\": 100 }")));
            Assert.False(LegacyIndustryComponentConverter.HasLegacyLoadOperationShape(JObject.Parse("{ \"name\": \"x\" }")));
        }

        [Fact]
        public void HasStandaloneComponentShape_detects_passenger_keys()
        {
            Assert.True(LegacyIndustryComponentConverter.HasStandaloneComponentShape(JObject.Parse("{ \"timetableCode\": \"NORTH\" }")));
            Assert.True(LegacyIndustryComponentConverter.HasStandaloneComponentShape(JObject.Parse("{ \"teamProfiles\": {} }")));
            Assert.False(LegacyIndustryComponentConverter.HasStandaloneComponentShape(JObject.Parse("{ \"name\": \"x\" }")));
        }

        [Fact]
        public void ShouldConvertComponentAsPartial_returns_false_when_type_set()
        {
            Assert.False(LegacyIndustryComponentConverter.ShouldConvertComponentAsPartial(JObject.Parse("{ \"type\": \"loader\" }")));
        }

        [Fact]
        public void ShouldConvertComponentAsPartial_returns_true_for_shape_without_standalone_marker()
        {
            // No type, no standalone shape → partial.
            Assert.True(LegacyIndustryComponentConverter.ShouldConvertComponentAsPartial(JObject.Parse("{ \"name\": \"x\" }")));
        }

        [Fact]
        public void ShouldConvertComponentAsPartial_returns_true_for_legacy_load_op_without_binding()
        {
            // A load-op block that names track spans is a full, standalone
            // loader definition → NOT partial.
            Assert.False(LegacyIndustryComponentConverter.ShouldConvertComponentAsPartial(
                JObject.Parse("{ \"loadId\": \"oil\", \"trackSpanIds\": [\"s1\"] }")));

            // A load-op block with NO track spans is a partial field-merge
            // onto an existing component (which already owns the spans) →
            // partial. A bare loadId must NOT defeat this. Regression for
            // Nexus 1326 "Production Tweaks", whose l1/lp1/l23 patch
            // base-game loader rates by id with a loadId but no spans.
            Assert.True(LegacyIndustryComponentConverter.ShouldConvertComponentAsPartial(
                JObject.Parse("{ \"maxStorage\": 100 }")));
            Assert.True(LegacyIndustryComponentConverter.ShouldConvertComponentAsPartial(
                JObject.Parse("{ \"loadId\": \"logs\", \"storageChangeRate\": 72, \"maxStorage\": 72, \"carTransferRate\": 144 }")));
        }

        // ------------------------------------------------------------------
        // InferComponentType
        // ------------------------------------------------------------------

        [Fact]
        public void InferComponentType_uses_explicit_type_first()
        {
            var t = LegacyIndustryComponentConverter.InferComponentType("anything",
                JObject.Parse("{ \"type\": \"teamTrack\" }"), context: null);
            Assert.Equal("teamTrack", t);
        }

        [Fact]
        public void InferComponentType_recognises_canonical_id()
        {
            var t = LegacyIndustryComponentConverter.InferComponentType("loader",
                JObject.Parse("{}"), context: null);
            Assert.Equal("loader", t);
        }

        [Fact]
        public void InferComponentType_uses_shape_heuristics()
        {
            Assert.Equal("formulaic", LegacyIndustryComponentConverter.InferComponentType(
                "abc", JObject.Parse("{ \"inputTermsPerDay\": {} }"), context: null));
            Assert.Equal("teamTrack", LegacyIndustryComponentConverter.InferComponentType(
                "abc", JObject.Parse("{ \"teamProfiles\": {} }"), context: null));
            Assert.Equal("passengerStop", LegacyIndustryComponentConverter.InferComponentType(
                "abc", JObject.Parse("{ \"timetableCode\": \"X\" }"), context: null));
            Assert.Equal("repairTrack", LegacyIndustryComponentConverter.InferComponentType(
                "abc", JObject.Parse("{ \"canOverhaul\": true }"), context: null));
        }

        [Fact]
        public void InferComponentType_uses_input_output_context()
        {
            var context = new LegacyIndustryComponentConverter.InferenceContext();
            context.Inputs.Add("oil");
            context.Outputs.Add("gasoline");

            Assert.Equal("unloader", LegacyIndustryComponentConverter.InferComponentType(
                "oil", JObject.Parse("{}"), context));
            Assert.Equal("loader", LegacyIndustryComponentConverter.InferComponentType(
                "gasoline", JObject.Parse("{}"), context));
        }

        [Fact]
        public void InferComponentType_defaults_to_loader_when_no_signal()
        {
            Assert.Equal("loader", LegacyIndustryComponentConverter.InferComponentType(
                "mystery", JObject.Parse("{}"), context: null));
        }

        // ------------------------------------------------------------------
        // InferenceContext
        // ------------------------------------------------------------------

        [Fact]
        public void BuildInferenceContext_collects_input_and_output_load_ids()
        {
            var components = JObject.Parse(@"{
                ""loader-a"": { ""outputTermsPerDay"": { ""oil"": 1, ""gasoline"": 1 } },
                ""unloader-b"": { ""inputTermsPerDay"": { ""oil"": 1 } }
            }");

            var ctx = LegacyIndustryComponentConverter.BuildInferenceContext(components);

            Assert.Contains("oil", ctx.Inputs);
            Assert.Contains("oil", ctx.Outputs);
            Assert.Contains("gasoline", ctx.Outputs);
        }

        // ------------------------------------------------------------------
        // InferLoadIdFromComponentId
        // ------------------------------------------------------------------

        [Fact]
        public void InferLoadIdFromComponentId_uses_id_for_load_binding_types()
        {
            var result = LegacyIndustryComponentConverter.InferLoadIdFromComponentId(
                "oil", "loader", JObject.Parse("{ \"loadId\": \"oil\" }"));
            // Despite having loadId set, this function infers from the
            // component id when the shape says "yes this is a load op".
            Assert.Equal("oil", result);
        }

        [Fact]
        public void InferLoadIdFromComponentId_returns_null_for_passenger_or_repair()
        {
            Assert.Null(LegacyIndustryComponentConverter.InferLoadIdFromComponentId(
                "id", "passengerStop", JObject.Parse("{ \"loadId\": \"x\" }")));
            Assert.Null(LegacyIndustryComponentConverter.InferLoadIdFromComponentId(
                "id", "repairTrack", JObject.Parse("{ \"loadId\": \"x\" }")));
        }

        [Fact]
        public void InferLoadIdFromComponentId_returns_null_when_no_legacy_load_shape()
        {
            Assert.Null(LegacyIndustryComponentConverter.InferLoadIdFromComponentId(
                "oil", "loader", JObject.Parse("{}")));
        }

        // ------------------------------------------------------------------
        // CollectCustomComponentFields
        // ------------------------------------------------------------------

        [Fact]
        public void CollectCustomComponentFields_returns_null_for_canonical_types()
        {
            Assert.Null(LegacyIndustryComponentConverter.CollectCustomComponentFields(
                "loader",
                JObject.Parse("{ \"randomField\": 42 }"),
                extra: new JObject()));
        }

        [Fact]
        public void CollectCustomComponentFields_collects_non_schema_fields_for_custom_type()
        {
            var fields = LegacyIndustryComponentConverter.CollectCustomComponentFields(
                "MyCustomType",
                JObject.Parse("{ \"name\": \"keep-out\", \"randomField\": 42, \"another\": \"x\" }"),
                extra: new JObject());

            Assert.NotNull(fields);
            // 'name' is canonical, should be excluded.
            Assert.False(fields.ContainsKey("name"));
            Assert.Equal(42, fields.Value<int>("randomField"));
            Assert.Equal("x", fields.Value<string>("another"));
        }

        [Fact]
        public void CollectCustomComponentFields_lets_explicit_fields_win()
        {
            // Explicit fields dict wins over inferred entries (setdefault order).
            var fields = LegacyIndustryComponentConverter.CollectCustomComponentFields(
                "MyCustomType",
                JObject.Parse("{ \"fields\": { \"alpha\": 1 }, \"alpha\": 999 }"),
                extra: new JObject());
            Assert.Equal(1, fields.Value<int>("alpha"));
        }

        // ------------------------------------------------------------------
        // ConvertComponent — end-to-end
        // ------------------------------------------------------------------

        [Fact]
        public void ConvertComponent_marks_partial_when_no_type_and_no_standalone_shape()
        {
            var converted = LegacyIndustryComponentConverter.ConvertComponent("any", JObject.Parse("{ \"name\": \"x\" }"), context: null);
            Assert.True(converted.Value<bool>("partial"));
            Assert.Null(converted["type"]);
        }

        [Fact]
        public void ConvertComponent_assigns_passenger_load_id_for_passenger_stop()
        {
            var converted = LegacyIndustryComponentConverter.ConvertComponent(
                "pax-1", JObject.Parse("{ \"type\": \"passengerStop\", \"trackSpanIds\": [\"s\"] }"), context: null);
            Assert.Equal("passengers", converted.Value<string>("loadId"));
            Assert.Equal("pax-1", converted.Value<string>("passengerStopId"));
        }

        [Fact]
        public void ConvertComponent_infers_load_id_for_load_binding_loader()
        {
            // Loader with load-op shape (maxStorage) and no explicit
            // loadId → use the component id as the load id.
            var converted = LegacyIndustryComponentConverter.ConvertComponent(
                "iron-ore", JObject.Parse("{ \"type\": \"loader\", \"maxStorage\": 100 }"), context: null);
            Assert.Equal("iron-ore", converted.Value<string>("loadId"));
        }

        [Fact]
        public void ConvertComponent_buckets_unknown_type_into_fields()
        {
            var converted = LegacyIndustryComponentConverter.ConvertComponent(
                "x", JObject.Parse("{ \"type\": \"Some.Unknown.Type\", \"trackSpanIds\": [\"s\"], \"weird\": 7 }"), context: null);
            // Custom fields should land in `fields`.
            var fields = converted["fields"] as JObject;
            Assert.NotNull(fields);
            Assert.Equal(7, fields.Value<int>("weird"));
        }

        [Fact]
        public void ConvertComponent_marks_spanless_loadId_rate_patch_as_partial()
        {
            // Regression — Nexus 1326 "Woodys ... Production Tweaks": the
            // mod patches the production rates of an existing base-game
            // logging-camp loader (l1) by id — a loadId plus rate fields,
            // but no track spans. This must convert as a partial field-merge
            // (which the apply path layers onto the existing loader), NOT as
            // a full loader — a full loader with no spans trips
            // "loader requires at least one track span".
            var converted = LegacyIndustryComponentConverter.ConvertComponent(
                "l1",
                JObject.Parse("{ \"carTypeFilter\": \"FL\", \"loadId\": \"logs\", \"storageChangeRate\": 72, \"maxStorage\": 72, \"carTransferRate\": 144 }"),
                context: null);

            Assert.True(converted.Value<bool>("partial"));
            Assert.Null(converted["type"]);
            // No spans were invented for the patch.
            Assert.Null(converted["trackSpanIds"]);
            // The rate fields the mod actually wants to change survive.
            Assert.Equal(72, converted.Value<int>("storageChangeRate"));
            Assert.Equal("logs", converted.Value<string>("loadId"));
        }

        // ------------------------------------------------------------------
        // MakeComponentSubId
        // ------------------------------------------------------------------

        [Fact]
        public void MakeComponentSubId_returns_input_when_id_non_blank()
        {
            var existing = new Dictionary<string, JToken>();
            var report = new List<FuseConversionReportEntry>();
            Assert.Equal("real-id", LegacyIndustryComponentConverter.MakeComponentSubId(
                "ind", "real-id", new JObject(), existing, report));
        }

        [Fact]
        public void MakeComponentSubId_generates_formula_for_formulaic_type()
        {
            var existing = new Dictionary<string, JToken>();
            var report = new List<FuseConversionReportEntry>();
            var subId = LegacyIndustryComponentConverter.MakeComponentSubId(
                "ind", "", new JObject { ["type"] = "formulaic" }, existing, report);
            Assert.Equal("formula", subId);
            Assert.Contains(report, r => r.Concept == "industry-component-empty-id");
        }

        [Fact]
        public void MakeComponentSubId_disambiguates_collisions_with_numeric_suffix()
        {
            var existing = new Dictionary<string, JToken>
            {
                ["repair"] = new JObject(),
            };
            var report = new List<FuseConversionReportEntry>();
            var subId = LegacyIndustryComponentConverter.MakeComponentSubId(
                "ind", "", new JObject { ["type"] = "repairTrack" }, existing, report);
            Assert.Equal("repair-2", subId);
        }

        [Fact]
        public void MakeComponentSubId_falls_back_to_loadId_then_name_then_component()
        {
            var existing = new Dictionary<string, JToken>();
            var report = new List<FuseConversionReportEntry>();
            var subId = LegacyIndustryComponentConverter.MakeComponentSubId(
                "ind", "", new JObject { ["loadId"] = "iron-ore" }, existing, report);
            Assert.Equal("iron-ore", subId);
        }

        // ------------------------------------------------------------------
        // FlagSpanlessPassengerStop
        // ------------------------------------------------------------------

        [Fact]
        public void FlagSpanlessPassengerStop_warns_when_passenger_has_no_spans()
        {
            var report = new List<FuseConversionReportEntry>();
            var converted = JObject.Parse("{ \"type\": \"passengerStop\", \"trackSpanIds\": [] }");
            LegacyIndustryComponentConverter.FlagSpanlessPassengerStop(
                "ind", "comp", converted, sourceName: "source.json", report);

            Assert.Contains(report, r =>
                r.Concept == "passenger-stop-spanless" &&
                r.Message.Contains("virtual stop"));
        }

        [Fact]
        public void FlagSpanlessPassengerStop_silent_when_passenger_has_spans()
        {
            var report = new List<FuseConversionReportEntry>();
            var converted = JObject.Parse("{ \"type\": \"passengerStop\", \"trackSpanIds\": [\"s1\"] }");
            LegacyIndustryComponentConverter.FlagSpanlessPassengerStop(
                "ind", "comp", converted, sourceName: "source.json", report);
            Assert.Empty(report);
        }

        [Fact]
        public void FlagSpanlessPassengerStop_silent_for_non_passenger()
        {
            var report = new List<FuseConversionReportEntry>();
            LegacyIndustryComponentConverter.FlagSpanlessPassengerStop(
                "ind", "comp", JObject.Parse("{ \"type\": \"loader\" }"), null, report);
            Assert.Empty(report);
        }
    }
}
