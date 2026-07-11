using System;
using System.Collections.Generic;
using System.Linq;
using FUSE.Runtime.API;
using Xunit;

namespace FUSE.Tests.API
{
    /// <summary>
    /// Tests for the pure analysis core of the passenger-stop graph validation
    /// (visible via InternalsVisibleTo). The shapes mirror the field failure
    /// that motivated the validator: a pack shipped a leftover duplicate depot
    /// binding the same track span as the real stop, with an empty neighbor
    /// list — two stops fighting over the same parked cars while the report
    /// said "graph 0".
    /// </summary>
    public class FusePassengerStopValidationTests
    {
        private static FusePassengerStopValidation.StopInfo Stop(
            string id,
            string[] spans = null,
            int neighbors = 1)
        {
            return new FusePassengerStopValidation.StopInfo(id, spans ?? Array.Empty<string>(), neighbors);
        }

        [Fact]
        public void Analyze_NoStops_ReturnsNoIssues()
        {
            Assert.Empty(FusePassengerStopValidation.Analyze(Array.Empty<FusePassengerStopValidation.StopInfo>()));
            Assert.Empty(FusePassengerStopValidation.Analyze(null));
        }

        [Fact]
        public void Analyze_DistinctSpansAndWiredNeighbors_ReturnsNoIssues()
        {
            var stops = new List<FusePassengerStopValidation.StopInfo>
            {
                Stop("ela", new[] { "Pbhv" }),
                Stop("bryson", new[] { "P83p" })
            };

            Assert.Empty(FusePassengerStopValidation.Analyze(stops));
        }

        [Fact]
        public void Analyze_SharedSpanAndIsolatedDuplicate_FlagsBoth()
        {
            // The Ela shape: the real stop binds two spans (one shared), the
            // leftover duplicate binds only the shared span and no neighbors.
            var stops = new List<FusePassengerStopValidation.StopInfo>
            {
                Stop("ela", new[] { "Pbhv", "CollieElaDepot" }, neighbors: 2),
                Stop("CollieElaDepot", new[] { "CollieElaDepot" }, neighbors: 0)
            };

            var issues = FusePassengerStopValidation.Analyze(stops);

            Assert.Equal(2, issues.Count);

            var sharedSpan = Assert.Single(issues, issue => issue.Reason.Contains("bind track span"));
            Assert.Equal("CollieElaDepot+ela", sharedSpan.ObjectId);
            Assert.Contains("'CollieElaDepot'", sharedSpan.Reason);

            var isolated = Assert.Single(issues, issue => issue.Reason.Contains("declares no neighbors"));
            Assert.Equal("CollieElaDepot", isolated.ObjectId);
        }

        [Fact]
        public void Analyze_IsolatedStop_NotFlaggedWhenItIsTheOnlyStop()
        {
            var stops = new List<FusePassengerStopValidation.StopInfo>
            {
                Stop("ela", new[] { "Pbhv" }, neighbors: 0)
            };

            Assert.Empty(FusePassengerStopValidation.Analyze(stops));
        }

        [Fact]
        public void Analyze_SharedSpanKeys_MatchCaseInsensitively()
        {
            var stops = new List<FusePassengerStopValidation.StopInfo>
            {
                Stop("ela", new[] { "CollieElaDepot" }),
                Stop("depot2", new[] { "collieeladepot" })
            };

            var issues = FusePassengerStopValidation.Analyze(stops);

            var issue = Assert.Single(issues);
            Assert.Contains("bind track span", issue.Reason);
        }

        [Fact]
        public void Analyze_StopBindingItsOwnSpanTwice_NotFlagged()
        {
            var stops = new List<FusePassengerStopValidation.StopInfo>
            {
                Stop("ela", new[] { "Pbhv", "Pbhv" }),
                Stop("bryson", new[] { "P83p" })
            };

            Assert.Empty(FusePassengerStopValidation.Analyze(stops));
        }

        [Fact]
        public void Analyze_ThreeStopsOnOneSpan_ReportsOneIssueNamingAll()
        {
            var stops = new List<FusePassengerStopValidation.StopInfo>
            {
                Stop("a", new[] { "shared" }),
                Stop("b", new[] { "shared" }),
                Stop("c", new[] { "shared" })
            };

            var issues = FusePassengerStopValidation.Analyze(stops);

            var issue = Assert.Single(issues);
            Assert.Equal("a+b+c", issue.ObjectId);
            Assert.Contains("'a', 'b', 'c'", issue.Reason);
        }

        [Fact]
        public void Analyze_DuplicateIdentifiers_Flagged()
        {
            var stops = new List<FusePassengerStopValidation.StopInfo>
            {
                Stop("Ela", new[] { "Pbhv" }),
                Stop("ela", new[] { "P83p" })
            };

            var issues = FusePassengerStopValidation.Analyze(stops);

            var issue = Assert.Single(issues, candidate => candidate.Reason.Contains("share identifier"));
            Assert.Contains("2 live passenger stop instances", issue.Reason);
        }

        [Fact]
        public void Analyze_SameIdStopsSharingASpan_ReportBothDuplicateAndSpanIssues()
        {
            // Owner tracking is by stop instance, not id: an id-based dedupe
            // would collapse the two owners into one and lose the span-conflict
            // message that explains the marker-flip symptom.
            var stops = new List<FusePassengerStopValidation.StopInfo>
            {
                Stop("ela", new[] { "shared" }),
                Stop("ela", new[] { "shared" })
            };

            var issues = FusePassengerStopValidation.Analyze(stops);

            Assert.Equal(2, issues.Count);
            Assert.Single(issues, issue => issue.Reason.Contains("share identifier"));
            var spanIssue = Assert.Single(issues, issue => issue.Reason.Contains("bind track span"));
            Assert.Equal("ela+ela", spanIssue.ObjectId);
        }

        [Fact]
        public void Analyze_UnnamedStopsSharingASpan_StillFlagged()
        {
            // Blank ids are excluded from the duplicate-id check, so the span
            // check is the only detector left for this shape — it must not
            // collapse the two unnamed owners into one.
            var stops = new List<FusePassengerStopValidation.StopInfo>
            {
                Stop(string.Empty, new[] { "shared" }),
                Stop(string.Empty, new[] { "shared" })
            };

            var issues = FusePassengerStopValidation.Analyze(stops);

            var issue = Assert.Single(issues, candidate => candidate.Reason.Contains("bind track span"));
            Assert.Equal("<unnamed>+<unnamed>", issue.ObjectId);
        }

        [Fact]
        public void Analyze_UnnamedSpanInstanceKeys_OnlyCollideOnTheSameInstance()
        {
            // CollectStopInfos keys unnamed spans as "instance:<id>", so two
            // stops only ever collide on an unnamed span when they bind the
            // same span object.
            var stops = new List<FusePassengerStopValidation.StopInfo>
            {
                Stop("a", new[] { "instance:1001" }),
                Stop("b", new[] { "instance:1002" }),
                Stop("c", new[] { "instance:1001" })
            };

            var issues = FusePassengerStopValidation.Analyze(stops);

            var issue = Assert.Single(issues);
            Assert.Equal("a+c", issue.ObjectId);
        }

        [Fact]
        public void Analyze_IssueOrder_IsDeterministic()
        {
            var stops = new List<FusePassengerStopValidation.StopInfo>
            {
                Stop("zulu", new[] { "sharedB" }, neighbors: 0),
                Stop("alpha", new[] { "sharedB" }),
                Stop("mike", new[] { "sharedA" }),
                Stop("november", new[] { "sharedA" })
            };

            var first = FusePassengerStopValidation.Analyze(stops).Select(issue => issue.ObjectId).ToArray();
            var second = FusePassengerStopValidation.Analyze(stops.AsEnumerable().Reverse().ToList())
                .Select(issue => issue.ObjectId).ToArray();

            Assert.Equal(new[] { "mike+november", "alpha+zulu", "zulu" }, first);
            Assert.Equal(first, second);
        }
    }
}
