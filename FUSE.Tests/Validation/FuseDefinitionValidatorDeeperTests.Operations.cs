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

        public class OperationsLoadRules
        {
            [Theory]
            [InlineData("Bananas")]
            [InlineData("kg")]
            public void Load_WithUnknownUnits_EmitsError(string units)
            {
                var definition = MinimalValid();
                definition.Operations.Loads["coal"] = new FuseLoad { Name = "Coal", Units = units };

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Code == "fuse.operations.loads.units");
            }

            [Theory]
            [InlineData("pounds")]
            [InlineData("Pounds")]
            [InlineData("GALLONS")]
            [InlineData("Quantity")]
            public void Load_WithKnownUnits_NoError(string units)
            {
                var definition = MinimalValid();
                definition.Operations.Loads["coal"] = new FuseLoad { Name = "Coal", Units = units };

                var result = NewValidator().Validate(definition);

                Assert.DoesNotContain(result.Errors, e => e.Code == "fuse.operations.loads.units");
            }

            [Fact]
            public void Load_NegativeDensity_EmitsError()
            {
                var definition = MinimalValid();
                definition.Operations.Loads["coal"] = new FuseLoad { Name = "Coal", Density = -0.1f };

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Code == "fuse.operations.loads.density");
            }

            [Fact]
            public void Load_NegativeUnitWeight_EmitsError()
            {
                var definition = MinimalValid();
                definition.Operations.Loads["coal"] = new FuseLoad { Name = "Coal", UnitWeightInPounds = -1f };

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Code == "fuse.operations.loads.unitWeightInPounds");
            }

            [Fact]
            public void Load_BlankName_EmitsRequiredError()
            {
                var definition = MinimalValid();
                definition.Operations.Loads["coal"] = new FuseLoad { Name = null };

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Field == "operations.loads.coal.name" && e.Code == "fuse.required");
            }
        }

        public class OperationsIndustryRules
        {
            [Fact]
            public void Industry_BlankName_EmitsRequiredError()
            {
                var definition = MinimalValid();
                definition.Operations.Industries["mill"] = new FuseIndustry { Name = null };

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Field == "operations.industries.mill.name" && e.Code == "fuse.required");
            }

            [Fact]
            public void Industry_NullComponent_EmitsError()
            {
                var definition = MinimalValid();
                definition.Operations.Industries["mill"] = new FuseIndustry
                {
                    Name = "Mill",
                    Components = new Dictionary<string, FuseIndustryComponent> { ["loader"] = null }
                };

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Code == "fuse.operations.component.required");
            }

            [Fact]
            public void NonPartialComponent_RequiresTypeAndName()
            {
                var definition = MinimalValid();
                definition.Operations.Industries["mill"] = new FuseIndustry
                {
                    Name = "Mill",
                    Components = new Dictionary<string, FuseIndustryComponent>
                    {
                        ["loader"] = new FuseIndustryComponent { Partial = false, Type = null, Name = null }
                    }
                };

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Field == "operations.industries.mill.components.loader.type" && e.Code == "fuse.required");
                Assert.Contains(result.Errors, e => e.Field == "operations.industries.mill.components.loader.name" && e.Code == "fuse.required");
            }

            [Fact]
            public void PartialComponent_DoesNotRequireTypeAndName()
            {
                var definition = MinimalValid();
                definition.Operations.Industries["mill"] = new FuseIndustry
                {
                    Name = "Mill",
                    Components = new Dictionary<string, FuseIndustryComponent>
                    {
                        ["loader"] = new FuseIndustryComponent { Partial = true }
                    }
                };

                var result = NewValidator().Validate(definition);

                Assert.DoesNotContain(result.Errors, e => e.Field.StartsWith("operations.industries.mill.components.loader.type"));
                Assert.DoesNotContain(result.Errors, e => e.Field.StartsWith("operations.industries.mill.components.loader.name"));
            }
        }

        public class OperationsLoaderAndStationRules
        {
            [Fact]
            public void Loader_BlankPrefab_EmitsRequiredError()
            {
                var definition = MinimalValid();
                definition.Operations.Loaders["pier"] = new FuseLoader { Prefab = null };

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Field == "operations.loaders.pier.prefab" && e.Code == "fuse.required");
            }

            [Fact]
            public void Station_BlankPrefab_And_BlankPassengerStop_BothEmitRequiredErrors()
            {
                var definition = MinimalValid();
                definition.Operations.Stations["depot"] = new FuseStation { Prefab = null, PassengerStopId = null };

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Field == "operations.stations.depot.prefab" && e.Code == "fuse.required");
                Assert.Contains(result.Errors, e => e.Field == "operations.stations.depot.passengerStopId" && e.Code == "fuse.required");
            }
        }
    }
}
