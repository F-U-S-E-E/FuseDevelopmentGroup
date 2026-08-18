using FUSE.Loading;
using Xunit;

namespace FUSE.Tests.Loading
{
    public sealed class FuseModLoaderTrackMergingTests
    {
        [Theory]
        [InlineData("ARC.Whittier.FUSE.arc-whittier", "ARC.Whittier.FUSE.arc-whittier", false, true)]
        [InlineData("ARC.Whittier.FUSE.arc-whittier", "ARC.Whittier.FUSE.arc-whittier", true, false)]
        [InlineData("ARC.Whittier.FUSE.arc-whittier", "another.package", false, false)]
        [InlineData("ARC.Whittier.FUSE.arc-whittier", null, false, false)]
        [InlineData(null, "ARC.Whittier.FUSE.arc-whittier", false, false)]
        public void MissingMergedSpan_IsReconciledOnlyForItsWinningOwner(
            string plannedOwner,
            string registryOwner,
            bool spanPresent,
            bool expected)
        {
            Assert.Equal(
                expected,
                FuseModLoader.ShouldReconcileMergedSpan(
                    plannedOwner,
                    registryOwner,
                    spanPresent));
        }
    }
}
