using System;
using System.Threading.Tasks;
using FUSE.Patches;
using Xunit;

namespace FUSE.Tests.Patches
{
    public sealed class FuseSceneryLoadFailureWatchTests
    {
        private long _timestamp;

        public FuseSceneryLoadFailureWatchTests()
        {
            FuseSceneryLoadFailurePatch.ResetForNewMap();
        }

        [Fact]
        public void TimeSeparatedRetries_KeepOneReportButRequestOneQuarantine()
        {
            for (var attempt = 0; attempt < 7; attempt++)
            {
                ObserveTaskFailure("bridge-style-a");
                AdvancePastEpisodeWindow();
            }

            Assert.Equal(1, FuseSceneryLoadFailurePatch.PendingCountForTests);
            Assert.Equal(1, FuseSceneryLoadFailurePatch.QuarantinePendingCountForTests);
            Assert.True(FuseSceneryLoadFailurePatch.IsQuarantined("BRIDGE-STYLE-A"));
        }

        [Fact]
        public void FewerThanThresholdFailures_DoNotQuarantine()
        {
            for (var attempt = 0; attempt < 4; attempt++)
            {
                ObserveTaskFailure("occasionally-transient-asset");
                AdvancePastEpisodeWindow();
            }

            Assert.Equal(1, FuseSceneryLoadFailurePatch.PendingCountForTests);
            Assert.Equal(0, FuseSceneryLoadFailurePatch.QuarantinePendingCountForTests);
            Assert.False(FuseSceneryLoadFailurePatch.IsQuarantined("occasionally-transient-asset"));
        }

        [Fact]
        public void Threshold_IsCaseInsensitiveAndIndependentPerIdentifier()
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                ObserveTaskFailure(attempt % 2 == 0 ? "Mixed-Case-Asset" : "mixed-case-asset");
                if (attempt < 4)
                {
                    ObserveTaskFailure("other-asset");
                }

                AdvancePastEpisodeWindow();
            }

            Assert.Equal(2, FuseSceneryLoadFailurePatch.PendingCountForTests);
            Assert.Equal(1, FuseSceneryLoadFailurePatch.QuarantinePendingCountForTests);
        }

        [Fact]
        public void SameIdentifierBurst_IsOneEpisodePerObserver()
        {
            for (var placement = 0; placement < 50; placement++)
            {
                ObserveTaskFailure("dense-placement-asset");
                ObserveLogFailure("dense-placement-asset");
            }

            Assert.Equal(1, FuseSceneryLoadFailurePatch.PendingCountForTests);
            Assert.Equal(0, FuseSceneryLoadFailurePatch.QuarantinePendingCountForTests);
            Assert.False(FuseSceneryLoadFailurePatch.IsQuarantined("dense-placement-asset"));
        }

        [Fact]
        public void ContinuousBurst_CountsFromLastCountedEpisodeNotLastObservation()
        {
            var window = FuseSceneryLoadFailurePatch.FailureEpisodeCoalesceWindowTicksForTests;
            ObserveTaskFailure("continuous-storm");

            // This coalesced observation must not slide the episode boundary.
            _timestamp = window - 1;
            ObserveTaskFailure("continuous-storm");

            for (var episode = 1; episode < 5; episode++)
            {
                _timestamp = episode * window;
                ObserveTaskFailure("continuous-storm");
            }

            Assert.True(FuseSceneryLoadFailurePatch.IsQuarantined("continuous-storm"));
            Assert.Equal(1, FuseSceneryLoadFailurePatch.QuarantinePendingCountForTests);
        }

        [Fact]
        public void TaskAndLogObservers_KeepIndependentRetryEpisodeCounts()
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                ObserveTaskFailure("dual-observed-asset");
                ObserveLogFailure("dual-observed-asset");
                AdvancePastEpisodeWindow();
            }

            // Six total observations must not combine into one threshold: each
            // observer has independently seen only three retry episodes.
            Assert.False(FuseSceneryLoadFailurePatch.IsQuarantined("dual-observed-asset"));

            ObserveTaskFailure("dual-observed-asset");
            AdvancePastEpisodeWindow();
            ObserveTaskFailure("dual-observed-asset");

            Assert.True(FuseSceneryLoadFailurePatch.IsQuarantined("dual-observed-asset"));
            Assert.Equal(1, FuseSceneryLoadFailurePatch.QuarantinePendingCountForTests);
        }

        [Fact]
        public void ResetForNewMap_ClearsCountsAndRearmsQuarantine()
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                ObserveTaskFailure("map-scoped-asset");
                AdvancePastEpisodeWindow();
            }

            Assert.Equal(1, FuseSceneryLoadFailurePatch.QuarantinePendingCountForTests);

            FuseSceneryLoadFailurePatch.ResetForNewMap();

            Assert.Equal(0, FuseSceneryLoadFailurePatch.PendingCountForTests);
            Assert.Equal(0, FuseSceneryLoadFailurePatch.QuarantinePendingCountForTests);
            Assert.False(FuseSceneryLoadFailurePatch.IsQuarantined("map-scoped-asset"));
            for (var attempt = 0; attempt < 5; attempt++)
            {
                ObserveTaskFailure("map-scoped-asset");
                AdvancePastEpisodeWindow();
            }

            Assert.Equal(1, FuseSceneryLoadFailurePatch.QuarantinePendingCountForTests);
        }

        [Fact]
        public void MapWillLoadReset_IgnoresOldCompletionButRetainsCurrentLoadFailure()
        {
            var oldMapSource = new TaskCompletionSource<object>();
            FuseSceneryLoadFailurePatch.Postfix("old-map-asset", oldMapSource.Task);

            // FuseLifecycle calls this at the start of MapWillLoad, before any
            // current-map LoadScenery task can be observed.
            FuseSceneryLoadFailurePatch.ResetForNewMap();
            var currentMapSource = new TaskCompletionSource<object>();
            FuseSceneryLoadFailurePatch.Postfix("current-map-asset", currentMapSource.Task);

            oldMapSource.SetException(new Exception("late old-map failure"));
            currentMapSource.SetException(new Exception("current map failure"));

            Assert.True(oldMapSource.Task.IsFaulted);
            Assert.True(currentMapSource.Task.IsFaulted);
            Assert.Equal(1, FuseSceneryLoadFailurePatch.PendingCountForTests);
            Assert.Equal(0, FuseSceneryLoadFailurePatch.QuarantinePendingCountForTests);
        }

        [Theory]
        [InlineData(true, true, true)]
        [InlineData(true, false, false)]
        [InlineData(false, true, false)]
        [InlineData(false, false, false)]
        public void QuarantineSuppression_AppliesOnlyToLoadRequests(
            bool loaded,
            bool quarantined,
            bool expected)
        {
            Assert.Equal(
                expected,
                FuseSceneryLoadThrottlePatch.ShouldSuppressQuarantinedLoad(loaded, quarantined));
        }

        [Fact]
        public void BuildQuarantineIdentifierSet_DeduplicatesCaseInsensitivelyAndSkipsBlanks()
        {
            var requested = FuseSceneryLoadFailurePatch.BuildQuarantineIdentifierSet(
                new[] { "asset-a", "ASSET-A", null, "  ", "asset-b" });

            Assert.Equal(2, requested.Count);
            Assert.Contains(requested, identifier =>
                string.Equals(identifier, "Asset-A", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(requested, identifier =>
                string.Equals(identifier, "ASSET-B", StringComparison.OrdinalIgnoreCase));
        }

        [Theory]
        [InlineData("Error loading scenery bridge-style-a", "bridge-style-a")]
        [InlineData("Error loading scenery asset with spaces ", "asset with spaces")]
        public void TryParseSceneryLoadErrorLine_ExtractsIdentifier(string line, string expected)
        {
            Assert.True(FuseSceneryLoadFailurePatch.TryParseSceneryLoadErrorLine(line, out var identifier));
            Assert.Equal(expected, identifier);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("Error loading scenery ")]
        [InlineData("error loading scenery bridge-style-a")]
        [InlineData("Error preparing loaded scenery bridge-style-a")]
        public void TryParseSceneryLoadErrorLine_RejectsOtherLines(string line)
        {
            Assert.False(FuseSceneryLoadFailurePatch.TryParseSceneryLoadErrorLine(line, out var identifier));
            Assert.Null(identifier);
        }

        private void ObserveTaskFailure(string identifier)
        {
            FuseSceneryLoadFailurePatch.ObserveFailureForTests(
                identifier,
                fromGameLog: false,
                _timestamp);
        }

        private void ObserveLogFailure(string identifier)
        {
            FuseSceneryLoadFailurePatch.ObserveFailureForTests(
                identifier,
                fromGameLog: true,
                _timestamp);
        }

        private void AdvancePastEpisodeWindow()
        {
            _timestamp += FuseSceneryLoadFailurePatch.FailureEpisodeCoalesceWindowTicksForTests + 1;
        }
    }
}
