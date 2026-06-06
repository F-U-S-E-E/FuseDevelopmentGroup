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
            Assert.Null(LegacyDefinitionConverter.ConvertRequirement(JToken.Parse("\"FUSE\"")));
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
