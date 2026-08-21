using System;
using System.Collections.Generic;
using FUSE.Authoring.Data;
using FUSE.Authoring.Data.Common;
using FUSE.Authoring.Validation;
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
