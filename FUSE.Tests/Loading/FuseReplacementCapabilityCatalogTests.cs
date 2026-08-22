using FUSE.Loading;
using Xunit;

namespace FUSE.Tests.Loading
{
    public sealed class FuseReplacementCapabilityCatalogTests
    {
        [Theory]
        [InlineData("Zamu.StrangeCustoms")]
        [InlineData("Zamu.ConfusingSupplements.FUSE")]
        [InlineData("Zamu.C1CD")]
        [InlineData("C1CD.FUSE")]
        [InlineData("Zamu.FallFromGrace")]
        [InlineData("FallFromGrace.FUSE")]
        [InlineData("Zamu.AbsoluteMadness")]
        [InlineData("AbsoluteMadness.FUSE")]
        [InlineData("Zamu.SomeKindOfMadness")]
        [InlineData("SomeKindOfMadness.FUSE")]
        [InlineData("Zamu.Interchange2Interchange")]
        [InlineData("Interchange2Interchange.FUSE")]
        [InlineData("AlinaNova21.AlinasMapMod")]
        [InlineData("AssetLoader")]
        [InlineData("Railloader.Injector")]
        [InlineData("Railloader.Interchange.FUSE")]
        public void IsProvided_accepts_only_declared_replacement_families(string packageId)
        {
            Assert.True(FuseReplacementCapabilityCatalog.IsProvided(packageId));
        }

        [Theory]
        [InlineData("AlinaNova21.AlinasMapExpansionSW")]
        [InlineData("Joo.SignalsEverywhere")]
        [InlineData("C_L_B.DKW")]
        [InlineData("Embedded.BuildingBlocks")]
        [InlineData("Zamu.SerialTrafficControl")]
        public void IsProvided_does_not_waive_real_content_dependencies(string packageId)
        {
            Assert.False(FuseReplacementCapabilityCatalog.IsProvided(packageId));
        }
    }
}
