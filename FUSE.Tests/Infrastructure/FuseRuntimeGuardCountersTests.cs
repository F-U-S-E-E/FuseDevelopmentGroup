using FUSE.Infrastructure;
using Xunit;

namespace FUSE.Tests.Infrastructure
{
    /// <summary>
    /// Tests for the shared runtime-guard counter registry (visible via
    /// InternalsVisibleTo). The registry is the single source the guard
    /// patches write and the load report / Health tab read, so its totals,
    /// idle detection, and summary line are the contract a pasted
    /// /fuse.report depends on. Counters are static and session-cumulative;
    /// every test resets first and the collection is serialized by xUnit's
    /// same-class default, so tests cannot interleave.
    /// </summary>
    public class FuseRuntimeGuardCountersTests
    {
        public FuseRuntimeGuardCountersTests()
        {
            FuseRuntimeGuardCounters.ResetForTests();
        }

        [Fact]
        public void FreshRegistry_IsIdle_AndSummarizesAllZero()
        {
            Assert.True(FuseRuntimeGuardCounters.AllIdle);
            Assert.Equal(0, FuseRuntimeGuardCounters.GuardTotal);
            Assert.Equal(
                "decalScrubbed=0 decalVisibility=0 decalHelperEnable=0 decalHelperDisable=0 " +
                "curveMesh=0 sceneryCarDecalsDisabled=0 sceneryLoadFailures=0 flares=0 frameSpikes=0",
                FuseRuntimeGuardCounters.FormatSummary());
        }

        [Fact]
        public void RecordMethods_ReturnTheNewCount_AndFeedTheTotal()
        {
            Assert.Equal(1, FuseRuntimeGuardCounters.RecordDecalVisibilitySuppressed());
            Assert.Equal(2, FuseRuntimeGuardCounters.RecordDecalVisibilitySuppressed());
            Assert.Equal(1, FuseRuntimeGuardCounters.RecordFlareSuppressed());
            Assert.Equal(1, FuseRuntimeGuardCounters.RecordCurveMeshSuppressed());

            Assert.False(FuseRuntimeGuardCounters.AllIdle);
            Assert.Equal(4, FuseRuntimeGuardCounters.GuardTotal);
        }

        [Fact]
        public void FrameSpikes_TrackWorstFrame_AndStayOutOfGuardTotal()
        {
            FuseRuntimeGuardCounters.RecordFrameSpike(120f);
            FuseRuntimeGuardCounters.RecordFrameSpike(850f);
            FuseRuntimeGuardCounters.RecordFrameSpike(200f);

            Assert.Equal(3, FuseRuntimeGuardCounters.FrameSpikes);
            Assert.Equal(850f, FuseRuntimeGuardCounters.FrameSpikeWorstMs);
            // Spikes are measurements, not contained faults.
            Assert.Equal(0, FuseRuntimeGuardCounters.GuardTotal);
            Assert.True(FuseRuntimeGuardCounters.AllIdle);
            Assert.Contains("frameSpikes=3 (worst 850ms)", FuseRuntimeGuardCounters.FormatSummary());
        }

        [Fact]
        public void FormatSummary_CarriesEveryGuardCounter()
        {
            FuseRuntimeGuardCounters.RecordDecalRegistryScrubbed();
            FuseRuntimeGuardCounters.RecordDecalHelperEnableSuppressed();
            FuseRuntimeGuardCounters.RecordDecalHelperDisableSuppressed();
            FuseRuntimeGuardCounters.RecordSceneryDecalComponentDisabled();
            FuseRuntimeGuardCounters.RecordSceneryLoadFailure();

            var summary = FuseRuntimeGuardCounters.FormatSummary();
            Assert.Contains("decalScrubbed=1", summary);
            Assert.Contains("decalHelperEnable=1", summary);
            Assert.Contains("decalHelperDisable=1", summary);
            Assert.Contains("sceneryCarDecalsDisabled=1", summary);
            Assert.Contains("sceneryLoadFailures=1", summary);
            Assert.Equal(5, FuseRuntimeGuardCounters.GuardTotal);
        }
    }
}
