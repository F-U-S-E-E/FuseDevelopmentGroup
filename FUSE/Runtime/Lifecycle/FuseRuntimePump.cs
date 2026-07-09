using System;
using FUSE.Infrastructure;
using UnityEngine;

namespace FUSE.Runtime.Lifecycle
{
    /// <summary>
    /// Always-on per-frame pump for FUSE work that must drain on the main
    /// thread regardless of which optional UI hosts exist.
    ///
    /// The scenery load-failure drain originally rode
    /// <c>FuseHealthUi.Update</c> — but the menu-UI rewrite retired the call
    /// that created that host, so the component never existed and the drain
    /// NEVER ran in the field: both failure nets enqueued faults into a queue
    /// nobody emptied (observed as sessions with dozens of load failures and
    /// zero records, toasts, or quarantines, while the enqueue-side counters
    /// kept climbing). Queue-consuming work now lives here, on a host created
    /// unconditionally at plugin load, so a UI refactor can never silently
    /// starve it again. Cost when idle: two empty TryDequeue calls per frame.
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

            try
            {
                _host = new GameObject("FUSE.RuntimePump");
                UnityEngine.Object.DontDestroyOnLoad(_host);
                _host.hideFlags = HideFlags.HideAndDontSave;
                _host.AddComponent<FuseRuntimePumpRunner>();
                FuseLog.Info("FUSE runtime pump initialized.");
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE runtime pump host creation failed", ex);
            }
        }

        internal static void Shutdown()
        {
            if (_host == null)
            {
                return;
            }

            UnityEngine.Object.Destroy(_host);
            _host = null;
        }

        private sealed class FuseRuntimePumpRunner : MonoBehaviour
        {
            private void Update()
            {
                // Scenery load-failure records + broken-scenery quarantines:
                // queued from task continuations / the log hook / the bundle
                // audit (any thread), resolved and applied here.
                FUSE.Patches.FuseSceneryLoadFailurePatch.DrainPending();
            }
        }
    }
}
