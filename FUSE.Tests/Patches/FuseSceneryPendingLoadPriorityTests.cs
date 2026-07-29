using FUSE.Patches;
using UnityEngine;
using Xunit;

namespace FUSE.Tests.Patches
{
    public sealed class FuseSceneryPendingLoadPriorityTests
    {
        [Fact]
        public void FirstBacklogAlwaysEstablishesPriorityAnchor()
        {
            Assert.True(
                FuseSceneryLoadThrottlePatch.ShouldResortPendingLoads(
                    hasAnchor: false,
                    anchor: Vector3.zero,
                    cameraPosition: Vector3.zero,
                    pendingCount: 2,
                    pendingCountAtLastSort: 0,
                    framesSinceLastSort: int.MaxValue));
        }

        [Fact]
        public void CameraTeleportTriggersResortAfterFrameGap()
        {
            Assert.True(
                FuseSceneryLoadThrottlePatch.ShouldResortPendingLoads(
                    hasAnchor: true,
                    anchor: Vector3.zero,
                    cameraPosition: new Vector3(
                        FuseSceneryLoadThrottlePatch
                            .PendingPriorityResortDistance,
                        0f,
                        0f),
                    pendingCount: 20,
                    pendingCountAtLastSort: 20,
                    framesSinceLastSort:
                        FuseSceneryLoadThrottlePatch
                            .PendingPriorityResortFrameGap));
        }

        [Fact]
        public void DestinationQueueGrowthTriggersFollowupResort()
        {
            Assert.True(
                FuseSceneryLoadThrottlePatch.ShouldResortPendingLoads(
                    hasAnchor: true,
                    anchor: Vector3.zero,
                    cameraPosition: Vector3.zero,
                    pendingCount:
                        20 +
                        FuseSceneryLoadThrottlePatch
                            .PendingPriorityResortGrowth,
                    pendingCountAtLastSort: 20,
                    framesSinceLastSort:
                        FuseSceneryLoadThrottlePatch
                            .PendingPriorityResortFrameGap));
        }

        [Fact]
        public void SmallStableBacklogDoesNotResortEveryFrame()
        {
            Assert.False(
                FuseSceneryLoadThrottlePatch.ShouldResortPendingLoads(
                    hasAnchor: true,
                    anchor: Vector3.zero,
                    cameraPosition: new Vector3(10f, 0f, 0f),
                    pendingCount: 20,
                    pendingCountAtLastSort: 20,
                    framesSinceLastSort: 100));
            Assert.False(
                FuseSceneryLoadThrottlePatch.ShouldResortPendingLoads(
                    hasAnchor: true,
                    anchor: Vector3.zero,
                    cameraPosition: new Vector3(1000f, 0f, 0f),
                    pendingCount: 1,
                    pendingCountAtLastSort: 1,
                    framesSinceLastSort: 100));
        }

        [Fact]
        public void PendingPriority_PrefersImmediateThenForwardThenNearThenFifo()
        {
            var immediateDistance =
                FuseSceneryLoadThrottlePatch.ImmediateSceneryPriorityDistance;
            Assert.True(
                FuseSceneryLoadThrottlePatch.ComparePendingLoadPriority(
                    leftIsPriorityStructure: false,
                    leftInFront: false,
                    leftDistanceSqr:
                        immediateDistance * immediateDistance,
                    leftSequence: 1,
                    rightIsPriorityStructure: false,
                    rightInFront: true,
                    rightDistanceSqr:
                        (immediateDistance + 1f) *
                        (immediateDistance + 1f),
                    rightSequence: 0) < 0);
            Assert.True(
                FuseSceneryLoadThrottlePatch.ComparePendingLoadPriority(
                    leftIsPriorityStructure: false,
                    leftInFront: true,
                    leftDistanceSqr: 100000f,
                    leftSequence: 1,
                    rightIsPriorityStructure: false,
                    rightInFront: false,
                    rightDistanceSqr: 90000f,
                    rightSequence: 0) < 0);
            Assert.True(
                FuseSceneryLoadThrottlePatch.ComparePendingLoadPriority(
                    leftIsPriorityStructure: false,
                    leftInFront: true,
                    leftDistanceSqr: 100f,
                    leftSequence: 1,
                    rightIsPriorityStructure: false,
                    rightInFront: true,
                    rightDistanceSqr: 200f,
                    rightSequence: 0) < 0);
            Assert.True(
                FuseSceneryLoadThrottlePatch.ComparePendingLoadPriority(
                    leftIsPriorityStructure: false,
                    leftInFront: true,
                    leftDistanceSqr: 100f,
                    leftSequence: 0,
                    rightIsPriorityStructure: false,
                    rightInFront: true,
                    rightDistanceSqr: 100f,
                    rightSequence: 1) < 0);
        }

        [Fact]
        public void PendingPriority_PrefersStructureBeforeNearerBackground()
        {
            Assert.True(
                FuseSceneryLoadThrottlePatch.ComparePendingLoadPriority(
                    leftIsPriorityStructure: true,
                    leftInFront: false,
                    leftDistanceSqr: 100f,
                    leftSequence: 1,
                    rightIsPriorityStructure: false,
                    rightInFront: true,
                    rightDistanceSqr: 50f,
                    rightSequence: 0) < 0);
        }
    }
}
