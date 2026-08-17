using FUSE.Patches;
using Xunit;

namespace FUSE.Tests.Patches
{
    public sealed class FuseEquipmentRetentionPolicyTests
    {
        [Theory]
        [InlineData(false, true)]
        [InlineData(true, false)]
        public void ShouldReleaseAbandonedModel_OnlyReleasesCanceledCompletion(
            bool modelLoadPending,
            bool expected)
        {
            Assert.Equal(
                expected,
                FuseEquipmentRetentionPolicy.ShouldReleaseAbandonedModel(modelLoadPending));
        }

        [Fact]
        public void ReflectionLayoutMatchesCurrentRailroaderAssembly()
        {
            Assert.True(FuseCarCullerPendingProcessor.Available);
            Assert.True(FuseCarModelCompletionScheduler.Available);
            Assert.True(FuseAggregateLoadMaterialReleasePatch.Available);
        }

        [Fact]
        public void CompletionBudget_AlwaysMakesForwardProgress()
        {
            Assert.True(
                FuseEquipmentRetentionPolicy.CanCompleteAnother(
                    completed: 0,
                    elapsedMilliseconds: 500,
                    budgetMilliseconds: 6,
                    maximumPerFrame: 4));
            Assert.False(
                FuseEquipmentRetentionPolicy.CanCompleteAnother(
                    completed: 1,
                    elapsedMilliseconds: 6,
                    budgetMilliseconds: 6,
                    maximumPerFrame: 4));
            Assert.False(
                FuseEquipmentRetentionPolicy.CanCompleteAnother(
                    completed: 4,
                    elapsedMilliseconds: 0,
                    budgetMilliseconds: 6,
                    maximumPerFrame: 4));
        }

        [Fact]
        public void CompletionPriority_PrefersVisibleThenNearThenFifo()
        {
            Assert.True(
                FuseEquipmentRetentionPolicy.CompareCompletionPriority(
                    leftVisible: true,
                    leftDistanceSqr: 100,
                    leftSequence: 0,
                    rightVisible: false,
                    rightDistanceSqr: 1,
                    rightSequence: 1) > 0);
            Assert.True(
                FuseEquipmentRetentionPolicy.CompareCompletionPriority(
                    leftVisible: true,
                    leftDistanceSqr: 10,
                    leftSequence: 1,
                    rightVisible: true,
                    rightDistanceSqr: 100,
                    rightSequence: 0) > 0);
            Assert.True(
                FuseEquipmentRetentionPolicy.CompareCompletionPriority(
                    leftVisible: true,
                    leftDistanceSqr: 10,
                    leftSequence: 0,
                    rightVisible: true,
                    rightDistanceSqr: 10,
                    rightSequence: 1) > 0);
        }

        [Fact]
        public void LoadPriority_PrefersVisibleThenNearThenFifo()
        {
            Assert.True(
                FuseEquipmentRetentionPolicy.CompareLoadPriority(
                    leftVisible: true,
                    leftDistanceSqr: 100,
                    leftSequence: 1,
                    rightVisible: false,
                    rightDistanceSqr: 1,
                    rightSequence: 0) < 0);
            Assert.True(
                FuseEquipmentRetentionPolicy.CompareLoadPriority(
                    leftVisible: true,
                    leftDistanceSqr: 10,
                    leftSequence: 1,
                    rightVisible: true,
                    rightDistanceSqr: 100,
                    rightSequence: 0) < 0);
            Assert.True(
                FuseEquipmentRetentionPolicy.CompareLoadPriority(
                    leftVisible: true,
                    leftDistanceSqr: 10,
                    leftSequence: 0,
                    rightVisible: true,
                    rightDistanceSqr: 10,
                    rightSequence: 1) < 0);
        }

        [Fact]
        public void SceneryBacklog_ReducesEquipmentContentionWithoutStoppingProgress()
        {
            Assert.Equal(
                FuseEquipmentRetentionPolicy.SceneryBusyMaxLoadRetainsPerFrame,
                FuseEquipmentRetentionPolicy.LoadRetainMaximum(
                    loading: true,
                    sceneryBusy: true));
            Assert.Equal(
                FuseEquipmentRetentionPolicy.SceneryBusyMaxCompletionsPerFrame,
                FuseEquipmentRetentionPolicy.CompletionMaximum(
                    loading: false,
                    sceneryBusy: true));
            Assert.True(
                FuseEquipmentRetentionPolicy.LoadRetainMaximum(
                    loading: false,
                    sceneryBusy: true) > 0);
            Assert.True(
                FuseEquipmentRetentionPolicy.CompletionMaximum(
                    loading: true,
                    sceneryBusy: true) > 0);
        }
    }
}
