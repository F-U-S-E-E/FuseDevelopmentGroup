using FUSE.Authoring.Data;
using FUSE.Loading;
using Xunit;

namespace FUSE.Tests.Loading
{
    public sealed class FuseIndustryComponentOwnershipTests
    {
        [Fact]
        public void ClaimId_MatchesEquivalentDestinationsAcrossDifferentIndustryIds()
        {
            var first = new FuseIndustryComponent
            {
                Type = "consumer",
                Name = "Whittier Saw Mill MP1",
                TrackSpanIds = new[] { "R3", "R2" }
            };
            var second = new FuseIndustryComponent
            {
                Type = "consumer",
                Name = "Whittier Saw Mill MP1",
                TrackSpanPatch = new FuseStringListPatch
                {
                    Replace = new[] { "r2", "r3" }
                }
            };

            var firstId = FuseModLoader.BuildIndustryComponentClaimId(
                "bjorn-sawmill",
                "mp1",
                first);
            var secondId = FuseModLoader.BuildIndustryComponentClaimId(
                "appy-sawmill",
                "main-product-one",
                second);

            Assert.Equal(firstId, secondId, ignoreCase: true);
            Assert.Contains("Whittier Saw Mill MP1", firstId);
            Assert.Contains("spans=R2,R3", firstId);
        }

        [Fact]
        public void ClaimId_FallsBackToExactComponentForPartialPatchWithoutSemanticData()
        {
            var id = FuseModLoader.BuildIndustryComponentClaimId(
                "sylva-paperboard",
                "R2-R3",
                new FuseIndustryComponent { Partial = true });

            Assert.Equal("component:sylva-paperboard.R2-R3", id);
        }
    }
}
