using System.Collections.Generic;
using FUSE.Authoring.Data;
using FUSE.Authoring.Serialization;
using FUSE.Loading;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FUSE.Tests.Loading
{
    public sealed class FuseLegacyStartOptionRegistryTests
    {
        [Fact]
        public void MergeEnabledFeaturesIntoProgressions_PromotesLegacyStartList()
        {
            var progressionDefinition = new FuseModDefinition
            {
                Id = "test.progressions",
            };
            progressionDefinition.Progression.Progressions["appa"] =
                new FuseProgression
                {
                    BaseProgression = "ewh",
                    EnableFeaturesAtStart = FuseStringPatch.FromPatch(
                        new Dictionary<string, bool>
                        {
                            ["existing"] = true,
                        }),
                };
            var startDefinition = new FuseModDefinition
            {
                Id = "test.start",
            };
            startDefinition.Extensions["legacyStartOption"] = JObject.Parse(@"{
                ""identifier"": ""test-start"",
                ""name"": ""Test Start"",
                ""progressionId"": ""appa"",
                ""enabledFeatures"": [""start-track"", ""start-yard""]
            }");

            FuseLegacyStartOptionRegistry.MergeEnabledFeaturesIntoProgressions(
                new[]
                {
                    new FuseLoadedMod("", "", progressionDefinition),
                    new FuseLoadedMod("", "", startDefinition),
                });

            var patch = progressionDefinition.Progression
                .Progressions["appa"]
                .EnableFeaturesAtStart;
            Assert.True(patch.Patch["existing"]);
            Assert.True(patch.Patch["start-track"]);
            Assert.True(patch.Patch["start-yard"]);
        }
    }
}
