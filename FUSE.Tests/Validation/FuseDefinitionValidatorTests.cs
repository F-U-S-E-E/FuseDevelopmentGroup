using System.Collections.Generic;
using System.Linq;
using FUSE.Authoring.Data;
using FUSE.Authoring.Validation;
using Xunit;

namespace FUSE.Tests.Validation
{
    public class FuseDefinitionValidatorTests
    {
        private static FuseDefinitionValidator NewValidator() => new FuseDefinitionValidator();

        private static FuseModDefinition MinimalValid() => new FuseModDefinition
        {
            Id = "pkg",
            Name = "Package",
            SchemaVersion = "1.0"
        };

        public class EntryPoint
        {
            [Fact]
            public void Null_ProducesSingleDefinitionRequiredError()
            {
                var result = NewValidator().Validate(null);

                Assert.False(result.IsValid);
                var error = Assert.Single(result.Errors);
                Assert.Equal("$", error.Field);
                Assert.Equal("fuse.definition.required", error.Code);
            }

            [Fact]
            public void EmptyDefinition_ReportsIdAndNameAsRequired()
            {
                var result = NewValidator().Validate(new FuseModDefinition());

                Assert.False(result.IsValid);
                Assert.Contains(result.Errors, e => e.Field == "id" && e.Code == "fuse.required");
                Assert.Contains(result.Errors, e => e.Field == "name" && e.Code == "fuse.required");
            }

            [Fact]
            public void MinimalValidDefinition_HasNoErrors()
            {
                var result = NewValidator().Validate(MinimalValid());

                Assert.True(result.IsValid);
            }
        }

        public class SchemaVersionRules
        {
            [Fact]
            public void CurrentVersion_NoSchemaError()
            {
                var result = NewValidator().Validate(MinimalValid());

                Assert.DoesNotContain(result.Errors, e => e.Field == "schemaVersion");
                Assert.DoesNotContain(result.Warnings, w => w.Field == "schemaVersion");
            }

            [Fact]
            public void FutureVersion_EmitsWarning()
            {
                var definition = MinimalValid();
                definition.SchemaVersion = "2.0";

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Warnings, w => w.Field == "schemaVersion" && w.Code == "fuse.schema.version.future");
                Assert.DoesNotContain(result.Errors, e => e.Field == "schemaVersion");
            }

            [Fact]
            public void OlderKnownVersion_EmitsError()
            {
                // 0.5 parses successfully but isn't current, and isn't future — must error.
                var definition = MinimalValid();
                definition.SchemaVersion = "0.5";

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Field == "schemaVersion" && e.Code == "fuse.schema.version");
            }

            [Fact]
            public void InvalidVersion_IsNormalizedToCurrent_NoError()
            {
                // FuseMigration.Normalize (called inside Validate) corrects invalid schema
                // strings to CurrentVersion silently — locking in that behavior so a stricter
                // rewrite is deliberate.
                var definition = MinimalValid();
                definition.SchemaVersion = "garbage";

                var result = NewValidator().Validate(definition);

                Assert.DoesNotContain(result.Errors, e => e.Field == "schemaVersion");
            }
        }

        public class MixintoRules
        {
            [Fact]
            public void NullMixinto_IsAllowed()
            {
                var definition = MinimalValid();
                definition.Mixinto = null;

                var result = NewValidator().Validate(definition);

                Assert.DoesNotContain(result.Warnings, w => w.Field.StartsWith("mixinto"));
            }

            [Fact]
            public void BlankTarget_EmitsWarning()
            {
                var definition = MinimalValid();
                definition.Mixinto = new FuseMixintoDefinition
                {
                    Target = null,
                    SourceFile = "src.json"
                };

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Warnings, w => w.Field == "mixinto.target" && w.Code == "fuse.mixinto.target.blank");
            }

            [Fact]
            public void BlankSourceFile_EmitsWarning()
            {
                var definition = MinimalValid();
                definition.Mixinto = new FuseMixintoDefinition
                {
                    Target = "game-graph",
                    SourceFile = null
                };

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Warnings, w => w.Field == "mixinto.sourceFile" && w.Code == "fuse.mixinto.sourceFile.blank");
            }

            [Fact]
            public void NullRequirementEntry_EmitsWarning()
            {
                var definition = MinimalValid();
                definition.Mixinto = new FuseMixintoDefinition
                {
                    Target = "game-graph",
                    SourceFile = "src.json",
                    Requires = new FuseModRequirement[] { null }
                };

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Warnings, w => w.Field == "mixinto.requires[0]" && w.Code == "fuse.mixinto.requirement.null");
            }

            [Fact]
            public void BlankRequirementId_EmitsWarning()
            {
                var definition = MinimalValid();
                definition.Mixinto = new FuseMixintoDefinition
                {
                    Target = "game-graph",
                    SourceFile = "src.json",
                    Requires = new[] { new FuseModRequirement { Id = "   " } }
                };

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Warnings, w => w.Field == "mixinto.requires[0].id" && w.Code == "fuse.mixinto.requirement.id.blank");
            }
        }

        public class SettingsRules
        {
            [Fact]
            public void BlankSettingKey_EmitsError()
            {
                var definition = MinimalValid();
                definition.Settings = new Dictionary<string, FuseModSettingDefinition>
                {
                    [""] = new FuseModSettingDefinition { Type = "text" }
                };

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Code == "fuse.settings.id.blank");
            }

            [Fact]
            public void NullSettingValue_EmitsWarning()
            {
                var definition = MinimalValid();
                definition.Settings = new Dictionary<string, FuseModSettingDefinition>
                {
                    ["my-setting"] = null
                };

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Warnings, w => w.Code == "fuse.settings.null");
            }

            [Fact]
            public void EnumWithNoValues_EmitsWarning()
            {
                var definition = MinimalValid();
                definition.Settings = new Dictionary<string, FuseModSettingDefinition>
                {
                    ["pick"] = new FuseModSettingDefinition { Type = "enum", Values = null }
                };

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Warnings, w => w.Code == "fuse.settings.enum.values.empty");
            }

            [Fact]
            public void NumberWithMinGreaterThanMax_EmitsError()
            {
                var definition = MinimalValid();
                definition.Settings = new Dictionary<string, FuseModSettingDefinition>
                {
                    ["count"] = new FuseModSettingDefinition { Type = "number", Min = 10, Max = 5 }
                };

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Code == "fuse.settings.number.range");
            }

            [Fact]
            public void NumberWithNonPositiveStep_EmitsWarning()
            {
                var definition = MinimalValid();
                definition.Settings = new Dictionary<string, FuseModSettingDefinition>
                {
                    ["count"] = new FuseModSettingDefinition { Type = "number", Step = 0 }
                };

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Warnings, w => w.Code == "fuse.settings.number.step");
            }
        }

        public class TrackRules
        {
            [Fact]
            public void NullSegment_EmitsError()
            {
                var definition = MinimalValid();
                definition.Tracks.Segments["s1"] = null;

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Code == "fuse.track.segment.required" && e.Field == "tracks.segments.s1");
            }

            [Fact]
            public void PartialSegmentWithoutEndpoints_EmitsError()
            {
                var definition = MinimalValid();
                definition.Tracks.Segments["s1"] = new FuseSegment
                {
                    Partial = true,
                    StartNodeId = null,
                    EndNodeId = null
                };

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Code == "fuse.track.segment.partialEndpoint.empty");
            }

            [Fact]
            public void PartialSegmentWithOneEndpoint_EmitsWarning()
            {
                var definition = MinimalValid();
                definition.Tracks.Segments["s1"] = new FuseSegment
                {
                    Partial = true,
                    StartNodeId = "n1",
                    EndNodeId = null
                };
                definition.Tracks.Nodes["n1"] = new FuseNode();

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Warnings, w => w.Code == "fuse.track.segment.partialEndpoint");
            }

            [Fact]
            public void NonPartialSegmentMissingEndpoint_EmitsRequiredError()
            {
                var definition = MinimalValid();
                definition.Tracks.Segments["s1"] = new FuseSegment
                {
                    Partial = false,
                    StartNodeId = null,
                    EndNodeId = "n2"
                };
                definition.Tracks.Nodes["n2"] = new FuseNode();

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Field == "tracks.segments.s1.startNodeId" && e.Code == "fuse.required");
            }

            [Fact]
            public void ExternalNodeReference_EmitsWarning()
            {
                // Segment endpoint references a node not in this document — valid (the node
                // is expected to exist in the base graph at runtime) but flagged.
                var definition = MinimalValid();
                definition.Tracks.Segments["s1"] = new FuseSegment
                {
                    StartNodeId = "external-node",
                    EndNodeId = "external-node-2"
                };

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Warnings, w => w.Code == "fuse.track.node.external" &&
                                                       w.Field == "tracks.segments.s1.startNodeId");
            }

            [Theory]
            [InlineData(-1)]
            [InlineData(81)]
            [InlineData(100)]
            public void SpeedLimitOutOfRange_EmitsError(int speedLimit)
            {
                var definition = MinimalValid();
                definition.Tracks.Nodes["a"] = new FuseNode();
                definition.Tracks.Nodes["b"] = new FuseNode();
                definition.Tracks.Segments["s1"] = new FuseSegment
                {
                    StartNodeId = "a",
                    EndNodeId = "b",
                    SpeedLimit = speedLimit
                };

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Code == "fuse.track.speedLimit");
            }

            [Theory]
            [InlineData(0)]
            [InlineData(45)]
            [InlineData(80)]
            public void SpeedLimitInRange_NoError(int speedLimit)
            {
                var definition = MinimalValid();
                definition.Tracks.Nodes["a"] = new FuseNode();
                definition.Tracks.Nodes["b"] = new FuseNode();
                definition.Tracks.Segments["s1"] = new FuseSegment
                {
                    StartNodeId = "a",
                    EndNodeId = "b",
                    SpeedLimit = speedLimit
                };

                var result = NewValidator().Validate(definition);

                Assert.DoesNotContain(result.Errors, e => e.Code == "fuse.track.speedLimit");
            }

            [Fact]
            public void NegativeAreaRadius_EmitsError()
            {
                var definition = MinimalValid();
                definition.Tracks.Areas["a1"] = new FuseArea { Radius = -1f };

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Code == "fuse.track.area.radius");
            }

            [Theory]
            [InlineData(2)]
            [InlineData(5)]
            public void AreaTagColorWithWrongLength_EmitsError(int length)
            {
                var definition = MinimalValid();
                definition.Tracks.Areas["a1"] = new FuseArea
                {
                    TagColor = Enumerable.Range(0, length).Select(i => (float)i).ToArray()
                };

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Code == "fuse.track.area.tagColor");
            }

            [Theory]
            [InlineData(3)]
            [InlineData(4)]
            public void AreaTagColorWith3Or4Values_NoError(int length)
            {
                var definition = MinimalValid();
                definition.Tracks.Areas["a1"] = new FuseArea
                {
                    TagColor = Enumerable.Range(0, length).Select(i => 0.5f).ToArray()
                };

                var result = NewValidator().Validate(definition);

                Assert.DoesNotContain(result.Errors, e => e.Code == "fuse.track.area.tagColor");
            }
        }
    }
}
