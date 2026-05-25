using FUSE.Authoring.Serialization;
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
