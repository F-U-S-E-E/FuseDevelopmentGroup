using System;
using FUSE.Infrastructure;
using HarmonyLib;
using Model.Ops;

namespace FUSE.Patches
{
    /// <summary>
    /// Diagnostic Harmony patches that log every write to
    /// <see cref="PassengerStop"/>.<c>ProgressionDisabled</c> and
    /// <see cref="Industry"/>.<c>ProgressionDisabled</c>. Gated behind
    /// <see cref="FuseSettings.VerboseApplyReportDetails"/> because both
    /// setters are called in tight loops during the game's feature pass and
    /// during FUSE's passenger stop refreshes.
    ///
    /// Purpose: when locked passenger stops appear in the car destination
    /// picker, these logs tell us which call sites are writing which values
    /// and in what order. The suspect chain we want to verify is:
    ///   1. Game's MapFeatureManager.UpdateFeatureForUnlocked iterates
    ///      areas and sets Industry + PassengerStop ProgressionDisabled.
    ///   2. FUSE's TryRefreshPassengerStop later destroys+recreates the
    ///      PassengerStop and re-writes ProgressionDisabled — if it
    ///      sources the new value from the wrong field, the game's
    ///      correct setting gets overwritten.
    /// </summary>
    [HarmonyPatch(typeof(PassengerStop), nameof(PassengerStop.ProgressionDisabled), MethodType.Setter)]
    internal static class FusePassengerStopProgressionDisabledSetterPatch
    {
        private static void Postfix(PassengerStop __instance, bool value)
        {
            if (!FuseSettings.VerboseApplyReportDetails)
            {
                return;
            }

            try
            {
                var id = __instance != null ? (__instance.identifier ?? "<null>") : "<null>";
                FuseLog.Info(
                    $"FUSE diag PassengerStop.ProgressionDisabled set id='{id}' value={value} " +
                    $"caller='{FormatShortCaller()}'.");
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE diag PassengerStop.ProgressionDisabled postfix failed", ex);
            }
        }

        internal static string FormatShortCaller()
        {
            try
            {
                var trace = new System.Diagnostics.StackTrace(2, false);
                for (var i = 0; i < trace.FrameCount; i++)
                {
                    var method = trace.GetFrame(i)?.GetMethod();
                    if (method == null) continue;
                    var typeName = method.DeclaringType?.FullName ?? "<unknown>";
                    if (typeName.StartsWith("HarmonyLib", StringComparison.Ordinal)) continue;
                    if (typeName.Contains("DynamicMethodDefinition")) continue;
                    if (typeName.StartsWith("FUSE.Patches.FusePassengerStopProgressionDisabledSetterPatch", StringComparison.Ordinal)) continue;
                    if (typeName.StartsWith("FUSE.Patches.FuseIndustryProgressionDisabledSetterPatch", StringComparison.Ordinal)) continue;
                    return $"{typeName}.{method.Name}";
                }
            }
            catch
            {
            }

            return "<unavailable>";
        }
    }

    [HarmonyPatch(typeof(Industry), nameof(Industry.ProgressionDisabled), MethodType.Setter)]
    internal static class FuseIndustryProgressionDisabledSetterPatch
    {
        private static void Postfix(Industry __instance, bool value)
        {
            if (!FuseSettings.VerboseApplyReportDetails)
            {
                return;
            }

            try
            {
                var id = __instance != null ? (__instance.identifier ?? "<null>") : "<null>";
                FuseLog.Info(
                    $"FUSE diag Industry.ProgressionDisabled set id='{id}' value={value} " +
                    $"caller='{FusePassengerStopProgressionDisabledSetterPatch.FormatShortCaller()}'.");
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE diag Industry.ProgressionDisabled postfix failed", ex);
            }
        }
    }

    /// <summary>
    /// Diagnostic on <see cref="IndustryComponent"/>'s ProgressionDisabled
    /// setter. Captures every write so we can see who manages this flag
    /// for FUSE-created loaders/unloaders inside locked industries.
    ///
    /// Critical observation from the game IL: the game's
    /// <see cref="MapFeatureManager"/>.<c>UpdateFeatureForUnlocked</c> only
    /// touches <c>IndustryComponent</c>.<c>ProgressionDisabled</c> when the
    /// component is explicitly listed in
    /// <c>feature.unlockIncludeIndustryComponents</c>. Components inside a
    /// locked area's Industry are NOT updated — and
    /// <see cref="IndustryComponent.IsVisible"/> only checks the component's
    /// own flag (plus track-span presence), NOT the parent Industry's
    /// flag. Mod-added loaders/unloaders inside locked industries are
    /// therefore <c>IsVisible=true</c> on the dispatcher map even when the
    /// industry itself is gated.
    /// </summary>
    [HarmonyPatch(typeof(IndustryComponent), nameof(IndustryComponent.ProgressionDisabled), MethodType.Setter)]
    internal static class FuseIndustryComponentProgressionDisabledSetterPatch
    {
        private static void Postfix(IndustryComponent __instance, bool value)
        {
            if (!FuseSettings.VerboseApplyReportDetails)
            {
                return;
            }

            try
            {
                var id = __instance != null ? (__instance.Identifier ?? "<null>") : "<null>";
                FuseLog.Info(
                    $"FUSE diag IndustryComponent.ProgressionDisabled set id='{id}' value={value} " +
                    $"caller='{FusePassengerStopProgressionDisabledSetterPatch.FormatShortCaller()}'.");
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE diag IndustryComponent.ProgressionDisabled postfix failed", ex);
            }
        }
    }
}
