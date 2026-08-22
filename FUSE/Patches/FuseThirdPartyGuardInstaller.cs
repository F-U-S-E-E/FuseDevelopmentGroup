using System;
using System.Threading;
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

        private static readonly object InstallGate = new object();
        private static Harmony _harmony;
        private static string _lastSummary;
        private static bool _assemblyLoadRetryRegistered;
        private static int _active;
        private static int _assemblyLoadRetryPending;

        internal static void EnsureInstalled()
        {
            lock (InstallGate)
            {
                Volatile.Write(ref _active, 1);
                EnsureInstalledCore();
            }
        }

        private static void EnsureInstalledCore()
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

            var mapEnhancerStatus = InstallGuard(
                "Map Enhancer culling guard",
                () => FuseMapEnhancerCullingGuardPatches.EnsureInstalled(_harmony));
            var rebillStatus = InstallGuard(
                "Rebill Industry Cars guard",
                () => FuseRebillIndustryCarsGuardPatches.EnsureInstalled(_harmony));
            var brssStatus = InstallGuard(
                "BRSS mod-menu guard",
                () => FuseBrssModMenuGuardPatches.EnsureInstalled(_harmony));
            var timeSyncStatus = InstallGuard(
                "TimeSync main-thread guard",
                () => FuseTimeSyncMainThreadGuardPatches.EnsureInstalled(_harmony));
            var utilitiesQueryStatus = InstallGuard(
                "RR Utilities query compatibility",
                () => FuseUtilitiesQueryTooltipCompatibility.EnsureInstalled(_harmony));
            var utilitiesMapLoadStatus = InstallGuard(
                "RR Utilities map-load compatibility",
                () => FuseUtilitiesMapLoadCompatibility.EnsureInstalled(_harmony));
            var realisticRerailStatus = InstallGuard(
                "Realistic Rerail startup guard",
                () => FuseRealisticRerailCraneGuardPatches.EnsureInstalled(_harmony));
            var memoryLeakFpsStatus = InstallGuard(
                "Memory Leak & FPS compatibility",
                () => FuseMemoryLeakFpsCompatibility.EnsureInstalled(_harmony));
            var bmanLocomotiveAudioStatus = InstallGuard(
                "Bman locomotive audio compatibility",
                () => FuseBmanLocomotiveAudioCompatibility.EnsureInstalled(_harmony));
            var legosLibraryStatus = InstallGuard(
                "Legos Library compatibility",
                () => FuseLegosLibraryCompatibility.EnsureInstalled(_harmony));
            var assetLoaderStatus = InstallGuard(
                "AssetLoader replacement compatibility",
                () => FuseAssetLoaderReplacementCompatibility.EnsureInstalled(_harmony));
            var alinasUtilitiesStatus = InstallGuard(
                "Alina Utilities compatibility",
                FuseAlinasUtilitiesCompatibility.EnsureInstalled);

            var summary =
                $"FUSE third-party guards: mapEnhancerCulling='{mapEnhancerStatus}' " +
                $"rebillIndustryCars='{rebillStatus}' brssModMenu='{brssStatus}' " +
                $"timeSyncMainThread='{timeSyncStatus}' " +
                $"utilitiesQuery='{utilitiesQueryStatus}' " +
                $"utilitiesMapLoad='{utilitiesMapLoadStatus}' " +
                $"realisticRerail='{realisticRerailStatus}' " +
                $"memoryLeakFps='{memoryLeakFpsStatus}' " +
                $"bmanLocomotiveAudio='{bmanLocomotiveAudioStatus}' " +
                $"legosLibrary='{legosLibraryStatus}' " +
                $"assetLoader='{assetLoaderStatus}' " +
                $"alinasUtilities='{alinasUtilitiesStatus}'.";
            if (!string.Equals(summary, _lastSummary, StringComparison.Ordinal))
            {
                _lastSummary = summary;
                FuseLog.Info(summary);
            }
        }

        private static string InstallGuard(string name, Func<string> install)
        {
            try
            {
                return install();
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE {name} failed to install", ex);
                return "failed";
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
            var isLegosLibrary = string.Equals(
                assemblyName,
                "LegosLibraryOfStuff",
                StringComparison.Ordinal);
            var isAssetLoader = FuseAssetLoaderReplacementCompatibility.IsTargetAssemblyName(assemblyName);
            var isAlinasUtilities = FuseAlinasUtilitiesCompatibility.IsTargetAssemblyName(assemblyName);
            if (!isRealisticRerail && !isUtilities && !isMemoryLeakFps &&
                !isBmanLocomotiveAudio && !isLegosLibrary && !isAssetLoader &&
                !isAlinasUtilities)
            {
                return;
            }

            if (Volatile.Read(ref _active) == 0)
            {
                return;
            }

            // AssemblyLoad runs on whichever thread loaded the assembly. Do not
            // resolve Unity types or mutate Harmony patches here; the always-on
            // runtime pump drains this coalesced retry on Unity's main thread.
            Interlocked.Exchange(ref _assemblyLoadRetryPending, 1);
        }

        internal static void DrainPending()
        {
            if (Interlocked.Exchange(ref _assemblyLoadRetryPending, 0) == 0)
            {
                return;
            }

            lock (InstallGate)
            {
                if (Volatile.Read(ref _active) == 0)
                {
                    return;
                }

                try
                {
                    EnsureInstalledCore();
                }
                catch (Exception ex)
                {
                    FuseLog.Exception(
                        "FUSE third-party compatibility failed during a main-thread assembly-load retry",
                        ex);
                }
            }
        }

        internal static void Shutdown()
        {
            lock (InstallGate)
            {
                Volatile.Write(ref _active, 0);
                Interlocked.Exchange(ref _assemblyLoadRetryPending, 0);
                if (_assemblyLoadRetryRegistered)
                {
                    AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
                    _assemblyLoadRetryRegistered = false;
                }

                _harmony = null;
                _lastSummary = null;
            }
        }
    }
}
