using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FUSE.Profiler.Infrastructure;

namespace FUSE.Profiler.Engine
{
    /// <summary>
    /// Session orchestration: the global sampling gate the instrumented code
    /// checks, the two cycle clocks (frame and sim tick), and the throttled
    /// background rollup that turns ring buffers into the sorted row list
    /// the UI renders.
    /// </summary>
    internal static class ProfilerSession
    {
        internal const string FrameProbeKey = "profiler.frame";
        internal const string FrameProbeLabel = "Whole frame";

        private static volatile bool _sampling;
        private static volatile bool _paused;

        /// <summary>
        /// The single gate the instrumented wrappers read: true only while
        /// sampling and not paused. Kept as one volatile so pausing freezes
        /// accumulation too — without this, probes kept measuring through a
        /// pause and the first resumed sample contained the whole pause.
        /// </summary>
        internal static volatile bool Recording;

        /// <summary>Master switch; flipped by open/close.</summary>
        internal static bool Sampling
        {
            get => _sampling;
            set
            {
                _sampling = value;
                Recording = _sampling && !_paused;
            }
        }

        /// <summary>Freezes measurement without unpatching.</summary>
        internal static bool Paused
        {
            get => _paused;
            set
            {
                _paused = value;
                Recording = _sampling && !_paused;
            }
        }

        /// <summary>
        /// Seconds between stats rollups; assigned from settings at load.
        /// </summary>
        internal static float StatsIntervalSeconds = 0.5f;

        /// <summary>
        /// How many recent cycles the rollup aggregates. The full ring is
        /// ~33s of frames at 60fps; a shorter window keeps the numbers
        /// responsive to what is happening right now.
        /// </summary>
        internal static int RollupWindowCycles = 600;

        private static readonly object RowsLock = new object();
        private static readonly HashSet<string> Pinned = new HashSet<string>(StringComparer.Ordinal);
        private static List<ProbeRow> _rows = new List<ProbeRow>();
        private static int _resetGeneration;
        private static float _sinceRollup;
        private static int _rollupRunning;
        private static ProbeSortMode _sortMode = ProbeSortMode.Average;

        internal static ProbeSortMode SortMode
        {
            get => _sortMode;
            set => _sortMode = value;
        }

        internal static void TogglePinned(string key)
        {
            lock (RowsLock)
            {
                if (!Pinned.Remove(key))
                {
                    Pinned.Add(key);
                }
            }
        }

        internal static bool IsPinned(string key)
        {
            lock (RowsLock)
            {
                return Pinned.Contains(key);
            }
        }

        /// <summary>
        /// Frame clock, called from the host's LateUpdate (after the frame's
        /// Update work, so per-frame probes have finished their cycle;
        /// rendering-side work lands in the next bucket by design).
        /// </summary>
        internal static void FrameBoundary(float unscaledDeltaSeconds)
        {
            if (!Recording)
            {
                return;
            }

            var frameProbe = ProbeRegistry.GetOrAdd(FrameProbeKey, FrameProbeLabel, ProbeCadence.Frame);
            frameProbe.AddExternalMilliseconds(unscaledDeltaSeconds * 1000d);
            ProbeRegistry.CloseCycles(ProbeCadence.Frame);

            _sinceRollup += unscaledDeltaSeconds;
            if (_sinceRollup >= StatsIntervalSeconds)
            {
                _sinceRollup = 0f;
                ScheduleRollup();
            }
        }

        /// <summary>
        /// Sim-tick clock, called from the TrainController fixed-step
        /// postfix. Closes only sim-tick probes.
        /// </summary>
        internal static void SimTickBoundary()
        {
            if (!Recording)
            {
                return;
            }

            ProbeRegistry.CloseCycles(ProbeCadence.SimTick);
        }

        /// <summary>Latest rolled-up rows (copy; safe to hold across frames).</summary>
        internal static List<ProbeRow> CopyRows()
        {
            lock (RowsLock)
            {
                return new List<ProbeRow>(_rows);
            }
        }

        internal static void Reset()
        {
            Sampling = false;
            Paused = false;
            _sinceRollup = 0f;
            lock (RowsLock)
            {
                _resetGeneration++;
                _rows = new List<ProbeRow>();
                Pinned.Clear();
            }
        }

        private static void ScheduleRollup()
        {
            // One rollup in flight at a time; a skipped interval just means
            // slightly staler rows.
            if (Interlocked.CompareExchange(ref _rollupRunning, 1, 0) != 0)
            {
                return;
            }

            Task.Run(() =>
            {
                try
                {
                    RollupOnce();
                }
                catch (Exception ex)
                {
                    ProfilerLog.Exception("FUSE.Profiler stats rollup failed", ex);
                }
                finally
                {
                    Interlocked.Exchange(ref _rollupRunning, 0);
                }
            });
        }

        private static void RollupOnce()
        {
            int generationAtStart;
            lock (RowsLock)
            {
                generationAtStart = _resetGeneration;
            }

            var probes = ProbeRegistry.SnapshotProbes();
            var window = RollupWindowCycles;

            double frameTotalMs = 0d;
            if (ProbeRegistry.TryGet(FrameProbeKey, out var frameProbe))
            {
                frameTotalMs = frameProbe.Aggregate(window).TotalMs;
            }

            var rows = new List<ProbeRow>(probes.Length);
            for (var i = 0; i < probes.Length; i++)
            {
                var probe = probes[i];
                var agg = probe.Aggregate(window);
                if (agg.Samples == 0)
                {
                    continue;
                }

                var percent = probe.Cadence == ProbeCadence.Frame && frameTotalMs > 0d
                    ? agg.TotalMs / frameTotalMs * 100d
                    : -1d;
                rows.Add(new ProbeRow(
                    probe.Key,
                    probe.Label,
                    probe.GroupKey,
                    probe.Cadence,
                    agg.AverageMs,
                    agg.MaxMs,
                    agg.TotalMs,
                    agg.Calls,
                    agg.MaxCallsPerCycle,
                    percent));
            }

            HashSet<string> pinnedCopy;
            lock (RowsLock)
            {
                pinnedCopy = new HashSet<string>(Pinned, StringComparer.Ordinal);
            }

            ProbeRow.SortForDisplay(rows, _sortMode, pinnedCopy);

            lock (RowsLock)
            {
                // A Reset (cleanup/close) that landed mid-rollup wins: don't
                // resurrect rows built from the torn-down registry.
                if (_resetGeneration == generationAtStart)
                {
                    _rows = rows;
                }
            }
        }
    }
}
