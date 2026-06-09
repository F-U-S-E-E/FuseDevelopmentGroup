using System;
using System.Collections;
using System.Collections.Generic;
using FUSE.Infrastructure;
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
    /// live camera — camera-less activation left masks stuck — and is then held
    /// resident by FuseSceneryCullingDebouncePatch so it never unloads. Every failure
    /// path falls back to inline (synchronous) activation, so a deferral problem can
    /// never leave scenery invisible or wedge a load. Only the initial map-load wave
    /// defers; reapply/live-reload stay synchronous.
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

        // Activation-wave tuning: per-frame time budget, the hard wave timeout after
        // which any remaining scenery is activated inline, and a per-frame item cap so
        // a burst of sub-millisecond activations can't spike GC in one frame.
        private const float FrameBudgetMilliseconds = 6f;
        private const float WaveTimeoutSeconds = 30f;
        private const int MaxItemsPerFrame = 64;

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

            SortByDistance(ResolveAnchor());

            var budgetSeconds = Mathf.Max(0.001f, FrameBudgetMilliseconds / 1000f);
            var maxPerFrame = Mathf.Max(1, MaxItemsPerFrame);

            var index = 0;
            var processed = 0;
            var activated = 0;
            var failed = 0;
            var keptHidden = 0;

            while (index < Queue.Count)
            {
                var frameStart = Time.realtimeSinceStartup;
                var thisFrame = 0;

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

                    index++;
                    thisFrame++;
                    processed++;
                }

                if (Time.realtimeSinceStartup - _waveStart >= WaveTimeoutSeconds)
                {
                    FuseLog.Warning(
                        $"FUSE deferred scenery wave exceeded {WaveTimeoutSeconds:0}s; " +
                        $"activating the remaining {Queue.Count - index} item(s) inline.");
                    while (index < Queue.Count)
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

                        index++;
                        processed++;
                    }

                    break;
                }

                yield return null;
            }

            OnDrainComplete(reason, processed, activated, failed, keptHidden);
        }

        private static void OnDrainComplete(string reason, int processed, int activated, int failed, int keptHidden)
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
            FuseLog.Info(
                $"FUSE load timing phase='deferred scenery activation wave' elapsedMs={elapsedMs} " +
                $"reason='{reason ?? "map load"}' activated={activated} failed={failed} keptHidden={keptHidden} processed={processed}.");
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

        private static Vector3 ResolveAnchor()
        {
            // Camera.main is usually live by the time the wait above completes; fall
            // back to the active camera, then the world origin.
            var camera = Camera.main ?? Camera.current;
            return camera != null ? camera.transform.position : Vector3.zero;
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

        private sealed class FuseDeferredSceneryRunner : MonoBehaviour
        {
        }
    }
}
