using FUSE.Patches;
using Game;
using HarmonyLib;
using Model.Ops;
using Xunit;

namespace FUSE.Tests.Patches
{
    public sealed class FuseC1CdCompatibilityPatchTests
    {
        [Theory]
        [InlineData(0, 8f, 150, 0f, 24f, 0, 10.5f)]
        [InlineData(0, 4f, 30, 6f, 18f, 0, 6f)]
        [InlineData(0, 17f, 120, 6f, 18f, 1, 6f)]
        [InlineData(0, 12f, 30, 22f, 6f, 0, 22f)]
        [InlineData(0, 23f, 30, 22f, 6f, 0, 23.5f)]
        public void Schedule_policy_respects_interval_and_service_window(
            int day,
            float hour,
            int interval,
            float notBefore,
            float notAfter,
            int expectedDay,
            float expectedHour)
        {
            var actual = FuseC1CdSchedulePolicy.CalculateNextServiceTime(
                new GameDateTime(day, hour),
                interval,
                notBefore,
                notAfter);

            Assert.Equal(expectedDay, actual.Day);
            Assert.Equal(expectedHour, actual.Hours, 3);
        }

        [Fact]
        public void Schedule_policy_rejects_non_positive_interval_without_hanging()
        {
            var actual = FuseC1CdSchedulePolicy.CalculateNextServiceTime(
                new GameDateTime(2, 8f),
                0,
                0f,
                24f);

            Assert.Equal(2, actual.Day);
            Assert.Equal(10.5f, actual.Hours, 3);
        }

        [Fact]
        public void Patch_targets_current_static_service_method()
        {
            Assert.NotNull(AccessTools.Method(
                typeof(Interchange),
                nameof(Interchange.NextAvailableServiceTime),
                new[] { typeof(GameDateTime) }));
        }
    }
}
