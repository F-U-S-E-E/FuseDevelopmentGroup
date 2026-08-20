using FUSE.Authoring.Data;
using FUSE.Authoring.Validation;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FUSE.Tests.Validation
{
    public sealed class FuseFeatureRuleValidationTests
    {
        [Fact]
        public void ValidRule_PassesValidation()
        {
            var definition = ValidDefinition();

            var result = new FuseDefinitionValidator().Validate(definition);

            Assert.DoesNotContain(result.Errors, error => error.Field.StartsWith("featureRules."));
        }

        [Fact]
        public void MissingSetting_IsActionableError()
        {
            var definition = ValidDefinition();
            definition.FeatureRules["optional"].Setting = "missing";

            var result = new FuseDefinitionValidator().Validate(definition);

            Assert.Contains(result.Errors, error => error.Code == "fuse.featureRule.setting.missing");
        }

        [Fact]
        public void UnknownTarget_IsActionableError()
        {
            var definition = ValidDefinition();
            definition.FeatureRules["optional"].Targets.Scenery = new[] { "not-authored-here" };

            var result = new FuseDefinitionValidator().Validate(definition);

            Assert.Contains(result.Errors, error => error.Code == "fuse.featureRule.target.missing" && error.Field.Contains("scenery"));
        }

        [Fact]
        public void NumericOperator_OnBooleanSetting_IsRejected()
        {
            var definition = ValidDefinition();
            definition.FeatureRules["optional"].Operator = "greaterThan";

            var result = new FuseDefinitionValidator().Validate(definition);

            Assert.Contains(result.Errors, error => error.Code == "fuse.featureRule.operator.type");
        }

        [Fact]
        public void EmptyTargets_AreRejected()
        {
            var definition = ValidDefinition();
            definition.FeatureRules["optional"].Targets = new FuseFeatureTargets();

            var result = new FuseDefinitionValidator().Validate(definition);

            Assert.Contains(result.Errors, error => error.Code == "fuse.featureRule.targets.empty");
        }

        private static FuseModDefinition ValidDefinition()
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
            definition.World.Scenery["optional-scenery"] = new FuseScenery();
            definition.FeatureRules["optional"] = new FuseFeatureRule
            {
                Setting = "enabled",
                Value = new JValue(true),
                Targets = new FuseFeatureTargets { Scenery = new[] { "optional-scenery" } }
            };
            return definition;
        }
    }
}
