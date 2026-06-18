using System.Collections.Generic;
using FUSE.Authoring.Data;
using FUSE.Authoring.Data.Common;
using FUSE.Authoring.Validation;
using UnityEngine;
using Xunit;

namespace FUSE.Tests.Validation
{
    public partial class FuseDefinitionValidatorDeeperTests
    {

        public class ProgressionRules
        {
            [Fact]
            public void Section_BlankId_EmitsRequiredError()
            {
                var definition = MinimalValid();
                definition.Progression.Sections = new[]
                {
                    new FuseSection { Id = null, DisplayName = "First" }
                };

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Field == "progression.sections[0].id" && e.Code == "fuse.required");
            }

            [Fact]
            public void DuplicateRootSectionIds_EmitError()
            {
                var definition = MinimalValid();
                definition.Progression.Sections = new[]
                {
                    new FuseSection { Id = "phase", DisplayName = "A" },
                    new FuseSection { Id = "phase", DisplayName = "B" }
                };

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Code == "fuse.progression.section.duplicate");
            }

            [Fact]
            public void NullProgressionDefinition_EmitsError()
            {
                var definition = MinimalValid();
                definition.Progression.Progressions["main"] = null;

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Code == "fuse.progression.required");
            }

            [Fact]
            public void NullMapFeature_EmitsError()
            {
                var definition = MinimalValid();
                definition.Progression.MapFeatures["feat"] = null;

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Code == "fuse.progression.mapFeature.required");
            }

            [Fact]
            public void MapFeature_BlankDisplayName_EmitsRequiredError()
            {
                var definition = MinimalValid();
                definition.Progression.MapFeatures["feat"] = new FuseMapFeature { DisplayName = null };

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Field == "progression.mapFeatures.feat.displayName" && e.Code == "fuse.required");
            }

            [Fact]
            public void DeliveryPhase_WithDeliveriesButNoIndustryComponent_AndNoDestination_EmitsError()
            {
                var definition = MinimalValid();
                definition.Progression.Progressions["main"] = new FuseProgression
                {
                    Sections = new Dictionary<string, FuseSection>
                    {
                        ["s1"] = new FuseSection
                        {
                            DisplayName = "S1",
                            DeliveryPhases = new[]
                            {
                                new FuseDeliveryPhase
                                {
                                    IndustryComponentId = null,
                                    Deliveries = new[] { new FuseDelivery { LoadId = "coal", Count = 1, DestinationIndustryId = null } }
                                }
                            }
                        }
                    }
                };

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Code == "fuse.progression.deliveryPhase.industryComponentId");
            }

            [Fact]
            public void Delivery_WithNonPositiveCount_EmitsError()
            {
                var definition = MinimalValid();
                definition.Progression.Progressions["main"] = new FuseProgression
                {
                    Sections = new Dictionary<string, FuseSection>
                    {
                        ["s1"] = new FuseSection
                        {
                            DisplayName = "S1",
                            DeliveryPhases = new[]
                            {
                                new FuseDeliveryPhase
                                {
                                    IndustryComponentId = "x",
                                    Deliveries = new[] { new FuseDelivery { LoadId = "coal", Count = 0 } }
                                }
                            }
                        }
                    }
                };

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Code == "fuse.progression.delivery.count");
            }

            [Fact]
            public void Delivery_WithUnknownDirection_EmitsError()
            {
                var definition = MinimalValid();
                definition.Progression.Progressions["main"] = new FuseProgression
                {
                    Sections = new Dictionary<string, FuseSection>
                    {
                        ["s1"] = new FuseSection
                        {
                            DisplayName = "S1",
                            DeliveryPhases = new[]
                            {
                                new FuseDeliveryPhase
                                {
                                    IndustryComponentId = "x",
                                    Deliveries = new[] { new FuseDelivery { LoadId = "coal", Count = 1, Direction = "sideways" } }
                                }
                            }
                        }
                    }
                };

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Code == "fuse.progression.delivery.direction");
            }

            [Theory]
            [InlineData("loadToIndustry")]
            [InlineData("loadFromIndustry")]
            [InlineData("import")]
            [InlineData("export")]
            [InlineData("to")]
            [InlineData("from")]
            public void Delivery_KnownDirectionAliases_NoError(string direction)
            {
                var definition = MinimalValid();
                definition.Progression.Progressions["main"] = new FuseProgression
                {
                    Sections = new Dictionary<string, FuseSection>
                    {
                        ["s1"] = new FuseSection
                        {
                            DisplayName = "S1",
                            DeliveryPhases = new[]
                            {
                                new FuseDeliveryPhase
                                {
                                    IndustryComponentId = "x",
                                    Deliveries = new[] { new FuseDelivery { LoadId = "coal", Count = 1, Direction = direction } }
                                }
                            }
                        }
                    }
                };

                var result = NewValidator().Validate(definition);

                Assert.DoesNotContain(result.Errors, e => e.Code == "fuse.progression.delivery.direction");
            }
        }
    }
}
