using System.Collections.Generic;
using FUSE.Patches;
using Xunit;

namespace FUSE.Tests.Patches
{
    public sealed class FuseCompanyStarterPlacementPatchTests
    {
        [Fact]
        public void StarterPool_RequiresAppalachianWhittierStartDefinition()
        {
            Assert.False(
                FuseCompanyStarterPlacementPatch.ShouldQueueStarterSetup(
                    "ewh-company",
                    new[] { "FUSE", "Some.Other.Mod" }));
            Assert.True(
                FuseCompanyStarterPlacementPatch.ShouldQueueStarterSetup(
                    "ewh-company",
                    new[]
                    {
                        "FUSE",
                        "KingG.Appalachian-Railway.start-whit",
                    }));
        }

        [Fact]
        public void StarterPool_DoesNotAffectOtherCompanyStarts()
        {
            var loaded = new[]
            {
                "KingG.Appalachian-Railway.start-whit",
            };

            Assert.False(
                FuseCompanyStarterPlacementPatch.ShouldQueueStarterSetup(
                    "ela-company",
                    loaded));
            Assert.False(
                FuseCompanyStarterPlacementPatch.ShouldQueueStarterSetup(
                    "",
                    loaded));
        }

        [Theory]
        [InlineData(false, false, false, 1)]
        [InlineData(true, true, false, 1)]
        [InlineData(true, false, true, 0)]
        public void StarterPool_PreparesQueueOnlyForEligibleInactiveSetup(
            bool shouldQueue,
            bool presentationActive,
            bool expectedPrepared,
            int expectedRemaining)
        {
            var pending = new Queue<string>();
            pending.Enqueue("retained-cut");

            var prepared =
                FuseCompanyStarterPlacementPatch.TryPrepareStarterQueue(
                    pending,
                    shouldQueue,
                    presentationActive);

            Assert.Equal(expectedPrepared, prepared);
            Assert.Equal(expectedRemaining, pending.Count);
        }

        [Theory]
        [InlineData(false, 3, 0, false)]
        [InlineData(true, 3, 0, false)]
        [InlineData(true, 3, 2, false)]
        [InlineData(true, 3, 3, true)]
        [InlineData(true, 0, 0, false)]
        public void StarterPool_ConsumesCutOnlyAfterConfirmedCompletePlacement(
            bool callbackReportedPlaced,
            int expectedCarCount,
            int placedCarCount,
            bool expected)
        {
            Assert.Equal(
                expected,
                FuseCompanyStarterPlacementPatch.WasPlacementCommitted(
                    callbackReportedPlaced,
                    expectedCarCount,
                    placedCarCount));
        }

        [Theory]
        [InlineData(false, 0, false, 1)]
        [InlineData(true, 0, true, 0)]
        [InlineData(true, 1, false, 1)]
        public void StarterPool_DiscardsOnlySuccessfullyPreparedEmptyCuts(
            bool preparationSucceeded,
            int descriptorCount,
            bool expectedConsumed,
            int expectedRemaining)
        {
            var pending = new Queue<string>();
            pending.Enqueue("retained-cut");

            var consumed =
                FuseCompanyStarterPlacementPatch.TryConsumeConfirmedEmpty(
                    pending,
                    preparationSucceeded,
                    descriptorCount);

            Assert.Equal(expectedConsumed, consumed);
            Assert.Equal(expectedRemaining, pending.Count);
        }
    }
}
