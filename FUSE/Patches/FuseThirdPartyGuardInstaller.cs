using System;
using FUSE.Infrastructure;
using HarmonyLib;

namespace FUSE.Patches
{
    /// <summary>
    /// Single idempotent entry point for the guards FUSE keeps around
    /// third-party mods it has no compile-time reference to (currently the
    /// Map Enhancer culling guard, the Rebill Industry Cars config-load
    /// guard, the BRSS mod-menu startup guard, the TimeSync main-thread
    /// guard, the RR Utilities compatibility fixes, the Memory Leak &amp; FPS
    /// Enviro compatibility fix, the Realistic Rerail startup guard, and
    /// Bman's shared locomotive audio initialization fixes).
    /// Each guard resolves its target by name and idles
    /// silently when the mod — or the exact member surface the guard understands
    /// — is absent.
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
        private static bool _assemblyLoadRetryRegistered;

        internal static void EnsureInstalled()
        {
            try
            {
                if (_harmony == null)
                {
                    _harmony = new Harmony(HarmonyId);
                }

                if (!_assemblyLoadRetryRegistered)
                {
                    AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
                    _assemblyLoadRetryRegistered = true;
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

            string brssStatus;
            try
            {
                brssStatus = FuseBrssModMenuGuardPatches.EnsureInstalled(_harmony);
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE BRSS mod-menu guard failed to install", ex);
                brssStatus = "failed";
            }

            string timeSyncStatus;
            try
            {
                timeSyncStatus = FuseTimeSyncMainThreadGuardPatches.EnsureInstalled(_harmony);
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE TimeSync main-thread guard failed to install", ex);
                timeSyncStatus = "failed";
            }

            string utilitiesQueryStatus;
            try
            {
                utilitiesQueryStatus = FuseUtilitiesQueryTooltipCompatibility.EnsureInstalled(_harmony);
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE RR Utilities query compatibility failed to install", ex);
                utilitiesQueryStatus = "failed";
            }

            string utilitiesMapLoadStatus;
            try
            {
                utilitiesMapLoadStatus = FuseUtilitiesMapLoadCompatibility.EnsureInstalled(_harmony);
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE RR Utilities map-load compatibility failed to install", ex);
                utilitiesMapLoadStatus = "failed";
            }

            string realisticRerailStatus;
            try
            {
                realisticRerailStatus = FuseRealisticRerailCraneGuardPatches.EnsureInstalled(_harmony);
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE Realistic Rerail startup guard failed to install", ex);
                realisticRerailStatus = "failed";
            }

            string memoryLeakFpsStatus;
            try
            {
                memoryLeakFpsStatus = FuseMemoryLeakFpsCompatibility.EnsureInstalled(_harmony);
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE Memory Leak & FPS compatibility failed to install", ex);
                memoryLeakFpsStatus = "failed";
            }

            string bmanLocomotiveAudioStatus;
            try
            {
                bmanLocomotiveAudioStatus =
                    FuseBmanLocomotiveAudioCompatibility.EnsureInstalled(_harmony);
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE Bman locomotive audio compatibility failed to install", ex);
                bmanLocomotiveAudioStatus = "failed";
            }

            var summary =
                $"FUSE third-party guards: mapEnhancerCulling='{mapEnhancerStatus}' " +
                $"rebillIndustryCars='{rebillStatus}' brssModMenu='{brssStatus}' " +
                $"timeSyncMainThread='{timeSyncStatus}' " +
                $"utilitiesQuery='{utilitiesQueryStatus}' " +
                $"utilitiesMapLoad='{utilitiesMapLoadStatus}' " +
                $"realisticRerail='{realisticRerailStatus}' " +
                $"memoryLeakFps='{memoryLeakFpsStatus}' " +
                $"bmanLocomotiveAudio='{bmanLocomotiveAudioStatus}'.";
            if (!string.Equals(summary, _lastSummary, StringComparison.Ordinal))
            {
                _lastSummary = summary;
                FuseLog.Info(summary);
            }
        }

        private static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args)
        {
            var assemblyName = args?.LoadedAssembly?.GetName()?.Name;
            var isRealisticRerail = string.Equals(
                assemblyName,
                "CraneRerailing",
                StringComparison.Ordinal);
            var isUtilities = string.Equals(
                assemblyName,
                "Utilities",
                StringComparison.Ordinal);
            var isMemoryLeakFps = string.Equals(
                assemblyName,
                "MemoryLeakFPSfix",
                StringComparison.Ordinal);
            var isBmanLocomotiveAudio = string.Equals(
                assemblyName,
                "GP38Scripts",
                StringComparison.Ordinal);
            if (!isRealisticRerail && !isUtilities && !isMemoryLeakFps && !isBmanLocomotiveAudio)
            {
                return;
            }

            try
            {
                if (isRealisticRerail)
                {
                    FuseRealisticRerailCraneGuardPatches.EnsureInstalled(_harmony);
                }

                if (isUtilities)
                {
                    FuseUtilitiesQueryTooltipCompatibility.EnsureInstalled(_harmony);
                    FuseUtilitiesMapLoadCompatibility.EnsureInstalled(_harmony);
                }

                if (isMemoryLeakFps)
                {
                    FuseMemoryLeakFpsCompatibility.EnsureInstalled(_harmony);
                }

                if (isBmanLocomotiveAudio)
                {
                    FuseBmanLocomotiveAudioCompatibility.EnsureInstalled(_harmony);
                }
            }
            catch (Exception ex)
            {
                FuseLog.Exception(
                    $"FUSE third-party compatibility failed during '{assemblyName}' assembly-load retry",
                    ex);
            }
        }
    }
}
