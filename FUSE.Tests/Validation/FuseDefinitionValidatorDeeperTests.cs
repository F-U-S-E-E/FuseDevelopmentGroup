using System.Collections.Generic;
using FUSE.Data;
using FUSE.Data.Common;
using FUSE.Validation;
using UnityEngine;
using Xunit;

namespace FUSE.Tests.Validation
{
    public class FuseDefinitionValidatorDeeperTests
    {
        private static FuseDefinitionValidator NewValidator() => new FuseDefinitionValidator();

        private static FuseModDefinition MinimalValid() => new FuseModDefinition
        {
            Id = "pkg",
            Name = "Package",
            SchemaVersion = "1.0"
        };

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
                    new FuseTelegraphPoleMovement { PoleIndices = new int[0] }
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
            public void BlankSuppressionId_EmitsWarning()
            {
                var definition = MinimalValid();
                definition.World.SuppressBaseScenePaths = new[] { "   " };

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Warnings, w => w.Code == "fuse.world.suppression.scenePath.empty");
            }
        }

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

        public class TrackLocationRules
        {
            private static FuseModDefinition WithSpan(FuseTrackLocation upper, FuseTrackLocation lower)
            {
                var definition = MinimalValid();
                definition.Tracks.Spans["sp1"] = new FuseSpan { Upper = upper, Lower = lower };
                return definition;
            }

            private static FuseTrackLocation Loc(string segmentId, float? normalized = null, float? distance = null, string end = null)
            {
                return new FuseTrackLocation
                {
                    SegmentId = segmentId,
                    Normalized = normalized,
                    Distance = distance,
                    End = end
                };
            }

            [Fact]
            public void NullUpperOrLower_EmitsError()
            {
                var definition = WithSpan(null, Loc("seg-1", normalized: 0.5f));

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Code == "fuse.track.location.required" && e.Field == "tracks.spans.sp1.upper");
            }

            [Fact]
            public void BlankSegmentId_EmitsRequiredError()
            {
                var definition = WithSpan(Loc(null, normalized: 0.1f), Loc("seg-1", normalized: 0.9f));

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Field == "tracks.spans.sp1.upper.segmentId" && e.Code == "fuse.required");
            }

            [Fact]
            public void Neither_Normalized_Nor_Distance_EmitsError()
            {
                var definition = WithSpan(Loc("seg-1"), Loc("seg-1", normalized: 0.5f));

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Code == "fuse.track.location.measure");
            }

            [Fact]
            public void Both_Normalized_And_Distance_EmitsExclusiveError()
            {
                var definition = WithSpan(
                    Loc("seg-1", normalized: 0.5f, distance: 10f),
                    Loc("seg-1", normalized: 0.7f));

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Code == "fuse.track.location.measure.exclusive");
            }

            [Theory]
            [InlineData(-0.1f)]
            [InlineData(1.5f)]
            public void NormalizedOutsideRange_EmitsError(float normalized)
            {
                var definition = WithSpan(
                    Loc("seg-1", normalized: normalized),
                    Loc("seg-1", normalized: 0.5f));

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Code == "fuse.track.location.normalized");
            }

            [Theory]
            [InlineData(0f)]
            [InlineData(0.5f)]
            [InlineData(1f)]
            public void NormalizedInRange_NoError(float normalized)
            {
                var definition = WithSpan(
                    Loc("seg-1", normalized: normalized),
                    Loc("seg-2", normalized: 0.5f));

                var result = NewValidator().Validate(definition);

                Assert.DoesNotContain(result.Errors, e => e.Code == "fuse.track.location.normalized");
            }

            [Fact]
            public void NegativeDistance_EmitsError()
            {
                var definition = WithSpan(
                    Loc("seg-1", distance: -1f),
                    Loc("seg-1", normalized: 0.5f));

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Code == "fuse.track.location.distance");
            }

            [Theory]
            [InlineData("A")]
            [InlineData("B")]
            [InlineData("Start")]
            [InlineData("END")]
            [InlineData("  start  ")] // trimmed
            public void Valid_End_Tokens_NoError(string endToken)
            {
                var definition = WithSpan(
                    Loc("seg-1", normalized: 0.5f, end: endToken),
                    Loc("seg-2", normalized: 0.5f));

                var result = NewValidator().Validate(definition);

                Assert.DoesNotContain(result.Errors, e => e.Code == "fuse.track.location.end");
            }

            [Theory]
            [InlineData("X")]
            [InlineData("middle")]
            [InlineData("C")]
            public void Invalid_End_Tokens_EmitError(string endToken)
            {
                var definition = WithSpan(
                    Loc("seg-1", normalized: 0.5f, end: endToken),
                    Loc("seg-2", normalized: 0.5f));

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Code == "fuse.track.location.end");
            }

            [Fact]
            public void ExternalSegmentReference_EmitsWarning()
            {
                var definition = WithSpan(
                    Loc("external-segment", normalized: 0.5f),
                    Loc("another-external", normalized: 0.5f));

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Warnings, w => w.Code == "fuse.track.segment.external");
            }
        }

        public class SameSegmentSpanRules
        {
            private static FuseModDefinition WithSameSegmentSpan(string upperEnd, string lowerEnd, float upperNormalized = 0.2f, float lowerNormalized = 0.8f)
            {
                var definition = MinimalValid();
                definition.Tracks.Nodes["n1"] = new FuseNode { Position = new Vector3(0, 0, 0) };
                definition.Tracks.Nodes["n2"] = new FuseNode { Position = new Vector3(100, 0, 0) };
                definition.Tracks.Segments["seg-1"] = new FuseSegment
                {
                    StartNodeId = "n1",
                    EndNodeId = "n2"
                };
                definition.Tracks.Spans["sp1"] = new FuseSpan
                {
                    Upper = new FuseTrackLocation { SegmentId = "seg-1", Normalized = upperNormalized, End = upperEnd },
                    Lower = new FuseTrackLocation { SegmentId = "seg-1", Normalized = lowerNormalized, End = lowerEnd }
                };
                return definition;
            }

            [Fact]
            public void SameSegment_SameEnd_EmitsWarning()
            {
                // Both endpoints face "A" — legacy-compatible but flagged.
                var definition = WithSameSegmentSpan(upperEnd: "A", lowerEnd: "A");

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Warnings, w => w.Code == "fuse.track.span.sameSegment.sameDirection");
            }

            [Fact]
            public void SameSegment_OppositeEnds_NoSameDirectionWarning()
            {
                var definition = WithSameSegmentSpan(upperEnd: "A", lowerEnd: "B");

                var result = NewValidator().Validate(definition);

                Assert.DoesNotContain(result.Warnings, w => w.Code == "fuse.track.span.sameSegment.sameDirection");
            }

            [Fact]
            public void DifferentSegments_DoNotTriggerSameSegmentChecks()
            {
                var definition = MinimalValid();
                definition.Tracks.Segments["seg-1"] = new FuseSegment { StartNodeId = "n1", EndNodeId = "n2" };
                definition.Tracks.Segments["seg-2"] = new FuseSegment { StartNodeId = "n3", EndNodeId = "n4" };
                definition.Tracks.Spans["sp1"] = new FuseSpan
                {
                    Upper = new FuseTrackLocation { SegmentId = "seg-1", Normalized = 0.5f, End = "A" },
                    Lower = new FuseTrackLocation { SegmentId = "seg-2", Normalized = 0.5f, End = "A" }
                };

                var result = NewValidator().Validate(definition);

                Assert.DoesNotContain(result.Warnings, w => w.Code == "fuse.track.span.sameSegment.sameDirection");
            }

            [Fact]
            public void SameSegment_DistanceOutsideEstimatedLength_EmitsWarning()
            {
                // Nodes 100 units apart; distance of 500 is well outside the
                // straight-line estimate. The validator emits a warning that
                // runtime will use the actual curved length.
                var definition = MinimalValid();
                definition.Tracks.Nodes["n1"] = new FuseNode { Position = new Vector3(0, 0, 0) };
                definition.Tracks.Nodes["n2"] = new FuseNode { Position = new Vector3(100, 0, 0) };
                definition.Tracks.Segments["seg-1"] = new FuseSegment { StartNodeId = "n1", EndNodeId = "n2" };
                definition.Tracks.Spans["sp1"] = new FuseSpan
                {
                    Upper = new FuseTrackLocation { SegmentId = "seg-1", Distance = 500f, End = "A" },
                    Lower = new FuseTrackLocation { SegmentId = "seg-1", Distance = 10f, End = "B" }
                };

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Warnings, w => w.Code == "fuse.track.span.upper.distance");
            }
        }

        public class IndustryComponentDeepRules
        {
            private static FuseModDefinition WithComponent(FuseIndustryComponent component)
            {
                var definition = MinimalValid();
                definition.Operations.Industries["mill"] = new FuseIndustry
                {
                    Name = "Mill",
                    Components = new Dictionary<string, FuseIndustryComponent> { ["x"] = component }
                };
                return definition;
            }

            [Fact]
            public void UnknownNonCustomType_EmitsError()
            {
                var result = NewValidator().Validate(WithComponent(new FuseIndustryComponent
                {
                    Type = "garbageType",
                    Name = "x"
                }));

                Assert.Contains(result.Errors, e => e.Code == "fuse.operations.component.type");
            }

            [Fact]
            public void DottedCustomType_EmitsWarning_NotError()
            {
                var result = NewValidator().Validate(WithComponent(new FuseIndustryComponent
                {
                    Type = "My.Custom.Component",
                    Name = "x"
                }));

                Assert.Contains(result.Warnings, w => w.Code == "fuse.operations.component.type.custom");
                Assert.DoesNotContain(result.Errors, e => e.Code == "fuse.operations.component.type");
            }

            [Fact]
            public void TypeUsingTrackSpans_WithoutSpans_EmitsError()
            {
                var result = NewValidator().Validate(WithComponent(new FuseIndustryComponent
                {
                    Type = "loader",
                    Name = "x",
                    LoadId = "coal",
                    TrackSpanIds = null
                }));

                Assert.Contains(result.Errors, e => e.Code == "fuse.operations.component.trackSpanIds");
            }

            [Fact]
            public void PassengerStop_WithoutTrackSpans_IsAccepted()
            {
                // Legacy AMM allowed spanless passenger stops — locked in.
                var result = NewValidator().Validate(WithComponent(new FuseIndustryComponent
                {
                    Type = "passengerStop",
                    Name = "x",
                    PassengerStopId = "stop-1",
                    TimetableCode = "T1",
                    TrackSpanIds = null
                }));

                Assert.DoesNotContain(result.Errors, e => e.Code == "fuse.operations.component.trackSpanIds");
            }

            [Fact]
            public void LoaderType_WithoutLoadId_EmitsWarning()
            {
                var result = NewValidator().Validate(WithComponent(new FuseIndustryComponent
                {
                    Type = "loader",
                    Name = "x",
                    LoadId = null,
                    TrackSpanIds = new[] { "span-1" }
                }));

                Assert.Contains(result.Warnings, w => w.Code == "fuse.operations.component.loadId");
            }

            [Fact]
            public void PassengerStop_WithoutLoadId_NoWarning()
            {
                // PassengerStop uses LoadId per the policy table, but the validator
                // explicitly suppresses the warning for it.
                var result = NewValidator().Validate(WithComponent(new FuseIndustryComponent
                {
                    Type = "passengerStop",
                    Name = "x",
                    PassengerStopId = "stop-1",
                    TimetableCode = "T1",
                    LoadId = null
                }));

                Assert.DoesNotContain(result.Warnings, w => w.Code == "fuse.operations.component.loadId");
            }

            [Fact]
            public void NegativeStorageChangeRate_EmitsError()
            {
                var result = NewValidator().Validate(WithComponent(new FuseIndustryComponent
                {
                    Type = "loader",
                    Name = "x",
                    LoadId = "coal",
                    TrackSpanIds = new[] { "s1" },
                    StorageChangeRate = -0.5f
                }));

                Assert.Contains(result.Errors, e => e.Code == "fuse.operations.component.storageChangeRate");
            }

            [Fact]
            public void NegativeMaxStorage_EmitsError()
            {
                var result = NewValidator().Validate(WithComponent(new FuseIndustryComponent
                {
                    Type = "loader",
                    Name = "x",
                    LoadId = "coal",
                    TrackSpanIds = new[] { "s1" },
                    MaxStorage = -1f
                }));

                Assert.Contains(result.Errors, e => e.Code == "fuse.operations.component.maxStorage");
            }

            [Fact]
            public void NegativeCarTransferRate_EmitsError()
            {
                var result = NewValidator().Validate(WithComponent(new FuseIndustryComponent
                {
                    Type = "loader",
                    Name = "x",
                    LoadId = "coal",
                    TrackSpanIds = new[] { "s1" },
                    CarTransferRate = -0.1f
                }));

                Assert.Contains(result.Errors, e => e.Code == "fuse.operations.component.carTransferRate");
            }

            [Fact]
            public void Formulaic_WithoutTerms_EmitsError()
            {
                var result = NewValidator().Validate(WithComponent(new FuseIndustryComponent
                {
                    Type = "formulaic",
                    Name = "x",
                    InputTermsPerDay = null,
                    OutputTermsPerDay = null
                }));

                Assert.Contains(result.Errors, e => e.Code == "fuse.operations.formulaic.terms");
            }

            [Fact]
            public void TeamTrack_WithoutProfiles_EmitsError()
            {
                var result = NewValidator().Validate(WithComponent(new FuseIndustryComponent
                {
                    Type = "teamTrack",
                    Name = "x",
                    TrackSpanIds = new[] { "s1" },
                    TeamProfiles = null
                }));

                Assert.Contains(result.Errors, e => e.Code == "fuse.operations.teamTrack.profile");
            }

            [Fact]
            public void PassengerStop_BlankIdOrTimetable_EmitsRequiredErrors()
            {
                var result = NewValidator().Validate(WithComponent(new FuseIndustryComponent
                {
                    Type = "passengerStop",
                    Name = "x",
                    PassengerStopId = null,
                    TimetableCode = null
                }));

                Assert.Contains(result.Errors, e => e.Field.EndsWith(".passengerStopId") && e.Code == "fuse.required");
                Assert.Contains(result.Errors, e => e.Field.EndsWith(".timetableCode") && e.Code == "fuse.required");
            }

            [Fact]
            public void TeleportLoading_WithoutSpans_EmitsError()
            {
                var result = NewValidator().Validate(WithComponent(new FuseIndustryComponent
                {
                    Type = "teleportLoading",
                    Name = "x",
                    InputSpanIds = null,
                    OutputSpanIds = null
                }));

                Assert.Contains(result.Errors, e => e.Code == "fuse.operations.teleportLoading.spans");
            }

            [Fact]
            public void TeleportLoading_NegativeCarLoadPeriod_EmitsError()
            {
                var result = NewValidator().Validate(WithComponent(new FuseIndustryComponent
                {
                    Type = "teleportLoading",
                    Name = "x",
                    InputSpanIds = new[] { "s1" },
                    CarLoadPeriod = -1f
                }));

                Assert.Contains(result.Errors, e => e.Code == "fuse.operations.teleportLoading.carLoadPeriod");
            }

            [Fact]
            public void TeleportLoading_NegativeCarLengthFeet_EmitsError()
            {
                var result = NewValidator().Validate(WithComponent(new FuseIndustryComponent
                {
                    Type = "teleportLoading",
                    Name = "x",
                    OutputSpanIds = new[] { "s1" },
                    CarLengthFeet = -10f
                }));

                Assert.Contains(result.Errors, e => e.Code == "fuse.operations.teleportLoading.carLengthFeet");
            }
        }

        public class InterchangeTransferRules
        {
            [Fact]
            public void BlankSourceKey_IsPreFilteredByNormalize_NotFlaggedByValidator()
            {
                // FuseMigration.NormalizeInterchangeTransfers strips blank-key entries
                // before the validator runs. That makes the validator's
                // "fuse.progression.interchangeTransfer.source.empty" rule effectively
                // unreachable through the public Validate() entry point. Locking in
                // the actual observable contract: no error surfaces, the entry is
                // silently dropped.
                var definition = MinimalValid();
                definition.Progression.Progressions["main"] = new FuseProgression
                {
                    Sections = new Dictionary<string, FuseSection>
                    {
                        ["s1"] = new FuseSection
                        {
                            DisplayName = "S1",
                            InterchangeTransfers = new Dictionary<string, string>
                            {
                                ["   "] = "destination"
                            }
                        }
                    }
                };

                var result = NewValidator().Validate(definition);

                Assert.DoesNotContain(result.Errors, e => e.Code == "fuse.progression.interchangeTransfer.source.empty");
                // Sanity: the normalized section's transfers dict is empty.
                var normalizedSection = definition.Progression.Progressions["main"].Sections["s1"];
                Assert.Empty(normalizedSection.InterchangeTransfers);
            }

            [Fact]
            public void SourceEqualsTarget_EmitsWarning()
            {
                var definition = MinimalValid();
                definition.Progression.Progressions["main"] = new FuseProgression
                {
                    Sections = new Dictionary<string, FuseSection>
                    {
                        ["s1"] = new FuseSection
                        {
                            DisplayName = "S1",
                            InterchangeTransfers = new Dictionary<string, string>
                            {
                                ["same"] = "Same"
                            }
                        }
                    }
                };

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Warnings, w => w.Code == "fuse.progression.interchangeTransfer.sameTarget");
            }
        }

        public class AudioRules
        {
            [Fact]
            public void NullWhistle_EmitsError()
            {
                var definition = MinimalValid();
                definition.Audio.Whistles["w"] = null;

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Code == "fuse.audio.whistle.required");
            }

            [Fact]
            public void Whistle_BlankNameOrClip_EmitsRequiredErrors()
            {
                var definition = MinimalValid();
                definition.Audio.Whistles["w"] = new FuseWhistleAudio { Name = null, Clip = null };

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Field == "audio.whistles.w.name" && e.Code == "fuse.required");
                Assert.Contains(result.Errors, e => e.Field == "audio.whistles.w.clip" && e.Code == "fuse.required");
            }

            [Fact]
            public void NullHorn_EmitsError()
            {
                var definition = MinimalValid();
                definition.Audio.Horns["h"] = null;

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Code == "fuse.audio.horn.required");
            }

            [Fact]
            public void Horn_WithNoLayers_EmitsError()
            {
                var definition = MinimalValid();
                definition.Audio.Horns["h"] = new FuseHornAudio { Name = "Horn", Layers = new FuseHornLayer[0] };

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Code == "fuse.audio.horn.layers");
            }

            [Fact]
            public void Horn_LayerMissingFile_EmitsRequiredError()
            {
                var definition = MinimalValid();
                definition.Audio.Horns["h"] = new FuseHornAudio
                {
                    Name = "Horn",
                    Layers = new[] { new FuseHornLayer { File = null } }
                };

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Field == "audio.horns.h.layers[0].file" && e.Code == "fuse.required");
            }

            [Fact]
            public void Horn_LayerWithoutKeyframes_EmitsWarning()
            {
                var definition = MinimalValid();
                definition.Audio.Horns["h"] = new FuseHornAudio
                {
                    Name = "Horn",
                    Layers = new[] { new FuseHornLayer { File = "h.ogg", Keyframes = null } }
                };

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Warnings, w => w.Code == "fuse.audio.horn.keyframes.empty");
            }

            [Fact]
            public void NullBell_EmitsError()
            {
                var definition = MinimalValid();
                definition.Audio.Bells["b"] = null;

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Code == "fuse.audio.bell.required");
            }

            [Fact]
            public void Bell_BlankNameOrFile_EmitsRequiredErrors()
            {
                var definition = MinimalValid();
                definition.Audio.Bells["b"] = new FuseBellAudio { Name = null, File = null };

                var result = NewValidator().Validate(definition);

                Assert.Contains(result.Errors, e => e.Field == "audio.bells.b.name" && e.Code == "fuse.required");
                Assert.Contains(result.Errors, e => e.Field == "audio.bells.b.file" && e.Code == "fuse.required");
            }
        }
    }
}
