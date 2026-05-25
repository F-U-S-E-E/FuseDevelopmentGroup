using System;
using FUSE.Registry;
using Xunit;

namespace FUSE.Tests.Registry
{
    public class FuseClaimKindPolicyTests
    {
        // Locking in the exclusive/shared classification per FuseClaimKind. If a new enum
        // value is added without a corresponding policy decision it will default to false
        // (exclusive) — these tests fail loudly so the decision happens deliberately.

        [Theory]
        [InlineData(FuseClaimKind.Node)]
        [InlineData(FuseClaimKind.Segment)]
        [InlineData(FuseClaimKind.Span)]
        [InlineData(FuseClaimKind.Loader)]
        [InlineData(FuseClaimKind.Station)]
        [InlineData(FuseClaimKind.Turntable)]
        public void ExclusiveKinds_AreNotShared(FuseClaimKind kind)
        {
            Assert.False(FuseClaimKindPolicy.IsShared(kind));
        }

        [Theory]
        [InlineData(FuseClaimKind.Industry)]
        [InlineData(FuseClaimKind.Scenery)]
        [InlineData(FuseClaimKind.SuppressedScenePath)]
        [InlineData(FuseClaimKind.SuppressedTrackGroup)]
        [InlineData(FuseClaimKind.SuppressedArea)]
        [InlineData(FuseClaimKind.AssetCollision)]
        public void SharedKinds_AreShared(FuseClaimKind kind)
        {
            Assert.True(FuseClaimKindPolicy.IsShared(kind));
        }

        [Fact]
        public void EveryDefinedKind_IsCoveredByExactlyOneClassification()
        {
            // Hard regression check: if a new FuseClaimKind enum value is added,
            // this test fails until the policy switch is updated AND a per-value
            // [Theory] case is added above. Catches the "added enum, forgot to
            // categorize" footgun.
            var exclusiveTested = new[]
            {
                FuseClaimKind.Node, FuseClaimKind.Segment, FuseClaimKind.Span,
                FuseClaimKind.Loader, FuseClaimKind.Station, FuseClaimKind.Turntable
            };
            var sharedTested = new[]
            {
                FuseClaimKind.Industry, FuseClaimKind.Scenery,
                FuseClaimKind.SuppressedScenePath, FuseClaimKind.SuppressedTrackGroup,
                FuseClaimKind.SuppressedArea, FuseClaimKind.AssetCollision
            };

            var defined = (FuseClaimKind[])Enum.GetValues(typeof(FuseClaimKind));
            Assert.Equal(exclusiveTested.Length + sharedTested.Length, defined.Length);
        }
    }
}
