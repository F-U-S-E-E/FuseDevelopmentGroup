using System.Linq;
using FUSE.Authoring.Serialization;
using FUSE.Authoring.Validation;
using FUSE.Loading;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FUSE.Tests.Loading
{
    /// <summary>
    /// Pin the legacy "industries" → FUSE conversion contract. SC packages
    /// routinely patch an existing base-game industry (e.g.
    /// <c>{ "industries": { "whittier-sawmill": { "components": {
    /// "r1": { ... } } } } }</c>) without supplying an areaId, position or
    /// rotation. Earlier versions of the converter always emitted
    /// <c>position</c>/<c>rotation</c> as <c>{x:0,y:0,z:0}</c> and deserialized
    /// <c>FuseIndustry.AreaId</c> as null, after which
    /// <see cref="FUSE.Runtime.API.IndustryAPI.UpdateIndustry"/> reparented
    /// the existing base-game industry to the arbitrary first Area and yanked
    /// its transform to the origin — visible in-game as Map Enhancer
    /// painting Whittier Sawmill / progression dropoff tracks with the wrong
    /// area colour. These tests lock in that the converter no longer fabricates
    /// transform/area directives when the source did not author them.
    /// </summary>
    public class FuseLegacyIndustryConverterTests
    {
        private static (JObject root, JObject industries) ConvertIndustries(JObject source)
        {
            var manifest = new FuseLegacyPackageManifest
            {
                PackageId = "test-pkg",
                DisplayName = "Test Package",
                Author = "tester",
                Version = "1.0.0"
            };
            var root = FuseLegacyDataConverter.CreateSkeleton(manifest, "industry-fragment");
            FuseLegacyDataConverter.ConvertSource(source, root, manifest);
            var industries = (JObject)root["operations"]["industries"];
            return (root, industries);
        }

        public class TopLevelPartialPatch
        {
            [Fact]
            public void NoAreaIdNoPositionNoRotation_ConverterOmitsTransformDirectives()
            {
                var source = new JObject
                {
                    ["industries"] = new JObject
                    {
                        ["whittier-sawmill"] = new JObject
                        {
                            ["components"] = new JObject
                            {
                                ["r1"] = new JObject
                                {
                                    ["trackSpans"] = new JArray("whittier-r1-span")
                                }
                            }
                        }
                    }
                };

                var (_, industries) = FuseLegacyIndustryConverterTests.ConvertIndustries(source);
                var entry = (JObject)industries["whittier-sawmill"];
                Assert.NotNull(entry);
                Assert.False(entry.ContainsKey("areaId"),
                    "no areaId in source -> no areaId in converted output");
                Assert.False(entry.ContainsKey("position"),
                    "no position in source -> no position in converted output (unconditional position used to drag the existing industry to the origin)");
                Assert.False(entry.ContainsKey("rotation"),
                    "no rotation in source -> no rotation in converted output");
            }

            [Fact]
            public void NoAreaIdNoPositionNoRotation_DeserializedDefinitionIsNullForAllThreeFields()
            {
                var source = new JObject
                {
                    ["industries"] = new JObject
                    {
                        ["whittier-sawmill"] = new JObject
                        {
                            ["components"] = new JObject
                            {
                                ["r1"] = new JObject
                                {
                                    ["trackSpans"] = new JArray("whittier-r1-span")
                                }
                            }
                        }
                    }
                };

                var (root, _) = FuseLegacyIndustryConverterTests.ConvertIndustries(source);
                var definition = FuseSerializer.FromJson(root.ToString());
                var industry = definition.Operations.Industries["whittier-sawmill"];
                Assert.Null(industry.AreaId);
                Assert.False(industry.Position.HasValue);
                Assert.False(industry.Rotation.HasValue);
            }

            [Fact]
            public void ExplicitPositionAndRotation_RoundTripThroughConverter()
            {
                var source = new JObject
                {
                    ["industries"] = new JObject
                    {
                        ["new-industry"] = new JObject
                        {
                            ["areaId"] = "whittier",
                            ["position"] = new JObject
                            {
                                ["x"] = 10f,
                                ["y"] = 0f,
                                ["z"] = -25f
                            },
                            ["rotation"] = new JObject
                            {
                                ["x"] = 0f,
                                ["y"] = 90f,
                                ["z"] = 0f
                            },
                            ["components"] = new JObject()
                        }
                    }
                };

                var (root, industries) = FuseLegacyIndustryConverterTests.ConvertIndustries(source);
                var entry = (JObject)industries["new-industry"];
                Assert.Equal("whittier", (string)entry["areaId"]);
                Assert.True(entry.ContainsKey("position"));
                Assert.True(entry.ContainsKey("rotation"));

                var definition = FuseSerializer.FromJson(root.ToString());
                var industry = definition.Operations.Industries["new-industry"];
                Assert.Equal("whittier", industry.AreaId);
                Assert.True(industry.Position.HasValue);
                Assert.Equal(10f, industry.Position.Value.x);
                Assert.Equal(-25f, industry.Position.Value.z);
                Assert.True(industry.Rotation.HasValue);
                Assert.Equal(90f, industry.Rotation.Value.y);
            }

            [Fact]
            public void ExplicitZeroPosition_IsHonoredNotDroppedByConverter()
            {
                // A package that genuinely wants the industry at the origin
                // must still be able to express that. Only the absence of the
                // key suppresses the directive — an explicit { x:0, y:0, z:0 }
                // round-trips as a real Vector3 value.
                var source = new JObject
                {
                    ["industries"] = new JObject
                    {
                        ["origin-industry"] = new JObject
                        {
                            ["areaId"] = "whittier",
                            ["position"] = new JObject
                            {
                                ["x"] = 0f,
                                ["y"] = 0f,
                                ["z"] = 0f
                            },
                            ["components"] = new JObject()
                        }
                    }
                };

                var (root, industries) = FuseLegacyIndustryConverterTests.ConvertIndustries(source);
                var entry = (JObject)industries["origin-industry"];
                Assert.True(entry.ContainsKey("position"));

                var definition = FuseSerializer.FromJson(root.ToString());
                var industry = definition.Operations.Industries["origin-industry"];
                Assert.True(industry.Position.HasValue);
                Assert.Equal(0f, industry.Position.Value.x);
                Assert.Equal(0f, industry.Position.Value.y);
                Assert.Equal(0f, industry.Position.Value.z);
            }
        }

        public class SpanlessLoaderRatePatch
        {
            // Regression — Nexus 1326 "Woodys Upper Walker Pulpwood and
            // Production Tweaks". The mod patches the production rates of
            // three existing base-game logging-camp loaders (l1/lp1/l23) by
            // id — each a loadId plus rate fields with no track spans — and
            // adds one genuinely new loader (lp2) that does declare spans.
            // Before the fix the converter counted a bare loadId as a load
            // binding, so the spanless rate patches converted as full
            // loaders; each then tripped "loader requires at least one track
            // span" (fuse.operations.component.trackSpanIds) and the whole
            // .FUSE package faulted at deserialization.
            private static JObject MakeSource() => new JObject
            {
                ["areas"] = new JObject
                {
                    ["bryson-above"] = new JObject
                    {
                        ["industries"] = new JObject
                        {
                            ["logcamp1"] = new JObject
                            {
                                ["components"] = new JObject
                                {
                                    ["l1"] = new JObject
                                    {
                                        ["carTypeFilter"] = "FL",
                                        ["loadId"] = "logs",
                                        ["storageChangeRate"] = 72,
                                        ["maxStorage"] = 72,
                                        ["carTransferRate"] = 144
                                    },
                                    ["lp1"] = new JObject
                                    {
                                        ["carTypeFilter"] = "FB",
                                        ["loadId"] = "pulpwood",
                                        ["storageChangeRate"] = 4000000.0,
                                        ["maxStorage"] = 4000000.0,
                                        ["carTransferRate"] = 3000000.0
                                    },
                                    ["l23"] = new JObject
                                    {
                                        ["carTypeFilter"] = "FL",
                                        ["loadId"] = "logs",
                                        ["storageChangeRate"] = 120,
                                        ["maxStorage"] = 120,
                                        ["carTransferRate"] = 240
                                    },
                                    ["lp2"] = new JObject
                                    {
                                        ["type"] = "Model.Ops.IndustryLoader",
                                        ["name"] = "Walker Upper Pulpwood",
                                        ["trackSpans"] = new JArray("WD_Upper_Pulp1", "WD_Upper_Pulp2"),
                                        ["carTypeFilter"] = "FB",
                                        ["sharedStorage"] = false,
                                        ["loadId"] = "pulpwood",
                                        ["storageChangeRate"] = 6000000.0,
                                        ["maxStorage"] = 6000000.0,
                                        ["carTransferRate"] = 3000000.0
                                    }
                                }
                            }
                        }
                    }
                }
            };

            [Fact]
            public void SpanlessRatePatches_ConvertAsPartial_NewLoaderStaysFull()
            {
                var (_, industries) = FuseLegacyIndustryConverterTests.ConvertIndustries(MakeSource());
                var components = (JObject)industries["logcamp1"]["components"];

                foreach (var id in new[] { "l1", "lp1", "l23" })
                {
                    var c = (JObject)components[id];
                    Assert.True(c.Value<bool>("partial"), $"{id} should convert as a partial field-merge patch");
                    Assert.Null(c["type"]);
                    // The rate fields the mod actually wants to change survive.
                    Assert.NotNull(c["storageChangeRate"]);
                }

                // The genuinely new loader keeps its full shape and its spans.
                var lp2 = (JObject)components["lp2"];
                Assert.Null(lp2["partial"]);
                Assert.False(string.IsNullOrEmpty(lp2.Value<string>("type")));
                Assert.NotNull(lp2["trackSpanIds"]);
            }

            [Fact]
            public void ConvertedDefinition_PassesValidation_NoTrackSpanError()
            {
                var (root, _) = FuseLegacyIndustryConverterTests.ConvertIndustries(MakeSource());
                var definition = FuseSerializer.FromJson(root.ToString());

                var result = new FuseDefinitionValidator().Validate(definition);

                Assert.DoesNotContain(result.Errors, e => e.Code == "fuse.operations.component.trackSpanIds");
            }

            [Fact]
            public void ExplicitLegacyLoaderTypes_StillPatchBaseComponentsAndPassValidation()
            {
                var components = new JObject();
                foreach (var id in new[] { "l1", "p1", "l2", "p2", "l3", "p34" })
                {
                    components[id] = new JObject
                    {
                        ["type"] = "Model.Ops.IndustryLoader",
                        ["name"] = "Connelly Creek " + id,
                        ["loadId"] = id.StartsWith("p") ? "pulpwood" : "logs",
                        ["storageChangeRate"] = 64.0,
                        ["maxStorage"] = 128.0,
                        ["carTransferRate"] = 256.0
                    };
                }

                var source = new JObject
                {
                    ["areas"] = new JObject
                    {
                        ["connelly"] = new JObject
                        {
                            ["industries"] = new JObject
                            {
                                ["connelly0"] = new JObject
                                {
                                    ["components"] = components
                                }
                            }
                        }
                    }
                };

                var (root, industries) = FuseLegacyIndustryConverterTests.ConvertIndustries(source);
                var converted = (JObject)industries["connelly0"]["components"];
                foreach (var id in new[] { "l1", "p1", "l2", "p2", "l3", "p34" })
                {
                    Assert.True(converted[id].Value<bool>("partial"), $"{id} should inherit its base-game track spans");
                    Assert.Null(converted[id]["type"]);
                    Assert.Null(converted[id]["trackSpanIds"]);
                }

                var definition = FuseSerializer.FromJson(root.ToString());
                var result = new FuseDefinitionValidator().Validate(definition);
                Assert.DoesNotContain(result.Errors, error => error.Code == "fuse.operations.component.trackSpanIds");
            }
        }

        public class ReplaceDirectives
        {
            [Fact]
            public void TrackSpanReplace_RoundTripsAsReplacementPatch()
            {
                var source = new JObject
                {
                    ["industries"] = new JObject
                    {
                        ["wh-e-engine"] = new JObject
                        {
                            ["components"] = new JObject
                            {
                                ["coaling"] = new JObject
                                {
                                    ["trackSpans"] = new JObject
                                    {
                                        ["$replace"] = new JArray("Pn9z", "WhittierCoal2")
                                    }
                                }
                            }
                        }
                    }
                };

                var (root, industries) = FuseLegacyIndustryConverterTests.ConvertIndustries(source);
                var converted = (JObject)industries["wh-e-engine"]["components"]["coaling"];
                Assert.Equal(
                    new[] { "Pn9z", "WhittierCoal2" },
                    converted["trackSpanPatch"]["replace"].Values<string>());

                var definition = FuseSerializer.FromJson(root.ToString());
                var component = definition.Operations.Industries["wh-e-engine"].Components["coaling"];
                Assert.Equal(new[] { "Pn9z", "WhittierCoal2" }, component.TrackSpanPatch.Replace);
            }

            [Fact]
            public void ComponentDictionaryReplace_RoundTripsAsWholesaleReplacement()
            {
                var source = new JObject
                {
                    ["industries"] = new JObject
                    {
                        ["wh-e-engine"] = new JObject
                        {
                            ["components"] = new JObject
                            {
                                ["$replace"] = new JObject
                                {
                                    ["diesel"] = new JObject
                                    {
                                        ["trackSpans"] = new JObject
                                        {
                                            ["$replace"] = new JArray("Pq23")
                                        }
                                    }
                                }
                            }
                        }
                    }
                };

                var (root, industries) = FuseLegacyIndustryConverterTests.ConvertIndustries(source);
                var converted = (JObject)industries["wh-e-engine"];
                Assert.True(converted.Value<bool>("replaceComponents"));
                Assert.False(converted.Value<bool>("mergeComponents"));
                Assert.Equal(new[] { "diesel" }, ((JObject)converted["components"]).Properties().Select(p => p.Name));

                var definition = FuseSerializer.FromJson(root.ToString());
                var industry = definition.Operations.Industries["wh-e-engine"];
                Assert.True(industry.ReplaceComponents);
                Assert.False(industry.MergeComponents);
                Assert.Equal(new[] { "diesel" }, industry.Components.Keys);
            }
        }

        public class RootLevelLegacySpans
        {
            private static JObject Span(string upperSegment, string upperEnd, string lowerSegment, string lowerEnd)
            {
                return new JObject
                {
                    ["upper"] = new JObject
                    {
                        ["segmentId"] = upperSegment,
                        ["distance"] = 0,
                        ["end"] = upperEnd
                    },
                    ["lower"] = new JObject
                    {
                        ["segmentId"] = lowerSegment,
                        ["distance"] = 0,
                        ["end"] = lowerEnd
                    }
                };
            }

            [Fact]
            public void FlyingSpudsShape_ConvertsRootNodesSegmentsAndSpans()
            {
                var source = new JObject
                {
                    ["nodes"] = new JObject
                    {
                        ["NPOE-7j00"] = new JObject
                        {
                            ["position"] = new JObject { ["x"] = 9458.39, ["y"] = 546.64, ["z"] = 7536.29 },
                            ["rotation"] = new JObject { ["x"] = 0, ["y"] = 152.5, ["z"] = 0 }
                        },
                        ["NPOE-7j01"] = new JObject
                        {
                            ["position"] = new JObject { ["x"] = 9477.55, ["y"] = 546.64, ["z"] = 7499.48 },
                            ["rotation"] = new JObject { ["x"] = 0, ["y"] = 152.5, ["z"] = 0 }
                        }
                    },
                    ["segments"] = new JObject
                    {
                        ["SPOE-7j00"] = new JObject
                        {
                            ["style"] = "Standard",
                            ["trackClass"] = "Mainline",
                            ["startId"] = "NPOE-7j01",
                            ["endId"] = "NPOE-7j00"
                        }
                    },
                    ["spans"] = new JObject
                    {
                        ["EPO_EP1"] = Span("SPOE-7j00", "Start", "SPOE-7j00", "End")
                    }
                };

                var (root, _) = FuseLegacyIndustryConverterTests.ConvertIndustries(source);
                var tracks = (JObject)root["tracks"];
                Assert.Equal(2, ((JObject)tracks["nodes"]).Count);
                Assert.Single((JObject)tracks["segments"]);
                Assert.Single((JObject)tracks["spans"]);
                Assert.Equal("NPOE-7j01", (string)tracks["segments"]["SPOE-7j00"]["startNodeId"]);
                Assert.Equal("NPOE-7j00", (string)tracks["segments"]["SPOE-7j00"]["endNodeId"]);

                var definition = FuseSerializer.FromJson(root.ToString());
                Assert.Equal(2, definition.Tracks.Nodes.Count);
                Assert.Single(definition.Tracks.Segments);
                Assert.Single(definition.Tracks.Spans);
            }

            [Fact]
            public void CompatibilityExpansion_NormalizesRootTrackDictionariesBeforePatchingRuntimeState()
            {
                var source = new JObject
                {
                    ["nodes"] = new JObject
                    {
                        ["root-node"] = new JObject { ["position"] = new JObject() },
                        ["shared-node"] = new JObject { ["position"] = new JObject { ["x"] = 1 } }
                    },
                    ["segments"] = new JObject
                    {
                        ["root-segment"] = new JObject { ["startId"] = "root-node", ["endId"] = "shared-node" }
                    },
                    ["spans"] = new JObject
                    {
                        ["root-span"] = Span("root-segment", "Start", "root-segment", "End")
                    },
                    ["tracks"] = new JObject
                    {
                        ["nodes"] = new JObject
                        {
                            ["nested-node"] = new JObject { ["position"] = new JObject() },
                            ["shared-node"] = new JObject { ["position"] = new JObject { ["x"] = 2 } }
                        }
                    }
                };

                FuseLegacyGameGraphCompatibility.NormalizeRootTrackDictionaries(source);

                Assert.Null(source["nodes"]);
                Assert.Null(source["segments"]);
                Assert.Null(source["spans"]);
                var tracks = (JObject)source["tracks"];
                Assert.Equal(3, ((JObject)tracks["nodes"]).Count);
                Assert.NotNull(tracks["segments"]["root-segment"]);
                Assert.NotNull(tracks["spans"]["root-span"]);
                Assert.Equal(2, tracks["nodes"]["shared-node"]["position"].Value<int>("x"));

                var state = new JObject
                {
                    ["tracks"] = new JObject
                    {
                        ["nodes"] = new JObject(),
                        ["segments"] = new JObject(),
                        ["spans"] = new JObject()
                    }
                };
                FuseLegacyJsonPatch.Apply(state, source, "Potato.json");
                var expandedSlice = FuseLegacyGameGraphCompatibility.BuildPatchSlice(state, source);

                Assert.Equal(3, ((JObject)expandedSlice["tracks"]["nodes"]).Count);
                Assert.NotNull(expandedSlice["tracks"]["segments"]["root-segment"]);
                Assert.NotNull(expandedSlice["tracks"]["spans"]["root-span"]);
            }

            [Fact]
            public void AndrewsValleyPowerShape_AcceptsLegacySegmentIDCapitalization()
            {
                var source = new JObject
                {
                    ["tracks"] = new JObject
                    {
                        ["spans"] = new JObject
                        {
                            ["KRPcoalLoad1"] = new JObject
                            {
                                ["upper"] = new JObject
                                {
                                    ["segmentId"] = "S_WNC_KCM_y69t",
                                    ["distance"] = 0,
                                    ["end"] = "Start"
                                },
                                // Andrews Valley Power uses segmentID here.
                                ["lower"] = new JObject
                                {
                                    ["segmentID"] = "S_WNC_KCM_y69t",
                                    ["distance"] = 0,
                                    ["end"] = "End"
                                }
                            }
                        }
                    }
                };

                var (root, _) = FuseLegacyIndustryConverterTests.ConvertIndustries(source);
                var span = root["tracks"]["spans"]["KRPcoalLoad1"];
                Assert.Equal("S_WNC_KCM_y69t", (string)span["upper"]["segmentId"]);
                Assert.Equal("S_WNC_KCM_y69t", (string)span["lower"]["segmentId"]);

                var definition = FuseSerializer.FromJson(root.ToString());
                var result = new FuseDefinitionValidator().Validate(definition);
                Assert.DoesNotContain(result.Errors, error =>
                    error.Code == "fuse.required" && error.Field.Contains("KRPcoalLoad1"));
            }

            [Fact]
            public void SutherlandGapShape_ConvertsSpansAndKeepsLoaderBindings()
            {
                var source = new JObject
                {
                    ["areas"] = new JObject
                    {
                        ["nantahala"] = new JObject
                        {
                            ["industries"] = new JObject
                            {
                                ["KG_Mine"] = new JObject
                                {
                                    ["name"] = "Sutherland Gap Coal",
                                    ["components"] = new JObject
                                    {
                                        ["KG-coal-loader"] = new JObject
                                        {
                                            ["type"] = "Model.Ops.TeleportLoadingIndustry",
                                            ["name"] = "Sutherland Gap Coal R1/R2",
                                            ["trackSpans"] = new JArray("KG-R1", "KG-R2"),
                                            ["inputSpans"] = new JArray("KG-R1", "KG-R2"),
                                            ["outputSpans"] = new JArray("KG-O1", "KG-O2"),
                                            ["loadId"] = "coal",
                                            ["maxStorage"] = 2700000.0
                                        },
                                        ["KG-Supplies"] = new JObject
                                        {
                                            ["type"] = "Model.Ops.IndustryUnloader",
                                            ["trackSpans"] = new JArray("KG-S1"),
                                            ["loadId"] = "mining-supplies",
                                            ["maxStorage"] = 150000.0
                                        },
                                        ["Freight Depot"] = new JObject
                                        {
                                            ["type"] = "Model.Ops.IndustryUnloader",
                                            ["trackSpans"] = new JArray("KG-ER1"),
                                            ["loadId"] = "camp_supplies",
                                            ["maxStorage"] = 100000.0
                                        }
                                    }
                                }
                            }
                        }
                    },
                    // This root-level placement is the exact RailLoader shape
                    // used by Sutherland Gap Coal's Industry.json.
                    ["spans"] = new JObject
                    {
                        ["KG-O1"] = Span("S_KG_Mine_Output_1", "End", "S_KG_Mine_Output_1.1", "Start"),
                        ["KG-O2"] = Span("S_KG_Mine_Output_2", "End", "S_KG_Mine_Output_2.1", "Start"),
                        ["KG-R1"] = Span("S_KG_Mine_Input_0.9", "Start", "S_KG_Mine_Input_1.1", "End"),
                        ["KG-R2"] = Span("S_KG_Mine_Input_1.9", "Start", "S_KG_Mine_Input_2.1", "End"),
                        ["KG-S1"] = Span("S_KG_Mine_Supplies", "Start", "S_KG_Mine_Supplies", "End"),
                        ["KG-ER1"] = Span("S_KG_Mine_Freight_House", "Start", "S_KG_Mine_Freight_House", "End")
                    }
                };

                var (root, industries) = FuseLegacyIndustryConverterTests.ConvertIndustries(source);
                var spans = (JObject)root["tracks"]["spans"];
                Assert.Equal(6, spans.Count);
                Assert.Equal("S_KG_Mine_Input_0.9", (string)spans["KG-R1"]["upper"]["segmentId"]);
                Assert.Equal("A", (string)spans["KG-R1"]["upper"]["end"]);
                Assert.Equal("B", (string)spans["KG-R1"]["lower"]["end"]);

                var components = (JObject)industries["KG_Mine"]["components"];
                Assert.Equal(new[] { "KG-R1", "KG-R2" }, components["KG-coal-loader"]["trackSpanIds"].Values<string>());
                Assert.Equal(new[] { "KG-S1" }, components["KG-Supplies"]["trackSpanIds"].Values<string>());
                Assert.Equal(new[] { "KG-ER1" }, components["Freight Depot"]["trackSpanIds"].Values<string>());

                var definition = FuseSerializer.FromJson(root.ToString());
                Assert.Equal(6, definition.Tracks.Spans.Count);
                Assert.Equal(
                    new[] { "KG-R1", "KG-R2" },
                    definition.Operations.Industries["KG_Mine"].Components["KG-coal-loader"].TrackSpanIds);
            }
        }

        public class AreaNestedIndustry
        {
            [Fact]
            public void AreaNestedIndustry_GetsAreaIdFromParent()
            {
                // Industries authored under areas.{a}.industries.{id} carry
                // the area context implicitly. The converter passes the area
                // name as the areaId argument so the apply path can parent
                // the new industry to the correct Area transform.
                var source = new JObject
                {
                    ["areas"] = new JObject
                    {
                        ["whittier"] = new JObject
                        {
                            ["industries"] = new JObject
                            {
                                ["whittier-sawmill-r1"] = new JObject
                                {
                                    ["components"] = new JObject()
                                }
                            }
                        }
                    }
                };

                var (root, industries) = FuseLegacyIndustryConverterTests.ConvertIndustries(source);
                var entry = (JObject)industries["whittier-sawmill-r1"];
                Assert.Equal("whittier", (string)entry["areaId"]);

                var definition = FuseSerializer.FromJson(root.ToString());
                var industry = definition.Operations.Industries["whittier-sawmill-r1"];
                Assert.Equal("whittier", industry.AreaId);
            }
        }
    }
}
