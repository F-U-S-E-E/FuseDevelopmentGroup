using System;
using System.IO;
using System.Linq;
using FUSE.Converter.Conversion;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FUSE.Tests.Converter
{
    /// <summary>
    /// Coverage for the Definition.json metadata converters —
    /// requirements, file references, load-after chain, mixinto
    /// table.
    /// </summary>
    public sealed class LegacyDefinitionConverterTests
    {
        // ------------------------------------------------------------------
        // ExtractFileReference
        // ------------------------------------------------------------------

        [Theory]
        [InlineData("file(MyData.json)", "MyData.json")]
        [InlineData("file(\"path/to/file.json\")", "path/to/file.json")]
        [InlineData("  file('quoted.json')  ", "quoted.json")]
        public void ExtractFileReference_strips_wrapping_and_quotes(string input, string expected)
        {
            Assert.Equal(expected, LegacyDefinitionConverter.ExtractFileReference(input));
        }

        [Fact]
        public void ExtractFileReference_returns_empty_for_non_match()
        {
            Assert.Equal("", LegacyDefinitionConverter.ExtractFileReference("not a file reference"));
            Assert.Equal("", LegacyDefinitionConverter.ExtractFileReference(null));
        }

        // ------------------------------------------------------------------
        // ConvertRequirement
        // ------------------------------------------------------------------

        [Fact]
        public void ConvertRequirement_keeps_non_core_string_id()
        {
            var result = LegacyDefinitionConverter.ConvertRequirement(JToken.Parse("\"SomeMod\""));
            Assert.Equal("SomeMod", result.Value<string>("id"));
        }

        [Fact]
        public void ConvertRequirement_drops_core_legacy_ids()
        {
            Assert.Null(LegacyDefinitionConverter.ConvertRequirement(JToken.Parse("\"Railroader\"")));
            Assert.Null(LegacyDefinitionConverter.ConvertRequirement(JToken.Parse("\"StrangeCustoms\"")));
            Assert.Null(LegacyDefinitionConverter.ConvertRequirement(JToken.Parse("\"AlinaNova21.AlinasMapMod\"")));
            Assert.Null(LegacyDefinitionConverter.ConvertRequirement(JToken.Parse("\"Railloader.Interchange\"")));
            Assert.Null(LegacyDefinitionConverter.ConvertRequirement(JToken.Parse("\"AssetLoader\"")));
            Assert.Null(LegacyDefinitionConverter.ConvertRequirement(JToken.Parse("\"FUSE\"")));
        }

        [Fact]
        public void ConvertRequirement_keeps_alina_content_pack_as_real_dependency()
        {
            var converted = LegacyDefinitionConverter.ConvertRequirement(
                JToken.Parse("\"AlinaNova21.AlinasMapExpansionSW\""));

            Assert.Equal("AlinaNova21.AlinasMapExpansionSW", converted.Value<string>("id"));
        }

        [Fact]
        public void ConvertRequirement_keeps_zamu_mod_without_native_parity_as_real_dependency()
        {
            var converted = LegacyDefinitionConverter.ConvertRequirement(
                JToken.Parse("\"Zamu.SomeKindOfMadness\""));

            Assert.Equal("Zamu.SomeKindOfMadness", converted.Value<string>("id"));
        }

        [Fact]
        public void ConvertRequirement_extracts_notBefore_notAfter_from_object()
        {
            var result = LegacyDefinitionConverter.ConvertRequirement(JToken.Parse(@"{
                ""id"": ""SomeMod"",
                ""notBefore"": ""1.0.0"",
                ""notAfter"": ""2.0.0""
            }"));
            Assert.Equal("SomeMod", result.Value<string>("id"));
            Assert.Equal("1.0.0", result.Value<string>("notBefore"));
            Assert.Equal("2.0.0", result.Value<string>("notAfter"));
        }

        [Fact]
        public void ConvertRequirement_returns_null_for_object_without_id()
        {
            Assert.Null(LegacyDefinitionConverter.ConvertRequirement(JToken.Parse("{ \"notBefore\": \"1.0\" }")));
        }

        // ------------------------------------------------------------------
        // ConvertRequirements (list form)
        // ------------------------------------------------------------------

        [Fact]
        public void ConvertRequirements_filters_core_and_packs_rest()
        {
            var result = LegacyDefinitionConverter.ConvertRequirements(
                JArray.Parse("[ \"FUSE\", \"ModA\", { \"id\": \"ModB\" }, \"Railroader\" ]"));
            Assert.Equal(2, result.Count);
            Assert.Contains(result, t => t.Value<string>("id") == "ModA");
            Assert.Contains(result, t => t.Value<string>("id") == "ModB");
        }

        [Fact]
        public void ConvertRequirements_returns_empty_when_not_array()
        {
            Assert.Empty(LegacyDefinitionConverter.ConvertRequirements(JToken.Parse("\"single string\"")));
        }

        // ------------------------------------------------------------------
        // FusePackageId
        // ------------------------------------------------------------------

        [Theory]
        [InlineData("SomeMod", "SomeMod.FUSE")]
        [InlineData("Already.FUSE", "Already.FUSE")]
        [InlineData("ALREADY.FUSE", "ALREADY.FUSE")]
        public void FusePackageId_appends_suffix_unless_already_present(string input, string expected)
        {
            Assert.Equal(expected, LegacyDefinitionConverter.FusePackageId(input));
        }

        [Fact]
        public void FusePackageId_returns_null_for_core_ids()
        {
            Assert.Null(LegacyDefinitionConverter.FusePackageId("Railroader"));
            Assert.Null(LegacyDefinitionConverter.FusePackageId(""));
            Assert.Null(LegacyDefinitionConverter.FusePackageId(null));
        }

        // ------------------------------------------------------------------
        // LegacyLoadAfter — uses Definition.json on disk
        // ------------------------------------------------------------------

        [Fact]
        public void LegacyLoadAfter_returns_empty_when_no_definition()
        {
            var temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            try
            {
                Directory.CreateDirectory(temp);
                Assert.Empty(LegacyDefinitionConverter.LegacyLoadAfter(temp));
            }
            finally
            {
                try { Directory.Delete(temp, true); } catch { }
            }
        }

        [Fact]
        public void LegacyLoadAfter_walks_requires_plus_loadAfter_filtering_cores()
        {
            var temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            try
            {
                Directory.CreateDirectory(temp);
                File.WriteAllText(Path.Combine(temp, "Definition.json"), @"{
                    ""requires"": [ ""FUSE"", ""ModA"", ""StrangeCustoms"" ],
                    ""loadAfter"": [ ""ModB"" ]
                }");

                var result = LegacyDefinitionConverter.LegacyLoadAfter(temp);
                Assert.Equal(new[] { "ModA.FUSE", "ModB.FUSE" }, result);
            }
            finally
            {
                try { Directory.Delete(temp, true); } catch { }
            }
        }

        [Fact]
        public void LegacyLoadAfter_dedupes_case_insensitively()
        {
            var temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            try
            {
                Directory.CreateDirectory(temp);
                File.WriteAllText(Path.Combine(temp, "Definition.json"), @"{
                    ""requires"": [ ""ModA"" ],
                    ""loadAfter"": [ ""modA"" ]
                }");
                var result = LegacyDefinitionConverter.LegacyLoadAfter(temp);
                Assert.Single(result);
            }
            finally
            {
                try { Directory.Delete(temp, true); } catch { }
            }
        }

        [Fact]
        public void LegacyDependencies_preserves_hard_requirements_separately_from_ordering()
        {
            var temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            try
            {
                Directory.CreateDirectory(temp);
                File.WriteAllText(Path.Combine(temp, "Definition.json"), @"{
                    ""requires"": [ { ""id"": ""RequiredMod"", ""notBefore"": ""2.0"" } ],
                    ""loadAfter"": [ { ""id"": ""OptionalOrderingMod"" } ]
                }");

                var result = LegacyDefinitionConverter.LegacyDependencies(temp);

                Assert.Equal(new[] { "RequiredMod.FUSE" }, result.Requires);
                Assert.Equal(new[] { "OptionalOrderingMod.FUSE" }, result.LoadAfter);
            }
            finally
            {
                try { Directory.Delete(temp, true); } catch { }
            }
        }

        [Fact]
        public void LegacyDependencies_orders_after_conditional_mixinto_requirements_without_making_them_hard()
        {
            var temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            try
            {
                Directory.CreateDirectory(temp);
                File.WriteAllText(Path.Combine(temp, "Definition.json"), @"{
                    ""mixintos"": {
                        ""game-graph"": {
                            ""mixinto"": ""file(optional-track.json)"",
                            ""requires"": [ ""OptionalBase"" ]
                        }
                    }
                }");

                var result = LegacyDefinitionConverter.LegacyDependencies(temp);

                Assert.Empty(result.Requires);
                Assert.Equal(new[] { "OptionalBase.FUSE" }, result.LoadAfter);
            }
            finally
            {
                try { Directory.Delete(temp, true); } catch { }
            }
        }

        [Fact]
        public void LegacyConflictsWith_preserves_core_ids_and_version_bounds_for_manifest_enforcement()
        {
            var temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            try
            {
                Directory.CreateDirectory(temp);
                File.WriteAllText(Path.Combine(temp, "Definition.json"), @"{
                    ""conflictsWith"": [
                        { ""id"": ""Other.Route"", ""notBefore"": ""2.0"", ""notAfter"": ""3.0"" },
                        ""Zamu.StrangeCustoms""
                    ]
                }");

                var result = LegacyDefinitionConverter.LegacyConflictsWith(temp);

                Assert.Equal(2, result.Count);
                Assert.Equal("Other.Route", result[0].Value<string>("Id"));
                Assert.Equal("2.0", result[0].Value<string>("NotBefore"));
                Assert.Equal("3.0", result[0].Value<string>("NotAfter"));
                Assert.Equal("Zamu.StrangeCustoms", result[1].Value<string>("Id"));
            }
            finally
            {
                try { Directory.Delete(temp, true); } catch { }
            }
        }

        // ------------------------------------------------------------------
        // MixintoMetadata
        // ------------------------------------------------------------------

        [Fact]
        public void MixintoMetadata_returns_empty_when_no_definition()
        {
            var temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            try
            {
                Directory.CreateDirectory(temp);
                var (metadata, ordered) = LegacyDefinitionConverter.MixintoMetadata(temp);
                Assert.Empty(metadata);
                Assert.Empty(ordered);
            }
            finally
            {
                try { Directory.Delete(temp, true); } catch { }
            }
        }

        [Fact]
        public void MixintoMetadata_extracts_target_and_source_file_from_string_mixinto()
        {
            var temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            try
            {
                Directory.CreateDirectory(temp);
                File.WriteAllText(Path.Combine(temp, "Definition.json"), @"{
                    ""mixintos"": {
                        ""SomeTarget"": ""file(my-data.json)""
                    }
                }");

                var (metadata, ordered) = LegacyDefinitionConverter.MixintoMetadata(temp);
                Assert.Contains("my-data.json", ordered);
                Assert.Equal("my-data.json", metadata["my-data.json"].Value<string>("sourceFile"));
                Assert.Equal("SomeTarget", metadata["my-data.json"].Value<string>("target"));
            }
            finally
            {
                try { Directory.Delete(temp, true); } catch { }
            }
        }

        [Fact]
        public void MixintoMetadata_handles_object_form_with_requires()
        {
            var temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            try
            {
                Directory.CreateDirectory(temp);
                File.WriteAllText(Path.Combine(temp, "Definition.json"), @"{
                    ""mixintos"": {
                        ""SomeTarget"": {
                            ""mixinto"": ""file(my-data.json)"",
                            ""requires"": [ ""DependencyMod"" ]
                        }
                    }
                }");

                var (metadata, _) = LegacyDefinitionConverter.MixintoMetadata(temp);
                var entry = metadata["my-data.json"];
                Assert.Equal("SomeTarget", entry.Value<string>("target"));
                var requires = entry["requires"] as JArray;
                Assert.NotNull(requires);
                Assert.Single(requires);
                Assert.Equal("DependencyMod", ((JObject)requires[0]).Value<string>("id"));
            }
            finally
            {
                try { Directory.Delete(temp, true); } catch { }
            }
        }

        [Fact]
        public void MixintoMetadata_preserves_conditional_conflictsWith()
        {
            var temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            try
            {
                Directory.CreateDirectory(temp);
                File.WriteAllText(Path.Combine(temp, "Definition.json"), @"{
                    ""mixintos"": {
                        ""game-graph"": {
                            ""mixinto"": ""file(optional.json)"",
                            ""conflictsWith"": [ { ""id"": ""Other.Route"", ""notBefore"": ""2.0"" } ]
                        }
                    }
                }");

                var (metadata, _) = LegacyDefinitionConverter.MixintoMetadata(temp);
                var conflict = Assert.Single((JArray)metadata["optional.json"]["conflictsWith"]);
                Assert.Equal("Other.Route", conflict.Value<string>("id"));
                Assert.Equal("2.0", conflict.Value<string>("notBefore"));
            }
            finally
            {
                try { Directory.Delete(temp, true); } catch { }
            }
        }

        [Fact]
        public void MixintoMetadata_handles_array_of_references()
        {
            var temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            try
            {
                Directory.CreateDirectory(temp);
                File.WriteAllText(Path.Combine(temp, "Definition.json"), @"{
                    ""mixintos"": {
                        ""SomeTarget"": [
                            ""file(a.json)"",
                            ""file(b.json)""
                        ]
                    }
                }");

                var (metadata, ordered) = LegacyDefinitionConverter.MixintoMetadata(temp);
                Assert.Equal(2, ordered.Count);
                Assert.True(metadata.ContainsKey("a.json"));
                Assert.True(metadata.ContainsKey("b.json"));
            }
            finally
            {
                try { Directory.Delete(temp, true); } catch { }
            }
        }
    }
}
