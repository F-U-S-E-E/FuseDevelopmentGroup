using System;
using FUSE.Profiler.Engine;
using FUSE.Profiler.Infrastructure;
using HarmonyLib;

namespace FUSE.Profiler.Instrumentation
{
    /// <summary>
    /// Open/close/cleanup orchestration. Opening the window arms sampling and
    /// the sim-tick clock; closing stops sampling immediately and schedules a
    /// full teardown (unpatch everything, drop buffers) after a grace delay
    /// so quick reopen doesn't pay the patch cost again. A closed, cleaned-up
    /// profiler leaves zero patches installed.
    /// </summary>
    internal static class ProfilerRuntime
    {
        private static float _cleanupCountdown = -1f;

        internal static bool WindowVisible { get; private set; }

        internal static float CleanupDelaySeconds = 30f;

        internal static void ToggleWindow()
        {
            if (WindowVisible)
            {
                CloseWindow();
            }
            else
            {
                OpenWindow();
            }
        }

        internal static void OpenWindow()
        {
            _cleanupCountdown = -1f;
            EntryCatalog.EnsureBuiltInsRegistered();
            SimTickDriver.Install();
            ProfilerSession.Sampling = true;
            WindowVisible = true;
            ProfilerLog.Info("FUSE.Profiler window opened; sampling armed.");
        }

        internal static void CloseWindow()
        {
            WindowVisible = false;
            ProfilerSession.Sampling = false;
            if (CleanupDelaySeconds <= 0f)
            {
                // Zero delay means "immediately", not "never" — the countdown
                // tick treats non-positive as idle.
                CleanupNow();
                return;
            }

            _cleanupCountdown = CleanupDelaySeconds;
            ProfilerLog.Info(
                $"FUSE.Profiler window closed; instrumentation teardown in {CleanupDelaySeconds:F0}s unless reopened.");
        }

        /// <summary>Host Update drives the delayed-teardown countdown.</summary>
        internal static void TickCleanup(float deltaSeconds)
        {
            if (_cleanupCountdown <= 0f)
            {
                return;
            }

            _cleanupCountdown -= deltaSeconds;
            if (_cleanupCountdown <= 0f)
            {
                _cleanupCountdown = -1f;
                CleanupNow();
            }
        }

        /// <summary>Immediate full teardown (also used on mod disable).</summary>
        internal static void CleanupNow()
        {
            WindowVisible = false;
            ProfilerSession.Sampling = false;
            MethodInstrumenter.RemoveAll();
            SimTickDriver.Remove();
            ProbeRegistry.Clear();
            EntryCatalog.ResetAfterCleanup();
            ModAttribution.InvalidateMap();
            ProfilerSession.Reset();
            ProfilerLog.Info("FUSE.Profiler instrumentation removed and buffers cleared.");
        }
    }

    /// <summary>
    /// The sim-tick clock source: a prefix/postfix pair on the train
    /// controller's fixed step, installed only while the profiler is in use.
    /// The pair also measures the whole step into its own probe, and the
    /// postfix closes the sim-tick cycle AFTER recording that measurement —
    /// so boundary-vs-measurement ordering on the same method is
    /// deterministic by construction instead of by patch-priority ties.
    /// </summary>
    internal static class SimTickDriver
    {
        internal const string HarmonyId = "FUSE.Profiler.static";
        internal const string StepProbeKey = "physics.step";

        private static readonly Harmony Harmony = new Harmony(HarmonyId);
        private static bool _installed;
        private static ProbeRing _stepProbe;

        internal static void Install()
        {
            if (_installed)
            {
                return;
            }

            try
            {
                var fixedUpdate = AccessTools.Method(typeof(TrainController), "FixedUpdate");
                if (fixedUpdate == null)
                {
                    ProfilerLog.Warning(
                        "FUSE.Profiler could not find TrainController.FixedUpdate; sim-tick probes will not sample.");
                    return;
                }

                _stepProbe = ProbeRegistry.GetOrAdd(StepProbeKey, "Whole physics step", ProbeCadence.SimTick);
                Harmony.Patch(
                    fixedUpdate,
                    prefix: new HarmonyMethod(typeof(SimTickDriver), nameof(StepPrefix)) { priority = Priority.First },
                    postfix: new HarmonyMethod(typeof(SimTickDriver), nameof(StepPostfix)) { priority = Priority.Last });
                _installed = true;
            }
            catch (Exception ex)
            {
                ProfilerLog.Exception("FUSE.Profiler failed installing the sim-tick clock", ex);
            }
        }

        internal static void Remove()
        {
            if (!_installed)
            {
                return;
            }

            try
            {
                Harmony.UnpatchAll(HarmonyId);
            }
            catch (Exception ex)
            {
                ProfilerLog.Exception("FUSE.Profiler failed removing the sim-tick clock", ex);
            }

            _installed = false;
            _stepProbe = null;
        }

        private static void StepPrefix()
        {
            if (ProfilerSession.Recording)
            {
                _stepProbe?.Enter();
            }
        }

        private static void StepPostfix()
        {
            _stepProbe?.Exit();
            ProfilerSession.SimTickBoundary();
        }
    }
}
