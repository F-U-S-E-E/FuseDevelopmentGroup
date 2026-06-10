using FUSE.Authoring.Data;
using Xunit;

namespace FUSE.Tests.Validation
{
    public partial class FuseDefinitionValidatorDeeperTests
    {
        /// <summary>
        /// Mirror of the FUSE.Core carTypeFilter tests for the shipping
        /// validator: the filter is matched verbatim per comma-separated
        /// token at runtime, so surrounding whitespace is an error and an
        /// empty token from a doubled/edge comma is a warning.
        /// </summary>
        public class CarTypeFilterRules
        {
            private const string MalformedCode = "fuse.operations.component.carTypeFilter.malformed";
            private const string EmptyTokenCode = "fuse.operations.component.carTypeFilter.emptyToken";

            private static FuseModDefinition WithComponentFilter(string filter)
            {
                var definition = MinimalValid();
                definition.Operations.Industries["mill"] = new FuseIndustry
                {
                    Name = "Mill",
                    Components =
                    {
                        ["dock"] = new FuseIndustryComponent { Partial = true, CarTypeFilter = filter },
                    },
                };
                return definition;
            }

            [Fact]
            public void Component_Filter_With_Surrounding_Whitespace_Is_An_Error()
            {
                var result = NewValidator().Validate(WithComponentFilter("FB, XM"));

                Assert.Contains(
                    result.Errors,
                    e => e.Code == MalformedCode && e.Field == "operations.industries.mill.components.dock.carTypeFilter");
            }

            [Fact]
            public void Component_Filter_With_Doubled_Comma_Is_A_Warning()
            {
                var result = NewValidator().Validate(WithComponentFilter("FB,,XM"));

                Assert.Contains(result.Warnings, w => w.Code == EmptyTokenCode);
                Assert.DoesNotContain(result.Errors, e => e.Code == MalformedCode);
            }

            [Theory]
            [InlineData("FB,XM")]
            [InlineData("*")]
            [InlineData("FB*")]
            [InlineData("")]
            [InlineData(null)]
            public void Component_Filter_Valid_Values_Stay_Silent(string filter)
            {
                var result = NewValidator().Validate(WithComponentFilter(filter));

                Assert.DoesNotContain(result.Errors, e => e.Code == MalformedCode);
                Assert.DoesNotContain(result.Warnings, w => w.Code == EmptyTokenCode);
            }

            [Fact]
            public void TeamProfile_Filter_Is_Validated()
            {
                var definition = MinimalValid();
                definition.Operations.Industries["mill"] = new FuseIndustry
                {
                    Name = "Mill",
                    Components =
                    {
                        ["team"] = new FuseIndustryComponent
                        {
                            Partial = true,
                            TeamProfiles =
                            {
                                ["lumber"] = new FuseTeamTrackEntry { LoadId = "lumber", CarTypeFilter = "FB, GB" },
                            },
                        },
                    },
                };

                var result = NewValidator().Validate(definition);

                Assert.Contains(
                    result.Errors,
                    e => e.Code == MalformedCode &&
                         e.Field == "operations.industries.mill.components.team.teamProfiles.lumber.carTypeFilter");
            }

            [Fact]
            public void Load_Filter_Is_Validated()
            {
                var definition = MinimalValid();
                definition.Operations.Loads["coal"] = new FuseLoad { Name = "Coal", CarTypeFilter = "HM ,HT" };

                var result = NewValidator().Validate(definition);

                Assert.Contains(
                    result.Errors,
                    e => e.Code == MalformedCode && e.Field == "operations.loads.coal.carTypeFilter");
            }

            [Fact]
            public void Delivery_Filter_Is_Validated()
            {
                var definition = MinimalValid();
                definition.Progression.Progressions["main"] = new FuseProgression
                {
                    Sections =
                    {
                        ["s1"] = new FuseSection
                        {
                            DisplayName = "Section 1",
                            DeliveryPhases = new[]
                            {
                                new FuseDeliveryPhase
                                {
                                    IndustryComponentId = "mill.dock",
                                    Deliveries = new[]
                                    {
                                        new FuseDelivery { Count = 1, CarTypeFilter = "FB, XM" },
                                    },
                                },
                            },
                        },
                    },
                };

                var result = NewValidator().Validate(definition);

                Assert.Contains(
                    result.Errors,
                    e => e.Code == MalformedCode &&
                         e.Field == "progression.progressions.main.sections.s1.deliveryPhases[0].deliveries[0].carTypeFilter");
            }
        }
    }
}
