using System;

namespace FUSE.Interface
{
    // Pure, Unity-free show/hide + current-step state for the enhanced loading
    // screen (issue #83). Deliberately separated from the FuseLoadingScreen
    // MonoBehaviour so the two-flag hide gate and the watchdog backstop can be
    // unit-tested without the engine: nothing here touches UnityEngine, and every
    // method takes a monotonic "now" in seconds (the host passes
    // Time.realtimeSinceStartup). The host calls UpdateVisibility() each frame and
    // tears down when it returns false.
    //
    // Hide gate (the reason this is two flags, not one): FUSE's post-load pipeline
    // (FuseLifecycle.OnMapDidLoad) can finish AFTER the game hides its own loading
    // screen, so hiding the moment ShowLoadingScreen(false) fires would expose a
    // still-assembling world. We hide only when BOTH the game screen has hidden and
    // the FUSE pipeline has signalled complete — with a watchdog so a missed signal
    // can never trap the player behind the screen.
    internal sealed class FuseLoadingScreenState
    {
        // Absolute backstop: if no signal (progress, step, or completion) arrives
        // for this long, tear the screen down regardless of the flags. Sized well
        // above the heaviest single synchronous load phase so a legitimately long
        // freeze never trips it; only a genuinely missed terminal signal does.
        internal const float DefaultWatchdogSeconds = 120f;

        private readonly float _watchdogSeconds;

        private bool _active;
        private bool _gameScreenHidden;
        private bool _fusePipelineDone;
        private bool _aborted;
        private float _progress;
        private string _stepTitle;
        private string _stepDetail;
        private bool _inSyncPhase;
        private float _lastSignalAt;

        internal FuseLoadingScreenState(float watchdogSeconds = DefaultWatchdogSeconds)
        {
            _watchdogSeconds = watchdogSeconds > 0f ? watchdogSeconds : DefaultWatchdogSeconds;
        }

        internal bool Active => _active;

        internal bool GameScreenHidden => _gameScreenHidden;

        internal bool FusePipelineDone => _fusePipelineDone;

        internal float Progress => _progress;

        internal string StepTitle => _stepTitle;

        internal string StepDetail => _stepDetail;

        // True once a synchronous (main-thread-blocking) phase has been entered.
        // The host switches the progress bar from a determinate fill to a static
        // "deep in the load" treatment so a frozen frame never reads as a stalled
        // partial bar.
        internal bool InSyncPhase => _inSyncPhase;

        internal void BeginLoad(float now)
        {
            _active = true;
            _gameScreenHidden = false;
            _fusePipelineDone = false;
            _aborted = false;
            _progress = 0f;
            _stepTitle = "Loading world";
            _stepDetail = null;
            _inSyncPhase = false;
            _lastSignalAt = now;
        }

        internal void SetProgress(float fraction, float now)
        {
            if (!_active)
            {
                return;
            }

            _progress = Clamp01(fraction);
            _lastSignalAt = now;
        }

        internal void SetStep(string title, string detail, bool syncPhase, float now)
        {
            if (!_active)
            {
                return;
            }

            if (!string.IsNullOrEmpty(title))
            {
                _stepTitle = title;
            }

            _stepDetail = detail;
            if (syncPhase)
            {
                _inSyncPhase = true;
            }

            _lastSignalAt = now;
        }

        internal void NotifyGameScreenHidden(float now)
        {
            if (!_active)
            {
                return;
            }

            _gameScreenHidden = true;
            _lastSignalAt = now;
        }

        internal void NotifyFusePipelineComplete(float now)
        {
            if (!_active)
            {
                return;
            }

            _fusePipelineDone = true;
            _lastSignalAt = now;
        }

        // Immediate hide, used for load failure / return-to-menu. Distinct from the
        // gated hide so an aborted load never waits on the FUSE-pipeline flag.
        internal void Abort()
        {
            _aborted = true;
            _active = false;
        }

        // Returns whether the screen should remain visible. Flips the screen off
        // exactly once when the hide gate is satisfied so the host can run teardown
        // on the transition.
        internal bool UpdateVisibility(float now)
        {
            if (!_active)
            {
                return false;
            }

            if (_aborted)
            {
                _active = false;
                return false;
            }

            if (now - _lastSignalAt >= _watchdogSeconds)
            {
                _active = false;
                return false;
            }

            if (_gameScreenHidden && _fusePipelineDone)
            {
                _active = false;
                return false;
            }

            return true;
        }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || value < 0f)
            {
                return 0f;
            }

            return value > 1f ? 1f : value;
        }
    }
}
