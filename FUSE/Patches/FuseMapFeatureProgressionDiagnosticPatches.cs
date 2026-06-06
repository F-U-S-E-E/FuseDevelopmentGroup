using System;
using System.Collections.Generic;
using System.Linq;
using FUSE.Infrastructure;
using Game.Progression;
using Game.State;
using HarmonyLib;
using Model.Ops;

namespace FUSE.Patches
{
    /// <summary>
    /// Diagnostic Harmony patches that instrument the game's own progression
    /// passes so we can see what they actually do, not what we guess they do.
    /// Gated behind <see cref="FuseSettings.VerboseApplyReportDetails"/>;
    /// both methods are private game internals and fire repeatedly during
    /// load.
    ///
    /// Captures three critical pieces of ground truth:
    ///   1. Every <see cref="MapFeatureManager.HandleFeatureEnablesChanged"/>
    ///      call — its (initial, IsSandbox, oldValue, newValue) inputs and
    ///      classification result (how many features end up in the
    ///      enabled / disabled sets). Distinguishes between the game's
    ///      natural HandleSnapshotProperties-driven call vs FUSE's
    ///      ForceApplyCurrentMapFeatureState reflection invocation.
    ///   2. Every <see cref="MapFeatureManager.UpdateFeatureForUnlocked"/>
    ///      call — feature id, unlocked, the area count it iterated, and
    ///      the number of <see cref="Industry"/> and
    ///      <see cref="PassengerStop"/> instances it found AND mutated. If
    ///      a locked feature has areas but finds zero PassengerStops in
    ///      them, that's the smoking gun for a hierarchy/area-mismatch
    ///      problem (the stops aren't being found by
    ///      <c>a.GetComponentsInChildren&lt;PassengerStop&gt;()</c>
    ///      because they're not actually under the area's GameObject).
    ///   3. <see cref="PassengerStop"/> children found per area — so we
    ///      can spot whether FUSE-created stops are placed where the
    ///      game's iteration looks for them.
    /// </summary>
    [HarmonyPatch(typeof(MapFeatureManager), "HandleFeatureEnablesChanged")]
    internal static class FuseMapFeatureHandleFeatureEnablesChangedDiagnosticPatch
    {
        private static void Prefix(
            Dictionary<string, bool> oldValue,
            Dictionary<string, bool> newValue,
            bool initial)
        {
            if (!FuseSettings.VerboseApplyReportDetails)
            {
                return;
            }

            try
            {
                var sandbox = StateManager.IsSandbox;
                var mode = StateManager.Shared?.GameMode.ToString() ?? "<null>";
                var oldTrue = oldValue != null ? oldValue.Count(kvp => kvp.Value) : 0;
                var oldFalse = oldValue != null ? oldValue.Count - oldTrue : 0;
                var newTrue = newValue != null ? newValue.Count(kvp => kvp.Value) : 0;
                var newFalse = newValue != null ? newValue.Count - newTrue : 0;
                FuseLog.Info(
                    $"FUSE diag MapFeatureManager.HandleFeatureEnablesChanged entry initial={initial} " +
                    $"IsSandbox={sandbox} GameMode={mode} " +
                    $"oldCount={oldValue?.Count ?? 0} (true={oldTrue},false={oldFalse}) " +
                    $"newCount={newValue?.Count ?? 0} (true={newTrue},false={newFalse}) " +
                    $"caller='{FormatShortCaller()}'.");
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE diag HandleFeatureEnablesChanged prefix failed", ex);
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
                    if (typeName.StartsWith("FUSE.Patches.FuseMapFeature", StringComparison.Ordinal)) continue;
                    return $"{typeName}.{method.Name}";
                }
            }
            catch
            {
            }

            return "<unavailable>";
        }
    }

    [HarmonyPatch(typeof(MapFeatureManager), "UpdateFeatureForUnlocked")]
    internal static class FuseMapFeatureUpdateFeatureForUnlockedDiagnosticPatch
    {
        private static void Postfix(MapFeature feature, bool unlocked)
        {
            if (!FuseSettings.VerboseApplyReportDetails)
            {
                return;
            }

            try
            {
                if (feature == null)
                {
                    return;
                }

                var areas = feature.areasEnableOnUnlock ?? Array.Empty<Area>();
                var areaCount = areas.Count(a => a != null);

                // Mirror the game's own iteration: for each area, count the
                // Industry and PassengerStop children. If the area is empty
                // (zero industries / zero stops), the game's loop never gets
                // a chance to mark anything ProgressionDisabled for that
                // area — that's a strong signal of a hierarchy bug.
                var industryTotal = 0;
                var passengerStopTotal = 0;
                var areaSummaries = new List<string>(areas.Length);
                foreach (var area in areas)
                {
                    if (area == null)
                    {
                        continue;
                    }

                    var industries = area.Industries?.ToArray() ?? Array.Empty<Industry>();
                    // The game uses GetComponentsInChildren<PassengerStop>() WITHOUT
                    // includeInactive=true. Mirror that exact behaviour so the count
                    // reflects what the game actually reaches.
                    var stops = area.GetComponentsInChildren<PassengerStop>();
                    industryTotal += industries.Length;
                    passengerStopTotal += stops.Length;
                    areaSummaries.Add($"{area.identifier}(ind={industries.Length},stops={stops.Length})");
                }

                FuseLog.Info(
                    $"FUSE diag MapFeatureManager.UpdateFeatureForUnlocked id='{feature.identifier}' " +
                    $"unlocked={unlocked} areas={areaCount} industriesTouched={industryTotal} " +
                    $"passengerStopsTouched={passengerStopTotal} " +
                    $"unlockIncludeIndustries={feature.unlockIncludeIndustries?.Length ?? 0} " +
                    $"unlockIncludeIndustryComponents={feature.unlockIncludeIndustryComponents?.Length ?? 0} " +
                    $"areaBreakdown=[{string.Join(",", areaSummaries)}].");
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE diag UpdateFeatureForUnlocked postfix failed", ex);
            }
        }
    }
}
