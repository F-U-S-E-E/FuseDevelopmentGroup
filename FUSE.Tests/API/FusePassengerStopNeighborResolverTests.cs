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
            var airport = new Stop("mca-station", "MCA", neighborIds: new[] { "OH" });
            var iodla = new Stop("iodla-jct-station", "IJ", neighborIds: new[] { "MCA" });

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
            var source = new Stop("olivehill-station", "OH", neighborIds: new[] { "OH" });
            var blank = new Stop("blank", "BLK", neighborIds: new[] { " ", null });
            var unrelated = new Stop("mca-station", "MCA", neighborIds: new[] { "IJ" });

            var incoming = FusePassengerStopNeighborResolver.ResolveIncoming(
                source,
                new[] { source, blank, unrelated },
                stop => stop.NeighborIds,
                stop => stop.Identifier,
                stop => stop.TimetableCode);

            Assert.Empty(incoming);
        }

        [Fact]
        public void ResolveUnauthoredBranchNetwork_ReconstructsFontanaTerminalChain()
        {
            var noland = new Stop("noland", "ES", branch: "Fontana Branch", x: -2496.20, z: 6380.15);
            var bushnell = new Stop("bushnell", "BU", branch: "Fontana Branch", x: -8193.13, z: 6099.52);
            var collinwood = new Stop("collinwood", "CL", branch: "Fontana Branch", x: -10870.52, z: 5672.83);
            var marcus = new Stop("marcus", "MA", branch: "Fontana Branch", x: -18809.48, z: 6805.44);
            var ritter = new Stop("ritter", "RT", branch: "Fontana Branch", x: -23348.03, z: 6771.03);
            var fontana = new Stop("fontana", "FT", branch: "Fontana Branch", x: -26336.90, z: 7213.35);

            var network = ResolveBranchNetwork(noland, bushnell, collinwood, marcus, ritter, fontana);

            Assert.Equal(new[] { bushnell }, network[noland]);
            Assert.Equal(new[] { collinwood, noland }, network[bushnell]);
            Assert.Equal(new[] { bushnell, marcus }, network[collinwood]);
            Assert.Equal(new[] { collinwood, ritter }, network[marcus]);
            Assert.Equal(new[] { fontana, marcus }, network[ritter]);
            Assert.Equal(new[] { ritter }, network[fontana]);
        }

        [Fact]
        public void ResolveUnauthoredBranchNetwork_DoesNotOverridePartiallyAuthoredBranch()
        {
            var first = new Stop("first", "A", branch: "Branch", x: 0, z: 0);
            var second = new Stop("second", "B", branch: "Branch", x: 10, z: 0, neighborIds: new[] { "A" });

            Assert.Empty(ResolveBranchNetwork(first, second));
        }

        [Fact]
        public void ResolveUnauthoredBranchNetwork_RequiresNamedBranchAndDistinctPositions()
        {
            var unnamedA = new Stop("unnamed-a", "A", x: 0, z: 0);
            var unnamedB = new Stop("unnamed-b", "B", x: 10, z: 0);
            var coincidentA = new Stop("coincident-a", "C", branch: "Branch", x: 5, z: 5);
            var coincidentB = new Stop("coincident-b", "D", branch: "Branch", x: 5, z: 5);

            Assert.Empty(ResolveBranchNetwork(unnamedA, unnamedB, coincidentA, coincidentB));
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

        private static System.Collections.Generic.IReadOnlyDictionary<Stop, Stop[]> ResolveBranchNetwork(
            params Stop[] candidates)
        {
            return FusePassengerStopNeighborResolver.ResolveUnauthoredBranchNetwork(
                candidates,
                stop => stop.Branch,
                stop => stop.NeighborIds,
                stop => stop.Identifier,
                stop => stop.X,
                stop => stop.Y,
                stop => stop.Z);
        }

        private sealed class Stop
        {
            internal Stop(
                string identifier,
                string timetableCode,
                string branch = null,
                double x = 0,
                double y = 0,
                double z = 0,
                params string[] neighborIds)
            {
                Identifier = identifier;
                TimetableCode = timetableCode;
                Branch = branch;
                X = x;
                Y = y;
                Z = z;
                NeighborIds = neighborIds;
            }

            internal string Identifier { get; }
            internal string TimetableCode { get; }
            internal string Branch { get; }
            internal double X { get; }
            internal double Y { get; }
            internal double Z { get; }
            internal string[] NeighborIds { get; }
        }
    }
}
