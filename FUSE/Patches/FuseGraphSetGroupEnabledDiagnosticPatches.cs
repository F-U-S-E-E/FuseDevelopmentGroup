using System;
using FUSE.Infrastructure;
using Game.State;
using HarmonyLib;
using Track;

namespace FUSE.Patches
{
    /// <summary>
    /// Diagnostic Harmony patches that log every mutation of the
    /// <see cref="Graph"/>'s enabled / available track-group sets. Gated behind
    /// <see cref="FuseSettings.VerboseApplyReportDetails"/> because in normal
    /// gameplay these methods fire on every section unlock / KVO change / FUSE
    /// refresh and would otherwise spam the log.
    ///
    /// Purpose: the Alarka Branch / Ela bridge regression manifests as
    /// <c>trackGroup id='ela-bridge' graphEnabled=True</c> in the progression
    /// dump even when the player has not unlocked S2. Removing FUSE's KVO
    /// pre-fill stopped one writer (the persistent dict pollution) but the
    /// bridge stayed visible, which means something is still calling
    /// <see cref="Graph.SetGroupEnabled(string, bool)"/> with
    /// (<c>"ela-bridge"</c>, <c>true</c>) during load. This patch tells us
    /// definitively who that caller is and at what point in load it fires.
    /// </summary>
    [HarmonyPatch(typeof(Graph), nameof(Graph.SetGroupEnabled), typeof(string), typeof(bool))]
    internal static class FuseGraphSetGroupEnabledDiagnosticPatch
    {
        private static void Postfix(string groupId, bool groupEnabled, bool __result)
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
                    $"FUSE diag Graph.SetGroupEnabled id='{groupId ?? "<null>"}' value={groupEnabled} " +
                    $"changed={__result} IsSandbox={sandbox} GameMode={mode} " +
                    $"caller='{FormatShortCaller()}'.");
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE diag SetGroupEnabled postfix failed", ex);
            }
        }

        internal static string FormatShortCaller()
        {
            // Walk the stack briefly to find the first non-FUSE / non-internal
            // method that called into SetGroupEnabled. Cheap breadcrumb without
            // dumping the full stack on every call.
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
                    if (typeName.StartsWith("FUSE.Patches.FuseGraph", StringComparison.Ordinal)) continue;
                    return $"{typeName}.{method.Name}";
                }
            }
            catch
            {
            }

            return "<unavailable>";
        }
    }

    [HarmonyPatch(typeof(Graph), nameof(Graph.SetGroupAvailable), typeof(string), typeof(bool))]
    internal static class FuseGraphSetGroupAvailableDiagnosticPatch
    {
        private static void Postfix(string groupId, bool groupAvailable, bool __result)
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
                    $"FUSE diag Graph.SetGroupAvailable id='{groupId ?? "<null>"}' value={groupAvailable} " +
                    $"changed={__result} IsSandbox={sandbox} GameMode={mode} " +
                    $"caller='{FuseGraphSetGroupEnabledDiagnosticPatch.FormatShortCaller()}'.");
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE diag SetGroupAvailable postfix failed", ex);
            }
        }
    }
}
