using System;
using System.Collections.Generic;
using System.Reflection;
using FUSE.Infrastructure;
using HarmonyLib;
using Helpers;
using UnityEngine;

namespace FUSE.Patches
{
    /// <summary>
    /// Issue #76 follow-up: smooths the one-time asset-load stall when a large batch
    /// of FUSE scenery loads at once (e.g. teleport-in), the bottleneck the culling
    /// debounce (<see cref="FuseSceneryCullingDebouncePatch"/>) deliberately left out
    /// of scope.
    ///
    /// The game loads a scenery model in <c>SceneryAssetInstance.SetLoaded(true)</c>:
    /// it awaits an async asset-bundle load and then, on the main thread, runs
    /// <c>Object.Instantiate</c> + component setup. When a whole region crosses into
    /// the load band in the same settle (a teleport, or the debounce widening the
    /// resident set to its 3&#160;km deadband), hundreds of those continuations resume
    /// together and the per-frame Instantiate burst drops the frame rate to ~1&#160;fps
    /// with multi-second per-object latency.
    ///
    /// This prefix throttles how many FUSE scenery loads may <em>start</em> per frame
    /// (<see cref="MaxLoadsPerFrame"/>). Over-budget loads are deferred into a queue
    /// and released a few per frame by a persistent pump
    /// (<see cref="FuseSceneryLoadThrottlePump"/>). Staggering the starts staggers the
    /// completions, so the Instantiate work spreads across frames instead of spiking.
    /// A pump is required because <c>CullingSphereStateChanged</c> is event/world-shift
    /// driven (not per-frame), so a deferred object would otherwise never be revisited
    /// — the same cadence constraint the debounce patch documents.
    ///
    /// Scope and safety:
    ///  - FUSE-owned scenery only (<see cref="FUSE.Runtime.API.SceneryAPI.FuseSceneryMarker"/>);
    ///    vanilla loading is untouched.
    ///  - Only the <c>loaded == true</c> (load) path is gated; unloads run vanilla.
    ///  - Objects already loading/loaded (<c>_wantsLoaded</c>) pass straight through so
    ///    the original's own no-op guard handles them and no budget is wasted.
    ///  - On release the pump drops anything that has left the debounce deadband
    ///    (reusing <see cref="FuseSceneryCullingDebouncePatch.ShouldHoldResident"/>), so
    ///    a backlog can't force-load scenery the camera has already moved away from.
    ///  - Every failure path is fail-open: if reflection is unavailable or anything
    ///    throws, the original load runs normally (no stranded scenery).
    ///
    /// Always on (no user setting), matching the debounce. The benchmark can force it
    /// off for an A/B baseline via <see cref="BenchmarkThrottleOverride"/>.
    /// </summary>
    [HarmonyPatch(typeof(SceneryAssetInstance), "SetLoaded", new[] { typeof(bool) })]
    internal static class FuseSceneryLoadThrottlePatch
    {
        /// <summary>
        /// Max FUSE scenery loads allowed to START per frame. The single tunable: too
        /// low and a big region takes many seconds to populate; too high and the
        /// Instantiate burst still spikes the frame. Sized so the per-frame
        /// Instantiate cost stays within a ~30-60&#160;fps frame for typical prefabs.
        /// </summary>
        internal const int MaxLoadsPerFrame = 8;

        // Benchmark-only override (NOT a user setting): null = normal always-on
        // throttling; false = force OFF for an A/B baseline pass; true = force ON.
        // Set transiently by FuseSceneryBenchmark and cleared after a run.
        internal static bool? BenchmarkThrottleOverride;

        private static readonly FuseSceneryLoadBudget Budget = new FuseSceneryLoadBudget(MaxLoadsPerFrame);

        // Deferred loads, FIFO. The instance id is captured at enqueue time (while the
        // object is alive) so set membership never depends on hashing a Unity object —
        // a destroyed UnityEngine.Object collapses to "== null" and is unsafe as a key.
        private static readonly Queue<PendingLoad> Pending = new Queue<PendingLoad>();
        private static readonly HashSet<int> PendingIds = new HashSet<int>();

        // Fast typed accessor for the private load-state flag (also covered by the
        // reflection-surface canary test). Null only if the game renamed the field, in
        // which case we fail open and never throttle.
        private static readonly AccessTools.FieldRef<SceneryAssetInstance, bool> WantsLoadedRef =
            BuildWantsLoadedRef();

        private static readonly MethodInfo SetLoadedMethod =
            AccessTools.Method(typeof(SceneryAssetInstance), "SetLoaded", new[] { typeof(bool) });

        // Reused arg buffer for the pump's reflective SetLoaded(true) re-drive
        // (main-thread only, so a shared buffer is safe).
        private static readonly object[] LoadTrueArgs = { true };

        // True while the pump is re-driving a deferred load, so our own prefix lets
        // that call through instead of re-deferring it.
        private static bool _pumping;

        private static Camera _camera;
        private static FuseSceneryLoadThrottlePump _pump;

        private static long _deferredLoads;
        private static long _releasedLoads;
        private static long _droppedStaleLoads;
        private static int _peakQueueDepth;

        /// <summary>FUSE loads deferred (queued) since the last reset.</summary>
        internal static long DeferredLoads => _deferredLoads;

        /// <summary>Deferred loads released by the pump since the last reset.</summary>
        internal static long ReleasedLoads => _releasedLoads;

        /// <summary>Queued loads dropped because they left the deadband before release.</summary>
        internal static long DroppedStaleLoads => _droppedStaleLoads;

        /// <summary>High-water mark of the deferred queue since the last reset.</summary>
        internal static int PeakQueueDepth => _peakQueueDepth;

        /// <summary>Current number of deferred loads awaiting release.</summary>
        internal static int QueueDepth => Pending.Count;

        /// <summary>True when reflection bound and the throttle can operate.</summary>
        internal static bool Available => WantsLoadedRef != null && SetLoadedMethod != null;

        internal static void ResetStats()
        {
            _deferredLoads = 0;
            _releasedLoads = 0;
            _droppedStaleLoads = 0;
            _peakQueueDepth = 0;
        }

        /// <summary>
        /// Pure budget decision, extracted for unit testing: may a load start now
        /// given how many have already started this frame? Mirrors
        /// <see cref="FuseSceneryLoadBudget.TryConsume"/>'s ceiling check without the
        /// side effect.
        /// </summary>
        internal static bool ShouldStartLoadNow(int startedThisFrame, int maxLoadsPerFrame)
        {
            return startedThisFrame < maxLoadsPerFrame;
        }

        private static bool Prefix(SceneryAssetInstance __instance, bool loaded)
        {
            // Unloads, pump re-drives, the forced-off baseline, and the case where
            // reflection didn't bind all run the vanilla load path unchanged.
            if (!loaded || _pumping || __instance == null ||
                BenchmarkThrottleOverride == false || !Available)
            {
                return true;
            }

            try
            {
                if (__instance.GetComponent<FUSE.Runtime.API.SceneryAPI.FuseSceneryMarker>() == null)
                {
                    return true; // FUSE-owned scenery only; vanilla loads normally.
                }

                // Already loading or loaded: the original SetLoaded(true) is a cheap
                // no-op, so let it through rather than spend a budget slot on it. The
                // debounce clamps band 3 -> 2 repeatedly for resident objects, which
                // would otherwise hammer this path.
                if (WantsLoadedRef(__instance))
                {
                    return true;
                }

                Budget.BeginFrame(Time.frameCount);

                if (PendingIds.Contains(__instance.GetInstanceID()))
                {
                    return false; // already queued; the pump will release it.
                }

                if (Budget.TryConsume())
                {
                    return true; // under budget — start the load this frame.
                }

                Enqueue(__instance);
                EnsurePump();
                return false; // over budget — defer to the pump.
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE scenery load-throttle prefix failed", ex);
                return true; // fail open: never strand a load on our account.
            }
        }

        private static void Enqueue(SceneryAssetInstance instance)
        {
            var id = instance.GetInstanceID();
            Pending.Enqueue(new PendingLoad(instance, id));
            PendingIds.Add(id);
            _deferredLoads++;
            if (Pending.Count > _peakQueueDepth)
            {
                _peakQueueDepth = Pending.Count;
            }
        }

        /// <summary>
        /// Releases deferred loads up to the remaining per-frame budget. Driven every
        /// frame by <see cref="FuseSceneryLoadThrottlePump"/> so backlog drains even
        /// when the culler is quiet. Null skips and deadband drops cost no budget, so a
        /// stale backlog clears quickly without starving live loads.
        /// </summary>
        internal static void Pump()
        {
            if (Pending.Count == 0)
            {
                return;
            }

            try
            {
                Budget.BeginFrame(Time.frameCount);
                if (_camera == null)
                {
                    _camera = Camera.main;
                }

                // The queue only shrinks inside this loop, so a count snapshot bounds
                // the work even though null/stale entries are skipped for free.
                var safety = Pending.Count;
                while (safety-- > 0 && Pending.Count > 0 && Budget.Remaining > 0)
                {
                    var pending = Pending.Dequeue();
                    PendingIds.Remove(pending.Id);
                    var instance = pending.Instance;

                    if (instance == null)
                    {
                        continue; // destroyed while queued.
                    }

                    // Loaded via another path since it was queued: nothing to do.
                    if (WantsLoadedRef != null && WantsLoadedRef(instance))
                    {
                        continue;
                    }

                    // Left the resident deadband while queued: don't force-load scenery
                    // the camera has moved away from (consistent with the debounce).
                    if (_camera != null &&
                        !FuseSceneryCullingDebouncePatch.ShouldHoldResident(
                            _camera.transform.position, instance.transform.position))
                    {
                        _droppedStaleLoads++;
                        continue;
                    }

                    if (!Budget.TryConsume())
                    {
                        // Budget exhausted between the Remaining check and here; requeue
                        // and stop for this frame.
                        Enqueue(instance);
                        break;
                    }

                    Release(instance);
                }
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE scenery load-throttle pump failed", ex);
            }
        }

        private static void Release(SceneryAssetInstance instance)
        {
            _pumping = true;
            try
            {
                SetLoadedMethod.Invoke(instance, LoadTrueArgs);
                _releasedLoads++;
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE scenery load-throttle release failed", ex);
            }
            finally
            {
                _pumping = false;
            }
        }

        private static void EnsurePump()
        {
            if (_pump != null)
            {
                return;
            }

            var host = new GameObject("FUSE.SceneryLoadThrottle");
            UnityEngine.Object.DontDestroyOnLoad(host);
            host.hideFlags = HideFlags.HideAndDontSave;
            _pump = host.AddComponent<FuseSceneryLoadThrottlePump>();
        }

        /// <summary>Tears down the pump and clears all deferred state (mod unload).</summary>
        internal static void Shutdown()
        {
            try
            {
                if (_pump != null)
                {
                    UnityEngine.Object.Destroy(_pump.gameObject);
                    _pump = null;
                }
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE scenery load-throttle shutdown failed", ex);
            }

            Pending.Clear();
            PendingIds.Clear();
            Budget.Reset();
            _camera = null;
            _pumping = false;
            ResetStats();
        }

        private static AccessTools.FieldRef<SceneryAssetInstance, bool> BuildWantsLoadedRef()
        {
            try
            {
                return AccessTools.FieldRefAccess<SceneryAssetInstance, bool>("_wantsLoaded");
            }
            catch (Exception ex)
            {
                FuseLog.Exception(
                    "FUSE scenery load-throttle could not bind SceneryAssetInstance._wantsLoaded; " +
                    "throttling disabled (loads run vanilla)", ex);
                return null;
            }
        }

        private readonly struct PendingLoad
        {
            internal PendingLoad(SceneryAssetInstance instance, int id)
            {
                Instance = instance;
                Id = id;
            }

            internal SceneryAssetInstance Instance { get; }

            internal int Id { get; }
        }
    }

    /// <summary>
    /// Persistent main-thread pump that drains the scenery load-throttle queue a few
    /// per frame. Created on first deferral and kept alive across scene loads; torn
    /// down by <see cref="FuseSceneryLoadThrottlePatch.Shutdown"/> on mod unload.
    /// </summary>
    internal sealed class FuseSceneryLoadThrottlePump : MonoBehaviour
    {
        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Performance", "CA1822:Mark members as static",
            Justification = "Unity invokes Update() as an instance message via reflection; a static method is never called.")]
        private void Update()
        {
            FuseSceneryLoadThrottlePatch.Pump();
        }
    }
}
