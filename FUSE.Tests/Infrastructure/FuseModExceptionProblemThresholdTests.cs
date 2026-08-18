using FUSE.Infrastructure;
using Xunit;

namespace FUSE.Tests.Infrastructure
{
    /// <summary>
    /// Issue #208: the health report and the Status page must agree on when a
    /// mod's observed exceptions count as a problem. A one-off third-party
    /// exception is informational; only a recurring thrower is a problem.
    /// </summary>
    public sealed class FuseModExceptionProblemThresholdTests
    {
        [Theory]
        [InlineData(1, 1, false)]
        [InlineData(2, 9, false)]
        [InlineData(3, 3, true)]
        [InlineData(1, 10, true)]
        [InlineData(0, 0, false)]
        public void IsProblem_MatchesRegistryThresholds(long episodes, long count, bool expected)
        {
            var record = new FuseModExceptionSnapshot
            {
                ModId = "Some.Mod",
                Episodes = episodes,
                Count = count
            };

            Assert.Equal(expected, record.IsProblem);
            Assert.Equal(expected, FuseModExceptionRegistry.IsProblem(record));
        }

        [Fact]
        public void IsProblem_NullRecord_IsNotAProblem()
        {
            Assert.False(FuseModExceptionRegistry.IsProblem(null));
        }

        [Fact]
        public void Thresholds_AreTheOnesTheReportDocuments()
        {
            // These values are quoted in the Status page copy; keep them in
            // lockstep with the documented "3+ episodes or 10+ occurrences".
            Assert.Equal(3, FuseModExceptionRegistry.ProblemEpisodeThreshold);
            Assert.Equal(10, FuseModExceptionRegistry.ProblemCountThreshold);
        }
    }
}
