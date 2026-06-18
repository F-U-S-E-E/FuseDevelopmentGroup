using System.Collections.Generic;
using System.Linq;
using FUSE.Converter.Conversion;
using FUSE.Converter.Models;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FUSE.Tests.Converter
{
    public sealed class LegacyProgressionConverterTests
    {
        // ------------------------------------------------------------------
        // NormalizeDeliveryDirection
        // ------------------------------------------------------------------

        [Theory]
        [InlineData("0", "loadToIndustry")]
        [InlineData("toIndustry", "loadToIndustry")]
        [InlineData("Import", "loadToIndustry")]
        [InlineData("1", "loadFromIndustry")]
        [InlineData("from", "loadFromIndustry")]
        [InlineData("Export", "loadFromIndustry")]
        public void NormalizeDeliveryDirection_maps_legacy_aliases(string input, string expected)
        {
            var token = JToken.FromObject(input);
            Assert.Equal(expected, LegacyProgressionConverter.NormalizeDeliveryDirection(token).Value<string>());
        }

        [Fact]
        public void NormalizeDeliveryDirection_passes_unknown_through()
        {
            var token = JToken.FromObject("unrecognised");
            Assert.Equal("unrecognised", LegacyProgressionConverter.NormalizeDeliveryDirection(token).Value<string>());
        }

        // ------------------------------------------------------------------
        // BoolDictionaryToArray
        // ------------------------------------------------------------------

        [Fact]
        public void BoolDictionaryToArray_keeps_only_truthy_keys()
        {
            var input = JObject.Parse("{ \"a\": true, \"b\": false, \"c\": true }");
            var result = LegacyProgressionConverter.BoolDictionaryToArray(input);
            Assert.Equal(new[] { "a", "c" }, result.Values<string>());
        }

        [Fact]
        public void BoolDictionaryToArray_returns_null_for_non_dict()
        {
            Assert.Null(LegacyProgressionConverter.BoolDictionaryToArray(JToken.Parse("[1, 2]")));
        }

        // ------------------------------------------------------------------
        // NormalizeProgressionValue
        // ------------------------------------------------------------------

        [Fact]
        public void NormalizeProgressionValue_renames_legacy_keys()
        {
            var input = JObject.Parse(@"{
                ""DisplayName"": ""Section A"",
                ""defaultEnableInSandbox"": true,
                ""prerequisites"": [ ""feat-1"" ],
                ""industryComponent"": ""ind.loader""
            }");

            var result = LegacyProgressionConverter.NormalizeProgressionValue(input) as JObject;
            Assert.Equal("Section A", result.Value<string>("displayName"));
            Assert.True(result.Value<bool>("initiallyEnabled"));
            Assert.Equal("feat-1", (result["prerequisiteFeatureIds"] as JArray)[0].Value<string>());
            Assert.Equal("ind.loader", result.Value<string>("industryComponentId"));
        }

        [Fact]
        public void NormalizeProgressionValue_normalises_direction()
        {
            var input = JObject.Parse("{ \"direction\": \"import\" }");
            var result = LegacyProgressionConverter.NormalizeProgressionValue(input) as JObject;
            Assert.Equal("loadToIndustry", result.Value<string>("direction"));
        }

        [Fact]
        public void NormalizeProgressionValue_converts_bool_dict_for_known_field()
        {
            var input = JObject.Parse(@"{
                ""trackGroupsEnableOnUnlock"": { ""a"": true, ""b"": false }
            }");
            var result = LegacyProgressionConverter.NormalizeProgressionValue(input) as JObject;
            var arr = result["trackGroupsEnableOnUnlock"] as JArray;
            Assert.Equal(new[] { "a" }, arr.Values<string>());
        }

        [Fact]
        public void NormalizeProgressionValue_drops_blank_industry_component_id()
        {
            var input = JObject.Parse("{ \"industryComponent\": \"   \" }");
            var result = LegacyProgressionConverter.NormalizeProgressionValue(input) as JObject;
            // Empty string industryComponentId → null → dropped by clean.
            Assert.False(result.ContainsKey("industryComponentId"));
        }

        // ------------------------------------------------------------------
        // AppendUniqueId
        // ------------------------------------------------------------------

        [Fact]
        public void AppendUniqueId_adds_to_empty_field()
        {
            var container = new JObject();
            LegacyProgressionConverter.AppendUniqueId(container, "enableFeaturesOnUnlock", "feat-1");
            Assert.Equal(new[] { "feat-1" }, ((JArray)container["enableFeaturesOnUnlock"]).Values<string>());
        }

        [Fact]
        public void AppendUniqueId_skips_existing_case_insensitive()
        {
            var container = JObject.Parse("{ \"x\": [\"FOO\"] }");
            LegacyProgressionConverter.AppendUniqueId(container, "x", "foo");
            Assert.Single((JArray)container["x"]);
        }

        [Fact]
        public void AppendUniqueId_normalises_dict_existing()
        {
            var container = JObject.Parse("{ \"x\": { \"a\": true, \"b\": false } }");
            LegacyProgressionConverter.AppendUniqueId(container, "x", "c");
            var arr = (JArray)container["x"];
            Assert.Equal(new[] { "a", "c" }, arr.Values<string>());
        }

        // ------------------------------------------------------------------
        // ReconcileProgressionSectionFeatureAliases
        // ------------------------------------------------------------------

        [Fact]
        public void ReconcileProgressionSectionFeatureAliases_emits_alias_for_section_referenced_as_feature()
        {
            // The mapFeature references "section-a" in its
            // prerequisites, but section-a is defined as a SECTION,
            // not a map feature. The reconciler should emit an alias
            // map feature and enable it from section-a.
            var rail = JObject.Parse(@"{
                ""progression"": {
                    ""sections"": [ { ""id"": ""section-a"" } ],
                    ""mapFeatures"": {
                        ""mf-1"": { ""prerequisiteFeatureIds"": [ ""section-a"" ] }
                    }
                }
            }");

            var report = new List<FuseConversionReportEntry>();
            LegacyProgressionConverter.ReconcileProgressionSectionFeatureAliases(rail, report);

            var mapFeatures = (JObject)((JObject)rail["progression"])["mapFeatures"];
            Assert.True(mapFeatures.ContainsKey("section-a"));
            // Section A should now enable section-a on unlock.
            var sections = (JArray)((JObject)rail["progression"])["sections"];
            var sectionA = sections.First(s => (s as JObject)?.Value<string>("id") == "section-a") as JObject;
            var enables = sectionA["enableFeaturesOnUnlock"] as JArray;
            Assert.Contains("section-a", enables.Values<string>());

            Assert.Contains(report, r => r.Concept == "progression-section-feature-alias");
        }

        [Fact]
        public void ReconcileProgressionSectionFeatureAliases_no_op_when_no_overlap()
        {
            var rail = JObject.Parse(@"{
                ""progression"": {
                    ""sections"": [ { ""id"": ""section-a"" } ],
                    ""mapFeatures"": {
                        ""mf-1"": { ""prerequisiteFeatureIds"": [ ""mf-2"" ] }
                    }
                }
            }");
            var report = new List<FuseConversionReportEntry>();
            LegacyProgressionConverter.ReconcileProgressionSectionFeatureAliases(rail, report);
            // mf-2 isn't a section so no alias is emitted.
            Assert.Empty(report);
        }

        // ------------------------------------------------------------------
        // ConvertProgression
        // ------------------------------------------------------------------

        [Fact]
        public void ConvertProgression_merges_legacy_progression_block_into_rail()
        {
            var rail = JObject.Parse(@"{
                ""progression"": { ""sections"": [], ""progressions"": {}, ""mapFeatures"": {} }
            }");
            var source = JObject.Parse(@"{
                ""progression"": {
                    ""progressionId"": ""my-pid"",
                    ""sections"": [ { ""DisplayName"": ""S1"" } ],
                    ""mapFeatures"": { ""mf-1"": { ""DisplayName"": ""MF One"" } }
                }
            }");
            var report = new List<FuseConversionReportEntry>();

            LegacyProgressionConverter.ConvertProgression(source, rail, report);

            var prog = (JObject)rail["progression"];
            Assert.Equal("my-pid", prog.Value<string>("progressionId"));
            var sections = prog["sections"] as JArray;
            Assert.Single(sections);
            Assert.Equal("S1", ((JObject)sections[0]).Value<string>("displayName"));

            var mf = prog["mapFeatures"] as JObject;
            Assert.Equal("MF One", mf["mf-1"].Value<string>("displayName"));
        }

        [Fact]
        public void ConvertProgression_picks_up_sibling_progressions_and_mapFeatures()
        {
            // Legacy mods sometimes drop progressions/mapFeatures at
            // the source root instead of under a `progression` block.
            var rail = JObject.Parse(@"{
                ""progression"": { ""sections"": [], ""progressions"": {}, ""mapFeatures"": {} }
            }");
            var source = JObject.Parse(@"{
                ""progressions"": { ""p1"": { ""displayName"": ""P1"" } },
                ""mapFeatures"": { ""m1"": { ""displayName"": ""M1"" } }
            }");
            LegacyProgressionConverter.ConvertProgression(source, rail, report: null);

            var prog = (JObject)rail["progression"];
            Assert.True(((JObject)prog["progressions"]).ContainsKey("p1"));
            Assert.True(((JObject)prog["mapFeatures"]).ContainsKey("m1"));
        }
    }
}
