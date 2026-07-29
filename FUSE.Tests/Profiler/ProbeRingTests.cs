using FUSE.Profiler.Engine;
using Xunit;

namespace FUSE.Tests.Profiler
{
    public class ProbeRingTests
    {
        [Fact]
        public void CloseCycle_records_hits_and_advances_the_ring()
        {
            var ring = new ProbeRing("k", "label", ProbeCadence.Frame, null);
            ring.Enter();
            ring.Exit();
            ring.Enter();
            ring.Exit();
            ring.CloseCycle();
            ring.CloseCycle(); // empty cycle

            var agg = ring.Aggregate(10);
            Assert.Equal(2, agg.Samples);
            Assert.Equal(2, agg.Calls);
            Assert.Equal(2, agg.MaxCallsPerCycle);
        }

        [Fact]
        public void Reentrant_Enter_does_not_double_count_time_but_counts_calls()
        {
            var ring = new ProbeRing("k", "label", ProbeCadence.Frame, null);
            ring.Enter();
            ring.Enter(); // nested call: depth 2, one shared interval
            ring.Exit();
            ring.Exit();
            ring.CloseCycle();

            var agg = ring.Aggregate(1);
            Assert.Equal(2, agg.Calls);
            Assert.Equal(1, agg.Samples);
        }

        [Fact]
        public void Nested_Exit_keeps_timing_until_the_outermost_Exit()
        {
            var ring = new ProbeRing("k", "label", ProbeCadence.Frame, null);
            ring.Enter();
            ring.Enter();
            ring.Exit(); // inner return: depth 1 — the watch must keep running
            var tail = System.Diagnostics.Stopwatch.StartNew();
            while (tail.Elapsed.TotalMilliseconds < 6)
            {
                // The outer method's tail work.
            }

            ring.Exit();
            ring.CloseCycle();

            var agg = ring.Aggregate(1);
            Assert.True(agg.TotalMs >= 5.0, $"outer tail time was dropped: {agg.TotalMs:0.000}ms");
        }

        [Fact]
        public void Unbalanced_Exit_is_harmless_and_CloseCycle_heals_a_stuck_depth()
        {
            var ring = new ProbeRing("k", "label", ProbeCadence.Frame, null);
            ring.Exit(); // spurious exit at depth 0: no-op
            ring.Enter(); // a throwing method: Exit never came
            ring.CloseCycle(); // resets both watch and depth

            ring.Enter();
            ring.Exit();
            ring.CloseCycle();

            var agg = ring.Aggregate(1);
            Assert.Equal(1, agg.Calls);
            Assert.Equal(1, agg.Samples);
        }

        [Fact]
        public void External_milliseconds_feed_the_cycle_total()
        {
            var ring = new ProbeRing("k", "label", ProbeCadence.Frame, null);
            ring.AddExternalMilliseconds(16.5);
            ring.CloseCycle();

            var agg = ring.Aggregate(1);
            Assert.Equal(1, agg.Calls);
            Assert.Equal(16.5, agg.TotalMs, 3);
            Assert.Equal(16.5, agg.MaxMs, 3);
        }

        [Fact]
        public void Aggregate_window_is_bounded_by_recorded_cycles()
        {
            var ring = new ProbeRing("k", "label", ProbeCadence.Frame, null);
            for (var i = 0; i < 5; i++)
            {
                ring.AddExternalMilliseconds(1.0);
                ring.CloseCycle();
            }

            Assert.Equal(3, ring.Aggregate(3).Samples);
            Assert.Equal(5, ring.Aggregate(50).Samples);
            Assert.Equal(5.0, ring.Aggregate(50).TotalMs, 3);
        }

        [Fact]
        public void Ring_wraps_without_losing_the_newest_samples()
        {
            var ring = new ProbeRing("k", "label", ProbeCadence.Frame, null);
            for (var i = 0; i < ProbeRing.SlotCount + 10; i++)
            {
                ring.AddExternalMilliseconds(i);
                ring.CloseCycle();
            }

            var buffer = new double[3];
            var copied = ring.CopyRecentInto(buffer, 3);
            Assert.Equal(3, copied);
            // Newest-last ordering; the newest cycle recorded SlotCount+9.
            Assert.Equal(ProbeRing.SlotCount + 9, buffer[2], 3);
            Assert.Equal(ProbeRing.SlotCount + 8, buffer[1], 3);
            Assert.Equal(ProbeRing.SlotCount + 7, buffer[0], 3);
        }

        [Fact]
        public void Empty_cycles_do_not_contribute_time()
        {
            var ring = new ProbeRing("k", "label", ProbeCadence.Frame, null);
            ring.CloseCycle();
            ring.CloseCycle();

            var agg = ring.Aggregate(10);
            Assert.Equal(2, agg.Samples);
            Assert.Equal(0, agg.Calls);
            Assert.Equal(0d, agg.TotalMs, 6);
        }
    }
}
