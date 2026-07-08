using FUSE.Interface;
using Xunit;

namespace FUSE.Tests.Interface
{
    /// <summary>
    /// Guards the Phase-2 "inconclusive run" classifier. The first "corridor — single"
    /// benchmark shipped a regression while reporting all-zero counters; these assert
    /// that an all-zero run is classified NOT engaged (so the JSON/UI marks it
    /// inconclusive instead of a pass), and that any single non-zero engagement signal
    /// flips it to engaged.
    /// </summary>
    public class FuseSceneryBenchmarkEngagementTests
    {
        [Fact]
        public void AllZero_IsNotEngaged()
        {
            // The exact shape of the misleading "corridor — single" run.
            Assert.False(FuseSceneryBenchmarkEngagement.Engaged(
                fuseLoads: 0, fuseUnloads: 0, deferredLoads: 0, peakQueueDepth: 0));
        }

        [Theory]
        [InlineData(1, 0, 0, 0)] // FUSE load churn alone
        [InlineData(0, 1, 0, 0)] // FUSE unload churn alone (outbound-only run)
        [InlineData(0, 0, 1, 0)] // throttle deferral alone
        [InlineData(0, 0, 0, 1)] // a non-empty throttle queue alone
        public void AnySingleSignal_IsEngaged(long fuseLoads, long fuseUnloads, long deferredLoads, int peakQueueDepth)
        {
            Assert.True(FuseSceneryBenchmarkEngagement.Engaged(
                fuseLoads, fuseUnloads, deferredLoads, peakQueueDepth));
        }
    }
}
