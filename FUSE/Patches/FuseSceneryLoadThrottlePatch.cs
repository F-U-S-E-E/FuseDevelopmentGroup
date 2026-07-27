using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FUSE.Infrastructure;
using HarmonyLib;
using Helpers;
using UnityEngine;

namespace FUSE.Patches
{
    /// <summary>
    /// Issue #76 follow-up: smooths the one-time asset-load stall when a large batch
    /// of FUSE scenery loads at once (e.g. teleport-in).
    ///
    /// The game loads a scenery model in <c>SceneryAssetInstance.SetLoaded(true)</c>:
    /// it awaits an async asset-bundle load and then, on the main thread, runs
    /// <c>Object.Instantiate</c> + component setup. When a whole region crosses into
    /// the load band in the same settle (a teleport), hundreds of those continuations
    /// resume together and the per-frame Instantiate burst drops the frame rate to
    /// ~1&#160;fps with multi-second per-object latency.
    ///
    /// This prefix limits both how many FUSE scenery loads may <em>start</em> per frame
    /// (<see cref="MaxLoadsPerFrame"/>) and how many asset tasks may remain outstanding
    /// (<see cref="MaxConcurrentLoads"/>). Over-budget loads are deferred into a queue
    /// and released by a persistent pump (<see cref="FuseSceneryLoadThrottlePump"/>).
    /// The outstanding-load ceiling prevents a queue from draining into hundreds of
    /// simultaneous AssetBundle requests while starts are merely spread across frames.
    /// A pump is required because <c>CullingSphereStateChanged</c> is event/world-shift
    /// driven (not per-frame), so a deferred object would otherwise never be revisited.
    ///
    /// Scope and safety:
    ///  - FUSE-owned scenery only (<see cref="FUSE.Runtime.API.SceneryAPI.FuseSceneryMarker"/>);
    ///    vanilla loading is untouched.
    ///  - Only the <c>loaded == true</c> (load) path is gated. An unload invalidates
    ///    any queued load for that instance before continuing down the normal path.
    ///  - Objects already loading/loaded (<c>_wantsLoaded</c>) pass straight through so
    ///    the original's own no-op guard handles them and no budget is wasted.
    ///  - On release the pump drops anything now far outside the load band
    ///    (<see cref="StaleLoadDropDistance"/>), so a backlog can't force-load scenery
    ///    the camera has already moved away from.
    ///  - Every failure path is fail-open: if reflection is unavailable or anything
    ///    throws, the original load runs normally (no stranded scenery).
    ///
    /// Always on (no user setting). The benchmark can force it off for an A/B
    /// baseline via <see cref="BenchmarkThrottleOverride"/>.
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
        internal const int MaxLoadsPerFrame = 4;

        /// <summary>
        /// Hard ceiling for FUSE scenery asset tasks that have started but not
        /// completed. The per-frame budget alone cannot bound accumulated memory
        /// while individual loads take multiple frames.
        /// </summary>
        internal const int MaxConcurrentLoads = 4;

        /// <summary>
        /// Extra bounded lane reserved for structures inside the immediate
        /// destination ring. It doubles local structure throughput after a
        /// teleport without relaxing the four-task background ceiling.
        /// </summary>
        internal const int MaxDestinationLoadsPerFrame = 4;
        internal const int MaxDestinationConcurrentLoads = 4;

        // A deferred load whose object is now this far from the camera is dropped
        // instead of released. MUST stay well beyond the game's outermost scenery
        // band (~1500m): a dropped object beyond that band sits in band 3 and
        // receives a fresh band-transition event when the camera returns, but one
        // dropped while still inside band <= 2 never gets another CullingGroup event
        // and would be stranded invisible until a teleport re-evaluates it. The 2x
        // margin also covers band distances being measured against the object's
        // culling SPHERE, not its center — a large-radius landmark can still be in
        // band <= 2 with its center far past 1500m. The trade-off of the margin:
        // a queued object released while 1500-3000m out loads into band 3 and stays
        // resident until the camera next revisits and leaves its band — bounded to
        // whatever was queued when the camera departed, and self-correcting, unlike
        // a stranded drop.
        internal const float StaleLoadDropDistance = 3000f;
        private const float StaleLoadDropDistanceSqr = StaleLoadDropDistance * StaleLoadDropDistance;

        // Re-prioritize a non-trivial runtime backlog after a camera jump so the
        // destination's nearest scenery is not trapped behind requests from the
        // location the player just left. Background keeps the proven
        // four-start/four-in-flight ceiling; nearby structures may also use the
        // separate four-slot destination lane.
        internal const float PendingPriorityResortDistance = 50f;
        internal const int PendingPriorityResortGrowth = 4;
        internal const int PendingPriorityResortFrameGap = 3;
        internal const float ImmediateSceneryPriorityDistance = 250f;
        private const float PendingPriorityResortDistanceSqr =
            PendingPriorityResortDistance * PendingPriorityResortDistance;
        private const float ImmediateSceneryPriorityDistanceSqr =
            ImmediateSceneryPriorityDistance *
            ImmediateSceneryPriorityDistance;

        // Benchmark-only override (NOT a user setting): null = normal always-on
        // throttling; false = force OFF for an A/B baseline pass; true = force ON.
        // Set transiently by FuseSceneryBenchmark and cleared after a run.
        internal static bool? BenchmarkThrottleOverride;

        private static readonly FuseSceneryLoadBudget Budget = new FuseSceneryLoadBudget(MaxLoadsPerFrame);
        private static readonly FuseSceneryLoadConcurrencyGate ConcurrencyGate =
            new FuseSceneryLoadConcurrencyGate(MaxConcurrentLoads);
        private static readonly FuseSceneryLoadBudget DestinationBudget =
            new FuseSceneryLoadBudget(MaxDestinationLoadsPerFrame);
        private static readonly FuseSceneryLoadConcurrencyGate
            DestinationConcurrencyGate =
                new FuseSceneryLoadConcurrencyGate(
                    MaxDestinationConcurrentLoads);

        // Deferred loads, FIFO. The instance id is captured while the object is alive,
        // so the registry never hashes a destroyed Unity object. A per-request token
        // distinguishes the current entry from canceled tombstones and newer requests.
        private static readonly Queue<PendingLoad> Pending = new Queue<PendingLoad>();
        private static readonly List<PrioritizedPendingLoad> PendingPriorityScratch =
            new List<PrioritizedPendingLoad>();
        private static readonly FuseSceneryPendingLoadTokens PendingTokens =
            new FuseSceneryPendingLoadTokens();

        private static readonly MethodInfo SetLoadedMethod =
            AccessTools.Method(typeof(SceneryAssetInstance), "SetLoaded", new[] { typeof(bool) });

        // Reused arg buffer for the pump's reflective SetLoaded(true) re-drive
        // (main-thread only, so a shared buffer is safe).
        private static readonly object[] LoadTrueArgs = { true };

        // True while the pump is re-driving a deferred load, so our own prefix lets
        // that call through instead of re-deferring it.
        private static bool _pumping;

        private static FuseSceneryLoadThrottlePump _pump;

        private static long _deferredLoads;
        private static long _releasedLoads;
        private static long _droppedStaleLoads;
        private static long _priorityResorts;
        private static int _peakQueueDepth;
        private static bool _hasPrioritySortAnchor;
        private static Vector3 _prioritySortAnchor;
        private static int _lastPrioritySortFrame;
        private static int _pendingCountAtLastPrioritySort;

        /// <summary>FUSE loads deferred (queued) since the last reset.</summary>
        internal static long DeferredLoads => _deferredLoads;

        /// <summary>Deferred loads released by the pump since the last reset.</summary>
        internal static long ReleasedLoads => _releasedLoads;

        /// <summary>Queued loads dropped because they moved beyond <see cref="StaleLoadDropDistance"/> before release.</summary>
        internal static long DroppedStaleLoads => _droppedStaleLoads;

        /// <summary>Camera-jump queue reorders completed since the last reset.</summary>
        internal static long PriorityResorts => _priorityResorts;

        /// <summary>High-water mark of the deferred queue since the last reset.</summary>
        internal static int PeakQueueDepth => _peakQueueDepth;

        /// <summary>Current number of deferred loads awaiting release.</summary>
        internal static int QueueDepth => PendingTokens.Count;

        /// <summary>FUSE scenery asset tasks currently outstanding.</summary>
        internal static int InFlightLoads =>
            ConcurrencyGate.Active + DestinationConcurrencyGate.Active;

        /// <summary>Highest outstanding FUSE scenery task count since the last reset.</summary>
        internal static int PeakInFlightLoads =>
            ConcurrencyGate.Peak + DestinationConcurrencyGate.Peak;

        internal static int DestinationInFlightLoads =>
            DestinationConcurrencyGate.Active;

        /// <summary>True when reflection bound and the throttle can operate.</summary>
        internal static bool Available =>
            FuseSceneryModelState.Available &&
            FuseSceneryModelState.LoadTaskAvailable &&
            SetLoadedMethod != null;

        internal static void ResetStats()
        {
            _deferredLoads = 0;
            _releasedLoads = 0;
            _droppedStaleLoads = 0;
            _priorityResorts = 0;
            _peakQueueDepth = 0;
            ConcurrencyGate.ResetPeak();
            DestinationConcurrencyGate.ResetPeak();
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

        /// <summary>
        /// Pure stale-drop decision, extracted for unit testing (see
        /// FUSE.UnityTests): should a queued load be dropped because its object is
        /// now beyond <see cref="StaleLoadDropDistance"/> from the camera? The
        /// comparison direction and the constant both carry the stranding invariant
        /// documented on <see cref="StaleLoadDropDistance"/>.
        /// </summary>
        internal static bool ShouldDropStale(Vector3 cameraPos, Vector3 objectPos)
        {
            return (cameraPos - objectPos).sqrMagnitude >= StaleLoadDropDistanceSqr;
        }

        /// <summary>
        /// Pure queue-resort decision for unit tests. A camera jump is the primary
        /// trigger; substantial queue growth shortly after that jump catches
        /// destination culling callbacks that arrived after the first reorder.
        /// </summary>
        internal static bool ShouldResortPendingLoads(
            bool hasAnchor,
            Vector3 anchor,
            Vector3 cameraPosition,
            int pendingCount,
            int pendingCountAtLastSort,
            int framesSinceLastSort)
        {
            if (pendingCount <= 1)
            {
                return false;
            }

            if (!hasAnchor)
            {
                return true;
            }

            if (framesSinceLastSort < PendingPriorityResortFrameGap)
            {
                return false;
            }

            return (cameraPosition - anchor).sqrMagnitude >=
                       PendingPriorityResortDistanceSqr ||
                   pendingCount >=
                       pendingCountAtLastSort + PendingPriorityResortGrowth;
        }

        [HarmonyPriority(Priority.First)]
        private static bool Prefix(
            SceneryAssetInstance __instance,
            bool loaded,
            out LoadReservation __state)
        {
            __state = null;

            // A queued true belongs to the culling decision that produced it. Once
            // false arrives, replaying that old request would resurrect scenery that
            // the game has already asked to unload. Leave a cheap tombstone in the
            // FIFO; Pump skips it using the per-request token.
            if (!loaded)
            {
                if (__instance != null)
                {
                    PendingTokens.Invalidate(__instance.GetInstanceID());
                }

                return true;
            }

            // This MUST precede the pump bypass and every fail-open branch. A
            // deferred placement may be quarantined while queued, and the pump's
            // reflective SetLoaded(true) re-drive must not resurrect it. The unload
            // return above keeps false calls out of the IsQuarantined lock+probe.
            if (__instance != null &&
                FuseSceneryLoadFailurePatch.IsQuarantined(__instance.identifier))
            {
                return false;
            }

            // Pump re-drives, the forced-off baseline, and the case where reflection
            // didn't bind all run the normal load path unchanged.
            if (_pumping || __instance == null ||
                BenchmarkThrottleOverride == false || !Available)
            {
                return true;
            }

            try
            {
                var marker = __instance.GetComponent<FUSE.Runtime.API.SceneryAPI.FuseSceneryMarker>();
                if (marker == null)
                {
                    return true; // FUSE-owned scenery only; vanilla loads normally.
                }

                // Mask-bearing buildings load immediately — never throttled or deferred.
                // Their FIRST load is what registers the terrain flatten/cut (the welded
                // masks are decoupled on model load), so queueing one behind plain scenery
                // leaves visibly wrong ground for the wait; and the mask-bearing set near
                // any single spot is small, so the bypass costs little. Reloads after a
                // teleport keep the bypass too: the standalone mask already holds the
                // terrain, but the building itself is what the player is usually standing
                // next to when it re-streams.
                if (marker.IsMaskBearing)
                {
                    return true;
                }

                // Already loading or loaded: the original SetLoaded(true) is a cheap
                // no-op, so let it through rather than spend a budget slot on it —
                // band 3 -> 2 re-entries for resident objects would otherwise burn
                // budget on work the game ignores anyway.
                if (FuseSceneryModelState.IsLoadRequested(__instance))
                {
                    return true;
                }

                var useDestinationLane =
                    ShouldUseDestinationPriorityLane(
                        marker.IsPriorityStructure,
                        FuseSceneryCameraRef.Resolve(),
                        __instance);
                var budget = useDestinationLane
                    ? DestinationBudget
                    : Budget;
                var concurrencyGate = useDestinationLane
                    ? DestinationConcurrencyGate
                    : ConcurrencyGate;
                budget.BeginFrame(Time.frameCount);

                if (PendingTokens.Contains(__instance.GetInstanceID()))
                {
                    return false; // already queued; the pump will release it.
                }

                if (concurrencyGate.TryAcquire(out var lease))
                {
                    if (budget.TryConsume())
                    {
                        __state = new LoadReservation(lease);
                        return true;
                    }

                    lease.Dispose();
                }

                // Create the pump BEFORE recording the pending token: if EnsurePump
                // threw after Enqueue, the catch below would fail open while a stale
                // token blocked future SetLoaded(true) calls with nothing draining it.
                EnsurePump();
                Enqueue(
                    __instance,
                    marker.IsPriorityStructure);
                return false; // over budget — defer to the pump.
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE scenery load-throttle prefix failed", ex);
                return true; // fail open: never strand a load on our account.
            }
        }

        private static void Postfix(
            SceneryAssetInstance __instance,
            LoadReservation __state)
        {
            if (__state == null)
            {
                return;
            }

            AttachReservationToLoadTask(__instance, __state.Lease);
            __state.Transferred = true;
        }

        private static Exception Finalizer(
            Exception __exception,
            LoadReservation __state)
        {
            if (__state != null && !__state.Transferred)
            {
                __state.Lease.Dispose();
            }

            return __exception;
        }

        private static void Enqueue(
            SceneryAssetInstance instance,
            bool isPriorityStructure)
        {
            Enqueue(
                instance,
                isPriorityStructure,
                countDeferral: true);
        }

        private static void Enqueue(
            SceneryAssetInstance instance,
            bool isPriorityStructure,
            bool countDeferral)
        {
            var id = instance.GetInstanceID();
            var token = PendingTokens.Issue(id);
            Pending.Enqueue(
                new PendingLoad(
                    instance,
                    id,
                    token,
                    isPriorityStructure));
            if (countDeferral)
            {
                _deferredLoads++;
            }

            if (PendingTokens.Count > _peakQueueDepth)
            {
                _peakQueueDepth = PendingTokens.Count;
            }
        }

        /// <summary>
        /// Releases deferred loads up to the remaining per-frame budget. Driven every
        /// frame by <see cref="FuseSceneryLoadThrottlePump"/> so backlog drains even
        /// when the culler is quiet. Null skips and stale-distance drops cost no
        /// budget, so a stale backlog clears quickly without starving live loads.
        /// </summary>
        internal static void Pump()
        {
            if (PendingTokens.Count == 0)
            {
                Pending.Clear();
                return;
            }

            try
            {
                Budget.BeginFrame(Time.frameCount);
                DestinationBudget.BeginFrame(Time.frameCount);
                var camera = FuseSceneryCameraRef.Resolve();
                if (camera != null)
                {
                    var cameraTransform = camera.transform;
                    PrioritizePendingLoads(
                        cameraTransform.position,
                        cameraTransform.forward);
                }

                // The queue only shrinks inside this loop, so a count snapshot bounds
                // the work even though null/stale entries are skipped for free.
                var safety = Pending.Count;
                while (safety-- > 0 &&
                       Pending.Count > 0 &&
                       HasAnyLoadLaneCapacity())
                {
                    var pending = Pending.Dequeue();
                    if (!PendingTokens.TryConsume(pending.Id, pending.Token))
                    {
                        continue;
                    }
                    var instance = pending.Instance;

                    if (instance == null)
                    {
                        continue; // destroyed while queued.
                    }

                    if (FuseSceneryLoadFailurePatch.IsQuarantined(instance.identifier))
                    {
                        continue; // quarantined after it entered the throttle queue.
                    }

                    // Loaded via another path since it was queued: nothing to do.
                    if (FuseSceneryModelState.IsLoadRequested(instance))
                    {
                        continue;
                    }

                    // Moved far outside the load band while queued: don't force-load
                    // scenery the camera has moved away from. Uses a freshly-resolved
                    // camera so a stale reference can't drop a load the player is
                    // actually next to.
                    if (camera != null &&
                        ShouldDropStale(camera.transform.position, instance.transform.position))
                    {
                        _droppedStaleLoads++;
                        continue;
                    }

                    var useDestinationLane =
                        ShouldUseDestinationPriorityLane(
                            pending.IsPriorityStructure,
                            camera,
                            instance);
                    var budget = useDestinationLane
                        ? DestinationBudget
                        : Budget;
                    var concurrencyGate = useDestinationLane
                        ? DestinationConcurrencyGate
                        : ConcurrencyGate;

                    if (budget.Remaining <= 0 ||
                        !concurrencyGate.TryAcquire(out var lease))
                    {
                        Enqueue(
                            instance,
                            pending.IsPriorityStructure,
                            countDeferral: false);
                        continue;
                    }

                    if (!budget.TryConsume())
                    {
                        lease.Dispose();
                        Enqueue(
                            instance,
                            pending.IsPriorityStructure,
                            countDeferral: false);
                        continue;
                    }

                    Release(instance, lease);
                }
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE scenery load-throttle pump failed", ex);
            }
        }

        private static void Release(
            SceneryAssetInstance instance,
            FuseSceneryLoadConcurrencyGate.Lease lease)
        {
            _pumping = true;
            try
            {
                SetLoadedMethod.Invoke(instance, LoadTrueArgs);
                AttachReservationToLoadTask(instance, lease);
                _releasedLoads++;
            }
            catch (Exception ex)
            {
                lease.Dispose();
                FuseLog.Exception("FUSE scenery load-throttle release failed", ex);
            }
            finally
            {
                _pumping = false;
            }
        }

        internal static int ComparePendingLoadPriority(
            bool leftIsPriorityStructure,
            bool leftInFront,
            float leftDistanceSqr,
            int leftSequence,
            bool rightIsPriorityStructure,
            bool rightInFront,
            float rightDistanceSqr,
            int rightSequence)
        {
            var leftPriorityClass = PendingLoadPriorityClass(
                leftIsPriorityStructure,
                leftInFront,
                leftDistanceSqr);
            var rightPriorityClass = PendingLoadPriorityClass(
                rightIsPriorityStructure,
                rightInFront,
                rightDistanceSqr);
            var comparison = leftPriorityClass.CompareTo(rightPriorityClass);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = leftDistanceSqr.CompareTo(rightDistanceSqr);
            return comparison != 0
                ? comparison
                : leftSequence.CompareTo(rightSequence);
        }

        private static int PendingLoadPriorityClass(
            bool isPriorityStructure,
            bool inFront,
            float distanceSqr)
        {
            // Everything immediately around the player wins first, including
            // structures just behind the camera. Outside that safety ring,
            // favor the visible half-space before loading scenery behind the
            // player. This changes only order, never range or load eligibility.
            if (distanceSqr <= ImmediateSceneryPriorityDistanceSqr)
            {
                return isPriorityStructure ? 0 : 1;
            }

            if (isPriorityStructure)
            {
                return inFront ? 2 : 3;
            }

            return inFront ? 4 : 5;
        }

        internal static bool ShouldUseDestinationPriorityLane(
            bool isPriorityStructure,
            Camera camera,
            SceneryAssetInstance instance)
        {
            if (!isPriorityStructure ||
                camera == null ||
                instance == null)
            {
                return false;
            }

            try
            {
                return (camera.transform.position -
                        instance.transform.position)
                    .sqrMagnitude <=
                    ImmediateSceneryPriorityDistanceSqr;
            }
            catch
            {
                return false;
            }
        }

        private static bool HasAnyLoadLaneCapacity()
        {
            return (Budget.Remaining > 0 &&
                    ConcurrencyGate.HasCapacity) ||
                   (DestinationBudget.Remaining > 0 &&
                    DestinationConcurrencyGate.HasCapacity);
        }

        private static void PrioritizePendingLoads(
            Vector3 cameraPosition,
            Vector3 cameraForward)
        {
            var framesSinceLastSort = _hasPrioritySortAnchor
                ? Time.frameCount - _lastPrioritySortFrame
                : int.MaxValue;
            if (!ShouldResortPendingLoads(
                    _hasPrioritySortAnchor,
                    _prioritySortAnchor,
                    cameraPosition,
                    PendingTokens.Count,
                    _pendingCountAtLastPrioritySort,
                    framesSinceLastSort))
            {
                return;
            }

            PendingPriorityScratch.Clear();
            var sequence = 0;
            while (Pending.Count > 0)
            {
                var pending = Pending.Dequeue();
                if (!PendingTokens.IsCurrent(pending.Id, pending.Token))
                {
                    continue;
                }

                var instance = pending.Instance;
                if (instance == null)
                {
                    PendingTokens.Invalidate(pending.Id);
                    continue;
                }

                float distanceSqr;
                var inFront = false;
                try
                {
                    var offset =
                        instance.transform.position - cameraPosition;
                    distanceSqr = offset.sqrMagnitude;
                    inFront = Vector3.Dot(cameraForward, offset) >= 0f;
                }
                catch
                {
                    // Keep an unexpected but live entry at the back of the queue.
                    // Its normal release path remains fail-open.
                    distanceSqr = float.MaxValue;
                }

                PendingPriorityScratch.Add(
                    new PrioritizedPendingLoad(
                        pending,
                        pending.IsPriorityStructure,
                        inFront,
                        distanceSqr,
                        sequence++));
            }

            PendingPriorityScratch.Sort(
                PrioritizedPendingLoadComparer.Instance);
            for (var index = 0; index < PendingPriorityScratch.Count; index++)
            {
                Pending.Enqueue(PendingPriorityScratch[index].Pending);
            }

            PendingPriorityScratch.Clear();
            _hasPrioritySortAnchor = true;
            _prioritySortAnchor = cameraPosition;
            _lastPrioritySortFrame = Time.frameCount;
            _pendingCountAtLastPrioritySort = PendingTokens.Count;
            _priorityResorts++;
        }

        private static void AttachReservationToLoadTask(
            SceneryAssetInstance instance,
            FuseSceneryLoadConcurrencyGate.Lease lease)
        {
            try
            {
                var loadTask = FuseSceneryModelState.GetLoadTask(instance);
                if (loadTask == null)
                {
                    lease.Dispose();
                    return;
                }

                _ = loadTask.ContinueWith(
                    (_, state) =>
                        ((FuseSceneryLoadConcurrencyGate.Lease)state).Dispose(),
                    lease,
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
            catch (Exception ex)
            {
                lease.Dispose();
                FuseLog.Exception(
                    "FUSE scenery load-throttle could not track an outstanding load",
                    ex);
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
            PendingPriorityScratch.Clear();
            PendingTokens.Clear();
            Budget.Reset();
            DestinationBudget.Reset();
            ConcurrencyGate.Reset();
            DestinationConcurrencyGate.Reset();
            FuseSceneryCameraRef.Reset();
            _pumping = false;
            _hasPrioritySortAnchor = false;
            _prioritySortAnchor = Vector3.zero;
            _lastPrioritySortFrame = 0;
            _pendingCountAtLastPrioritySort = 0;
            ResetStats();
        }

        private readonly struct PendingLoad
        {
            internal PendingLoad(
                SceneryAssetInstance instance,
                int id,
                long token,
                bool isPriorityStructure)
            {
                Instance = instance;
                Id = id;
                Token = token;
                IsPriorityStructure = isPriorityStructure;
            }

            internal SceneryAssetInstance Instance { get; }

            internal int Id { get; }

            internal long Token { get; }

            internal bool IsPriorityStructure { get; }
        }

        private readonly struct PrioritizedPendingLoad
        {
            internal PrioritizedPendingLoad(
                PendingLoad pending,
                bool isPriorityStructure,
                bool inFront,
                float distanceSqr,
                int sequence)
            {
                Pending = pending;
                IsPriorityStructure = isPriorityStructure;
                InFront = inFront;
                DistanceSqr = distanceSqr;
                Sequence = sequence;
            }

            internal PendingLoad Pending { get; }

            internal bool IsPriorityStructure { get; }

            internal bool InFront { get; }

            internal float DistanceSqr { get; }

            internal int Sequence { get; }
        }

        private sealed class PrioritizedPendingLoadComparer
            : IComparer<PrioritizedPendingLoad>
        {
            internal static readonly PrioritizedPendingLoadComparer Instance =
                new PrioritizedPendingLoadComparer();

            public int Compare(
                PrioritizedPendingLoad left,
                PrioritizedPendingLoad right)
            {
                return ComparePendingLoadPriority(
                    left.IsPriorityStructure,
                    left.InFront,
                    left.DistanceSqr,
                    left.Sequence,
                    right.IsPriorityStructure,
                    right.InFront,
                    right.DistanceSqr,
                    right.Sequence);
            }
        }

        private sealed class LoadReservation
        {
            internal LoadReservation(FuseSceneryLoadConcurrencyGate.Lease lease)
            {
                Lease = lease;
            }

            internal FuseSceneryLoadConcurrencyGate.Lease Lease { get; }

            internal bool Transferred { get; set; }
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
