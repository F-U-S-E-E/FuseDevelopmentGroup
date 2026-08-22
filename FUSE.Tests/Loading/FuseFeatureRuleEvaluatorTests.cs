using System.Collections.Generic;
using FUSE.Authoring.Data;
using FUSE.Loading;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FUSE.Tests.Loading
{
    public sealed class FuseFeatureRuleEvaluatorTests
    {
        [Fact]
        public void FalseBooleanRule_RemovesEveryNamedTargetAndKeepsUntargetedObjects()
        {
            var definition = DefinitionWithOptionalObjects();

            var result = FuseFeatureRuleEvaluator.Apply(
                definition,
                (_, __) => new JValue(false));

            Assert.Equal(1, result.DisabledRuleCount);
            Assert.Equal(4, result.RemovedObjectCount);
            Assert.DoesNotContain("optional-node", definition.Tracks.Nodes.Keys);
            Assert.DoesNotContain("optional-segment", definition.Tracks.Segments.Keys);
            Assert.DoesNotContain("optional-scenery", definition.World.Scenery.Keys);
            Assert.DoesNotContain("optional-component", definition.Operations.Industries["yard"].Components.Keys);
            Assert.Contains("always-scenery", definition.World.Scenery.Keys);
        }

        [Fact]
        public void TrueBooleanRule_KeepsTargets()
        {
            var definition = DefinitionWithOptionalObjects();

            var result = FuseFeatureRuleEvaluator.Apply(
                definition,
                (_, __) => new JValue(true));

            Assert.Equal(1, result.EnabledRuleCount);
            Assert.Equal(0, result.RemovedObjectCount);
            Assert.Contains("optional-node", definition.Tracks.Nodes.Keys);
            Assert.Contains("optional-scenery", definition.World.Scenery.Keys);
        }

        [Theory]
        [InlineData("high", "equals", "high", true)]
        [InlineData("low", "notEquals", "high", true)]
        [InlineData(4, "greaterThanOrEqual", 4, true)]
        [InlineData(3, "lessThan", 4, true)]
        [InlineData(5, "lessThanOrEqual", 4, false)]
        public void Matches_SupportsChoiceAndNumericOperators(object current, string operation, object expected, bool matches)
        {
            Assert.Equal(matches, FuseFeatureRuleEvaluator.Matches(JToken.FromObject(current), operation, JToken.FromObject(expected)));
        }

        [Fact]
        public void MultipleFalseRules_TargetingSameObject_CountRemovalOnce()
        {
            var definition = DefinitionWithOptionalObjects();
            definition.FeatureRules["second"] = new FuseFeatureRule
            {
                Setting = "enabled",
                Value = new JValue(true),
                Targets = new FuseFeatureTargets { Scenery = new[] { "optional-scenery" } }
            };

            var result = FuseFeatureRuleEvaluator.Apply(
                definition,
                (_, __) => new JValue(false));

            Assert.Equal(2, result.DisabledRuleCount);
            Assert.Equal(4, result.RemovedObjectCount);
        }

        [Fact]
        public void NullSettingsDictionary_IsPassedToResolverAsAnUndefinedSetting()
        {
            var definition = DefinitionWithOptionalObjects();
            definition.Settings = null;
            FuseModSettingDefinition resolvedSetting = new FuseModSettingDefinition();

            var result = FuseFeatureRuleEvaluator.Apply(
                definition,
                (_, setting) =>
                {
                    resolvedSetting = setting;
                    return new JValue(true);
                });

            Assert.Null(resolvedSetting);
            Assert.Equal(1, result.EnabledRuleCount);
            Assert.Contains("optional-scenery", definition.World.Scenery.Keys);
        }

        private static FuseModDefinition DefinitionWithOptionalObjects()
        {
            var definition = new FuseModDefinition
            {
                Id = "feature-test",
                Name = "Feature Test"
            };
            definition.Settings["enabled"] = new FuseModSettingDefinition
            {
                Type = "bool",
                Default = new JValue(true),
                ReloadRequired = true
            };
            definition.Tracks.Nodes["optional-node"] = new FuseNode();
            definition.Tracks.Segments["optional-segment"] = new FuseSegment();
            definition.World.Scenery["optional-scenery"] = new FuseScenery();
            definition.World.Scenery["always-scenery"] = new FuseScenery();
            definition.Operations.Industries["yard"] = new FuseIndustry
            {
                Components = new Dictionary<string, FuseIndustryComponent>
                {
                    ["optional-component"] = new FuseIndustryComponent()
                }
            };
            definition.FeatureRules["optional"] = new FuseFeatureRule
            {
                Setting = "enabled",
                Value = new JValue(true),
                Targets = new FuseFeatureTargets
                {
                    TrackNodes = new[] { "optional-node" },
                    TrackSegments = new[] { "optional-segment" },
                    Scenery = new[] { "optional-scenery" },
                    IndustryComponents = new[] { "yard/optional-component" }
                }
            };
            return definition;
        }
    }
}
