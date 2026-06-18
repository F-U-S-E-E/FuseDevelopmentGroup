namespace FUSE.Patches
{
    /// <summary>
    /// Unity-free, single-thread per-frame "load starts" budget used by
    /// <see cref="FuseSceneryLoadThrottlePatch"/> to bound how many FUSE scenery
    /// models may begin loading in a single frame.
    ///
    /// The counter resets the first time <see cref="BeginFrame"/> is called with a
    /// new frame index, so any number of callers within the same frame (the cull
    /// callback and the throttle pump) share one budget. Pulled out of the patch so
    /// the frame-reset / ceiling logic can be asserted in plain unit tests without a
    /// live game, Harmony, or a Unity frame loop (see FUSE.Tests).
    /// </summary>
    internal sealed class FuseSceneryLoadBudget
    {
        private readonly int _maxPerFrame;
        private int _frame;
        private bool _hasFrame;
        private int _startedThisFrame;

        internal FuseSceneryLoadBudget(int maxPerFrame)
        {
            // A ceiling below 1 would stall loading entirely; clamp defensively.
            _maxPerFrame = maxPerFrame < 1 ? 1 : maxPerFrame;
        }

        internal int MaxPerFrame => _maxPerFrame;

        internal int StartedThisFrame => _startedThisFrame;

        /// <summary>Slots still available this frame (never negative).</summary>
        internal int Remaining => _startedThisFrame >= _maxPerFrame ? 0 : _maxPerFrame - _startedThisFrame;

        /// <summary>
        /// Advance the budget to <paramref name="frame"/>. The starts counter resets
        /// on the first call of each new frame and is a no-op on repeat calls within
        /// the same frame, so the prefix and the pump can both call it freely.
        /// </summary>
        internal void BeginFrame(int frame)
        {
            if (!_hasFrame || frame != _frame)
            {
                _frame = frame;
                _hasFrame = true;
                _startedThisFrame = 0;
            }
        }

        /// <summary>
        /// Consume one slot if the per-frame ceiling has not been reached. Returns
        /// true (and increments the counter) when a load may start now, false when it
        /// must be deferred to a later frame.
        /// </summary>
        internal bool TryConsume()
        {
            if (_startedThisFrame >= _maxPerFrame)
            {
                return false;
            }

            _startedThisFrame++;
            return true;
        }

        /// <summary>Forget all frame state (used when tearing the throttle down).</summary>
        internal void Reset()
        {
            _hasFrame = false;
            _frame = 0;
            _startedThisFrame = 0;
        }
    }
}
