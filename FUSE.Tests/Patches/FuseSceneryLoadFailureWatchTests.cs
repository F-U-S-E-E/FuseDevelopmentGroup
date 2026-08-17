using System;
using System.Threading;
using System.Threading.Tasks;
using FUSE.Infrastructure;
using FUSE.Patches;
using FUSE.Tests.Infrastructure;
using Xunit;

namespace FUSE.Tests.Patches
{
    /// <summary>
    /// Tests for the managed core of the scenery load-failure watch (visible
    /// via InternalsVisibleTo): the postfix's continuation shape and the
    /// game-log line parser. Motivated by a field session where 81 bundle
    /// load failures produced zero records — these pin that the pieces FUSE
    /// controls behave, so a silent field miss localizes to the load path
    /// (e.g. a third-party loader) rather than this code. The Harmony apply
    /// itself is covered by FusePatchTargetingTests; the Unity-touching
    /// drain/record side stays out of xUnit per the repo test policy.
    /// </summary>
    [Collection(FuseRuntimeGuardCountersTestCollection.Name)]
    public sealed class FuseSceneryLoadFailureWatchTests
    {
        private long _timestamp;

        public FuseSceneryLoadFailureWatchTests()
        {
            // Static per-map state; make each test independent.
            FuseSceneryLoadFailurePatch.SetGameLogAcceptanceForTests(false);
            FuseSceneryLoadFailurePatch.ResetForNewMap();
            FuseRuntimeGuardCounters.ResetForTests();
        }

        [Fact]
        public void Postfix_FaultedTask_QueuesExactlyOneFailurePerIdentifier()
        {
            var source = new TaskCompletionSource<object>();
            FuseSceneryLoadFailurePatch.Postfix("aspenbridgeclear", source.Task);
            Assert.Equal(1, FuseRuntimeGuardCounters.SceneryLoadWatchAttached);
            Assert.Equal(0, FuseSceneryLoadFailurePatch.PendingCountForTests);

            source.SetException(new Exception("Failed to load asset from asset bundle"));
            // OnlyOnFaulted + ExecuteSynchronously: the continuation runs on
            // the completing thread; give the already-faulted path no excuse.
            Assert.True(source.Task.IsFaulted);
            Assert.Equal(1, FuseSceneryLoadFailurePatch.PendingCountForTests);

            // The game retries broken assets forever — later loads of the same
            // identifier must not grow the queue.
            var retry = new TaskCompletionSource<object>();
            FuseSceneryLoadFailurePatch.Postfix("aspenbridgeclear", retry.Task);
            retry.SetException(new Exception("Failed to load asset from asset bundle"));
            Assert.Equal(1, FuseSceneryLoadFailurePatch.PendingCountForTests);
            Assert.Equal(2, FuseRuntimeGuardCounters.SceneryLoadWatchAttached);
        }

        [Fact]
        public void Postfix_AlreadyFaultedTask_StillQueues()
        {
            var source = new TaskCompletionSource<object>();
            source.SetException(new Exception("Failed to load asset from asset bundle"));

            FuseSceneryLoadFailurePatch.Postfix("aspenbridgeclear", source.Task);

            Assert.Equal(1, FuseSceneryLoadFailurePatch.PendingCountForTests);
        }

        [Fact]
        public void Postfix_SuccessfulTask_QueuesNothing()
        {
            var source = new TaskCompletionSource<object>();
            FuseSceneryLoadFailurePatch.Postfix("aspenbridgeclear", source.Task);
            source.SetResult(new object());

            Assert.Equal(0, FuseSceneryLoadFailurePatch.PendingCountForTests);
            Assert.Equal(1, FuseRuntimeGuardCounters.SceneryLoadWatchAttached);
        }

        [Fact]
        public void Postfix_NullTaskOrBlankIdentifier_NoAttach()
        {
            FuseSceneryLoadFailurePatch.Postfix("aspenbridgeclear", null);
            FuseSceneryLoadFailurePatch.Postfix("   ", new TaskCompletionSource<object>().Task);

            Assert.Equal(0, FuseRuntimeGuardCounters.SceneryLoadWatchAttached);
            Assert.Equal(0, FuseSceneryLoadFailurePatch.PendingCountForTests);
        }

        [Fact]
        public void TimeSeparatedRetries_KeepOneReportButRequestOneQuarantine()
        {
            for (var attempt = 0; attempt < 7; attempt++)
            {
                ObserveTaskFailure("aspenbridgeclear");
                AdvancePastEpisodeWindow();
            }

            Assert.Equal(1, FuseSceneryLoadFailurePatch.QuarantinePendingCountForTests);
            Assert.Equal(1, FuseSceneryLoadFailurePatch.PendingCountForTests);
            Assert.True(FuseSceneryLoadFailurePatch.IsQuarantined("ASPENBRIDGECLEAR"));
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
        public void RequestQuarantine_IsIdempotentPerIdentifier_AndResetsPerMap()
        {
            FuseSceneryLoadFailurePatch.RequestQuarantine("aspenbridgeclear");
            FuseSceneryLoadFailurePatch.RequestQuarantine("ASPENBRIDGECLEAR");
            FuseSceneryLoadFailurePatch.RequestQuarantine("  ");

            Assert.Equal(1, FuseSceneryLoadFailurePatch.QuarantinePendingCountForTests);
            Assert.True(FuseSceneryLoadFailurePatch.IsQuarantined("AspenBridgeClear"));
            Assert.False(FuseSceneryLoadFailurePatch.IsQuarantined("other"));

            FuseSceneryLoadFailurePatch.ResetForNewMap();
            Assert.Equal(0, FuseSceneryLoadFailurePatch.QuarantinePendingCountForTests);
            Assert.False(FuseSceneryLoadFailurePatch.IsQuarantined("aspenbridgeclear"));

            // A fixed pack stays fixed, but a still-broken one re-quarantines
            // on the next map: the request set must re-arm.
            FuseSceneryLoadFailurePatch.RequestQuarantine("aspenbridgeclear");
            Assert.Equal(1, FuseSceneryLoadFailurePatch.QuarantinePendingCountForTests);
        }

        [Fact]
        public void CatalogMismatch_ReportsButDoesNotQuarantineOrCountTowardRuntimeThreshold()
        {
            var entry = new FuseAssetPackBundleAuditPatch.CatalogAssetEntry(
                "aspenbridgeclear",
                "Aspen Bridge Clear",
                "prefab",
                "aspenbridgeclear.prefab");
            FuseSceneryLoadFailurePatch.ReportCatalogMismatch(entry, "aspensassets");
            FuseSceneryLoadFailurePatch.ReportCatalogMismatch(
                new FuseAssetPackBundleAuditPatch.CatalogAssetEntry(
                    "ASPENBRIDGECLEAR",
                    "Aspen Bridge Clear",
                    "PREFAB",
                    "aspenbridgeclear.prefab"),
                "ASPENSASSETS");

            Assert.Equal(1, FuseSceneryLoadFailurePatch.PendingCountForTests);
            Assert.Equal(0, FuseSceneryLoadFailurePatch.QuarantinePendingCountForTests);
            Assert.False(FuseSceneryLoadFailurePatch.IsQuarantined("aspenbridgeclear"));

            for (var attempt = 0; attempt < 4; attempt++)
            {
                ObserveTaskFailure("aspenbridgeclear");
                AdvancePastEpisodeWindow();
            }

            Assert.False(FuseSceneryLoadFailurePatch.IsQuarantined("aspenbridgeclear"));

            ObserveTaskFailure("aspenbridgeclear");

            Assert.True(FuseSceneryLoadFailurePatch.IsQuarantined("ASPENBRIDGECLEAR"));
            Assert.Equal(1, FuseSceneryLoadFailurePatch.QuarantinePendingCountForTests);
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

            // A coalesced observation must not slide the episode boundary.
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

        [Fact]
        public void Shutdown_ClearsStateAndInvalidatesOutstandingTask()
        {
            FuseSceneryLoadFailurePatch.RequestQuarantine("quarantined-asset");
            var source = new TaskCompletionSource<object>();
            FuseSceneryLoadFailurePatch.Postfix("late-asset", source.Task);

            FuseSceneryLoadFailurePatch.Shutdown();
            source.SetException(new Exception("completed after shutdown"));

            Assert.False(FuseSceneryLoadFailurePatch.IsQuarantined("quarantined-asset"));
            Assert.Equal(0, FuseSceneryLoadFailurePatch.PendingCountForTests);
            Assert.Equal(0, FuseSceneryLoadFailurePatch.QuarantinePendingCountForTests);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task LifecycleInvalidation_IgnoresThreadedLogCallbackAlreadyInFlight(bool shutdown)
        {
            FuseSceneryLoadFailurePatch.SetGameLogAcceptanceForTests(true);
            var generationCaptured = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (var resumeObservation = new ManualResetEventSlim(false))
            {
                var observation = Task.Run(() =>
                    FuseSceneryLoadFailurePatch.ObserveGameLogMessageForTests(
                        "Error loading scenery stale-log-asset",
                        () =>
                        {
                            generationCaptured.TrySetResult(null);
                            resumeObservation.Wait();
                        }));

                await generationCaptured.Task;
                try
                {
                    if (shutdown)
                    {
                        FuseSceneryLoadFailurePatch.Shutdown();
                    }
                    else
                    {
                        FuseSceneryLoadFailurePatch.ResetForNewMap();
                    }
                }
                finally
                {
                    resumeObservation.Set();
                }

                await observation;
            }

            Assert.Equal(0, FuseSceneryLoadFailurePatch.PendingCountForTests);
            Assert.Equal(0, FuseSceneryLoadFailurePatch.QuarantinePendingCountForTests);
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
        [InlineData("Error loading scenery aspenbridgeclear", "aspenbridgeclear")]
        [InlineData("Error loading scenery some id with spaces ", "some id with spaces")]
        public void TryParseSceneryLoadErrorLine_MatchingLines_ExtractIdentifier(string line, string expected)
        {
            Assert.True(FuseSceneryLoadFailurePatch.TryParseSceneryLoadErrorLine(line, out var identifier));
            Assert.Equal(expected, identifier);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("Error loading scenery ")]
        [InlineData("Error loading scenery")]
        [InlineData("error loading scenery aspenbridgeclear")]
        [InlineData("Exception awaiting load of asset aspenbridgeclear:")]
        [InlineData("Error preparing loaded scenery aspenbridgeclear")]
        public void TryParseSceneryLoadErrorLine_NonMatchingLines_ReturnFalse(string line)
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
