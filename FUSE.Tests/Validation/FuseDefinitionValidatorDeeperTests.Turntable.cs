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

        public class TurntableRules
        {
            [Fact]
            public void NonPositiveRadius_EmitsError()
            {
                var definition = MinimalValid();
                definition.Operations.Turntables["t1"] = new FuseTurntable { Radius = 0f, Subdivisions = 16 };

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Code == "fuse.turntable.radius");
            }

            [Theory]
            [InlineData(3)]
            [InlineData(33)]
            public void SubdivisionsOutOfRange_EmitsError(int subdivisions)
            {
                var definition = MinimalValid();
                definition.Operations.Turntables["t1"] = new FuseTurntable { Radius = 5f, Subdivisions = subdivisions };

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Code == "fuse.turntable.subdivisions");
            }

            [Theory]
            [InlineData(4)]
            [InlineData(16)]
            [InlineData(32)]
            public void SubdivisionsInRange_NoError(int subdivisions)
            {
                var definition = MinimalValid();
                definition.Operations.Turntables["t1"] = new FuseTurntable { Radius = 5f, Subdivisions = subdivisions };

                var result = NewValidator().Validate(definition);

                Assert.DoesNotContain(result.Errors, e => e.Code == "fuse.turntable.subdivisions");
            }

            [Fact]
            public void Roundhouse_WithStallsButZeroTrackLength_EmitsError()
            {
                var definition = MinimalValid();
                definition.Operations.Turntables["t1"] = new FuseTurntable
                {
                    Radius = 5f,
                    Subdivisions = 16,
                    Roundhouse = new FuseRoundhouse { Stalls = 4, TrackLength = 0f }
                };

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Code == "fuse.turntable.roundhouse.trackLength");
            }
        }
    }
}
