using FUSE.Authoring.Data;
using FUSE.Runtime.API;
using Xunit;

namespace FUSE.Tests.API
{
    public sealed class SceneryApiLegacyHelperTests
    {
        [Theory]
        [InlineData("TurntableMeasurementTool")]
        [InlineData("scenery://TurntableMeasurementTool")]
        [InlineData("ALW_ModRes_TurntableMeasurementTool")]
        public void Turntable_measurement_plate_is_editor_only(string identifier)
        {
            Assert.True(SceneryAPI.IsEditorOnlyLegacySceneryReference(new FuseScenery
            {
                AssetIdentifier = identifier
            }));
        }

        [Fact]
        public void Ordinary_turntable_scenery_is_not_editor_only()
        {
            Assert.False(SceneryAPI.IsEditorOnlyLegacySceneryReference(new FuseScenery
            {
                AssetIdentifier = "scenery://ALW_ModRes_plate50x250"
            }));
        }
    }
}
