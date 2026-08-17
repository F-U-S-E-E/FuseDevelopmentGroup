using System;
using FUSE.Infrastructure;
using HarmonyLib;

namespace FUSE.Patches
{
    /// <summary>
    /// Single idempotent entry point for the guards FUSE keeps around
    /// third-party mods it has no compile-time reference to (currently the
    /// Map Enhancer culling guard and the Rebill Industry Cars config-load
    /// guard). Each guard resolves its target by name and idles silently
    /// when the mod — or the exact member surface the guard understands —
    /// is absent.
    ///
    /// Kept out of the attribute-driven FusePatchResilience pass on
    /// purpose: these guard classes carry no [HarmonyPatch] attributes, so
    /// neither the apply pass nor the patch-targeting smoke test ever tries
    /// to resolve third-party types that are legitimately absent from a
    /// test or user machine.
    ///
    /// Safe to call more than once (plugin load, then again once the full
    /// mod population is up): installed guards latch, absent targets are
    /// re-resolved on each call because third-party load order relative to
    /// FUSE is not guaranteed. One summary line is logged per state change,
    /// never per call.
    /// </summary>
    internal static class FuseThirdPartyGuardInstaller
    {
        // FusePlugin's Harmony id, so its shutdown UnpatchAll sweep removes
        // these manually-applied guards together with the attribute patches.
        private const string HarmonyId = "FUSE";

        private static Harmony _harmony;
        private static string _lastSummary;

        internal static void EnsureInstalled()
        {
            try
            {
                if (_harmony == null)
                {
                    _harmony = new Harmony(HarmonyId);
                }
            }
            catch (Exception ex)
            {
                FuseLog.Exception(
                    "FUSE third-party guard installer could not create its Harmony instance; " +
                    "no third-party guards are active",
                    ex);
                return;
            }

            string mapEnhancerStatus;
            try
            {
                mapEnhancerStatus = FuseMapEnhancerCullingGuardPatches.EnsureInstalled(_harmony);
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE Map Enhancer culling guard failed to install", ex);
                mapEnhancerStatus = "failed";
            }

            string rebillStatus;
            try
            {
                rebillStatus = FuseRebillIndustryCarsGuardPatches.EnsureInstalled(_harmony);
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE Rebill Industry Cars guard failed to install", ex);
                rebillStatus = "failed";
            }

            var summary =
                $"FUSE third-party guards: mapEnhancerCulling='{mapEnhancerStatus}' " +
                $"rebillIndustryCars='{rebillStatus}'.";
            if (!string.Equals(summary, _lastSummary, StringComparison.Ordinal))
            {
                _lastSummary = summary;
                FuseLog.Info(summary);
            }
        }
    }
}
