using System.Collections.Generic;
using FUSE.Converter.Conversion;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FUSE.Tests.Converter
{
    public sealed class LegacyLoadHelpersTests
    {
        [Fact]
        public void CollectLoadReferences_picks_up_loadId_convertedLoadId_load_keys()
        {
            var doc = JObject.Parse(@"{
                ""industries"": {
                    ""ind-a"": {
                        ""components"": {
                            ""x"": { ""loadId"": ""oil"" },
                            ""y"": { ""convertedLoadId"": ""gasoline"" },
                            ""z"": { ""load"": ""coal"" }
                        }
                    }
                }
            }");

            var sink = new HashSet<string>();
            LegacyLoadHelpers.CollectLoadReferences(doc, sink);

            Assert.Contains("oil", sink);
            Assert.Contains("gasoline", sink);
            Assert.Contains("coal", sink);
        }

        [Fact]
        public void CollectLoadReferences_skips_non_string_values()
        {
            var doc = JObject.Parse("{ \"loadId\": 42 }");
            var sink = new HashSet<string>();
            LegacyLoadHelpers.CollectLoadReferences(doc, sink);
            Assert.Empty(sink);
        }

        [Fact]
        public void CollectLoadReferences_ignores_property_names_that_arent_load_ids()
        {
            var doc = JObject.Parse("{ \"otherId\": \"abc\", \"load\": \"oil\" }");
            var sink = new HashSet<string>();
            LegacyLoadHelpers.CollectLoadReferences(doc, sink);
            Assert.Single(sink);
            Assert.Contains("oil", sink);
        }

        [Fact]
        public void CollectLoadReferences_walks_arrays()
        {
            var doc = JArray.Parse("[ { \"loadId\": \"a\" }, { \"loadId\": \"b\" } ]");
            var sink = new HashSet<string>();
            LegacyLoadHelpers.CollectLoadReferences(doc, sink);
            Assert.Equal(2, sink.Count);
        }

        [Fact]
        public void EnsureKnownCompatLoads_injects_referenced_compat_loads()
        {
            // machine-parts is in KnownCompatLoads. It's referenced by
            // an industry component but never defined → the helper
            // should inject the definition.
            var rail = JObject.Parse(@"{
                ""operations"": {
                    ""loads"": {},
                    ""industries"": {
                        ""ind-a"": {
                            ""components"": {
                                ""x"": { ""loadId"": ""machine-parts"" }
                            }
                        }
                    }
                },
                ""progression"": {}
            }");

            LegacyLoadHelpers.EnsureKnownCompatLoads(rail);

            var loads = (JObject)((JObject)rail["operations"])["loads"];
            Assert.True(loads.ContainsKey("machine-parts"));
            Assert.Equal("Machine Parts", loads["machine-parts"].Value<string>("name"));
        }

        [Fact]
        public void EnsureKnownCompatLoads_does_not_overwrite_defined_loads()
        {
            var rail = JObject.Parse(@"{
                ""operations"": {
                    ""loads"": {
                        ""machine-parts"": { ""name"": ""CUSTOM"" }
                    },
                    ""industries"": {
                        ""ind-a"": { ""components"": { ""x"": { ""loadId"": ""machine-parts"" } } }
                    }
                },
                ""progression"": {}
            }");

            LegacyLoadHelpers.EnsureKnownCompatLoads(rail);

            var loads = (JObject)((JObject)rail["operations"])["loads"];
            Assert.Equal("CUSTOM", loads["machine-parts"].Value<string>("name"));
        }

        [Fact]
        public void EnsureKnownCompatLoads_ignores_unknown_load_ids()
        {
            var rail = JObject.Parse(@"{
                ""operations"": {
                    ""loads"": {},
                    ""industries"": {
                        ""ind"": { ""components"": { ""x"": { ""loadId"": ""not-a-compat-load"" } } }
                    }
                },
                ""progression"": {}
            }");

            LegacyLoadHelpers.EnsureKnownCompatLoads(rail);

            var loads = (JObject)((JObject)rail["operations"])["loads"];
            Assert.Empty(loads);
        }
    }
}
