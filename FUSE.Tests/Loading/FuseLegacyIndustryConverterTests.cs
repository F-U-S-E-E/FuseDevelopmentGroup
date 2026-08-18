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
