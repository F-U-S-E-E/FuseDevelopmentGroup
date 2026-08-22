using System.Diagnostics;

namespace FUSE.Runtime.Lifecycle
{
    /// <summary>
    /// Keeps a two-frame ring of the slowest measured FUSE runtime-pump phase.
    /// Time.unscaledDeltaTime observed by the spike detector describes the
    /// preceding frame, so retaining both frames makes the attribution stable
    /// regardless of Unity component Update ordering.
    /// </summary>
    internal static class FuseMainThreadWorkTracker
    {
        private static int _currentFrame = -1;
        private static string _currentPhase = string.Empty;
        private static double _currentMilliseconds;
        private static int _previousFrame = -1;
        private static string _previousPhase = string.Empty;
        private static double _previousMilliseconds;

        internal static long Start() => Stopwatch.GetTimestamp();

        internal static void Record(int frame, string phase, long started)
        {
            var elapsed = (Stopwatch.GetTimestamp() - started) * 1000d
                / Stopwatch.Frequency;
            RecordElapsed(frame, phase, elapsed);
        }

        internal static void RecordElapsed(
            int frame,
            string phase,
            double elapsedMilliseconds)
        {
            if (_currentFrame != frame)
            {
                _previousFrame = _currentFrame;
                _previousPhase = _currentPhase;
                _previousMilliseconds = _currentMilliseconds;
                _currentFrame = frame;
                _currentPhase = string.Empty;
                _currentMilliseconds = 0d;
            }
            if (elapsedMilliseconds <= _currentMilliseconds)
                return;
            _currentPhase = phase ?? string.Empty;
            _currentMilliseconds = elapsedMilliseconds;
        }

        internal static bool TryGet(
            int frame,
            out string phase,
            out double elapsedMilliseconds)
        {
            if (_currentFrame == frame)
            {
                phase = _currentPhase;
                elapsedMilliseconds = _currentMilliseconds;
                return !string.IsNullOrEmpty(phase);
            }
            if (_previousFrame == frame)
            {
                phase = _previousPhase;
                elapsedMilliseconds = _previousMilliseconds;
                return !string.IsNullOrEmpty(phase);
            }
            phase = string.Empty;
            elapsedMilliseconds = 0d;
            return false;
        }

        internal static void Reset()
        {
            _currentFrame = -1;
            _currentPhase = string.Empty;
            _currentMilliseconds = 0d;
            _previousFrame = -1;
            _previousPhase = string.Empty;
            _previousMilliseconds = 0d;
        }
    }
}
