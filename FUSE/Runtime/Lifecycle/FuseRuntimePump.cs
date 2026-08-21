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
    /// starve it again. Every driven subsystem has a constant-time idle guard;
    /// the pump does not enumerate packages, scene objects, or asset stores while
    /// no work is pending.
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
            FuseMainThreadWorkTracker.Reset();

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
                // AssemblyLoad can run on a loader thread. Third-party guard
                // discovery and Harmony mutation are coalesced and replayed here.
                var measure = FuseSettings.EnableFrameSpikeDiagnostics;
                var frame = Time.frameCount;
                var started = measure ? FuseMainThreadWorkTracker.Start() : 0L;
                FUSE.Patches.FuseThirdPartyGuardInstaller.DrainPending();
                if (measure)
                    FuseMainThreadWorkTracker.Record(
                        frame,
                        "third-party guard installation",
                        started);

                // RailLoader plugins that implement IUpdateHandler expect their
                // callback every Unity frame. Hosting only OnEnable made those
                // plugins appear loaded while their runtime behavior stayed inert.
                started = measure ? FuseMainThreadWorkTracker.Start() : 0L;
                FUSE.Loading.FuseLegacyAssemblyHost.UpdateHostedPlugins();
                if (measure)
                    FuseMainThreadWorkTracker.Record(
                        frame,
                        "hosted legacy plugin updates",
                        started);

                // TimeSync Mod's ten-minute System.Threading.Timer callback
                // arrives on a worker thread. Replay it here before it can
                // touch StateManager and the Unity-backed in-game console.
                started = measure ? FuseMainThreadWorkTracker.Start() : 0L;
                FUSE.Patches.FuseTimeSyncMainThreadGuardPatches.DrainPending();
                if (measure)
                    FuseMainThreadWorkTracker.Record(
                        frame,
                        "time synchronization",
                        started);

                // Scenery load-failure records + broken-scenery quarantines:
                // queued from task continuations / the log hook / the bundle
                // audit (any thread), resolved and applied here.
                started = measure ? FuseMainThreadWorkTracker.Start() : 0L;
                FUSE.Patches.FuseSceneryLoadFailurePatch.DrainPending();
                if (measure)
                    FuseMainThreadWorkTracker.Record(
                        frame,
                        "scenery failure containment",
                        started);

                // Scenery models are destroyed at the end of the frame. Release
                // their asset references on the following frame so a load/unload
                // race cannot keep a bundle alive or unload it too early.
                started = measure ? FuseMainThreadWorkTracker.Start() : 0L;
                FUSE.Patches.FuseDeferredAssetReferenceReleaseQueue.Update();
                if (measure)
                    FuseMainThreadWorkTracker.Record(
                        frame,
                        "deferred asset release",
                        started);

                // Mod health exception observations: first-seen signatures
                // queued by the threaded log hook (any thread), attributed and
                // recorded (with throttled log lines) here.
                started = measure ? FuseMainThreadWorkTracker.Start() : 0L;
                FuseModExceptionLogHook.DrainPending();
                if (measure)
                    FuseMainThreadWorkTracker.Record(
                        frame,
                        "exception attribution",
                        started);

                // Completed FUSE asset requests whose reference count reached
                // zero are removed individually by the store patch. Reclaim
                // their now-unreachable Unity assets only after streaming has
                // remained quiet long enough to avoid load/unload thrashing.
                started = measure ? FuseMainThreadWorkTracker.Start() : 0L;
                FuseUnusedAssetReclaimer.Update();
                if (measure)
                    FuseMainThreadWorkTracker.Record(
                        frame,
                        "unused asset reclaim",
                        started);

                // AssetBundle requests may all complete together after a
                // teleport. Finish only a bounded number of car bodies,
                // trucks, unique materials, and load models per frame.
                started = measure ? FuseMainThreadWorkTracker.Start() : 0L;
                FUSE.Patches.FuseCarModelCompletionScheduler.Update();
                if (measure)
                    FuseMainThreadWorkTracker.Record(
                        frame,
                        "equipment model completion",
                        started);

                // EquipmentWindow synchronously enumerates every mounted
                // Definitions.json the first time it opens. Warm one cold
                // store per frame so legacy container edit patches (notably
                // Lego's large definition set) cannot produce a multi-second
                // buy-menu stall.
                started = measure ? FuseMainThreadWorkTracker.Start() : 0L;
                FUSE.Patches.FuseEquipmentCatalogWarmup.Update();
                if (measure)
                    FuseMainThreadWorkTracker.Record(
                        frame,
                        "equipment catalogue warm-up",
                        started);
            }
        }
    }
}
