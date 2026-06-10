using FUSE.Runtime.API;
using Xunit;

namespace FUSE.Tests.API
{
    public class FuseVisualConditionAPITests
    {
        [Fact]
        public void ResolveOverride_ReturnsNullWhenNeitherKeyIsSet()
        {
            Assert.Null(FuseVisualConditionAPI.ResolveOverride(null, null));
        }

        [Fact]
        public void ResolveOverride_PrefersFuseKeyOverLegacyKey()
        {
            Assert.Equal(0.4f, FuseVisualConditionAPI.ResolveOverride(0.4f, 0.9f));
        }

        [Fact]
        public void ResolveOverride_FallsBackToLegacyKey()
        {
            Assert.Equal(0.7f, FuseVisualConditionAPI.ResolveOverride(null, 0.7f));
        }

        [Theory]
        [InlineData(1.5f, 1f)]
        [InlineData(-0.2f, 0f)]
        [InlineData(0.5f, 0.5f)]
        public void ResolveOverride_ClampsToUnitRange(float stored, float expected)
        {
            Assert.Equal(expected, FuseVisualConditionAPI.ResolveOverride(stored, null));
        }

        [Theory]
        [InlineData(1.5f, 1f)]
        [InlineData(-0.2f, 0f)]
        public void ResolveOverride_ClampsLegacyValuesToo(float stored, float expected)
        {
            Assert.Equal(expected, FuseVisualConditionAPI.ResolveOverride(null, stored));
        }

        [Theory]
        [InlineData(0.8f, 0.5f, 0.5f)] // override below mechanical: car looks more worn
        [InlineData(0.3f, 0.9f, 0.3f)] // override above mechanical: capped at mechanical
        [InlineData(0.6f, 0.6f, 0.6f)]
        public void EffectiveCondition_CapsAtMechanicalConditionByDefault(float actual, float visual, float expected)
        {
            Assert.Equal(expected, FuseVisualConditionAPI.EffectiveCondition(actual, visual, decoupled: false));
        }

        [Theory]
        [InlineData(0.3f, 0.9f, 0.9f)] // decoupled: worn car can look fresh
        [InlineData(0.9f, 0.2f, 0.2f)]
        public void EffectiveCondition_UsesOverrideVerbatimWhenDecoupled(float actual, float visual, float expected)
        {
            Assert.Equal(expected, FuseVisualConditionAPI.EffectiveCondition(actual, visual, decoupled: true));
        }

        [Fact]
        public void EffectiveCondition_ClampsOutOfRangeInputs()
        {
            Assert.Equal(1f, FuseVisualConditionAPI.EffectiveCondition(2f, 1.5f, decoupled: false));
            Assert.Equal(0f, FuseVisualConditionAPI.EffectiveCondition(0.5f, -1f, decoupled: false));
            Assert.Equal(1f, FuseVisualConditionAPI.EffectiveCondition(0.1f, 3f, decoupled: true));
        }

        [Theory]
        [InlineData(0f, "0%")]
        [InlineData(0.25f, "25%")]
        [InlineData(0.5f, "50%")]
        [InlineData(0.996f, "100%")] // rounds to the nearest whole percent
        [InlineData(0.004f, "0%")]
        [InlineData(1f, "100%")]
        [InlineData(2f, "100%")] // clamped before formatting
        public void FormatPercent_RendersWholePercentages(float value, string expected)
        {
            Assert.Equal(expected, FuseVisualConditionAPI.FormatPercent(value));
        }

        [Theory]
        [InlineData(0.5f, 0.5f)]
        [InlineData(0.4567f, 0.46f)] // quantized to whole percents so drag ticks dedupe
        [InlineData(-0.2f, 0f)]
        public void NormalizeForStore_QuantizesToWholePercents(float raw, float expected)
        {
            Assert.Equal(expected, FuseVisualConditionAPI.NormalizeForStore(raw, decoupled: false));
        }

        [Fact]
        public void NormalizeForStore_ClearsAtFullValueInCappedMode()
        {
            // In capped mode a 100% override is indistinguishable from "no
            // override"; storing it anyway would silently force the car
            // pristine if the player later decouples the look.
            Assert.Null(FuseVisualConditionAPI.NormalizeForStore(1f, decoupled: false));
            Assert.Null(FuseVisualConditionAPI.NormalizeForStore(0.999f, decoupled: false));
        }

        [Fact]
        public void NormalizeForStore_KeepsFullValueWhenDecoupled()
        {
            // Decoupled, 100% is meaningful: it forces a worn car to render
            // pristine, so it must be stored rather than cleared.
            Assert.Equal(1f, FuseVisualConditionAPI.NormalizeForStore(1f, decoupled: true));
        }

        [Fact]
        public void Clamp01_NeutralizesNaN()
        {
            Assert.Equal(1f, FuseVisualConditionAPI.Clamp01(float.NaN));
            Assert.Equal(1f, FuseVisualConditionAPI.EffectiveCondition(float.NaN, float.NaN, decoupled: true));
        }

        // ----- spawn-randomization roll mapping -----
        // The roll is injected, so the full min/max/roll contract —
        // interpolation, clamping, and reversed-bounds normalization — is
        // assertable without the engine RNG or a live car.

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
