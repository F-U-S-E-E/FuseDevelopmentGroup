using System;
using System.IO;
using System.Linq;
using Fuse.Core.Model;
using Fuse.Core.Serialization;
using Fuse.Core.Validation;
using Xunit;

namespace Fuse.Core.Tests
{
    /// <summary>
    /// Exercises the Unity-free <see cref="FuseDefinitionValidator"/> ported into
    /// FUSE.Core. Note: the canonical example is <em>schema</em>-valid but is not
    /// expected to be <em>validator</em>-clean — it intentionally includes edge
    /// cases (e.g. a teleportLoading component without input/output spans) that
    /// the semantic validator flags, exactly as the shipping validator would.
    /// </summary>
    public class ValidationTests
    {
        private static string ExamplePath =>
            Path.Combine(AppContext.BaseDirectory, "fuse-mod.example.json");

        [Fact]
        public void Minimal_Definition_With_Id_And_Name_IsValid()
        {
            var definition = new FuseModDefinition { Id = "fuse.test.minimal", Name = "Minimal" };

            var result = new FuseDefinitionValidator().Validate(definition);

            Assert.True(
                result.IsValid,
                "A minimal id+name definition should validate clean. Errors: " +
                string.Join("; ", result.Errors.Select(e => $"{e.Field} [{e.Code}]")));
        }

        [Fact]
        public void Missing_Id_And_Name_Produces_Errors()
        {
            var definition = new FuseModDefinition { Id = null, Name = null };

            var result = new FuseDefinitionValidator().Validate(definition);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Field == "id");
            Assert.Contains(result.Errors, e => e.Field == "name");
        }

        [Fact]
        public void Example_Validation_Runs_And_Is_Deterministic()
        {
            var json = File.ReadAllText(ExamplePath);
            var validator = new FuseDefinitionValidator();

            var first = validator.Validate(FuseCoreSerializer.FromJson(json));
            var second = validator.Validate(FuseCoreSerializer.FromJson(json));

            // The validator must run over the full, rich example without throwing
            // and be deterministic (no leaked state between runs).
            Assert.NotNull(first);
            Assert.Equal(first.Errors.Count, second.Errors.Count);
            Assert.Equal(first.Warnings.Count, second.Warnings.Count);
        }

        [Fact]
        public void Explicitly_Null_Sections_Are_Normalized_Before_Feature_Rule_Validation()
        {
            var json = @"{
                ""id"": ""fuse.test.null-sections"",
                ""name"": ""Null Sections"",
                ""settings"": null,
                ""operations"": null,
                ""tracks"": null,
                ""world"": null,
                ""progression"": null,
                ""audio"": null,
                ""featureRules"": {
                    ""optional"": {
                        ""setting"": ""missing"",
                        ""value"": true,
                        ""targets"": { ""scenery"": [""missing""] }
                    }
                }
            }";

            var result = new FuseDefinitionValidator().Validate(FuseCoreSerializer.FromJson(json));

            Assert.Contains(result.Errors, error => error.Code == "fuse.featureRule.setting.missing");
            Assert.Contains(result.Errors, error => error.Code == "fuse.featureRule.target.missing");
        }
    }
}
