using FUSE.Authoring.Data;
using FUSE.Authoring.Data.Common;
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

        [Theory]
        [InlineData("S6jh", "Scv4", "S_AWHI_dtmt", "S_AWHI_dtmt", true)]
        [InlineData("A", "B", "A", "B", false)]
        [InlineData("A", "B", "B", "A", false)]
        [InlineData(null, "B", "A", "B", true)]
        [InlineData("A", "B", null, "B", false)]
        public void MergedSpan_RecreatesRuntimeObjectOnlyWhenEndpointTopologyChanges(
            string runtimeUpper,
            string runtimeLower,
            string replacementUpper,
            string replacementLower,
            bool expected)
        {
            Assert.Equal(
                expected,
                FuseModLoader.ShouldRecreateMergedSpanRuntime(
                    Span(runtimeUpper, runtimeLower),
                    Span(replacementUpper, replacementLower)));
        }

        private static FuseSpan Span(string upperSegmentId, string lowerSegmentId)
        {
            return new FuseSpan
            {
                Upper = new FuseTrackLocation { SegmentId = upperSegmentId },
                Lower = new FuseTrackLocation { SegmentId = lowerSegmentId }
            };
        }
    }
}
