using System;
using System.Linq;
using System.Reflection;
using FUSE.Infrastructure;
using HarmonyLib;
using Model.Ops;
using Track;

namespace FUSE.Patches
{
    /// <summary>
    /// Prevents PassengerStop.OnEnable from interpolating a span whose segment
    /// was removed by a conflicting track package. Railroader only checks that
    /// nullable endpoints have values before calling Graph.Lerp; a resolved
    /// location can still contain a null segment and throw from Location.Flipped.
    /// </summary>
    [HarmonyPatch]
    internal static class FusePassengerStopInvalidSpanGuardPatch
    {
        private static MethodInfo TargetMethod()
        {
            return AccessTools.Method(typeof(PassengerStop), "OnEnable");
        }

        private static void Prefix(PassengerStop __instance)
        {
            if (__instance == null)
            {
                return;
            }

            TrackSpan[] spans;
            try
            {
                spans = __instance.GetComponentsInChildren<TrackSpan>(true);
            }
            catch
            {
                return;
            }

            foreach (var span in spans.Where(span => span != null && !IsUsable(span)))
            {
                var id = span?.id ?? "<unknown>";
                try
                {
                    // Keep the TrackSpan object available for diagnostics, but
                    // clear endpoints so PassengerStop's own null checks skip it.
                    span.lower = null;
                    span.upper = null;
                    var count = FuseRuntimeGuardCounters.RecordPassengerStopSpanSanitized();
                    if (count <= 10)
                    {
                        FuseLog.Warning(
                            $"FUSE sanitized invalid passenger-stop span '{id}' on " +
                            $"'{__instance.identifier ?? __instance.name}'. A conflicting track package removed " +
                            "an endpoint segment; the stop remains loaded without that span.");
                    }
                }
                catch (Exception ex)
                {
                    FuseLog.Warning(
                        $"FUSE could not sanitize invalid passenger-stop span '{id}': " +
                        ex.GetBaseException().Message);
                }
            }
        }

        private static bool IsUsable(TrackSpan span)
        {
            if (span == null)
            {
                return false;
            }

            try
            {
                return span.IsValid;
            }
            catch
            {
                return false;
            }
        }
    }
}
