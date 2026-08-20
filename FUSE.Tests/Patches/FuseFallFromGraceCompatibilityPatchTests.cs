using FUSE.Patches;
using HarmonyLib;
using Model.Ops;
using Track;
using UI.Builder;
using UI.CarInspector;
using Xunit;

namespace FUSE.Tests.Patches
{
    public sealed class FuseFallFromGraceCompatibilityPatchTests
    {
        [Theory]
        [InlineData(0, 0, 1, 0, 0)]
        [InlineData(2, 0, 2, 1, 5)]
        [InlineData(0, 3, 1, 0, 3)]
        [InlineData(2, 0, int.MaxValue, int.MaxValue, int.MaxValue)]
        public void Grace_adjustment_is_identity_configurable_and_overflow_safe(
            int baseDays,
            int minimum,
            int multiplier,
            int added,
            int expected)
        {
            Assert.Equal(
                expected,
                FuseFallFromGraceCalculationPatch.AdjustGraceDays(baseDays, minimum, multiplier, added));
        }

        [Fact]
        public void Grace_patch_targets_the_location_overload()
        {
            var target = AccessTools.Method(
                typeof(OpsController),
                "CalculateGraceDays",
                new[] { typeof(Location), typeof(Location) });

            Assert.NotNull(target);
        }

        [Fact]
        public void Inspector_patch_targets_the_waybill_panel_overload()
        {
            var target = AccessTools.Method(
                typeof(CarInspector),
                "PopulateWaybillPanel",
                new[] { typeof(UIPanelBuilder), typeof(Waybill) });

            Assert.NotNull(target);
        }
    }
}
