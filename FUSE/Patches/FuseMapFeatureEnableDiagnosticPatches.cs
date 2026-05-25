using System;
using System.Collections.Generic;
using FUSE.Infrastructure;
using Game.Progression;
using Game.State;
using HarmonyLib;

namespace FUSE.Patches
{
    /// <summary>
    /// Diagnostic Harmony patches that log every external mutation of the
    /// MapFeatureManager's per-feature unlock state. Gated behind
    /// <see cref="FuseSettings.VerboseApplyReportDetails"/> because in normal
    /// gameplay these methods fire on every purchase / section unlock / refresh,
    /// which would otherwise spam the log.
    ///
    /// Purpose: when a feature shows as 'kvoUnlocked=True' in the FUSE progression
    /// dump but the player hasn't completed the section that's supposed to unlock
    /// it, these logs let us identify exactly who wrote the entry and when. The
    /// suspected culprits are the game's initial-pass HandleFeatureEnablesChanged
    /// (which writes defaults when GameMode is still its default Sandbox value
    /// because the save hasn't deserialized yet) and FUSE's own
    /// InitializeMissingMapFeatureStates pre-fill (which has the same race).
    /// </summary>
    [HarmonyPatch(typeof(MapFeatureManager), nameof(MapFeatureManager.SetFeatureEnabled), typeof(MapFeature), typeof(bool))]
    internal static class FuseMapFeatureSetFeatureEnabledByObjectPatch
    {
        private static void Postfix(MapFeature feature, bool unlocked)
        {
            if (!FuseSettings.VerboseApplyReportDetails)
            {
                return;
            }

            try
            {
                var id = feature != null ? (feature.identifier ?? "<null>") : "<null>";
                var sandbox = StateManager.IsSandbox;
                var mode = StateManager.Shared?.GameMode.ToString() ?? "<null>";
                FuseLog.Info(
                    $"FUSE diag MapFeatureManager.SetFeatureEnabled(feature) id='{id}' " +
                    $"value={unlocked} IsSandbox={sandbox} GameMode={mode} caller='{FormatShortCaller()}'.");
            }
            catch (Exception ex)
            {
                FuseLog.Warning($"FUSE diag SetFeatureEnabled postfix failed: {ex.Message}");
            }
        }

        private static string FormatShortCaller()
        {
            // Walk the stack briefly to find the first non-FUSE / non-game-internal
            // method that called into SetFeatureEnabled. Gives a cheap "who set this"
            // breadcrumb without dumping the full stack on every call.
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

    [HarmonyPatch(typeof(MapFeatureManager), nameof(MapFeatureManager.SetFeatureEnabled), typeof(string), typeof(bool))]
    internal static class FuseMapFeatureSetFeatureEnabledByIdPatch
    {
        private static void Postfix(string featureId, bool unlocked)
        {
            if (!FuseSettings.VerboseApplyReportDetails)
            {
                return;
            }

            try
            {
                var sandbox = StateManager.IsSandbox;
                var mode = StateManager.Shared?.GameMode.ToString() ?? "<null>";
                FuseLog.Info(
                    $"FUSE diag MapFeatureManager.SetFeatureEnabled(id) id='{featureId ?? "<null>"}' " +
                    $"value={unlocked} IsSandbox={sandbox} GameMode={mode}.");
            }
            catch (Exception ex)
            {
                FuseLog.Warning($"FUSE diag SetFeatureEnabled(id) postfix failed: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(MapFeatureManager), nameof(MapFeatureManager.SetFeatureEnables), typeof(Dictionary<string, bool>))]
    internal static class FuseMapFeatureSetFeatureEnablesPatch
    {
        private static void Prefix(Dictionary<string, bool> featureEnables)
        {
            if (!FuseSettings.VerboseApplyReportDetails || featureEnables == null)
            {
                return;
            }

            try
            {
                var sandbox = StateManager.IsSandbox;
                var mode = StateManager.Shared?.GameMode.ToString() ?? "<null>";
                var trueCount = 0;
                var falseCount = 0;
                foreach (var kvp in featureEnables)
                {
                    if (kvp.Value) trueCount++; else falseCount++;
                }

                FuseLog.Info(
                    $"FUSE diag MapFeatureManager.SetFeatureEnables(dict) count={featureEnables.Count} " +
                    $"trueCount={trueCount} falseCount={falseCount} " +
                    $"IsSandbox={sandbox} GameMode={mode}.");
            }
            catch (Exception ex)
            {
                FuseLog.Warning($"FUSE diag SetFeatureEnables prefix failed: {ex.Message}");
            }
        }
    }
}
