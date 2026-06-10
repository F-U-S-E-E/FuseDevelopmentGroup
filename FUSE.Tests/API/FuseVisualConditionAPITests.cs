using FUSE.Runtime.API;
using Xunit;

namespace FUSE.Tests.API
{
    /// <summary>
    /// Tests for the pure spawn-condition mapping behind the
    /// "randomize visual condition on spawn" setting. The roll is
    /// injected, so the full min/max/roll contract — interpolation,
    /// clamping, and reversed-bounds normalization — is assertable
    /// without the engine RNG or a live car.
    /// </summary>
    public class FuseVisualConditionAPITests
    {
        [Fact]
        public void ComputeSpawnCondition_RollZero_ReturnsMin()
        {
            Assert.Equal(0.6f, FuseVisualConditionAPI.ComputeSpawnCondition(0.6f, 1f, 0f));
        }

        [Fact]
        public void ComputeSpawnCondition_RollOne_ReturnsMax()
        {
            Assert.Equal(1f, FuseVisualConditionAPI.ComputeSpawnCondition(0.6f, 1f, 1f));
        }

        [Fact]
        public void ComputeSpawnCondition_MidRoll_Interpolates()
        {
            Assert.Equal(0.5f, FuseVisualConditionAPI.ComputeSpawnCondition(0.25f, 0.75f, 0.5f), 5);
        }

        [Fact]
        public void ComputeSpawnCondition_ReversedBounds_AreNormalized()
        {
            // A user typing min=0.9, max=0.5 should get the 0.5..0.9 range,
            // not a roll outside it.
            Assert.Equal(0.5f, FuseVisualConditionAPI.ComputeSpawnCondition(0.9f, 0.5f, 0f));
            Assert.Equal(0.9f, FuseVisualConditionAPI.ComputeSpawnCondition(0.9f, 0.5f, 1f));
        }

        [Fact]
        public void ComputeSpawnCondition_BoundsOutsideUnitRange_AreClamped()
        {
            Assert.Equal(0f, FuseVisualConditionAPI.ComputeSpawnCondition(-2f, 1.5f, 0f));
            Assert.Equal(1f, FuseVisualConditionAPI.ComputeSpawnCondition(-2f, 1.5f, 1f));
        }

        [Theory]
        [InlineData(-0.5f, 0.6f)]
        [InlineData(1.5f, 1f)]
        public void ComputeSpawnCondition_RollOutsideUnitRange_IsClamped(float roll, float expected)
        {
            Assert.Equal(expected, FuseVisualConditionAPI.ComputeSpawnCondition(0.6f, 1f, roll));
        }

        [Fact]
        public void ComputeSpawnCondition_EqualBounds_ReturnsThatValue()
        {
            Assert.Equal(0.7f, FuseVisualConditionAPI.ComputeSpawnCondition(0.7f, 0.7f, 0.42f));
        }
    }
}
