using System;
using FUSE.Infrastructure;
using UnityEngine;

namespace FUSE.Runtime.Lifecycle
{
    /// <summary>
    /// Always-on per-frame pump for FUSE work that must drain on the main
    /// thread regardless of which optional UI hosts exist.
    ///
    /// The scenery load-failure drain originally rode the Health window
    /// component's Update (since deleted) — but the menu-UI rewrite retired
    /// the call that created that host, so the component never existed and the drain
    /// NEVER ran in the field: both failure nets enqueued faults into a queue
    /// nobody emptied (observed as sessions with dozens of load failures and
    /// zero records, toasts, or quarantines, while the enqueue-side counters
    /// kept climbing). Queue-consuming work now lives here, on a host created
    /// unconditionally at plugin load, so a UI refactor can never silently
    /// starve it again. Cost when idle: two lock-free queue-empty snapshots.
    /// </summary>
    internal static class FuseRuntimePump
    {
        private static GameObject _host;

        internal static void EnsureStarted()
        {
            if (_host != null)
            {
                return;
            }

            GameObject host = null;
            try
            {
                host = new GameObject("FUSE.RuntimePump");
                host.hideFlags = HideFlags.HideAndDontSave;
                host.AddComponent<FuseRuntimePumpRunner>();
                UnityEngine.Object.DontDestroyOnLoad(host);
                _host = host;
                FuseLog.Info("FUSE runtime pump initialized.");
            }
            catch (Exception ex)
            {
                if (host != null)
                {
                    UnityEngine.Object.Destroy(host);
                }

                FuseLog.Exception("FUSE runtime pump host creation failed", ex);
            }
        }

        internal static void Shutdown()
        {
            if (_host == null)
            {
                return;
            }

            try
            {
                UnityEngine.Object.Destroy(_host);
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE runtime pump shutdown failed", ex);
            }
            finally
            {
                _host = null;
            }
        }

        private sealed class FuseRuntimePumpRunner : MonoBehaviour
        {
            // slopwatch-ignore: SW002 Unity invokes Update as an instance message, so CA1822 suppression is required.
            [System.Diagnostics.CodeAnalysis.SuppressMessage(
                "Performance", "CA1822:Mark members as static",
                Justification = "Unity invokes Update() as an instance message; a static method is never called.")]
            private void Update()
            {
                // Scenery load-failure records + broken-scenery quarantines:
                // queued from task continuations / the log hook / the bundle
                // audit (any thread), resolved and applied here.
                FUSE.Patches.FuseSceneryLoadFailurePatch.DrainPending();

                // Mod health exception observations: first-seen signatures
                // queued by the threaded log hook (any thread), attributed and
                // recorded (with throttled log lines) here.
                FuseModExceptionLogHook.DrainPending();
            }
        }
    }
}
