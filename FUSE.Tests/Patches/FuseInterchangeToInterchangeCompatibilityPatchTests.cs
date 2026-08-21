using FUSE.Infrastructure;
using FUSE.Patches;
using HarmonyLib;
using Model.Ops;
using Xunit;

namespace FUSE.Tests.Patches
{
    public sealed class FuseInterchangeToInterchangeCompatibilityPatchTests
    {
        [Theory]
        [InlineData(30, 1f, 30)]
        [InlineData(30, 0.5f, 15)]
        [InlineData(30, 2f, 60)]
        [InlineData(30, 0f, 0)]
        [InlineData(-1, 1f, 0)]
        [InlineData(
            FuseSettings.InterchangeToInterchangeMaximumCarsLimit,
            2f,
            FuseSettings.InterchangeToInterchangeMaximumCarsLimit)]
        public void Maximum_cut_policy_is_bounded(
            int configured,
            float multiplier,
            int expected)
        {
            Assert.Equal(expected,
                FuseInterchangeToInterchangePolicy.ScaleMaximumCars(configured, multiplier));
        }

        [Theory]
        [InlineData(-1, 0)]
        [InlineData(30, 30)]
        [InlineData(999, FuseSettings.InterchangeToInterchangeMaximumCarsLimit)]
        public void User_setting_is_bounded(int value, int expected)
        {
            Assert.Equal(expected, FuseSettings.ClampInterchangeToInterchangeMaximumCars(value));
        }

        [Fact]
        public void Harmony_targets_match_the_installed_game_contract()
        {
            Assert.NotNull(AccessTools.Method(typeof(Interchange), nameof(Interchange.OrderCars)));
            Assert.NotNull(AccessTools.Method(typeof(OpsController), "RebuildCollections"));
        }
    }
}
