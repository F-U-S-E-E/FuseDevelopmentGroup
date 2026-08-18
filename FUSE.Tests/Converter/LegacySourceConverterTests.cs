using System.Collections.Generic;
using FUSE.Converter.Conversion;
using FUSE.Converter.Models;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FUSE.Tests.Converter
{
    /// <summary>
    /// Unit coverage for the LegacySourceConverter orchestrator —
    /// section-by-section dispatch, ordering helpers,
    /// runtime-duplicate detection, count + group-coverage scans,
    /// rail-data-file weight ranking.
    /// </summary>
    public sealed class LegacySourceConverterTests
    {
        private static JObject NewSkeleton() => FuseFragmentBuilder.Build("mod", "Mod", "1.0", "alex", "frag");

        // ------------------------------------------------------------------
        // OrderState helpers
        // ------------------------------------------------------------------

        [Fact]
        public void LegacyOrderValue_returns_explicit_int()
        {
            var item = JObject.Parse("{ \"order\": 7 }");
            Assert.Equal(7, LegacySourceConverter.LegacyOrderValue(item, report: null));
        }

        [Fact]
        public void LegacyOrderValue_returns_null_for_missing_field()
        {
            Assert.Null(LegacySourceConverter.LegacyOrderValue(JObject.Parse("{}"), report: null));
        }

        [Fact]
        public void LegacyOrderValue_warns_on_non_integer()
        {
            var report = new List<FuseConversionReportEntry>();
            var result = LegacySourceConverter.LegacyOrderValue(JObject.Parse("{ \"order\": \"oops\" }"), report);
            Assert.Null(result);
            Assert.Contains(report, r => r.Concept == "invalid-order-value");
        }

        [Fact]
        public void LegacyOrderValue_rejects_booleans()
        {
            // Python explicitly excludes booleans (which would
            // otherwise int-coerce to 0/1).
            Assert.Null(LegacySourceConverter.LegacyOrderValue(JObject.Parse("{ \"order\": true }"), report: null));
        }

        [Fact]
        public void NextIndustryOrder_assigns_incrementing_per_area_when_no_explicit_order()
        {
            var state = new LegacySourceConverter.OrderState();
            var ind = JObject.Parse("{}");

            Assert.Equal(0, LegacySourceConverter.NextIndustryOrder(state, "yard-a", "ind-1", ind));
            Assert.Equal(1, LegacySourceConverter.NextIndustryOrder(state, "yard-a", "ind-2", ind));
            // Different area resets the counter.
            Assert.Equal(0, LegacySourceConverter.NextIndustryOrder(state, "yard-b", "ind-3", ind));
        }

        [Fact]
        public void NextIndustryOrder_uses_explicit_order_when_provided()
        {
            var state = new LegacySourceConverter.OrderState();
            Assert.Equal(42, LegacySourceConverter.NextIndustryOrder(state, "yard", "ind", JObject.Parse("{ \"order\": 42 }")));
        }

        [Fact]
        public void NextIndustryOrder_reuses_previously_assigned_value_for_same_id()
        {
            var state = new LegacySourceConverter.OrderState();
            var first = LegacySourceConverter.NextIndustryOrder(state, "yard", "ind", JObject.Parse("{}"));
            var second = LegacySourceConverter.NextIndustryOrder(state, "yard", "ind", JObject.Parse("{}"));
            Assert.Equal(first, second);
        }

        [Fact]
        public void RecordRuntimeDuplicate_emits_info_on_second_record()
        {
            var state = new LegacySourceConverter.OrderState();
            var report = new List<FuseConversionReportEntry>();

            LegacySourceConverter.RecordRuntimeDuplicate(state, "turntable", "tt-1", "fileA.json", report);
            LegacySourceConverter.RecordRuntimeDuplicate(state, "turntable", "tt-1", "fileB.json", report);

            Assert.Contains(report, r =>
                r.Concept == "duplicate-turntable-id" &&
                r.Message.Contains("fileB.json") &&
                r.Message.Contains("fileA.json"));
        }

        // ------------------------------------------------------------------
        // CountContent
        // ------------------------------------------------------------------

        [Fact]
        public void CountContent_returns_per_section_subsection_counts()
        {
            var rail = NewSkeleton();
            ((JObject)((JObject)rail["tracks"])["nodes"])["n1"] = new JObject();
            ((JObject)((JObject)rail["tracks"])["segments"])["s1"] = new JObject();
            ((JObject)((JObject)rail["operations"])["loads"])["coal"] = new JObject();

            var counts = LegacySourceConverter.CountContent(rail);
            Assert.Equal(1, counts["tracks.nodes"]);
            Assert.Equal(1, counts["tracks.segments"]);
            Assert.Equal(1, counts["operations.loads"]);
        }

        [Fact]
        public void CountContent_skips_top_level_removals_buckets_but_keeps_per_removal_counts()
        {
            var rail = NewSkeleton();
            ((JArray)((JObject)((JObject)rail["tracks"])["removals"])["nodes"]).Add("n1");
            var counts = LegacySourceConverter.CountContent(rail);
            Assert.DoesNotContain("tracks.removals", counts.Keys);
            Assert.Equal(1, counts["tracks.removals.nodes"]);
        }

        [Fact]
        public void HasContent_returns_false_for_empty_skeleton()
        {
            Assert.False(LegacySourceConverter.HasContent(NewSkeleton()));
        }

        // ------------------------------------------------------------------
        // CollectInitiallyEnabledGroups
        // ------------------------------------------------------------------

        [Fact]
        public void CollectInitiallyEnabledGroups_harvests_groups_from_initially_enabled_sections()
        {
            var rail = NewSkeleton();
            var prog = (JObject)rail["progression"];
            ((JArray)prog["sections"]).Add(new JObject
            {
                ["initiallyEnabled"] = true,
                ["trackGroupsEnableOnUnlock"] = new JArray("g1", "g2"),
            });
            ((JArray)prog["sections"]).Add(new JObject
            {
                ["initiallyEnabled"] = false,
                ["trackGroupsEnableOnUnlock"] = new JArray("g3"),
            });

            var sink = new HashSet<string>();
            LegacySourceConverter.CollectInitiallyEnabledGroups(rail, sink);

            Assert.Contains("g1", sink);
            Assert.Contains("g2", sink);
            Assert.DoesNotContain("g3", sink);
        }

        // ------------------------------------------------------------------
        // RailDataFileWeight
        // ------------------------------------------------------------------

        [Fact]
        public void RailDataFileWeight_uses_counts_to_categorize_when_available()
        {
            var trackCounts = new Dictionary<string, int> { ["tracks.nodes"] = 5 };
            Assert.Equal(10, LegacySourceConverter.RailDataFileWeight("anything.json", trackCounts));

            var loadCounts = new Dictionary<string, int> { ["operations.loads"] = 2 };
            // Only loads → weight 0 (loads need to come first since
            // other sections may reference them).
            Assert.Equal(0, LegacySourceConverter.RailDataFileWeight("anything.json", loadCounts));

            var indCounts = new Dictionary<string, int> { ["operations.industries"] = 3 };
            Assert.Equal(20, LegacySourceConverter.RailDataFileWeight("anything.json", indCounts));
        }

        [Fact]
        public void RailDataFileWeight_falls_back_to_filename_tokens()
        {
            Assert.Equal(0, LegacySourceConverter.RailDataFileWeight("loads-config.json", null));
            Assert.Equal(10, LegacySourceConverter.RailDataFileWeight("game-graph.json", null));
            Assert.Equal(20, LegacySourceConverter.RailDataFileWeight("industries.json", null));
            Assert.Equal(30, LegacySourceConverter.RailDataFileWeight("loaders.json", null));
            Assert.Equal(40, LegacySourceConverter.RailDataFileWeight("progression.json", null));
            Assert.Equal(50, LegacySourceConverter.RailDataFileWeight("scenery.json", null));
            Assert.Equal(90, LegacySourceConverter.RailDataFileWeight("misc.json", null));
        }

        // ------------------------------------------------------------------
        // ConvertSource end-to-end
        // ------------------------------------------------------------------

        [Fact]
        public void ConvertSource_dispatches_splineys_by_handler()
        {
            var source = JObject.Parse(@"{
                ""splineys"": {
                    ""tt-1"": { ""handler"": ""AlinasMapMod.Turntable.TurntableBuilder"", ""points"": [], ""radius"": 12 },
                    ""ln-1"": { ""handler"": ""AlinasMapMod.MapLabelBuilder"", ""text"": ""North"", ""position"": { ""x"": 0, ""y"": 0, ""z"": 0 } },
                    ""rd-1"": { ""handler"": ""StrangeCustoms.FlowyThingBuilder"", ""points"": [
                        { ""position"": { ""x"": 0, ""y"": 0, ""z"": 0 } },
                        { ""position"": { ""x"": 10, ""y"": 0, ""z"": 0 } }
                    ] }
                }
            }");

            var rail = NewSkeleton();
            LegacySourceConverter.ConvertSource(source, rail, "src.json", null, report: null);

            Assert.True(((JObject)((JObject)rail["operations"])["turntables"]).ContainsKey("tt-1"));
            Assert.True(((JObject)((JObject)rail["world"])["mapLabels"]).ContainsKey("ln-1"));
            Assert.True(((JObject)((JObject)rail["world"])["splineys"]).ContainsKey("rd-1"));
        }

        [Fact]
        public void ConvertSource_handles_track_node_removals_via_null_value()
        {
            // A null value at "nodes.n1" signals removal — should go
            // into tracks.removals.nodes rather than tracks.nodes.
            var source = JObject.Parse(@"{
                ""tracks"": {
                    ""nodes"": {
                        ""n1"": null,
                        ""n2"": { ""position"": { ""x"": 0, ""y"": 0, ""z"": 0 } }
                    }
                }
            }");

            var rail = NewSkeleton();
            LegacySourceConverter.ConvertSource(source, rail, "src.json", null, report: null);

            var removals = (JArray)((JObject)((JObject)rail["tracks"])["removals"])["nodes"];
            Assert.Contains("n1", removals.Values<string>());
            Assert.True(((JObject)((JObject)rail["tracks"])["nodes"]).ContainsKey("n2"));
        }

        [Fact]
        public void ConvertSource_accepts_root_level_legacy_track_dictionaries()
        {
            // Flying Spuds and other RailLoader packages place all three
            // dictionaries at the document root instead of under tracks.
            var source = JObject.Parse(@"{
                ""nodes"": {
                    ""NPOE-7j00"": {
                        ""position"": { ""x"": 9458.39, ""y"": 546.64, ""z"": 7536.29 },
                        ""rotation"": { ""x"": 0, ""y"": 152.5, ""z"": 0 }
                    },
                    ""NPOE-7j01"": {
                        ""position"": { ""x"": 9477.55, ""y"": 546.64, ""z"": 7499.48 },
                        ""rotation"": { ""x"": 0, ""y"": 152.5, ""z"": 0 }
                    }
                },
                ""segments"": {
                    ""SPOE-7j00"": {
                        ""style"": ""Standard"",
                        ""trackClass"": ""Mainline"",
                        ""startId"": ""NPOE-7j01"",
                        ""endId"": ""NPOE-7j00""
                    }
                },
                ""spans"": {
                    ""KG-R1"": {
                        ""upper"": { ""segmentId"": ""SPOE-7j00"", ""end"": ""Start"", ""distance"": 0 },
                        ""lower"": { ""segmentId"": ""SPOE-7j00"", ""end"": ""End"", ""distance"": 0 }
                    }
                }
            }");

            var rail = NewSkeleton();
            LegacySourceConverter.ConvertSource(source, rail, "Industry.json", null, report: null);

            var tracks = (JObject)rail["tracks"];
            Assert.Equal(2, ((JObject)tracks["nodes"]).Count);
            Assert.Equal("NPOE-7j01", tracks["segments"]["SPOE-7j00"].Value<string>("startNodeId"));
            Assert.Equal("NPOE-7j00", tracks["segments"]["SPOE-7j00"].Value<string>("endNodeId"));

            var spans = (JObject)tracks["spans"];
            var span = (JObject)spans["KG-R1"];
            Assert.NotNull(span);
            Assert.Equal("SPOE-7j00", span["upper"].Value<string>("segmentId"));
            Assert.Equal("A", span["upper"].Value<string>("end"));
            Assert.Equal("SPOE-7j00", span["lower"].Value<string>("segmentId"));
            Assert.Equal("B", span["lower"].Value<string>("end"));
        }

        [Fact]
        public void ConvertSource_nested_span_overrides_root_level_alias()
        {
            var source = JObject.Parse(@"{
                ""spans"": {
                    ""shared"": {
                        ""upper"": { ""segmentId"": ""old"" },
                        ""lower"": { ""segmentId"": ""old"" }
                    }
                },
                ""tracks"": {
                    ""spans"": {
                        ""shared"": {
                            ""upper"": { ""segmentId"": ""new"" },
                            ""lower"": { ""segmentId"": ""new"" }
                        }
                    }
                }
            }");

            var rail = NewSkeleton();
            LegacySourceConverter.ConvertSource(source, rail, "Industry.json", null, report: null);

            var span = rail["tracks"]["spans"]["shared"];
            Assert.Equal("new", span["upper"].Value<string>("segmentId"));
            Assert.Equal("new", span["lower"].Value<string>("segmentId"));
        }

        [Fact]
        public void ConvertSource_pushes_short_splineys_into_extensions_bucket()
        {
            // Splineys with <2 points get stashed under
            // extensions.legacySplineyObjects for inspection rather
            // than emitted as renderable splines.
            var source = JObject.Parse(@"{
                ""splineys"": {
                    ""sp"": { ""handler"": ""SomeUnknown"", ""points"": [ { ""position"": { ""x"": 0, ""y"": 0, ""z"": 0 } } ] }
                }
            }");
            var rail = NewSkeleton();
            LegacySourceConverter.ConvertSource(source, rail, "src.json", null, report: null);
            var ext = (JObject)((JObject)rail["extensions"])["legacySplineyObjects"];
            Assert.NotNull(ext);
            Assert.True(ext.ContainsKey("sp"));
        }

        [Fact]
        public void ConvertSource_walks_legacy_start_into_spawnPoints_and_extensions()
        {
            var source = JObject.Parse(@"{
                ""identifier"": ""start-x"",
                ""name"": ""Test Start"",
                ""spawnPoint"": { ""position"": { ""x"": 5, ""y"": 0, ""z"": 2 } }
            }");
            var rail = NewSkeleton();
            LegacySourceConverter.ConvertSource(source, rail, "src.json", null, report: null);

            var spawns = (JArray)((JObject)rail["world"])["spawnPoints"];
            Assert.Single(spawns);
            var ext = (JObject)((JObject)rail["extensions"])["legacyStartOption"];
            Assert.NotNull(ext);
            Assert.Equal("start-x", ext.Value<string>("identifier"));
        }
    }
}
