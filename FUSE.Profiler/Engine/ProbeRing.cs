using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace FUSE.Profiler.Engine
{
    /// <summary>
    /// Which sampling clock closes a probe's measurement cycle: once per
    /// rendered frame, or once per train-physics step. A probe must only be
    /// sampled on its own clock or its buckets mix cadences.
    /// </summary>
    internal enum ProbeCadence
    {
        Frame,
        SimTick,
    }

    /// <summary>
    /// One profiled key's measurement state: a single stopwatch accumulating
    /// inclusive time across all calls in the current cycle, plus fixed-size
    /// ring buffers of per-cycle totals.
    ///
    /// Reentrancy: a depth counter pairs Enter/Exit, so the stopwatch runs
    /// from the outermost Enter to the outermost Exit — recursive or
    /// overlapping calls extend one interval (no double-counting, no lost
    /// tail time), while the hit counter still counts every call. If an
    /// instrumented method throws, its <see cref="Exit"/> is skipped and the
    /// depth sticks until <see cref="CloseCycle"/> resets both watch and
    /// depth — the damage is bounded to that one sample.
    ///
    /// Threading: Enter/Exit/CloseCycle run on the Unity main thread. The
    /// stats worker reads the ring arrays concurrently from a background
    /// thread; on x64 the aligned double/int element writes are atomic, so a
    /// reader can at worst see a mix of cycles — acceptable for statistics,
    /// by design.
    /// </summary>
    internal sealed class ProbeRing
    {
        internal const int SlotCount = 2000;

        private readonly Stopwatch _watch = new Stopwatch();
        private readonly double[] _milliseconds = new double[SlotCount];
        private readonly int[] _hits = new int[SlotCount];
        private int _hitsThisCycle;
        private int _depth;
        private double _externalMsThisCycle;
        private int _cursor;
        private int _filled;

        internal ProbeRing(string key, string label, ProbeCadence cadence, string groupKey)
        {
            Key = key;
            Label = string.IsNullOrEmpty(label) ? key : label;
            Cadence = cadence;
            GroupKey = groupKey;
        }

        internal string Key { get; }
        internal string Label { get; }
        internal ProbeCadence Cadence { get; }

        /// <summary>
        /// Optional grouping id for rollups (e.g. the owning mod's name for
        /// foreign-Harmony-patch probes). Null for ungrouped probes.
        /// </summary>
        internal string GroupKey { get; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Enter()
        {
            _hitsThisCycle++;
            if (_depth++ == 0)
            {
                _watch.Start();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Exit()
        {
            if (_depth > 0 && --_depth == 0)
            {
                _watch.Stop();
            }
        }

        /// <summary>
        /// Contribute a duration measured outside the stopwatch (used by the
        /// whole-frame baseline probe, whose duration comes from Unity's
        /// delta time rather than an Enter/Exit pair).
        /// </summary>
        internal void AddExternalMilliseconds(double ms)
        {
            _hitsThisCycle++;
            _externalMsThisCycle += ms;
        }

        /// <summary>
        /// Close the current cycle: store this cycle's total and hit count
        /// into the ring and reset for the next cycle. Called once per frame
        /// or per sim tick (per <see cref="Cadence"/>) from the main thread.
        /// </summary>
        internal void CloseCycle()
        {
            var hits = _hitsThisCycle;
            _hits[_cursor] = hits;
            _milliseconds[_cursor] = hits > 0
                ? _watch.Elapsed.TotalMilliseconds + _externalMsThisCycle
                : 0d;
            _watch.Reset();
            _hitsThisCycle = 0;
            _depth = 0;
            _externalMsThisCycle = 0d;
            _cursor = (_cursor + 1) % SlotCount;
            if (_filled < SlotCount)
            {
                _filled++;
            }
        }

        /// <summary>
        /// Aggregate the most recent recorded cycles (up to
        /// <paramref name="windowCycles"/>). Safe to call from the stats
        /// worker thread; see the class remarks for the tearing model.
        /// </summary>
        internal ProbeAggregate Aggregate(int windowCycles)
        {
            var available = _filled;
            var take = windowCycles < available ? windowCycles : available;
            var index = _cursor;
            var result = default(ProbeAggregate);
            for (var i = 0; i < take; i++)
            {
                index = index == 0 ? SlotCount - 1 : index - 1;
                var ms = _milliseconds[index];
                var hits = _hits[index];
                result.TotalMs += ms;
                result.Calls += hits;
                if (ms > result.MaxMs)
                {
                    result.MaxMs = ms;
                }
                if (hits > result.MaxCallsPerCycle)
                {
                    result.MaxCallsPerCycle = hits;
                }
            }

            result.Samples = take;
            return result;
        }

        /// <summary>
        /// Copy the newest <paramref name="count"/> per-cycle totals into
        /// <paramref name="destination"/>, oldest first. Returns how many
        /// were copied (limited by what has been recorded so far).
        /// </summary>
        internal int CopyRecentInto(double[] destination, int count)
        {
            var available = _filled;
            var take = count < available ? count : available;
            if (take > destination.Length)
            {
                take = destination.Length;
            }

            var index = _cursor;
            for (var i = take - 1; i >= 0; i--)
            {
                index = index == 0 ? SlotCount - 1 : index - 1;
                destination[i] = _milliseconds[index];
            }

            return take;
        }
    }

    internal struct ProbeAggregate
    {
        public double TotalMs;
        public double MaxMs;
        public long Calls;
        public int MaxCallsPerCycle;
        public int Samples;

        public double AverageMs => Samples > 0 ? TotalMs / Samples : 0d;
    }
}
