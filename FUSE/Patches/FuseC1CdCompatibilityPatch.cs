using FUSE.Infrastructure;
using Game;
using HarmonyLib;
using Model.Ops;

namespace FUSE.Patches
{
    [HarmonyPatch(
        typeof(Interchange),
        nameof(Interchange.NextAvailableServiceTime),
        new[] { typeof(GameDateTime) })]
    internal static class FuseC1CdNextServiceTimePatch
    {
        private static bool Prefix(GameDateTime now, ref GameDateTime __result)
        {
            __result = FuseC1CdSchedulePolicy.CalculateNextServiceTime(
                now,
                FuseSettings.InterchangeServiceIntervalMinutes,
                FuseSettings.InterchangeNotBeforeHour,
                FuseSettings.InterchangeNotAfterHour);
            return false;
        }
    }

    internal static class FuseC1CdSchedulePolicy
    {
        internal static GameDateTime CalculateNextServiceTime(
            GameDateTime now,
            int intervalMinutes,
            float notBeforeHour,
            float notAfterHour)
        {
            var interval = FuseSettings.NormalizeInterchangeServiceIntervalMinutes(intervalMinutes);
            var start = FuseSettings.ClampInterchangeServiceHour(notBeforeHour);
            var end = FuseSettings.ClampInterchangeServiceHour(notAfterHour);
            var candidate = now.AddingMinutes(interval).RoundingMinutes(5);

            if (start <= 0f && end >= 24f)
            {
                return candidate;
            }

            if (start <= end)
            {
                if (candidate.Hours < start)
                {
                    return candidate.WithHours(start).RoundingMinutes(5);
                }

                if (candidate.Hours > end)
                {
                    return candidate.AddingDays(1f).WithHours(start).RoundingMinutes(5);
                }

                return candidate;
            }

            // A window whose start is later than its end crosses midnight
            // (for example 22:00–06:00). Only the daytime gap is excluded.
            if (candidate.Hours > end && candidate.Hours < start)
            {
                return candidate.WithHours(start).RoundingMinutes(5);
            }

            return candidate;
        }
    }
}
