using System;
using System.Threading.Tasks;
using FUSE.Infrastructure;
using FUSE.Patches;
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
    public class FuseSceneryLoadFailureWatchTests
    {
        public FuseSceneryLoadFailureWatchTests()
        {
            // Static per-map state; make each test independent.
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
        public void RepeatedFailures_RequestQuarantineExactlyOnceAtThreshold()
        {
            // Five observed failures of one identifier queue one quarantine
            // request; further failures do not queue another.
            for (var attempt = 0; attempt < 7; attempt++)
            {
                var source = new TaskCompletionSource<object>();
                FuseSceneryLoadFailurePatch.Postfix("aspenbridgeclear", source.Task);
                source.SetException(new Exception("Failed to load asset from asset bundle"));
            }

            Assert.Equal(1, FuseSceneryLoadFailurePatch.QuarantinePendingCountForTests);
            // Reporting stays deduped to one row regardless of retry count.
            Assert.Equal(1, FuseSceneryLoadFailurePatch.PendingCountForTests);
        }

        [Fact]
        public void FewFailures_DoNotRequestQuarantine()
        {
            for (var attempt = 0; attempt < 4; attempt++)
            {
                var source = new TaskCompletionSource<object>();
                FuseSceneryLoadFailurePatch.Postfix("aspenbridgeclear", source.Task);
                source.SetException(new Exception("Failed to load asset from asset bundle"));
            }

            Assert.Equal(0, FuseSceneryLoadFailurePatch.QuarantinePendingCountForTests);
        }

        [Fact]
        public void RequestQuarantine_IsIdempotentPerIdentifier_AndResetsPerMap()
        {
            FuseSceneryLoadFailurePatch.RequestQuarantine("aspenbridgeclear");
            FuseSceneryLoadFailurePatch.RequestQuarantine("aspenbridgeclear");
            FuseSceneryLoadFailurePatch.RequestQuarantine("  ");

            Assert.Equal(1, FuseSceneryLoadFailurePatch.QuarantinePendingCountForTests);

            FuseSceneryLoadFailurePatch.ResetForNewMap();
            Assert.Equal(0, FuseSceneryLoadFailurePatch.QuarantinePendingCountForTests);

            // A fixed pack stays fixed, but a still-broken one re-quarantines
            // on the next map: the request set must re-arm.
            FuseSceneryLoadFailurePatch.RequestQuarantine("aspenbridgeclear");
            Assert.Equal(1, FuseSceneryLoadFailurePatch.QuarantinePendingCountForTests);
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
    }
}
