using FUSE.Runtime.Lifecycle;
using Xunit;

namespace FUSE.Tests.Runtime
{
    /// <summary>
    /// Pins the spike-log throttle contract (visible via InternalsVisibleTo):
    /// early spikes and the heartbeat cadence log as before, and a session
    /// worst ALWAYS breaks through — a field capture lost its single biggest
    /// stall because it fell between heartbeats, leaving only the aggregate
    /// with no timestamp to correlate.
    /// </summary>
    public class FuseFrameSpikeShouldLogTests
    {
        [Theory]
        [InlineData(1, false, true)]    // early spikes log individually
        [InlineData(10, false, true)]
        [InlineData(11, false, false)]  // then the heartbeat takes over
        [InlineData(25, false, true)]
        [InlineData(26, false, false)]
        [InlineData(100, false, true)]
        public void HeartbeatCadence_IsUnchanged(long count, bool isNewWorst, bool expected)
        {
            Assert.Equal(expected, FuseFrameSpikeDiagnostic.ShouldLogSpike(count, isNewWorst));
        }

        [Theory]
        [InlineData(11)]
        [InlineData(26)]
        [InlineData(9999)]
        public void SessionWorst_AlwaysBreaksThroughTheThrottle(long offCadenceCount)
        {
            Assert.True(FuseFrameSpikeDiagnostic.ShouldLogSpike(offCadenceCount, isNewWorst: true));
        }
    }
}
