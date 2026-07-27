using FUSE.Patches;
using Xunit;

namespace FUSE.Tests.Patches
{
    public sealed class FuseTrackStreamSchedulingTests
    {
        [Fact]
        public void PrivateEntryLayoutMatchesCurrentRailroaderAssembly()
        {
            Assert.True(FuseTrackRebuilderQueueProcessor.Available);
        }

        [Fact]
        public void CanProcessAnother_AlwaysAllowsOneItemForForwardProgress()
        {
            Assert.True(
                FuseTrackStreamScheduling.CanProcessAnother(
                    processed: 0,
                    elapsedMilliseconds: 100d,
                    budgetMilliseconds: 8d,
                    maximumPerFrame: 24));
        }

        [Fact]
        public void CanProcessAnother_StopsAtTimeOrItemBudget()
        {
            Assert.False(
                FuseTrackStreamScheduling.CanProcessAnother(
                    processed: 1,
                    elapsedMilliseconds: 8d,
                    budgetMilliseconds: 8d,
                    maximumPerFrame: 24));
            Assert.False(
                FuseTrackStreamScheduling.CanProcessAnother(
                    processed: 24,
                    elapsedMilliseconds: 1d,
                    budgetMilliseconds: 8d,
                    maximumPerFrame: 24));
            Assert.True(
                FuseTrackStreamScheduling.CanProcessAnother(
                    processed: 3,
                    elapsedMilliseconds: 2d,
                    budgetMilliseconds: 8d,
                    maximumPerFrame: 24));
        }

        [Theory]
        [InlineData(true, true, true)]
        [InlineData(true, false, false)]
        [InlineData(false, true, false)]
        [InlineData(false, false, false)]
        public void IsCurrentBuildRequest_RequiresRangeAndRegistration(
            bool inRange,
            bool registered,
            bool expected)
        {
            Assert.Equal(
                expected,
                FuseTrackStreamScheduling.IsCurrentBuildRequest(
                    inRange,
                    registered));
        }

        [Fact]
        public void CompareBuildPriority_CurrentVisibleNearTrackSortsLastForTailConsumption()
        {
            var currentVersusStale =
                FuseTrackStreamScheduling.CompareBuildPriority(
                    leftCurrent: true,
                    leftVisible: false,
                    leftDistanceSqr: 100f,
                    leftSequence: 0,
                    rightCurrent: false,
                    rightVisible: true,
                    rightDistanceSqr: 1f,
                    rightSequence: 1);
            var visibleVersusHidden =
                FuseTrackStreamScheduling.CompareBuildPriority(
                    leftCurrent: true,
                    leftVisible: true,
                    leftDistanceSqr: 100f,
                    leftSequence: 0,
                    rightCurrent: true,
                    rightVisible: false,
                    rightDistanceSqr: 1f,
                    rightSequence: 1);
            var nearVersusFar =
                FuseTrackStreamScheduling.CompareBuildPriority(
                    leftCurrent: true,
                    leftVisible: true,
                    leftDistanceSqr: 25f,
                    leftSequence: 0,
                    rightCurrent: true,
                    rightVisible: true,
                    rightDistanceSqr: 100f,
                    rightSequence: 1);

            Assert.True(currentVersusStale > 0);
            Assert.True(visibleVersusHidden > 0);
            Assert.True(nearVersusFar > 0);
        }

        [Fact]
        public void CompareBuildPriority_PreservesFifoWhenPriorityIsEqual()
        {
            var earlierVersusLater =
                FuseTrackStreamScheduling.CompareBuildPriority(
                    leftCurrent: true,
                    leftVisible: true,
                    leftDistanceSqr: 25f,
                    leftSequence: 0,
                    rightCurrent: true,
                    rightVisible: true,
                    rightDistanceSqr: 25f,
                    rightSequence: 1);

            // The earlier FIFO item sorts later so tail consumption sees it first.
            Assert.True(earlierVersusLater > 0);
        }
    }
}
