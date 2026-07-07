namespace FUSE.Interface
{
    /// <summary>
    /// Pure classifier for whether a scenery-benchmark run actually exercised the FUSE
    /// scenery culling path. A run that engaged nothing — no FUSE load/unload churn
    /// and no throttle deferrals/queue — is INCONCLUSIVE, not a
    /// pass: a scenario too light to reproduce the bug must never read as a green
    /// regression guard (exactly how the first "corridor — single" run came back all
    /// zeros while the bug shipped). Unity-free so it is unit-testable in FUSE.Tests.
    /// </summary>
    internal static class FuseSceneryBenchmarkEngagement
    {
        /// <summary>
        /// True when the run touched the FUSE scenery culling pipeline at all. The
        /// counters are independent signals (an unload-only run has churn without
        /// loads or throttle activity, and vice versa), so engagement is their OR.
        /// </summary>
        internal static bool Engaged(long fuseLoads, long fuseUnloads, long deferredLoads, int peakQueueDepth)
        {
            return fuseLoads > 0
                || fuseUnloads > 0
                || deferredLoads > 0
                || peakQueueDepth > 0;
        }
    }
}
