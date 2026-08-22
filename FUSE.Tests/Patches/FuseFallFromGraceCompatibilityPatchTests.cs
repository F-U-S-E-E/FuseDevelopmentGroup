using FUSE.Patches;
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
        [InlineData(int.MaxValue, int.MinValue, -2, 0, int.MinValue)]
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

        [Theory]
        [InlineData("2 days", true, "2 days remaining")]
        [InlineData("3 hours", false, "3 hours overdue")]
        [InlineData(null, true, " remaining")]
        public void Due_status_is_total_and_distinguishes_remaining_from_overdue(
            string interval,
            bool remaining,
            string expected)
        {
            Assert.Equal(expected, FuseFallFromGraceInspectorPatch.FormatDueStatus(interval, remaining));
        }

    }
}
