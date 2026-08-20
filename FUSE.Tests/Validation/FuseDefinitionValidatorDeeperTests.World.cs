using System;
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

        public class WorldSceneryRules
        {
            [Fact]
            public void Scenery_WithoutAssetIdentifierOrModel_EmitsError()
            {
                var definition = MinimalValid();
                definition.World.Scenery["barn"] = new FuseScenery { AssetIdentifier = null, Model = null };

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Code == "fuse.world.scenery.assetIdentifier.required");
            }

            [Fact]
            public void Scenery_WithLegacyModelOnly_IsAccepted()
            {
                // Migration copies Model into AssetIdentifier during Validate→Normalize so
                // the legacy field continues to satisfy the requirement.
                var definition = MinimalValid();
                definition.World.Scenery["barn"] = new FuseScenery { Model = "legacy-id" };

                var result = NewValidator().Validate(definition);

                Assert.DoesNotContain(result.Errors, e => e.Code == "fuse.world.scenery.assetIdentifier.required");
            }

            [Fact]
            public void Scenery_WithBlankAnchorSpanId_EmitsWarning()
            {
                var definition = MinimalValid();
                definition.World.Scenery["barn"] = new FuseScenery
                {
                    AssetIdentifier = "barn-asset",
                    AnchorSpanIds = new[] { "   " }
                };

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Warnings, w => w.Code == "fuse.world.scenery.anchorSpan.empty");
            }
        }

        public class WorldSpawnPointRules
        {
            [Fact]
            public void Null_EmitsError()
            {
                var definition = MinimalValid();
                definition.World.SpawnPoints = new FuseSpawnPoint[] { null };

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Code == "fuse.world.spawnPoint.required");
            }

            [Fact]
            public void BlankName_EmitsRequiredError()
            {
                var definition = MinimalValid();
                definition.World.SpawnPoints = new[] { new FuseSpawnPoint { Name = null } };

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Field == "world.spawnPoints[0].name" && e.Code == "fuse.required");
            }

            [Fact]
            public void DuplicateName_EmitsError()
            {
                var definition = MinimalValid();
                definition.World.SpawnPoints = new[]
                {
                    new FuseSpawnPoint { Name = "dup" },
                    new FuseSpawnPoint { Name = "dup" }
                };

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Code == "fuse.world.spawnPoint.duplicate");
            }

            [Fact]
            public void NonPositiveRadius_EmitsError()
            {
                var definition = MinimalValid();
                definition.World.SpawnPoints = new[] { new FuseSpawnPoint { Name = "a", Radius = 0f } };

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Code == "fuse.world.spawnPoint.radius");
            }
        }

        public class WorldSplineyAndTelegraphRules
        {
            [Fact]
            public void WaterSurface_WithInvalidGeometry_EmitsActionableErrors()
            {
                var definition = MinimalValid();
                definition.World.WaterSurfaces["lake"] = new FuseWaterSurface
                {
                    Points = new[] { Vector3.zero, Vector3.one },
                    TriangleDensity = 0f,
                    MaximumTriangleArea = 0f,
                    UvScale = 0f,
                };

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Code == "fuse.waterSurface.points");
                Assert.Contains(result.Errors, e => e.Code == "fuse.waterSurface.triangleDensity");
                Assert.Contains(result.Errors, e => e.Code == "fuse.waterSurface.maximumTriangleArea");
                Assert.Contains(result.Errors, e => e.Code == "fuse.waterSurface.uvScale");
            }

            [Fact]
            public void WaterSurface_WithThreePointsAndSafeTessellation_IsAccepted()
            {
                var definition = MinimalValid();
                definition.World.WaterSurfaces["lake"] = new FuseWaterSurface
                {
                    Points = new[]
                    {
                        Vector3.zero,
                        new Vector3(20f, 0f, 0f),
                        new Vector3(0f, 0f, 20f),
                    },
                };

                var result = NewValidator().Validate(definition);

                Assert.DoesNotContain(result.Errors, e => e.Field.StartsWith("world.waterSurfaces.lake", StringComparison.Ordinal));
            }

            [Fact]
            public void Spliney_WithFewerThanTwoPoints_EmitsError()
            {
                var definition = MinimalValid();
                definition.World.Splineys["wires"] = new FuseSpliney
                {
                    Type = "telegraph",
                    Points = new[] { new FuseSplineyPoint() }
                };

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Code == "fuse.spliney.points");
            }

            [Fact]
            public void Spliney_WithBlankType_EmitsRequiredError()
            {
                var definition = MinimalValid();
                definition.World.Splineys["wires"] = new FuseSpliney
                {
                    Type = null,
                    Points = new[] { new FuseSplineyPoint(), new FuseSplineyPoint() }
                };

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Field == "world.splineys.wires.type" && e.Code == "fuse.required");
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
                        new FuseSplineyPoint { Position = Vector3.zero },
                        new FuseSplineyPoint { Position = Vector3.right },
                    },
                };

                var result = NewValidator().Validate(definition);

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
                        new FuseSplineyPoint { Position = Vector3.zero },
                        new FuseSplineyPoint { Position = Vector3.right * 20f },
                    },
                };

                var result = NewValidator().Validate(definition);

                Assert.DoesNotContain(
                    result.Errors,
                    error => error.Field.StartsWith(
                        "world.splineys.fence",
                        StringComparison.Ordinal));
            }

            [Fact]
            public void TelegraphPoles_WithFewerThanTwoPoints_EmitsError()
            {
                var definition = MinimalValid();
                definition.World.TelegraphPoles["poles"] = new FuseTelegraphPoles
                {
                    Points = new[] { Vector3.zero }
                };

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Code == "fuse.telegraph.points");
            }

            [Fact]
            public void TelegraphMovement_WithoutPoleIndices_EmitsError()
            {
                var definition = MinimalValid();
                definition.World.TelegraphPoleMovements = new[]
                {
                    new FuseTelegraphPoleMovement { PoleIndices = Array.Empty<int>() }
                };

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Code == "fuse.telegraphPoleMovement.poleIndices");
            }

            [Fact]
            public void TelegraphMovement_WithNegativePoleIndex_EmitsError()
            {
                var definition = MinimalValid();
                definition.World.TelegraphPoleMovements = new[]
                {
                    new FuseTelegraphPoleMovement { PoleIndices = new[] { -1 }, Offset = new Vector3(1, 0, 0) }
                };

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Code == "fuse.telegraphPoleMovement.poleIndex");
            }

            [Fact]
            public void TelegraphMovement_WithDuplicatePoleIndex_EmitsWarning()
            {
                var definition = MinimalValid();
                definition.World.TelegraphPoleMovements = new[]
                {
                    new FuseTelegraphPoleMovement { PoleIndices = new[] { 1, 1 }, Offset = new Vector3(1, 0, 0) }
                };

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Warnings, w => w.Code == "fuse.telegraphPoleMovement.duplicatePoleIndex");
            }

            [Fact]
            public void TelegraphMovement_WithZeroOffset_EmitsWarning()
            {
                var definition = MinimalValid();
                definition.World.TelegraphPoleMovements = new[]
                {
                    new FuseTelegraphPoleMovement { PoleIndices = new[] { 0 }, Offset = Vector3.zero }
                };

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Warnings, w => w.Code == "fuse.telegraphPoleMovement.zeroOffset");
            }
        }

        public class WorldMapMaskRules
        {
            [Fact]
            public void Circle_WithNonPositiveRadius_EmitsError()
            {
                var definition = MinimalValid();
                definition.World.MapMasks["m"] = new FuseMapMask { Type = "circle", Radius = 0f };

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Code == "fuse.mapMask.circle.radius");
            }

            [Fact]
            public void Rectangle_WithoutSize_EmitsError()
            {
                var definition = MinimalValid();
                definition.World.MapMasks["m"] = new FuseMapMask { Type = "rectangle", Size = null };

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Code == "fuse.mapMask.rectangle.size");
            }

            [Fact]
            public void Rectangle_WithZeroSize_EmitsError()
            {
                var definition = MinimalValid();
                definition.World.MapMasks["m"] = new FuseMapMask { Type = "rectangle", Size = Vector3.zero };

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Code == "fuse.mapMask.rectangle.size");
            }

            [Fact]
            public void Curve_WithFewerThanTwoPoints_EmitsError()
            {
                var definition = MinimalValid();
                definition.World.MapMasks["m"] = new FuseMapMask
                {
                    Type = "curve",
                    Points = new[] { Vector3.zero }
                };

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Code == "fuse.mapMask.curve.points");
            }

            [Theory]
            [InlineData("oval")]
            [InlineData("")]
            public void UnknownType_EmitsError(string type)
            {
                var definition = MinimalValid();
                definition.World.MapMasks["m"] = new FuseMapMask { Type = type };

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Code == "fuse.mapMask.type");
            }
        }

        public class WorldMapTilesAndSceneClonesRules
        {
            [Fact]
            public void NullMapTile_EmitsError()
            {
                var definition = MinimalValid();
                definition.World.MapTiles["tile"] = null;

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Code == "fuse.mapTiles.required");
            }

            [Fact]
            public void MapTile_BlankDirectory_AndBlankSource_EmitRequiredErrors()
            {
                var definition = MinimalValid();
                definition.World.MapTiles["tile"] = new FuseMapTileSource();

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Field == "world.mapTiles.tile.directory" && e.Code == "fuse.required");
                Assert.Contains(result.Errors, e => e.Field == "world.mapTiles.tile.sourceFolder" && e.Code == "fuse.required");
            }

            [Fact]
            public void NullSceneClone_EmitsError()
            {
                var definition = MinimalValid();
                definition.World.SceneClones["clone"] = null;

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Code == "fuse.sceneClone.required");
            }

            [Fact]
            public void SceneClone_BlankTargetPath_EmitsRequiredError()
            {
                var definition = MinimalValid();
                definition.World.SceneClones["clone"] = new FuseSceneClone { TargetPath = null };

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Field == "world.sceneClones.clone.targetPath" && e.Code == "fuse.required");
            }
        }

        public class WorldRemovalAndSuppressionRules
        {
            [Fact]
            public void RemovalId_Blank_EmitsError()
            {
                var definition = MinimalValid();
                definition.World.Removals = new FuseWorldRemovals
                {
                    Scenery = new[] { "   " }
                };

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Code == "fuse.world.removal.blank");
            }

            [Fact]
            public void RemovalId_Duplicate_EmitsWarning()
            {
                var definition = MinimalValid();
                definition.World.Removals = new FuseWorldRemovals
                {
                    Scenery = new[] { "barn", "barn" }
                };

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Warnings, w => w.Code == "fuse.world.removal.duplicate");
            }

            [Fact]
            public void RemovalId_AlsoDefined_EmitsConflictError()
            {
                // A package cannot define and remove the same world object in one document.
                var definition = MinimalValid();
                definition.World.Scenery["barn"] = new FuseScenery { AssetIdentifier = "barn-asset" };
                definition.World.Removals = new FuseWorldRemovals
                {
                    Scenery = new[] { "barn" }
                };

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Code == "fuse.world.removal.conflict");
            }

            [Fact]
            public void WaterSurfaceRemoval_AlsoDefined_EmitsConflictError()
            {
                var definition = MinimalValid();
                definition.World.WaterSurfaces["lake"] = new FuseWaterSurface
                {
                    Points = new[] { Vector3.zero, Vector3.right, Vector3.forward },
                };
                definition.World.Removals.WaterSurfaces = new[] { "lake" };

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Field == "world.removals.waterSurfaces[0]" && e.Code == "fuse.world.removal.conflict");
            }

            [Fact]
            public void BlankSuppressionId_EmitsWarning()
            {
                var definition = MinimalValid();
                definition.World.SuppressBaseScenePaths = new[] { "   " };

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Warnings, w => w.Code == "fuse.world.suppression.scenePath.empty");
            }
        }
    }
}
