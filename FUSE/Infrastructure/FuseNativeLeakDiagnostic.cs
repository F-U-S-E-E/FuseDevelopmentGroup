using System;
using Unity.Collections;

namespace FUSE.Infrastructure
{
    /// <summary>
    /// Owns FUSE's temporary change to Unity's process-wide native leak mode.
    /// </summary>
    internal static class FuseNativeLeakDiagnostic
    {
        private static bool _initialized;
        private static NativeLeakDetectionMode _restoreMode;
        private static NativeLeakDetectionMode _lastAppliedMode;
        private static bool _ownsMode;

        internal static string ModeLabel
        {
            get
            {
                try
                {
                    var mode = NativeLeakDetection.Mode.ToString();
                    return LeakTrackingAvailable ? mode : mode + " (inert: retail build)";
                }
                catch
                {
                    return "Unavailable";
                }
            }
        }

        /// <summary>
        /// Whether this player build actually compiles in Unity's native leak
        /// tracking. The <c>NativeLeakDetection.Mode</c> property stores and
        /// returns whatever is set in EVERY build, but the allocation tracking
        /// and stack capture behind it exist only when collections checks are
        /// compiled in — editor and development builds. In a retail player the
        /// mode is a stored value with no effect: no stacks, no leak reports,
        /// and essentially none of the warned-about overhead either.
        /// </summary>
        internal static bool LeakTrackingAvailable
        {
            get
            {
                try
                {
                    return UnityEngine.Debug.isDebugBuild;
                }
                catch
                {
                    // Outside a Unity player (tests) the probe itself is
                    // unavailable; report the pessimistic answer.
                    return false;
                }
            }
        }

        internal static void Initialize(bool enableStackTraces)
        {
            if (!_initialized)
            {
                try
                {
                    var currentMode = NativeLeakDetection.Mode;
                    _initialized = true;
                    FuseLog.Info($"FUSE observed Unity native leak detection mode: {currentMode}.");
                }
                catch (Exception ex)
                {
                    FuseLog.Exception("FUSE could not read Unity's native leak detection mode", ex);
                    return;
                }
            }

            Apply(enableStackTraces);
        }

        internal static void Apply(bool enableStackTraces)
        {
            if (!_initialized)
            {
                Initialize(enableStackTraces);
                return;
            }

            try
            {
                var currentMode = NativeLeakDetection.Mode;
                if (!enableStackTraces)
                {
                    RestoreOwnedMode(currentMode, "after the setting was disabled");
                    return;
                }

                if (!_ownsMode)
                {
                    // Capture at the moment the user enables the setting, not at
                    // mod startup: another mod may legitimately change this
                    // process-wide value while FUSE's diagnostic is off.
                    _restoreMode = currentMode;
                }

                var targetMode = NativeLeakDetectionMode.EnabledWithStackTrace;
                if (currentMode != targetMode)
                {
                    NativeLeakDetection.Mode = targetMode;
                }

                _lastAppliedMode = targetMode;
                _ownsMode = true;
                if (LeakTrackingAvailable)
                {
                    FuseLog.Warning(
                        "FUSE enabled Unity native-allocation leak stack traces process-wide. " +
                        "This has substantial CPU and memory overhead; reproduce briefly, then disable it. " +
                        "A game restart before capture gives the cleanest allocation history.");
                }
                else
                {
                    FuseLog.Warning(
                        "FUSE set Unity's native leak mode to EnabledWithStackTrace, but this is a retail " +
                        "(non-development) player build: Unity compiles native allocation tracking out of " +
                        "retail players, so NO allocation stacks or leak reports will be produced. The " +
                        "toggle is inert here — use the frame-spike census memory columns for leak hunts.");
                }
            }
            catch (Exception ex)
            {
                FuseLog.Exception(
                    $"FUSE could not {(enableStackTraces ? "enable" : "disable")} Unity native leak stack traces",
                    ex);
            }
        }

        internal static void Shutdown()
        {
            if (!_initialized)
            {
                return;
            }

            try
            {
                RestoreOwnedMode(NativeLeakDetection.Mode, "during shutdown");
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE could not restore Unity's native leak detection mode during shutdown", ex);
            }
            finally
            {
                _initialized = false;
                _ownsMode = false;
            }
        }

        private static void RestoreOwnedMode(NativeLeakDetectionMode currentMode, string phase)
        {
            if (!_ownsMode)
            {
                return;
            }

            if (ShouldRestoreOriginalMode(
                    _ownsMode,
                    _restoreMode,
                    _lastAppliedMode,
                    currentMode))
            {
                NativeLeakDetection.Mode = _restoreMode;
                FuseLog.Info($"FUSE restored Unity native leak detection mode to {_restoreMode} {phase}.");
            }
            else if (currentMode != _lastAppliedMode)
            {
                FuseLog.Warning(
                    $"Unity native leak detection mode changed externally to {currentMode}; " +
                    $"FUSE left it unchanged {phase}.");
            }

            _ownsMode = false;
        }

        internal static bool ShouldRestoreOriginalMode(
            bool ownsMode,
            NativeLeakDetectionMode restoreMode,
            NativeLeakDetectionMode lastAppliedMode,
            NativeLeakDetectionMode currentMode)
        {
            return ownsMode &&
                   currentMode == lastAppliedMode &&
                   currentMode != restoreMode;
        }
    }
}
