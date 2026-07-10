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
                    return NativeLeakDetection.Mode.ToString();
                }
                catch
                {
                    return "Unavailable";
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
                FuseLog.Warning(
                    "FUSE enabled Unity native-allocation leak stack traces process-wide. " +
                    "This has substantial CPU and memory overhead; reproduce briefly, then disable it. " +
                    "A game restart before capture gives the cleanest allocation history.");
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
