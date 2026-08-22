using FUSE.Loading;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FUSE.Tests.Loading
{
    public class FuseLegacyEditorHelperSceneryTests
    {
        [Theory]
        [InlineData("TurntableMeasurementTool", true)]
        [InlineData("scenery://TurntableMeasurementTool", true)]
        [InlineData("SCENERY://turntablemeasurementtool", true)]
        [InlineData("30m Turntable", false)]
        [InlineData("TurntableMeasurementTool Shed", false)]
        [InlineData(null, false)]
        public void EditorHelperDetection_IsExact(string assetIdentifier, bool expected)
        {
            Assert.Equal(
                expected,
                FuseLegacyDataConverter.IsLegacyEditorOnlySceneryAsset(assetIdentifier));
        }

        [Fact]
        public void StrykerBrysonMeasurementOverlay_IsNotMaterializedBesideRealTurntable()
        {
            var source = new JObject
            {
                ["scenery"] = new JObject
                {
                    ["TTPlaceholder"] = new JObject
                    {
                        ["modelIdentifier"] = "TurntableMeasurementTool",
                        ["position"] = new JObject
                        {
                            ["x"] = 4278.08,
                            ["y"] = 529.0,
                            ["z"] = 5371.88
                        }
                    },
                    ["RealBuilding"] = new JObject
                    {
                        ["modelIdentifier"] = "ALWHouses_CabooseHouse"
                    }
                },
                ["splineys"] = new JObject
                {
                    ["Bryson_new_TT"] = new JObject
                    {
                        ["handler"] = "AlinasMapMod.Turntable.TurntableBuilder",
                        ["position"] = new JObject
                        {
                            ["x"] = 4278.08,
                            ["y"] = 529.0,
                            ["z"] = 5371.88
                        },
                        ["rotation"] = new JObject { ["y"] = 2.5 }
                    }
                }
            };
            var manifest = new FuseLegacyPackageManifest
            {
                PackageId = "StrykerBryson.FUSE",
                DisplayName = "Stryker's Bryson",
                Version = "1.1"
            };
            var root = FuseLegacyDataConverter.CreateSkeleton(manifest, "brysonttplaceholder");

            FuseLegacyDataConverter.ConvertSource(source, root, manifest);

            var scenery = (JObject)root["world"]["scenery"];
            Assert.Null(scenery.Property("TTPlaceholder"));
            Assert.NotNull(scenery["RealBuilding"]);
            Assert.NotNull(root["operations"]["turntables"]["Bryson_new_TT"]);
        }

        [Fact]
        public void RailroadCrossingMeasurementOverlay_DoesNotLeaveAJsonNullSceneryEntry()
        {
            var source = new JObject
            {
                ["splineys"] = new JObject
                {
                    ["CrossingMeasurementOverlay"] = new JObject
                    {
                        ["handler"] = "strangecustoms.railroadcrossingbuilder",
                        ["modelIdentifier"] = "TurntableMeasurementTool"
                    }
                }
            };
            var manifest = new FuseLegacyPackageManifest
            {
                PackageId = "crossing-helper-test",
                DisplayName = "Crossing Helper Test",
                Version = "1.0"
            };
            var root = FuseLegacyDataConverter.CreateSkeleton(manifest, "crossing-helper");

            FuseLegacyDataConverter.ConvertSource(source, root, manifest);

            var scenery = (JObject)root["world"]["scenery"];
            Assert.Null(scenery.Property("CrossingMeasurementOverlay"));
        }
    }
}
