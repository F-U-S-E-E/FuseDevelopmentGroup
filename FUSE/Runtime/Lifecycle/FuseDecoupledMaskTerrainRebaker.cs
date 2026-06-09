using System;
using FUSE.Infrastructure;
using UnityEngine;

namespace FUSE.Runtime.Lifecycle
{
    /// <summary>
    /// Coalesces a terrain re-bake after FUSE decouples map masks.
    ///
    /// Map masks (terrain flatten / tree-cut / object-cut) are continuous MapManager
    /// modifiers: a tile bakes exactly the modifiers registered when it builds, and only
    /// re-bakes when invalidated. FUSE's single map-load terrain rebuild fires at the end of
    /// the apply, BEFORE building models stream in — so the masks FUSE decouples in
    /// <c>SceneryAssetInstance.OnDidLoadModels</c> register their modifiers AFTER that rebuild.
    /// The game's own per-modifier invalidate is a 0.5s debounce that is restarted by every
    /// AddModifier in the streaming burst and is gathered only once per load pass, so a tile
    /// that already built (e.g. the spawn/roundhouse tile) does not re-evaluate the new masks
    /// until the whole spawn tile-load backlog drains (~30s observed) — i.e. the flatten/cut
    /// never visibly applies.
    ///
    /// This host waits for the decouple burst (and any world-origin rebase re-bake) to settle,
    /// then issues ONE targeted invalidate over the union of the touched tiles. Targeted
    /// (terrain-only) so it never re-streams scenery models — which would re-enter the decouple
    /// path and loop. Union-coalesced so the burst collapses to a single re-bake.
    /// </summary>
    internal sealed class FuseDecoupledMaskTerrainRebaker : MonoBehaviour
    {
        // Long enough that the streaming burst (async asset loads, a few per second) has
        // quiesced before we fire, short enough to be unnoticeable. Each request pushes it out.
        private const float SettleSeconds = 1.5f;

        // A rebake can fail transiently (e.g. MapManager briefly unavailable). Retry on the
        // settle cadence rather than silently dropping the burst's only rebake, but give up
        // after this many re-attempts so a permanent refusal (multiplayer guard, missing
        // reflection surface) cannot retry forever — the game's own debounced per-modifier
        // invalidate remains as the slow-path fallback.
        private const int MaxRetries = 4;

        private static FuseDecoupledMaskTerrainRebaker _instance;

        private bool _pending;
        private int _retries;
        private float _fireAtUnscaledTime;
        private bool _haveBounds;
        // GAME space (offset-independent), never world: requests can arrive on both sides of a
        // floating-origin rebase, and a world-space union across that boundary mixes offset
        // states — inflating the union by a whole origin block and re-baking kilometres of
        // terrain that were never touched.
        private Bounds _gameBounds;

        private static FuseDecoupledMaskTerrainRebaker Instance
        {
            get
            {
                if (_instance != null)
                {
                    return _instance;
                }

                try
                {
                    var host = new GameObject("FUSE Decoupled Mask Terrain Rebaker")
                    {
                        hideFlags = HideFlags.HideAndDontSave,
                    };
                    UnityEngine.Object.DontDestroyOnLoad(host);
                    _instance = host.AddComponent<FuseDecoupledMaskTerrainRebaker>();
                }
                catch (Exception ex)
                {
                    FuseLog.Exception("FUSE could not create the decoupled-mask terrain rebaker host", ex);
                }

                return _instance;
            }
        }

        /// <summary>
        /// Requests a coalesced terrain re-bake over <paramref name="gameBounds"/> (GAME-space,
        /// the offset-independent space MapManager stores modifiers and tiles in) once the
        /// decouple burst settles. Safe to call many times per burst; the bounds union and the
        /// settle timer absorb it into a single re-bake. Must be called on the main thread.
        /// </summary>
        internal static void Request(Bounds gameBounds)
        {
            var self = Instance;
            if (self == null)
            {
                return;
            }

            self._pending = true;
            self._retries = 0;
            self._fireAtUnscaledTime = Time.unscaledTime + SettleSeconds;
            if (self._haveBounds)
            {
                self._gameBounds.Encapsulate(gameBounds);
            }
            else
            {
                self._gameBounds = gameBounds;
                self._haveBounds = true;
            }
        }

        /// <summary>
        /// Drops any settling request. Called on map unload: the host is DontDestroyOnLoad, so
        /// without this a request raised at the end of one map would fire into the teardown (or
        /// the next map's half-initialized MapManager) with bounds from the wrong map.
        /// </summary>
        internal static void Clear(string reason)
        {
            var self = _instance;
            if (self == null || !self._pending)
            {
                return;
            }

            self._pending = false;
            self._retries = 0;
            self._haveBounds = false;
            self._gameBounds = default(Bounds);
            FuseLog.Info($"FUSE decoupled-mask terrain rebake request dropped ({reason}).");
        }

        private void Update()
        {
            if (!_pending || Time.unscaledTime < _fireAtUnscaledTime)
            {
                return;
            }

            _pending = false;
            var bounds = _gameBounds;
            var hadBounds = _haveBounds;

            var succeeded = false;
            try
            {
                succeeded = FuseRuntimeReloadService.RebakeDecoupledMaskTerrain(hadBounds ? (Bounds?)bounds : null);
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE decoupled-mask terrain rebake failed", ex);
            }

            if (!succeeded && _retries < MaxRetries)
            {
                // Keep the accumulated bounds and try again after another settle period; a
                // Request arriving meanwhile folds in normally (and resets the retry count).
                _retries++;
                _pending = true;
                _fireAtUnscaledTime = Time.unscaledTime + SettleSeconds;
                return;
            }

            if (!succeeded)
            {
                FuseLog.Warning(
                    $"FUSE decoupled-mask terrain rebake gave up after {MaxRetries + 1} attempts; " +
                    "the game's own debounced modifier invalidate will cover the tiles eventually.");
            }

            _retries = 0;
            _haveBounds = false;
            _gameBounds = default(Bounds);
        }
    }
}
