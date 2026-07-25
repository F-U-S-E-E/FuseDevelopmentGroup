using System;
using FUSE.Infrastructure;
using HarmonyLib;

namespace FUSE.Patches
{
    /// <summary>
    /// Containment for a recurring config-load crash observed at runtime in
    /// Rebill Industry Cars 0.2.x on custom maps: its auto-config step
    /// throws deterministically, and because the mod only commits its
    /// change-detection counters after a SUCCESSFUL config load, the failed
    /// load re-arms itself on every update gate — a session-long retry loop
    /// (observed at 74-2,286 identical six-line stack dumps per session)
    /// during which no car is ever rebilled and even a valid hand-authored
    /// config file is never applied.
    ///
    /// Finalizers on the auto-config step and, as a storm breaker, on the
    /// config load itself suppress the throw so the rest of the load can
    /// run: that restores the JSON-configured half of the feature and
    /// collapses the log spam into counted, health-report-visible guard
    /// lines. It does NOT restore the auto-config feature — only the mod's
    /// own 0.2.3+ update can, which is why the guard message recommends it
    /// and why the version gate leaves repaired releases unpatched. Remove
    /// this guard once 0.2.3+ is verified fixed in the field.
    ///
    /// Deliberately NOT a [HarmonyPatch] class: the target is a third-party
    /// mod resolved by name at runtime (resolve-or-idle), so the
    /// attribute-driven apply pass and the patch-targeting smoke test never
    /// try to resolve types that are legitimately absent. Honesty gate: if
    /// EITHER method is missing (mod absent, or a release that renamed or
    /// removed the surface this guard understands), nothing installs — a
    /// half-guarded surface would claim containment it cannot deliver.
    /// </summary>
    internal static class FuseRebillIndustryCarsGuardPatches
    {
        /// <summary>
        /// First release reported repaired upstream; assemblies at or above
        /// this version run unpatched so the fix is observable in the field.
        /// </summary>
        private static readonly Version FirstFixedVersion = new Version(0, 2, 3);

        private static bool _autoConfigPatched;
        private static bool _loadConfigPatched;

        /// <summary>Suppressed config-load crashes since startup (diagnostics).</summary>
        internal static long SuppressedExceptions => FuseRuntimeGuardCounters.RebillAutoConfigSuppressed;

        internal static bool Installed => _autoConfigPatched && _loadConfigPatched;

        /// <summary>
        /// Idempotent. Re-resolves on every call while the target mod is
        /// absent (it may load after FUSE), latches once installed. Returns
        /// a short status token for the installer's summary line.
        /// </summary>
        internal static string EnsureInstalled(Harmony harmony)
        {
            if (_autoConfigPatched && _loadConfigPatched)
            {
                return "installed";
            }

            if (harmony == null)
            {
                return "unavailable (no harmony)";
            }

            var rebillSystemType = AccessTools.TypeByName("Us.Dchn.Railroader.RebillIndustryCars.RebillSystem");
            if (rebillSystemType == null)
            {
                return "idle (not present)";
            }

            var autoConfigUnloaders = AccessTools.Method(rebillSystemType, "AutoConfigUnloaders");
            var loadConfig = AccessTools.Method(rebillSystemType, "LoadConfig");
            if (autoConfigUnloaders == null || loadConfig == null)
            {
                return "idle (surface absent)";
            }

            Version assemblyVersion = null;
            try
            {
                assemblyVersion = rebillSystemType.Assembly.GetName().Version;
            }
            catch
            {
                // Unreadable version: fall through and install — the finalizers
                // only ever act on an observed throw, so over-installing is inert.
                FUSE.Infrastructure.FuseModExceptionRegistry.CountSelfFault();
            }

            if (!ShouldInstallForVersion(assemblyVersion))
            {
                return $"idle (assembly {assemblyVersion} is at or past the repaired release)";
            }

            // Signature-agnostic finalizers: an (Exception) -> Exception
            // finalizer attaches to any target signature, and a suppressed
            // call yields the target's default return value — for the config
            // load's boolean that reads as "load failed", which is exactly
            // what the caller already handles.
            if (!_autoConfigPatched)
            {
                harmony.Patch(
                    autoConfigUnloaders,
                    finalizer: new HarmonyMethod(
                        typeof(FuseRebillIndustryCarsGuardPatches),
                        nameof(AutoConfigUnloadersFinalizer)));
                _autoConfigPatched = true;
            }

            if (!_loadConfigPatched)
            {
                harmony.Patch(
                    loadConfig,
                    finalizer: new HarmonyMethod(
                        typeof(FuseRebillIndustryCarsGuardPatches),
                        nameof(LoadConfigFinalizer)));
                _loadConfigPatched = true;
            }

            return "installed";
        }

        /// <summary>
        /// Version gate, kept pure for tests: install for anything below the
        /// first repaired release, and default to installing when the
        /// assembly version is unreadable (many mod assemblies do not stamp
        /// their release version — the finalizers are inert on healthy code
        /// either way, while skipping a broken build is not recoverable).
        /// </summary>
        internal static bool ShouldInstallForVersion(Version assemblyVersion)
        {
            return assemblyVersion == null || assemblyVersion < FirstFixedVersion;
        }

        private static Exception AutoConfigUnloadersFinalizer(Exception __exception)
        {
            return Suppress(__exception, "auto-config");
        }

        private static Exception LoadConfigFinalizer(Exception __exception)
        {
            return Suppress(__exception, "config-load");
        }

        private static Exception Suppress(Exception exception, string stage)
        {
            if (exception == null)
            {
                return null;
            }

            var suppressed = FuseRuntimeGuardCounters.RecordRebillAutoConfigSuppressed();
            if (FuseGuardLog.ShouldLog(suppressed))
            {
                FuseLog.Exception(
                    $"FUSE contained Rebill Industry Cars {stage} crash #{suppressed} so the rest of " +
                    "its config load can still run (a hand-authored RICconfig.json applies again; the " +
                    "auto-config feature itself stays down). This crash retries every update gate for " +
                    "the whole session on affected maps — updating Rebill Industry Cars to 0.2.3 or " +
                    "newer is the recommended fix",
                    exception);
            }

            return null;
        }
    }
}
