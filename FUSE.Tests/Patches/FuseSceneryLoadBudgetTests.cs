using FUSE.Patches;
using Xunit;

namespace FUSE.Tests.Patches
{
    /// <summary>
    /// Deterministic tests for the issue #76 scenery load-throttle budget — the pure,
    /// Unity-free core of <see cref="FuseSceneryLoadThrottlePatch"/>. The throttle caps
    /// how many FUSE scenery loads START per frame so the teleport-in Instantiate burst
    /// can't drop the frame rate to ~1&#160;fps; these guard the per-frame ceiling and
    /// the frame-reset semantics the prefix and the pump both depend on (an off-by-one
    /// or a missed reset would either stall loading or defeat the throttle).
    /// </summary>
    public class FuseSceneryLoadBudgetTests
    {
        [Fact]
        public void TryConsume_AllowsUpToTheCeiling_ThenDefers()
        {
            var budget = new FuseSceneryLoadBudget(3);
            budget.BeginFrame(1);

            Assert.True(budget.TryConsume());
            Assert.True(budget.TryConsume());
            Assert.True(budget.TryConsume());
            Assert.False(budget.TryConsume()); // 4th in the same frame is deferred.
            Assert.Equal(0, budget.Remaining);
            Assert.Equal(3, budget.StartedThisFrame);
        }

        [Fact]
        public void BeginFrame_ResetsTheCounterOnANewFrame()
        {
            var budget = new FuseSceneryLoadBudget(2);
            budget.BeginFrame(10);
            Assert.True(budget.TryConsume());
            Assert.True(budget.TryConsume());
            Assert.False(budget.TryConsume());

            budget.BeginFrame(11); // next frame: full budget again.
            Assert.Equal(2, budget.Remaining);
            Assert.True(budget.TryConsume());
            Assert.True(budget.TryConsume());
            Assert.False(budget.TryConsume());
        }

        [Fact]
        public void BeginFrame_SameFrameDoesNotReset()
        {
            // The prefix and the pump both call BeginFrame within one frame; the second
            // call must not refill the budget or the per-frame cap is meaningless.
            var budget = new FuseSceneryLoadBudget(2);
            budget.BeginFrame(5);
            Assert.True(budget.TryConsume());

            budget.BeginFrame(5); // same frame again.
            Assert.Equal(1, budget.StartedThisFrame);
            Assert.True(budget.TryConsume());
            Assert.False(budget.TryConsume());
        }

        [Fact]
        public void Remaining_NeverGoesNegative()
        {
            var budget = new FuseSceneryLoadBudget(1);
            budget.BeginFrame(0);
            Assert.True(budget.TryConsume());
            Assert.False(budget.TryConsume());
            Assert.Equal(0, budget.Remaining);
        }

        [Fact]
        public void Constructor_ClampsCeilingToAtLeastOne()
        {
            // A zero/negative ceiling would stall scenery loading entirely; clamp it so
            // a misconfigured tunable degrades to "load one per frame", not "never".
            var budget = new FuseSceneryLoadBudget(0);
            Assert.Equal(1, budget.MaxPerFrame);
            budget.BeginFrame(1);
            Assert.True(budget.TryConsume());
            Assert.False(budget.TryConsume());
        }

        [Fact]
        public void Reset_ClearsFrameStateSoTheNextBeginFrameStartsFresh()
        {
            var budget = new FuseSceneryLoadBudget(2);
            budget.BeginFrame(7);
            Assert.True(budget.TryConsume());

            budget.Reset();
            budget.BeginFrame(7); // same frame index, but state was cleared.
            Assert.Equal(0, budget.StartedThisFrame);
            Assert.Equal(2, budget.Remaining);
        }

        [Theory]
        [InlineData(0, 8, true)]
        [InlineData(7, 8, true)]
        [InlineData(8, 8, false)]
        [InlineData(9, 8, false)]
        public void ShouldStartLoadNow_MatchesTheCeilingComparison(int startedThisFrame, int maxPerFrame, bool expected)
        {
            Assert.Equal(expected, FuseSceneryLoadThrottlePatch.ShouldStartLoadNow(startedThisFrame, maxPerFrame));
        }
    }
}
