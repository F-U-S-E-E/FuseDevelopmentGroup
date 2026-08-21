using FUSE.Patches;
using HarmonyLib;
using Model;
using UI.Map;
using UI.Tags;
using Xunit;

namespace FUSE.Tests.Patches
{
    public sealed class FuseForYourConvenienceCompatibilityPatchTests
    {
        [Theory]
        [InlineData(0f, "0.0 MPH")]
        [InlineData(10f, "22.4 MPH")]
        [InlineData(-10f, "22.4 MPH")]
        public void Speed_is_displayed_as_absolute_mph(float metersPerSecond, string expected)
        {
            Assert.Equal(expected, FuseForYourConveniencePolicy.FormatSpeed(metersPerSecond));
        }

        [Theory]
        [InlineData(5f, 10f, "Coal", "50% Coal")]
        [InlineData(50f, 10f, "Coal", "100% Coal")]
        [InlineData(5f, 0f, null, "0% Unknown load")]
        public void Load_summary_is_bounded_and_handles_bad_capacity(
            float quantity,
            float capacity,
            string description,
            string expected)
        {
            Assert.Equal(expected,
                FuseForYourConveniencePolicy.FormatLoad(quantity, capacity, description));
        }

        [Theory]
        [InlineData("Whittier", "whittier", null, true)]
        [InlineData("Whittier Station", "WHITTIER_AREA", null, true)]
        [InlineData("Bryson Depot", null, "bryson-depot", true)]
        [InlineData("Whittier Saw Mill", "whittier", null, false)]
        [InlineData("Whittier Saw Mill", "bryson", "bryson-depot", false)]
        [InlineData("R1", "r1", null, false)]
        public void Station_actions_require_a_matching_named_icon(
            string iconIdentity,
            string areaId,
            string passengerStopId,
            bool expected)
        {
            Assert.Equal(
                expected,
                FuseForYourConvenienceStationMapPatch.IsStationIdentityMatch(
                    iconIdentity,
                    areaId,
                    passengerStopId));
        }

        [Fact]
        public void Harmony_targets_match_the_installed_game_contract()
        {
            Assert.NotNull(AccessTools.Method(typeof(Car), nameof(Car.Setup)));
            Assert.NotNull(AccessTools.Method(typeof(TagController), "UpdateTag"));
            Assert.NotNull(AccessTools.Method(typeof(MapBuilder), nameof(MapBuilder.Rebuild)));
        }
    }
}
