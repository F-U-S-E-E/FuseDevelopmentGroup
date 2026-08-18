using FUSE.Patches;
using Xunit;

namespace FUSE.Tests.Patches
{
    public sealed class FuseRealisticRerailCraneGuardPatchesTests
    {
        [Theory]
        [InlineData(false, false)]
        [InlineData(true, true)]
        public void CoupledCarCount_RunsOnlyWithSelectedCrane(
            bool hasCraneCar,
            bool expected)
        {
            Assert.Equal(
                expected,
                FuseRealisticRerailCraneGuardPatches.ShouldRunCountCoupledMowCars(
                    hasCraneCar));
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, true)]
        public void InitialPopulate_WaitsForBuilderAssets(
            bool hasBuilderAssets,
            bool expected)
        {
            Assert.Equal(
                expected,
                FuseRealisticRerailCraneGuardPatches.ShouldPopulateOnEnable(
                    hasBuilderAssets));
        }
    }
}
