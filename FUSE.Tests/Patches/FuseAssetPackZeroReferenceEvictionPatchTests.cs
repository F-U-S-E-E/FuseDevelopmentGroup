using FUSE.Patches;
using Xunit;

namespace FUSE.Tests.Patches
{
    public sealed class FuseAssetPackZeroReferenceEvictionPatchTests
    {
        [Theory]
        [InlineData(-1, true)]
        [InlineData(0, true)]
        [InlineData(1, false)]
        [InlineData(8, false)]
        public void ShouldEvict_OnlyEvictsNonPositiveReferenceCounts(
            int referenceCount,
            bool expected)
        {
            Assert.Equal(
                expected,
                FuseAssetPackZeroReferenceEvictionPatch.ShouldEvict(referenceCount));
        }
    }
}
