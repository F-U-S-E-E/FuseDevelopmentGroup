using FUSE.Patches;
using Xunit;

namespace FUSE.Tests.Patches
{
    public sealed class FuseSceneryCullingReconciliationTests
    {
        [Fact]
        public void FirstLiveCameraRequestsReconciliation()
        {
            Assert.True(
                FuseCullingManagerUpdateRacePatch
                    .ShouldRequestDestinationReconciliation(
                        hasPreviousCameraPosition: false,
                        cameraMovementSqr: 0f));
        }

        [Fact]
        public void CameraTeleportRequestsReconciliationAtThreshold()
        {
            var threshold =
                FuseCullingManagerUpdateRacePatch
                    .DestinationCameraJumpDistance;

            Assert.True(
                FuseCullingManagerUpdateRacePatch
                    .ShouldRequestDestinationReconciliation(
                        hasPreviousCameraPosition: true,
                        cameraMovementSqr: threshold * threshold));
        }

        [Fact]
        public void OrdinaryCameraMotionDoesNotRescanScenery()
        {
            Assert.False(
                FuseCullingManagerUpdateRacePatch
                    .ShouldRequestDestinationReconciliation(
                        hasPreviousCameraPosition: true,
                        cameraMovementSqr: 25f));
        }

        [Theory]
        [InlineData(true, 0, true)]
        [InlineData(true, 2, true)]
        [InlineData(true, 3, false)]
        [InlineData(false, 0, false)]
        public void ReconciliationUsesVanillaLoadedBandsOnly(
            bool isActiveAndEnabled,
            int distanceBand,
            bool expected)
        {
            Assert.Equal(
                expected,
                FuseCullingManagerUpdateRacePatch
                    .ShouldReconcileScenery(
                        isActiveAndEnabled,
                        distanceBand));
        }
    }
}
