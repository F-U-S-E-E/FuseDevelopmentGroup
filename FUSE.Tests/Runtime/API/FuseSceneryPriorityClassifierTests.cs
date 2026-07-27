using FUSE.Runtime.API;
using Xunit;

namespace FUSE.Tests.Runtime.API
{
    public sealed class FuseSceneryPriorityClassifierTests
    {
        [Theory]
        [InlineData("BryShopContinuous", "ContinuousC")]
        [InlineData("ZCustom", "aspen-corner-drug")]
        [InlineData("ZCustom", "WarehouseBrick")]
        [InlineData("ZCustom", "house-sunbeam")]
        [InlineData("CarShopInterior", "UnknownPrefab")]
        public void StructuresEnterDestinationPriorityLane(
            string placementId,
            string assetIdentifier)
        {
            Assert.True(
                FuseSceneryPriorityClassifier.IsPriorityStructure(
                    placementId,
                    assetIdentifier));
        }

        [Theory]
        [InlineData("HouseTree03", "Tree 510")]
        [InlineData("ZCustom", "Aspen Oak 1")]
        [InlineData("Zflat", "aspenflat20")]
        [InlineData("ZCustom", "UnknownPrefab")]
        public void BackgroundAndUnknownSceneryStayOnNormalLane(
            string placementId,
            string assetIdentifier)
        {
            Assert.False(
                FuseSceneryPriorityClassifier.IsPriorityStructure(
                    placementId,
                    assetIdentifier));
        }
    }
}
