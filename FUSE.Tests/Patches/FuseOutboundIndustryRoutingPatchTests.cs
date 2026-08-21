using System;
using System.Collections.Generic;
using FUSE.Infrastructure;
using FUSE.Patches;
using HarmonyLib;
using Model.Ops;
using Xunit;

namespace FUSE.Tests.Patches
{
    public sealed class FuseOutboundIndustryRoutingPatchTests
    {
        [Theory]
        [InlineData(false, false, false, 0)]
        [InlineData(false, true, false, 1)]
        [InlineData(false, false, true, 2)]
        [InlineData(false, true, true, 2)]
        [InlineData(true, false, false, 2)]
        public void ResolveMode_preserves_opt_in_and_legacy_precedence(
            bool explicitOptIn,
            bool absolute,
            bool configurable,
            int expected)
        {
            Assert.Equal(expected, (int)FuseOutboundIndustryRoutingPatch.ResolveMode(
                explicitOptIn,
                absolute,
                configurable));
        }

        [Theory]
        [InlineData(0f, 0)]
        [InlineData(0.24f, 0)]
        [InlineData(0.25f, 0)]
        [InlineData(0.26f, 1)]
        [InlineData(0.99f, 1)]
        public void Weighted_selection_uses_remaining_capacity(float sample, int expected)
        {
            Assert.Equal(expected, FuseOutboundIndustryRoutingPatch.SelectWeightedIndex(
                new[] { 1f, 3f },
                sample));
        }

        [Fact]
        public void Weighted_selection_handles_empty_and_zero_weight_sets()
        {
            Assert.Equal(-1, FuseOutboundIndustryRoutingPatch.SelectWeightedIndex(Array.Empty<float>(), 0.5f));
            Assert.Equal(1, FuseOutboundIndustryRoutingPatch.SelectWeightedIndex(new[] { 0f, 0f }, 0.75f));
        }

        [Fact]
        public void Shuffle_is_deterministic_with_a_supplied_random_source()
        {
            var first = new List<int> { 1, 2, 3, 4, 5 };
            var second = new List<int> { 1, 2, 3, 4, 5 };

            FuseOutboundIndustryBlockingPatch.Shuffle(first, new Random(19));
            FuseOutboundIndustryBlockingPatch.Shuffle(second, new Random(19));

            Assert.Equal(first, second);
            Assert.NotEqual(new[] { 1, 2, 3, 4, 5 }, first);
        }

        [Fact]
        public void Settings_clamp_untrusted_routing_values()
        {
            Assert.Equal(0f, FuseSettings.ClampOutboundIndustryRerouteChance(-5f));
            Assert.Equal(1f, FuseSettings.ClampOutboundIndustryRerouteChance(99f));
            Assert.Equal(FuseSettings.DefaultOutboundIndustryRerouteChance,
                FuseSettings.ClampOutboundIndustryRerouteChance(float.NaN));
            Assert.Equal(0.1f, FuseSettings.ClampOutboundIndustryFillFactor(-5f));
            Assert.Equal(3f, FuseSettings.ClampOutboundIndustryFillFactor(99f));
            Assert.Equal(FuseSettings.DefaultOutboundIndustryFillFactor,
                FuseSettings.ClampOutboundIndustryFillFactor(float.NaN));
            Assert.Equal(0f, FuseSettings.ClampOutboundIndustryPaymentMultiplier(-5f));
            Assert.Equal(10f, FuseSettings.ClampOutboundIndustryPaymentMultiplier(99f));
        }

        [Fact]
        public void Harmony_targets_match_the_installed_game_contract()
        {
            Assert.NotNull(AccessTools.Method(typeof(OpsController), nameof(OpsController.AddOrderForOutboundEmptyCar)));
            Assert.NotNull(AccessTools.Method(typeof(OpsController), nameof(OpsController.AddOrderForOutboundLoadedCar)));
            Assert.NotNull(AccessTools.Method(typeof(IndustryContext), "AddOrderedCars"));
        }
    }
}
