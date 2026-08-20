using System;
using FUSE.Infrastructure;
using Game;
using HarmonyLib;
using Model.Ops;
using Track;
using UI.Builder;
using UI.CarInspector;

namespace FUSE.Patches
{
    /// <summary>
    /// Native FUSE implementation of the small public behavior contract exposed
    /// by ZAMU FallFromGrace. The default transform is an identity operation, so
    /// advertising the replacement cannot silently alter an existing railroad.
    /// </summary>
    [HarmonyPatch(typeof(OpsController), "CalculateGraceDays", new[] { typeof(Location), typeof(Location) })]
    internal static class FuseFallFromGraceCalculationPatch
    {
        private static void Postfix(ref int __result)
        {
            __result = AdjustGraceDays(
                __result,
                FuseSettings.GraceMinimumDays,
                FuseSettings.GraceMultiplier,
                FuseSettings.GraceAddedDays);
        }

        internal static int AdjustGraceDays(int baseDays, int minimumDays, int multiplier, int addedDays)
        {
            var adjusted = ((long)baseDays * multiplier) + addedDays;
            adjusted = Math.Max(minimumDays, adjusted);
            return adjusted > int.MaxValue
                ? int.MaxValue
                : adjusted < int.MinValue
                    ? int.MinValue
                    : (int)adjusted;
        }
    }

    [HarmonyPatch(typeof(CarInspector), "PopulateWaybillPanel", new[] { typeof(UIPanelBuilder), typeof(Waybill) })]
    internal static class FuseFallFromGraceInspectorPatch
    {
        private static void Postfix(UIPanelBuilder builder, Waybill waybill)
        {
            if (waybill.PaymentOnArrival <= 0)
            {
                return;
            }

            try
            {
                var row = builder.AddField(
                    "Due",
                    () => FormatDue(waybill, TimeWeather.Now),
                    UIPanelBuilder.Frequency.Periodic);
                row.RectTransform.SetSiblingIndex(2);
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    "FUSE contained a FallFromGrace-compatible inspector-row error; " +
                    $"the rest of the car inspector remains usable: {ex.GetBaseException().Message}");
            }
        }

        internal static string FormatDue(Waybill waybill, GameDateTime now)
        {
            if (waybill.Completed)
            {
                return "Completed";
            }

            var due = waybill.Created.AddingDays(waybill.GraceDays);
            var interval = due.IntervalString(now, GameDateTimeInterval.Style.Full);
            return due.TotalSeconds >= now.TotalSeconds
                ? interval + " remaining"
                : interval + " overdue";
        }
    }
}
