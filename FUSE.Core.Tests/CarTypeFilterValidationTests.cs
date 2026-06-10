using System.Linq;
using Fuse.Core.Model;
using Fuse.Core.Validation;
using Xunit;

namespace Fuse.Core.Tests
{
    /// <summary>
    /// Exercises the carTypeFilter rule at all four model sites. The filter
    /// is matched verbatim per comma-separated token at runtime, so
    /// surrounding whitespace is an error (the token can never match) and an
    /// empty token from a doubled/edge comma is a warning (the game ignores
    /// it).
    /// </summary>
    public class CarTypeFilterValidationTests
    {
        private const string MalformedCode = "fuse.operations.component.carTypeFilter.malformed";
        private const string EmptyTokenCode = "fuse.operations.component.carTypeFilter.emptyToken";

        private static FuseModDefinition Minimal() => new FuseModDefinition { Id = "pkg", Name = "Package" };

        private static ValidationResult Validate(FuseModDefinition definition) =>
            new FuseDefinitionValidator().Validate(definition);

        private static FuseModDefinition WithComponentFilter(string? filter)
        {
            var definition = Minimal();
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
            var result = Validate(WithComponentFilter("FB, XM"));

            var issue = Assert.Single(result.Errors, e => e.Code == MalformedCode);
            Assert.Equal("operations.industries.mill.components.dock.carTypeFilter", issue.Field);
            Assert.Equal("FB, XM", issue.Value);
        }

        [Fact]
        public void Component_Filter_With_Doubled_Comma_Is_A_Warning()
        {
            var result = Validate(WithComponentFilter("FB,,XM"));

            var issue = Assert.Single(result.Warnings, w => w.Code == EmptyTokenCode);
            Assert.Equal("operations.industries.mill.components.dock.carTypeFilter", issue.Field);
            Assert.DoesNotContain(result.Errors, e => e.Code == MalformedCode);
        }

        [Theory]
        [InlineData("FB,XM")]
        [InlineData("*")]
        [InlineData("FB*")]
        [InlineData("")]
        [InlineData(null)]
        public void Component_Filter_Valid_Values_Stay_Silent(string? filter)
        {
            var result = Validate(WithComponentFilter(filter));

            Assert.DoesNotContain(result.Errors, e => e.Code == MalformedCode);
            Assert.DoesNotContain(result.Warnings, w => w.Code == EmptyTokenCode);
        }

        [Fact]
        public void Trailing_Comma_Is_A_Warning_Not_An_Error()
        {
            var result = Validate(WithComponentFilter("FB,XM,"));

            Assert.Contains(result.Warnings, w => w.Code == EmptyTokenCode);
            Assert.DoesNotContain(result.Errors, e => e.Code == MalformedCode);
        }

        [Fact]
        public void Whitespace_Padded_Wildcard_Is_An_Error()
        {
            // " * " is not the bare "any" wildcard: the runtime keeps the
            // padding and the token can never match.
            var result = Validate(WithComponentFilter(" * "));

            Assert.Contains(result.Errors, e => e.Code == MalformedCode);
        }

        [Fact]
        public void TeamProfile_Filter_Is_Validated()
        {
            var definition = Minimal();
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

            var result = Validate(definition);

            var issue = Assert.Single(result.Errors, e => e.Code == MalformedCode);
            Assert.Equal("operations.industries.mill.components.team.teamProfiles.lumber.carTypeFilter", issue.Field);
        }

        [Fact]
        public void Load_Filter_Is_Validated()
        {
            var definition = Minimal();
            definition.Operations.Loads["coal"] = new FuseLoad { Name = "Coal", CarTypeFilter = "HM ,HT" };

            var result = Validate(definition);

            var issue = Assert.Single(result.Errors, e => e.Code == MalformedCode);
            Assert.Equal("operations.loads.coal.carTypeFilter", issue.Field);
        }

        [Fact]
        public void Delivery_Filter_Is_Validated()
        {
            var definition = Minimal();
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

            var result = Validate(definition);

            var issue = Assert.Single(result.Errors, e => e.Code == MalformedCode);
            Assert.Equal(
                "progression.progressions.main.sections.s1.deliveryPhases[0].deliveries[0].carTypeFilter",
                issue.Field);
        }

        [Fact]
        public void Multiple_Bad_Tokens_Flag_Each_Token()
        {
            var result = Validate(WithComponentFilter(" FB , XM "));

            Assert.Equal(2, result.Errors.Count(e => e.Code == MalformedCode));
        }
    }
}
