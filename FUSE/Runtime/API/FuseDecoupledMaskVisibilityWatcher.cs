using System;
using System.Collections;
using System.Collections.Generic;
using FUSE.Infrastructure;
using UnityEngine;

namespace FUSE.Runtime.API
{
    /// <summary>
    /// Keeps a mask-bearing scenery's decoupled terrain mask in step with whether the building is
    /// actually drawing. <see cref="MapAPI.DecoupleAttachedMapMasks"/> re-homes the mask onto a
    /// standalone, always-active object so it survives the model streaming out/in and teleports —
    /// but "always active" wrongly keeps the ground flattened when a pack HIDES the building at the
    /// renderer level (renderers disabled while the scenery GameObject stays active), leaving a flat
    /// patch with no building on it (e.g. Whittier).
    ///
    /// Attached to each mask-bearing scenery root, this watcher polls visibility on a low cadence
    /// and toggles the standalone mask via <see cref="MapAPI.SetDecoupledMasksActive"/>: dropped
    /// while hidden, restored when shown. Polling — rather than a hide/show event — is required
    /// because the hide is pack-authored, so FUSE gets no callback when it flips.
    /// <see cref="MapAPI.ClassifyMaskVisibility"/> keeps the mask while the model is merely streamed
    /// out (no renderers) or culled (forceRenderingOff), dropping it ONLY for an intentional
    /// renderer-level hide, so this never undoes the point of decoupling.
    ///
    /// Lifecycle: created/refreshed by <see cref="SceneryAPI"/> right after each decouple (on model
    /// load and on update). It dies with the scenery root — RemoveScenery destroys the root, which
    /// also drops the standalone via <see cref="MapAPI.RemoveDecoupledMasksFor"/>. Cost is bounded:
    /// only mask-bearing scenery carries it, that set is small and held resident, and the poll uses
    /// reused buffers so it allocates nothing after warmup.
    /// </summary>
    internal sealed class FuseDecoupledMaskVisibilityWatcher : MonoBehaviour
    {
        // The hide is a rare, coarse state change, not a per-frame value: a half-second cadence is
        // imperceptible for a terrain flatten appearing/disappearing yet negligible to poll.
        private const float CheckIntervalSeconds = 0.5f;

        // Shared across instances: WaitForSeconds is an immutable duration, so reusing one instance
        // is safe and avoids a per-iteration allocation on every watcher's loop.
        private static readonly WaitForSeconds CheckInterval = new WaitForSeconds(CheckIntervalSeconds);

        // Reused per-instance scratch buffers so the 2 Hz poll allocates nothing after warmup.
        private readonly List<Renderer> _renderers = new List<Renderer>();
        private readonly List<MapAPI.SceneryRendererVisibility> _samples = new List<MapAPI.SceneryRendererVisibility>();

        private string _sceneryId;

        // Last decisive (non-indeterminate) visibility. While the model is streamed out we cannot
        // measure, so we hold this instead of flipping: a building hidden THEN streamed out keeps
        // its mask dropped, and a visible one keeps it applied. Defaults to Visible so the mask
        // stays applied until the building is proven hidden (matches the decouple default).
        private MapAPI.DecoupledMaskVisibility _lastDecisive = MapAPI.DecoupledMaskVisibility.Visible;

        private Coroutine _loop;

        /// <summary>
        /// Ensures <paramref name="sceneryRoot"/> carries a watcher for <paramref name="sceneryId"/>
        /// and runs one immediate sync. Idempotent: called after every decouple (initial load and
        /// update), it adds the watcher once and just re-syncs thereafter.
        /// </summary>
        internal static FuseDecoupledMaskVisibilityWatcher Ensure(GameObject sceneryRoot, string sceneryId)
        {
            if (sceneryRoot == null || string.IsNullOrEmpty(sceneryId))
            {
                return null;
            }

            var watcher = sceneryRoot.GetComponent<FuseDecoupledMaskVisibilityWatcher>();
            if (watcher == null)
            {
                watcher = sceneryRoot.AddComponent<FuseDecoupledMaskVisibilityWatcher>();
            }

            watcher._sceneryId = sceneryId;
            // Sync immediately so a building that streamed in already-hidden never shows a flatten
            // waiting for the first poll, and a re-decouple (update) applies the current state now.
            watcher.SyncNow();
            return watcher;
        }

        /// <summary>Runs one visibility sync immediately, outside the poll cadence.</summary>
        internal void SyncNow()
        {
            try
            {
                Tick();
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE decoupled-mask visibility sync failed for scenery '{_sceneryId}'", ex);
            }
        }

        private void OnEnable()
        {
            _loop = StartCoroutine(PollLoop());
        }

        private void OnDisable()
        {
            if (_loop != null)
            {
                StopCoroutine(_loop);
                _loop = null;
            }
        }

        private IEnumerator PollLoop()
        {
            while (true)
            {
                // SyncNow swallows exceptions so a transient failure can't kill the loop.
                SyncNow();
                yield return CheckInterval;
            }
        }

        private void Tick()
        {
            if (string.IsNullOrEmpty(_sceneryId))
            {
                return;
            }

            var visibility = CaptureVisibility();
            if (visibility == MapAPI.DecoupledMaskVisibility.Indeterminate)
            {
                // Streamed out / not loaded: hold the last decisive state (keep the mask).
                visibility = _lastDecisive;
            }
            else
            {
                _lastDecisive = visibility;
            }

            MapAPI.SetDecoupledMasksActive(_sceneryId, visibility == MapAPI.DecoupledMaskVisibility.Visible);
        }

        private MapAPI.DecoupledMaskVisibility CaptureVisibility()
        {
            // Root inactive => not a renderer-level hide (the bug we target); it is likely a
            // deferred-not-yet-activated window. Treat as indeterminate so we never drop the mask
            // here. (A whole-root deactivate is ruled out as the hide mechanism: it would revert the
            // welded mask on its own, so the ground would not have stayed flat in the first place.)
            if (gameObject == null || !gameObject.activeInHierarchy)
            {
                return MapAPI.DecoupledMaskVisibility.Indeterminate;
            }

            _renderers.Clear();
            GetComponentsInChildren(true, _renderers);

            _samples.Clear();
            for (var index = 0; index < _renderers.Count; index++)
            {
                var renderer = _renderers[index];
                if (renderer == null)
                {
                    continue;
                }

                _samples.Add(new MapAPI.SceneryRendererVisibility(
                    renderer.enabled,
                    renderer.gameObject.activeInHierarchy,
                    renderer.forceRenderingOff));
            }

            return MapAPI.ClassifyMaskVisibility(_samples);
        }
    }
}
