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

        [Fact]
        public void ResolveIncoming_FindsStopThatReferencesSourceTimetableCode()
        {
            var oliveHill = new Stop("olivehill-station", "OH");
            var airport = new Stop("mca-station", "MCA", "OH");
            var iodla = new Stop("iodla-jct-station", "IJ", "MCA");

            var incoming = FusePassengerStopNeighborResolver.ResolveIncoming(
                oliveHill,
                new[] { oliveHill, airport, iodla },
                stop => stop.NeighborIds,
                stop => stop.Identifier,
                stop => stop.TimetableCode);

            Assert.Equal(new[] { airport }, incoming);
        }

        [Fact]
        public void ResolveIncoming_IgnoresSelfBlankAndUnrelatedReferences()
        {
            var source = new Stop("olivehill-station", "OH", "OH");
            var blank = new Stop("blank", "BLK", " ", null);
            var unrelated = new Stop("mca-station", "MCA", "IJ");

            var incoming = FusePassengerStopNeighborResolver.ResolveIncoming(
                source,
                new[] { source, blank, unrelated },
                stop => stop.NeighborIds,
                stop => stop.Identifier,
                stop => stop.TimetableCode);

            Assert.Empty(incoming);
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
            internal Stop(string identifier, string timetableCode, params string[] neighborIds)
            {
                Identifier = identifier;
                TimetableCode = timetableCode;
                NeighborIds = neighborIds;
            }

            internal string Identifier { get; }
            internal string TimetableCode { get; }
            internal string[] NeighborIds { get; }
        }
    }
}
