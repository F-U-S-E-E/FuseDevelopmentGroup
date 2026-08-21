using System;
using Fuse.Core.Model;
using Fuse.Core.Validation;
using Xunit;

namespace Fuse.Core.Tests
{
    public sealed class WorldValidationTests
    {
        [Fact]
        public void WaterSurface_WithInvalidGeometry_EmitsActionableErrors()
        {
            var definition = MinimalValid();
            definition.World.WaterSurfaces["lake"] = new FuseWaterSurface
            {
                Points = new[] { FuseVector3.zero, FuseVector3.one },
                TriangleDensity = 0f,
                MaximumTriangleArea = 0f,
                UvScale = 0f,
            };

            var result = Validate(definition);

            Assert.Contains(result.Errors, error => error.Code == "fuse.waterSurface.points");
            Assert.Contains(result.Errors, error => error.Code == "fuse.waterSurface.triangleDensity");
            Assert.Contains(result.Errors, error => error.Code == "fuse.waterSurface.maximumTriangleArea");
            Assert.Contains(result.Errors, error => error.Code == "fuse.waterSurface.uvScale");
        }

        [Fact]
        public void WaterSurface_WithThreePointsAndSafeTessellation_IsAccepted()
        {
            var definition = MinimalValid();
            definition.World.WaterSurfaces["lake"] = new FuseWaterSurface
            {
                Points = new[]
                {
                    FuseVector3.zero,
                    new FuseVector3(20f, 0f, 0f),
                    new FuseVector3(0f, 0f, 20f),
                },
            };

            var result = Validate(definition);

            Assert.DoesNotContain(
                result.Errors,
                error => error.Field.StartsWith("world.waterSurfaces.lake", StringComparison.Ordinal));
        }

        [Fact]
        public void ObjectLine_RequiresOneSourceAndSafeLayoutLimits()
        {
            var definition = MinimalValid();
            definition.World.Splineys["fence"] = new FuseSpliney
            {
                Type = "objectLine",
                AssetIdentifier = "fence-panel",
                Prefab = "path://scene/World/Fence",
                Spacing = 0f,
                MaximumInstances = 5000,
                Points = new[]
                {
                    new FuseSplineyPoint { Position = FuseVector3.zero },
                    new FuseSplineyPoint { Position = FuseVector3.one },
                },
            };

            var result = Validate(definition);

            Assert.Contains(result.Errors, error => error.Code == "fuse.spliney.objectLine.source");
            Assert.Contains(result.Errors, error => error.Code == "fuse.spliney.objectLine.spacing");
            Assert.Contains(result.Errors, error => error.Code == "fuse.spliney.objectLine.maximumInstances");
        }

        [Fact]
        public void ObjectLine_WithOneAssetSourceAndSafeLimits_IsAccepted()
        {
            var definition = MinimalValid();
            definition.World.Splineys["fence"] = new FuseSpliney
            {
                Type = "objectLine",
                AssetIdentifier = "fence-panel",
                Spacing = 3f,
                MaximumInstances = 200,
                Points = new[]
                {
                    new FuseSplineyPoint { Position = FuseVector3.zero },
                    new FuseSplineyPoint { Position = new FuseVector3(20f, 0f, 0f) },
                },
            };

            var result = Validate(definition);

            Assert.DoesNotContain(
                result.Errors,
                error => error.Field.StartsWith("world.splineys.fence", StringComparison.Ordinal));
        }

        [Fact]
        public void TelegraphPoles_WithFewerThanTwoPoints_EmitsError()
        {
            var definition = MinimalValid();
            definition.World.TelegraphPoles["poles"] = new FuseTelegraphPoles
            {
                Points = new[] { FuseVector3.zero }
            };

            var result = Validate(definition);

            Assert.Contains(result.Errors, error => error.Code == "fuse.telegraph.points");
        }

        [Fact]
        public void TelegraphMovement_VectorRules_AreValidated()
        {
            var definition = MinimalValid();
            definition.World.TelegraphPoleMovements = new[]
            {
                new FuseTelegraphPoleMovement { PoleIndices = new[] { -1 }, Offset = new FuseVector3(1f, 0f, 0f) },
                new FuseTelegraphPoleMovement { PoleIndices = new[] { 1, 1 }, Offset = new FuseVector3(1f, 0f, 0f) },
                new FuseTelegraphPoleMovement { PoleIndices = new[] { 0 }, Offset = FuseVector3.zero },
            };

            var result = Validate(definition);

            Assert.Contains(result.Errors, error => error.Code == "fuse.telegraphPoleMovement.poleIndex");
            Assert.Contains(result.Warnings, warning => warning.Code == "fuse.telegraphPoleMovement.duplicatePoleIndex");
            Assert.Contains(result.Warnings, warning => warning.Code == "fuse.telegraphPoleMovement.zeroOffset");
        }

        [Fact]
        public void MapMask_VectorRules_AreValidated()
        {
            var definition = MinimalValid();
            definition.World.MapMasks["rectangle"] = new FuseMapMask
            {
                Type = "rectangle",
                Size = FuseVector3.zero,
            };
            definition.World.MapMasks["curve"] = new FuseMapMask
            {
                Type = "curve",
                Points = new[] { FuseVector3.zero },
            };

            var result = Validate(definition);

            Assert.Contains(result.Errors, error => error.Code == "fuse.mapMask.rectangle.size");
            Assert.Contains(result.Errors, error => error.Code == "fuse.mapMask.curve.points");
        }

        [Fact]
        public void WaterSurfaceRemoval_AlsoDefined_EmitsConflictError()
        {
            var definition = MinimalValid();
            definition.World.WaterSurfaces["lake"] = new FuseWaterSurface
            {
                Points = new[] { FuseVector3.zero, FuseVector3.one, new FuseVector3(0f, 0f, 1f) },
            };
            definition.World.Removals.WaterSurfaces = new[] { "lake" };

            var result = Validate(definition);

            Assert.Contains(
                result.Errors,
                error => error.Field == "world.removals.waterSurfaces[0]" &&
                         error.Code == "fuse.world.removal.conflict");
        }

        private static ValidationResult Validate(FuseModDefinition definition)
        {
            return new FuseDefinitionValidator().Validate(definition);
        }

        private static FuseModDefinition MinimalValid()
        {
            return new FuseModDefinition { Id = "fuse.test.world", Name = "World Test" };
        }
    }
}
