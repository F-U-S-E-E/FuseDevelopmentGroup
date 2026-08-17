using System;

namespace FUSE.Runtime.Lifecycle
{
    /// <summary>
    /// Allocation-free-after-construction rolling-median hitch detector.
    ///
    /// A fixed threshold mistakes an ordinary frame for a hitch once sustained
    /// frame rate drops below that threshold. This detector instead compares a
    /// frame with the larger of the user-configured absolute floor and a
    /// multiple of the recent median. The median is intentionally robust to an
    /// isolated long frame, while the bounded window still adapts when the
    /// game's steady-state frame rate genuinely changes.
    /// </summary>
    internal sealed class AdaptiveFrameHitchDetector
    {
        internal const int DefaultWindowSize = 121;
        internal const float DefaultBaselineMultiplier = 1.5f;

        private readonly float[] _samples;
        private readonly float[] _selectionScratch;
        private readonly float _baselineMultiplier;

        private int _count;
        private int _nextIndex;
        private float _baselineMs;

        internal AdaptiveFrameHitchDetector(
            int windowSize = DefaultWindowSize,
            float baselineMultiplier = DefaultBaselineMultiplier)
        {
            if (windowSize < 3)
            {
                throw new ArgumentOutOfRangeException(nameof(windowSize), "Window size must be at least three frames.");
            }

            if (float.IsNaN(baselineMultiplier) || float.IsInfinity(baselineMultiplier) ||
                baselineMultiplier <= 1f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(baselineMultiplier),
                    "The baseline multiplier must be finite and greater than one.");
            }

            _samples = new float[windowSize];
            _selectionScratch = new float[windowSize];
            _baselineMultiplier = baselineMultiplier;
        }

        internal int SampleCount => _count;

        internal float BaselineMs => _baselineMs;

        /// <summary>
        /// Evaluates <paramref name="frameMs"/> against the baseline that
        /// existed before this frame, then adds it to the bounded rolling
        /// window. The first valid sample seeds the baseline and is never
        /// classified as a hitch.
        /// </summary>
        internal FrameHitchObservation Observe(float frameMs, float absoluteFloorMs)
        {
            var floorMs = IsFinitePositive(absoluteFloorMs) ? absoluteFloorMs : 0f;
            if (!IsFinitePositive(frameMs))
            {
                var invalidThreshold = Math.Max(floorMs, _baselineMs * _baselineMultiplier);
                return new FrameHitchObservation(false, _baselineMs, invalidThreshold);
            }

            if (_count == 0)
            {
                AddSample(frameMs);
                return new FrameHitchObservation(
                    false,
                    _baselineMs,
                    Math.Max(floorMs, _baselineMs * _baselineMultiplier));
            }

            var baselineMs = _baselineMs;
            var effectiveThresholdMs = Math.Max(floorMs, baselineMs * _baselineMultiplier);
            var isHitch = frameMs >= effectiveThresholdMs;

            AddSample(frameMs);
            return new FrameHitchObservation(isHitch, baselineMs, effectiveThresholdMs);
        }

        internal void Reset()
        {
            // The arrays are deliberately retained and overwritten. Reset is
            // called around loading screens/settings changes and must not add
            // GC pressure to the first gameplay frames afterward.
            _count = 0;
            _nextIndex = 0;
            _baselineMs = 0f;
        }

        private void AddSample(float frameMs)
        {
            _samples[_nextIndex] = frameMs;
            _nextIndex++;
            if (_nextIndex == _samples.Length)
            {
                _nextIndex = 0;
            }

            if (_count < _samples.Length)
            {
                _count++;
            }

            Array.Copy(_samples, _selectionScratch, _count);
            _baselineMs = Select(
                _selectionScratch,
                0,
                _count - 1,
                (_count - 1) / 2);
        }

        private static float Select(float[] values, int left, int right, int target)
        {
            // In-place quickselect over the preallocated scratch buffer. Using
            // the lower middle for an even warm-up count keeps one isolated
            // slow frame from pulling the baseline upward.
            while (left < right)
            {
                var pivot = values[left + ((right - left) / 2)];
                var low = left;
                var high = right;

                while (low <= high)
                {
                    while (values[low] < pivot)
                    {
                        low++;
                    }

                    while (values[high] > pivot)
                    {
                        high--;
                    }

                    if (low <= high)
                    {
                        var value = values[low];
                        values[low] = values[high];
                        values[high] = value;
                        low++;
                        high--;
                    }
                }

                if (target <= high)
                {
                    right = high;
                }
                else if (target >= low)
                {
                    left = low;
                }
                else
                {
                    return values[target];
                }
            }

            return values[target];
        }

        private static bool IsFinitePositive(float value)
        {
            return value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    internal readonly struct FrameHitchObservation
    {
        internal FrameHitchObservation(bool isHitch, float baselineMs, float effectiveThresholdMs)
        {
            IsHitch = isHitch;
            BaselineMs = baselineMs;
            EffectiveThresholdMs = effectiveThresholdMs;
        }

        internal bool IsHitch { get; }

        internal float BaselineMs { get; }

        internal float EffectiveThresholdMs { get; }
    }
}
