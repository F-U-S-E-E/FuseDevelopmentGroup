using System;
using System.Collections;
using System.Collections.Generic;
using FUSE.Infrastructure;
using FUSE.Patches;
using FUSE.Runtime.API;
using Helpers;
using UnityEngine;

namespace FUSE.Runtime.Lifecycle
{
    /// <summary>
    /// Moves the expensive part of scenery instantiation off the map-load critical
    /// path. During apply, <see cref="SceneryAPI.AddScenery"/> creates each scenery
    /// GameObject but leaves it inactive and hands it here; after the loading screen
    /// this activator flips the eligible ones active over several frames, nearest the
    /// player first, within a per-frame time budget.
    ///
    /// Why this helps: the game streams scenery models lazily, gated by a camera
    /// distance band, but FUSE used to call SetActive(true) on everything during the
    /// loading screen while the gameplay camera did not yet exist — and the game
    /// treats a null camera as the nearest band, so every object force-loaded at
    /// once. Deferring activation until the camera is live lets the game load only
    /// genuinely-near scenery and stream the rest as the player moves, exactly how
    /// vanilla scenery behaves.
    ///
    /// Plain static/visual scenery and mask-bearing scenery are deferred (see
    /// <see cref="FuseSceneryDeferralClassifier"/>); only stateful scenery (KeyValue/
    /// animation) stays eager. Mask-bearing scenery defers so it activates against a
    /// live camera — camera-less activation left masks stuck — and streams like any
    /// other scenery; its terrain contribution survives unloads because the mask is
    /// decoupled onto a persistent object (MapAPI.DecoupleAttachedMapMasks). Every
    /// failure path falls back to inline (synchronous) activation, so a deferral
    /// problem can never leave scenery invisible or wedge a load. Only the initial
    /// map-load wave defers; reapply/live-reload stay synchronous.
    ///
    /// FLAGGED — measured impact is modest (heavy 38-package install, 2026-06):
    /// loading screen ~110s -> ~96s (~13%); only ~14s of scenery activation moved
    /// off the critical path into a ~33s post-load "pop-in" tail (~2,200 objects).
    /// Two limitations to revisit if scenery load becomes a focus again: (1) only
    /// SetActive is deferred — the per-scenery RESERVE work in SceneryAPI.AddScenery
    /// (new GameObject + AddComponent + ApplyDefinition) is still synchronous and is
    /// itself a meaningful slice of apply-world-objects; (2) the dominant map-load
    /// cost is apply-operations (industries), NOT scenery, so this is a secondary
    /// win. Watch the post-load tail for visible pop-in; if it's objectionable, tune
    /// the frame budget or reconsider the approach.
    /// </summary>
    internal static class FuseDeferredSceneryActivator
    {
        private static readonly List<DeferredScenery> Queue = new List<DeferredScenery>();

        private static FuseDeferredSceneryRunner _runner;
        private static Coroutine _coroutine;
        private static bool _initialMapLoadWaveOpen;
        private static float _waveStart;

        // Wait at most this long for the gameplay camera before sorting/activating,
        // so nearest-first ordering has a real anchor and the game's distance bands
        // resolve against a live camera rather than the null-camera "nearest" band.
        private const float CameraWaitSeconds = 2f;

        // Activation-wave tuning: the enhanced loading screen remains visible while
        // this initial wave drains, so use a deliberately aggressive budget to make
        // the world complete before control is released. The scenery model loader's
        // independent concurrency gate still limits AssetBundle requests to four,
        // preventing this activation queue from recreating the VRAM load race.
        // A slow wave must never bulk-flush: doing so defeats both load throttling and
        // the null-camera safety this class exists to provide. Measured
        // per-item work is sub-millisecond; 24ms/128 made the 2,584-item E&A wave
        // item-cap-bound at 20-23 frames and 5.5-6.6 seconds.
        private const float FrameBudgetMilliseconds = 50f;
        private const float SlowWaveWarningSeconds = 30f;
        private const int MaxItemsPerFrame = 512;

        // Items beyond this distance from the activation anchor cannot trigger model
        // streaming (the game's streaming band is 1,500m; 2x leaves margin for the
        // player moving while the wave drains), so SetActive on them is pure
        // bookkeeping. They drain at a higher item cap to kill the wave's long tail —
        // on the reference load ~1,550 of 2,111 items sat beyond the band yet were
        // throttled to the near-item trickle. Only used when a live camera anchored
        // the distance sort: with no camera the game treats distance as "nearest", so
        // bulk activation would force-stream everything (see class comment).
        private const float FarActivationThresholdMeters = 3000f;
        private const int FarMaxItemsPerFrame = 2048;

        // Re-sort trigger: if the camera anchor moves this far from the position the
        // unprocessed queue was last sorted against (teleport, fast travel), the
        // nearest-first ordering is stale and near-player items would otherwise
        // activate up to several seconds late. The frame gap caps how often the
        // O(n log n) re-sort can run if a camera oscillates across the threshold.
        private const float AnchorResortDistanceMeters = 500f;
        private const int MinFramesBetweenResorts = 10;

        internal static bool IsDraining => _coroutine != null;

        internal static int PendingCount => Queue.Count;

        /// <summary>
        /// Opens the initial map-load deferral window. Only scenery created while the
        /// window is open is eligible to defer, so live reapply / editor reloads
        /// (which do not run the same terrain-bake sequencing) always stay
        /// synchronous. Clears any leftover state from a prior load.
        /// </summary>
        internal static void OpenInitialMapLoadWave()
        {
            CancelAndClear("new map-load wave");
            _initialMapLoadWaveOpen = true;
        }

        /// <summary>
        /// Decides whether <paramref name="scenery"/> should be deferred. Returns true
        /// if it was queued (the caller must leave it inactive); false if the caller
        /// should activate it inline now (today's behavior).
        /// </summary>
        internal static bool TryDefer(SceneryAssetInstance scenery, string id, string assetIdentifier, Action onActivated)
        {
            if (!_initialMapLoadWaveOpen || scenery == null)
            {
                return false;
            }

            if (!FuseSceneryDeferralClassifier.CanDefer(assetIdentifier))
            {
                return false;
            }

            Queue.Add(new DeferredScenery
            {
                Instance = scenery,
                Id = id ?? string.Empty,
                OnActivated = onActivated
            });
            return true;
        }

        /// <summary>
        /// Closes the deferral window and starts the post-load activation wave. Safe to
        /// call with nothing queued. Must be called AFTER the terrain mask rebuild so
        /// eager (mask-bearing) scenery is already baked.
        /// </summary>
        internal static void BeginDrain(string reason)
        {
            _initialMapLoadWaveOpen = false;

            if (Queue.Count == 0)
            {
                return;
            }

            try
            {
                EnsureRunner();
                if (_runner == null)
                {
                    FlushSynchronously("no activator host");
                    return;
                }

                if (_coroutine != null)
                {
                    _runner.StopCoroutine(_coroutine);
                    _coroutine = null;
                }

                _waveStart = Time.realtimeSinceStartup;
                _coroutine = _runner.StartCoroutine(DrainRoutine(reason ?? "map load"));
            }
            catch (Exception ex)
            {
                // Never leave scenery stranded inactive: if the wave can't start,
                // activate everything inline now.
                FuseLog.Exception("FUSE deferred scenery wave could not start; activating queued scenery inline.", ex);
                FlushSynchronously("drain start failed");
            }
        }

        /// <summary>
        /// Cancels an in-flight wave and drops the queue. Queued scenery stays inactive
        /// (map unload destroys it). Use on map unload, before scenery GameObjects are
        /// torn down.
        /// </summary>
        internal static void CancelAndClear(string reason)
        {
            _initialMapLoadWaveOpen = false;

            if (_coroutine != null && _runner != null)
            {
                _runner.StopCoroutine(_coroutine);
            }

            _coroutine = null;

            if (Queue.Count > 0)
            {
                FuseLog.Info($"FUSE deferred scenery wave cancelled with {Queue.Count} pending; reason='{reason ?? "unspecified"}'.");
                Queue.Clear();
            }
        }

        /// <summary>
        /// Activates everything still queued, right now, on the calling thread. Used as
        /// a hard safety valve (e.g. before a reapply needs all scenery live).
        /// </summary>
        internal static void FlushSynchronously(string reason)
        {
            if (_coroutine != null && _runner != null)
            {
                _runner.StopCoroutine(_coroutine);
            }

            _coroutine = null;

            if (Queue.Count == 0)
            {
                return;
            }

            var total = Queue.Count;
            var activated = 0;
            var failed = 0;
            var keptHidden = 0;
            for (var index = 0; index < Queue.Count; index++)
            {
                switch (ActivateOne(Queue[index]))
                {
                    case ActivationResult.Activated:
                        activated++;
                        break;
                    case ActivationResult.KeptHidden:
                        keptHidden++;
                        break;
                    default:
                        failed++;
                        break;
                }
            }

            Queue.Clear();
            FuseLog.Info($"FUSE deferred scenery flushed synchronously ({activated}/{total}, failed={failed}, keptHidden={keptHidden}); reason='{reason ?? "unspecified"}'.");
        }

        private static IEnumerator DrainRoutine(string reason)
        {
            // Give the gameplay camera a moment to come up so ordering and the game's
            // own band-based streaming resolve against the player's real position.
            var waitStart = Time.realtimeSinceStartup;
            while (Camera.main == null && Time.realtimeSinceStartup - waitStart < CameraWaitSeconds)
            {
                yield return null;
            }

            // The far-drain gate is keyed to Camera.main specifically — that is
            // the camera the game's streaming band resolves against (see
            // FuseSceneryCameraRef). Camera.current may be live while Camera.main
            // is still null (common in-editor), and arming the far drain on it
            // would bulk-activate into the null-camera "nearest band" — the
            // force-stream-everything case this gate exists to prevent. It
            // remains acceptable as a position-only fallback for sort ordering.
            var streamingCamera = Camera.main;
            var hadLiveAnchor = streamingCamera != null;
            var sortCamera = streamingCamera != null ? streamingCamera : Camera.current;
            var anchor = sortCamera != null ? sortCamera.transform.position : Vector3.zero;
            SortByDistance(anchor);

            // The far partition (no streaming side effects) only exists when the
            // streaming camera anchored the sort; with no camera every distance is
            // "nearest" to the game and bulk activation would force-stream the
            // entire queue.
            var farStartIndex = hadLiveAnchor ? FindFarPartitionStart(0) : Queue.Count;

            var budgetSeconds = Mathf.Max(0.001f, FrameBudgetMilliseconds / 1000f);

            var index = 0;
            var processed = 0;
            var activated = 0;
            var failed = 0;
            var keptHidden = 0;
            var frames = 0;
            var farDrained = 0;
            var resorts = 0;
            var framesSinceResort = int.MaxValue;
            var slowWarningLogged = false;

            while (index < Queue.Count)
            {
                frames++;
                if (framesSinceResort < int.MaxValue)
                {
                    framesSinceResort++;
                }

                // Re-resolve the streaming camera EVERY frame, not just at wave
                // start. Two transitions matter: (1) it can blank or be replaced
                // mid-wave (camera-mode change right after the loading screen) — a
                // frame without one pauses so no null-camera nearest-band loads
                // can begin; (2) it
                // can come up just AFTER the initial 2s wait expired, the
                // slow-startup path this wave most wants to help — arm the far
                // drain then by re-anchoring and re-sorting the unprocessed tail.
                var frameCamera = FuseSceneryCameraRef.Resolve();
                if (!hadLiveAnchor && frameCamera != null)
                {
                    hadLiveAnchor = true;
                    anchor = frameCamera.transform.position;
                    ResortUnprocessed(index, anchor);
                    farStartIndex = FindFarPartitionStart(index);
                    framesSinceResort = 0;
                }

                // Never activate scenery without the gameplay camera. The game's
                // culler treats a null camera as its nearest band, which turns an
                // otherwise harmless activation wave into "load every model".
                if (frameCamera == null)
                {
                    if (!slowWarningLogged &&
                        Time.realtimeSinceStartup - _waveStart >= SlowWaveWarningSeconds)
                    {
                        slowWarningLogged = true;
                        FuseLog.Warning(
                            "FUSE deferred scenery wave is paused because no active " +
                            "gameplay camera is available; queued scenery remains unloaded.");
                    }

                    yield return null;
                    continue;
                }

                var frameStart = Time.realtimeSinceStartup;
                var thisFrame = 0;
                var maxPerFrame = index >= farStartIndex && frameCamera != null
                    ? FarMaxItemsPerFrame
                    : Mathf.Max(1, MaxItemsPerFrame);

                // Always do at least one item per frame so a single heavy prefab can't
                // stall progress, then keep going until the time budget or item cap.
                while (index < Queue.Count &&
                       thisFrame < maxPerFrame &&
                       (thisFrame == 0 || Time.realtimeSinceStartup - frameStart < budgetSeconds))
                {
                    switch (ActivateOne(Queue[index]))
                    {
                        case ActivationResult.Activated:
                            activated++;
                            break;
                        case ActivationResult.KeptHidden:
                            keptHidden++;
                            break;
                        default:
                            failed++;
                            break;
                    }

                    if (index >= farStartIndex)
                    {
                        farDrained++;
                    }

                    index++;
                    thisFrame++;
                    processed++;
                }

                if (!slowWarningLogged &&
                    Time.realtimeSinceStartup - _waveStart >= SlowWaveWarningSeconds)
                {
                    slowWarningLogged = true;
                    FuseLog.Warning(
                        $"FUSE deferred scenery wave exceeded {SlowWaveWarningSeconds:0}s; " +
                        $"continuing the remaining {Queue.Count - index} item(s) under " +
                        "the normal per-frame budget.");
                }

                yield return null;

                // Teleport/fast-travel handling: if the camera moved far from the
                // position the remaining items were sorted against, re-sort ONLY the
                // unprocessed range so near-player scenery activates next instead of
                // up to several seconds late. Already-processed items are untouched.
                // Resolved fresh each frame (not the wave-start camera object) so a
                // destroyed-and-replaced camera keeps the re-sort armed. The frame
                // gap bounds sort cost if a camera oscillates across the threshold.
                if (hadLiveAnchor && index < Queue.Count && framesSinceResort >= MinFramesBetweenResorts)
                {
                    var current = FuseSceneryCameraRef.Resolve();
                    if (current != null)
                    {
                        var position = current.transform.position;
                        if ((position - anchor).sqrMagnitude >
                            AnchorResortDistanceMeters * AnchorResortDistanceMeters)
                        {
                            anchor = position;
                            ResortUnprocessed(index, anchor);
                            farStartIndex = FindFarPartitionStart(index);
                            resorts++;
                            framesSinceResort = 0;
                        }
                    }
                }
            }

            OnDrainComplete(reason, processed, activated, failed, keptHidden, frames, farDrained, resorts);
        }

        private static void OnDrainComplete(
            string reason,
            int processed,
            int activated,
            int failed,
            int keptHidden,
            int frames,
            int farDrained,
            int resorts)
        {
            _coroutine = null;
            Queue.Clear();

            var elapsedMs = (long)((Time.realtimeSinceStartup - _waveStart) * 1000f);
            FusePerformanceMetrics.RecordTiming("deferred scenery activation wave", elapsedMs);
            // NOTE: 'failed' counts every item ActivateOne could not activate, which
            // is usually BENIGN — most are scenery destroyed/removed by a later apply
            // step (world removals/suppressions) before the wave ran, not errors.
            // Genuine activation errors are logged separately via FuseLog.Exception in
            // ActivateOne. A small non-zero 'failed' is expected. (TODO: split into
            // 'skipped' vs 'errored' if this metric ever causes confusion.)
            // 'keptHidden' counts props a locked progression feature is intentionally
            // holding inactive (gameObjectsEnableOnUnlock); leaving them hidden is the
            // point of the lock check and is expected for any not-yet-unlocked area.
            // 'cullingDiagnostics' is logged because the per-item cost (and therefore
            // the wave duration) measures higher with the diagnostic logging enabled —
            // benchmark wave changes against a diagnostics-off log.
            FuseLog.Info(
                $"FUSE load timing phase='deferred scenery activation wave' elapsedMs={elapsedMs} " +
                $"reason='{reason ?? "map load"}' activated={activated} failed={failed} keptHidden={keptHidden} processed={processed} " +
                $"frames={frames} farDrained={farDrained} resorts={resorts} " +
                $"cullingDiagnostics={FuseSettings.EnableSceneryCullingDiagnostics}.");
        }

        /// <summary>
        /// First index in the (sorted, ascending) queue at or after
        /// <paramref name="fromIndex"/> where every later item is beyond the
        /// far-activation threshold. Returns Queue.Count when no far partition exists.
        /// </summary>
        private static int FindFarPartitionStart(int fromIndex)
        {
            var thresholdSqr = FarActivationThresholdMeters * FarActivationThresholdMeters;
            var start = Queue.Count;
            while (start > fromIndex && Queue[start - 1].SortKey > thresholdSqr)
            {
                start--;
            }

            return start;
        }

        private static void ResortUnprocessed(int fromIndex, Vector3 anchor)
        {
            var count = Queue.Count - fromIndex;
            if (count <= 1)
            {
                return;
            }

            for (var i = fromIndex; i < Queue.Count; i++)
            {
                var instance = Queue[i].Instance;
                Queue[i].SortKey = instance != null
                    ? (instance.transform.position - anchor).sqrMagnitude
                    : float.MaxValue;
            }

            Queue.Sort(fromIndex, count, DeferredScenerySortKeyComparer.Instance);
        }

        private static ActivationResult ActivateOne(DeferredScenery item)
        {
            if (item == null)
            {
                return ActivationResult.Failed;
            }

            var instance = item.Instance;
            // Unity's overloaded equality reports a destroyed object as null. This is
            // the common "failed" case and is benign: the scenery was removed/replaced
            // by a later apply step before the wave reached it (see the 'failed' note
            // in OnDrainComplete) — not an error.
            if (instance == null)
            {
                return ActivationResult.Failed;
            }

            var gameObject = instance.gameObject;
            if (gameObject == null)
            {
                return ActivationResult.Failed;
            }

            // A locked progression feature may have hidden this prop during apply via
            // gameObjectsEnableOnUnlock (the game SetActive(false)'d it and only re-shows
            // it on the unlock transition). Re-activating it here would strand a
            // not-yet-unlocked prop visible until the next feature change, so leave it
            // inactive; the game shows it when the owning feature unlocks.
            //
            // Guard the query separately: if progression state is momentarily
            // inconsistent and the check throws, fall back to activating (the pre-fix
            // behavior) rather than letting the exception escape ActivateOne and kill the
            // drain coroutine mid-wave — the class contract is that no deferral problem
            // leaves scenery stranded or wedges the load.
            try
            {
                if (ProgressionAPI.IsGameObjectHiddenByLockedFeature(gameObject))
                {
                    return ActivationResult.KeptHidden;
                }
            }
            catch (Exception ex)
            {
                FuseLog.Exception(
                    $"FUSE deferred scenery progression-gate check failed for '{item.Id}'; activating normally.",
                    ex);
            }

            try
            {
                if (!gameObject.activeSelf)
                {
                    gameObject.SetActive(true);
                }

                item.OnActivated?.Invoke();
                return ActivationResult.Activated;
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE deferred scenery activation failed for '{item.Id}'", ex);
                return ActivationResult.Failed;
            }
        }

        /// <summary>
        /// Outcome of a single deferred-activation attempt. <see cref="Failed"/> is the
        /// benign destroyed/removed case (and genuine errors, which are logged
        /// separately); <see cref="KeptHidden"/> means a locked progression feature is
        /// intentionally holding the prop inactive and it must not be shown.
        /// </summary>
        private enum ActivationResult
        {
            Activated,
            Failed,
            KeptHidden
        }

        private static void SortByDistance(Vector3 anchor)
        {
            foreach (var item in Queue)
            {
                var instance = item.Instance;
                item.SortKey = instance != null
                    ? (instance.transform.position - anchor).sqrMagnitude
                    : float.MaxValue;
            }

            Queue.Sort((left, right) => left.SortKey.CompareTo(right.SortKey));
        }

        private static void EnsureRunner()
        {
            if (_runner != null)
            {
                return;
            }

            var go = new GameObject("FUSE.DeferredSceneryActivator");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _runner = go.AddComponent<FuseDeferredSceneryRunner>();
        }

        private sealed class DeferredScenery
        {
            public SceneryAssetInstance Instance;
            public string Id;
            public Action OnActivated;
            public float SortKey;
        }

        private sealed class DeferredScenerySortKeyComparer : IComparer<DeferredScenery>
        {
            public static readonly DeferredScenerySortKeyComparer Instance = new DeferredScenerySortKeyComparer();

            public int Compare(DeferredScenery left, DeferredScenery right)
            {
                return left.SortKey.CompareTo(right.SortKey);
            }
        }

        private sealed class FuseDeferredSceneryRunner : MonoBehaviour
        {
        }
    }
}
