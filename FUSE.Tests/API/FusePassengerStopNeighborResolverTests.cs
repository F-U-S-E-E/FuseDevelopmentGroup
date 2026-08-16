using FUSE.Runtime.API;
using Xunit;

namespace FUSE.Tests.API
{
    public class FusePassengerStopNeighborResolverTests
    {
        [Fact]
        public void Resolve_TwoModdedStops_UsesCompletedLiveRegistry()
        {
            var whittier = new Stop("whittier", "WHT");
            var bryson = new Stop("bryson", "BRY");
            var liveStops = new[] { whittier, bryson };

            var whittierNeighbors = Resolve(new[] { "whittier", "bryson" }, whittier, liveStops);
            var brysonNeighbors = Resolve(new[] { "whittier", "bryson" }, bryson, liveStops);

            Assert.Equal(new[] { bryson }, whittierNeighbors);
            Assert.Equal(new[] { whittier }, brysonNeighbors);
        }

        [Fact]
        public void Resolve_MatchesIdentifierAndTimetableCode_IgnoringCaseAndWhitespace()
        {
            var source = new Stop("ela", "ELA");
            var whittier = new Stop("whittier", "WHT");
            var bryson = new Stop("bryson", "BRY");

            var neighbors = Resolve(
                new[] { " WHITTIER ", "bRy" },
                source,
                new[] { source, whittier, bryson });

            Assert.Equal(new[] { whittier, bryson }, neighbors);
        }

        [Fact]
        public void Resolve_MissingOrBlankNeighborIds_ReturnsNoNeighbors()
        {
            var source = new Stop("whittier", "WHT");
            var bryson = new Stop("bryson", "BRY");

            Assert.Empty(Resolve(null, source, new[] { source, bryson }));
            Assert.Empty(Resolve(new[] { " ", null }, source, new[] { source, bryson }));
        }

        private static Stop[] Resolve(string[] neighborIds, Stop source, Stop[] candidates)
        {
            return FusePassengerStopNeighborResolver.Resolve(
                neighborIds,
                source,
                candidates,
                stop => stop.Identifier,
                stop => stop.TimetableCode);
        }

        private sealed class Stop
        {
            internal Stop(string identifier, string timetableCode)
            {
                Identifier = identifier;
                TimetableCode = timetableCode;
            }

            internal string Identifier { get; }
            internal string TimetableCode { get; }
        }
    }
}
