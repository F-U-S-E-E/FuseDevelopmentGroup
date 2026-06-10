using FUSE.Interface;
using Xunit;

namespace FUSE.Tests.Interface
{
    // Pins the show/hide gate of the enhanced loading screen (issue #83). The state
    // machine is Unity-free and takes an injected monotonic "now", so the two-flag
    // gate, the abort path, and the watchdog backstop are all assertable without the
    // engine. The two-flag gate is the crux: FUSE's post-load pipeline can finish
    // after the game hides its own screen, so hiding on either signal alone would
    // expose a still-assembling world.
    public class FuseLoadingScreenStateTests
    {
        [Fact]
        public void UpdateVisibility_BeforeBeginLoad_IsHidden()
        {
            var state = new FuseLoadingScreenState();
            Assert.False(state.Active);
            Assert.False(state.UpdateVisibility(0f));
        }

        [Fact]
        public void UpdateVisibility_AfterBeginLoad_IsVisible()
        {
            var state = new FuseLoadingScreenState();
            state.BeginLoad(0f);
            Assert.True(state.Active);
            Assert.True(state.UpdateVisibility(1f));
        }

        [Fact]
        public void UpdateVisibility_GameHiddenThenPipelineDone_Hides()
        {
            var state = new FuseLoadingScreenState();
            state.BeginLoad(0f);
            state.SetProgress(0.5f, 1f);
            state.NotifyGameScreenHidden(2f);
            Assert.True(state.UpdateVisibility(2f)); // pipeline not yet done -> still up
            state.NotifyFusePipelineComplete(3f);
            Assert.False(state.UpdateVisibility(3f)); // both flags -> hide
            Assert.False(state.Active);
        }

        [Fact]
        public void UpdateVisibility_PipelineDoneThenGameHidden_AlsoHides()
        {
            // The opposite ordering — pipeline finishes before the game hides its
            // screen — must also release the gate.
            var state = new FuseLoadingScreenState();
            state.BeginLoad(0f);
            state.NotifyFusePipelineComplete(1f);
            Assert.True(state.UpdateVisibility(1f)); // game screen still up -> stay
            state.NotifyGameScreenHidden(2f);
            Assert.False(state.UpdateVisibility(2f));
        }

        [Fact]
        public void UpdateVisibility_GameHiddenAlone_StaysVisible()
        {
            var state = new FuseLoadingScreenState();
            state.BeginLoad(0f);
            state.NotifyGameScreenHidden(1f);
            Assert.True(state.UpdateVisibility(1f));
        }

        [Fact]
        public void UpdateVisibility_PipelineDoneAlone_StaysVisible()
        {
            var state = new FuseLoadingScreenState();
            state.BeginLoad(0f);
            state.NotifyFusePipelineComplete(1f);
            Assert.True(state.UpdateVisibility(1f));
        }

        [Fact]
        public void Abort_HidesImmediatelyRegardlessOfFlags()
        {
            var state = new FuseLoadingScreenState();
            state.BeginLoad(0f);
            state.Abort();
            Assert.False(state.Active);
            Assert.False(state.UpdateVisibility(1f));
        }

        [Fact]
        public void Abort_ThenBeginLoad_ReArmsTheScreen()
        {
            // A load aborted (failure / return-to-menu) must not poison the next
            // load: BeginLoad has to clear the abort latch.
            var state = new FuseLoadingScreenState();
            state.BeginLoad(0f);
            state.Abort();
            Assert.False(state.UpdateVisibility(1f));

            state.BeginLoad(2f);
            Assert.True(state.Active);
            Assert.True(state.UpdateVisibility(3f));
        }

        [Fact]
        public void UpdateVisibility_WatchdogExpiry_Hides()
        {
            var state = new FuseLoadingScreenState(watchdogSeconds: 60f);
            state.BeginLoad(0f);
            Assert.True(state.UpdateVisibility(59f));
            Assert.False(state.UpdateVisibility(60f)); // 60s since last signal -> backstop
            Assert.False(state.Active);
        }

        [Theory]
        [InlineData("progress")]
        [InlineData("step")]
        [InlineData("game-hidden")]
        [InlineData("pipeline-done")]
        public void UpdateVisibility_WatchdogResets_OnEverySignalKind(string signal)
        {
            // Every signal must push the watchdog deadline out, or a long load that
            // legitimately keeps emitting signals would still be torn down early.
            var state = new FuseLoadingScreenState(watchdogSeconds: 60f);
            state.BeginLoad(0f);

            switch (signal)
            {
                case "progress": state.SetProgress(0.5f, 50f); break;
                case "step": state.SetStep("Restoring rail cars", "1,432 cars", syncPhase: true, 50f); break;
                case "game-hidden": state.NotifyGameScreenHidden(50f); break;
                case "pipeline-done": state.NotifyFusePipelineComplete(50f); break;
            }

            Assert.True(state.UpdateVisibility(100f));  // only 50s since the 50f signal
            Assert.False(state.UpdateVisibility(110f)); // now 60s since the 50f signal
        }

        [Theory]
        [InlineData("progress")]
        [InlineData("game-hidden")]
        [InlineData("pipeline-done")]
        public void Signals_BeforeBeginLoad_AreIgnored(string signal)
        {
            // A stray signal before a load starts must not latch state that would
            // mis-fire the gate on the next BeginLoad.
            var state = new FuseLoadingScreenState();
            switch (signal)
            {
                case "progress": state.SetProgress(0.9f, 0f); break;
                case "game-hidden": state.NotifyGameScreenHidden(0f); break;
                case "pipeline-done": state.NotifyFusePipelineComplete(0f); break;
            }

            Assert.False(state.GameScreenHidden);
            Assert.False(state.FusePipelineDone);
            Assert.Equal(0f, state.Progress);
        }

        [Fact]
        public void SetStep_SyncPhase_IsAOneWayLatchWithinALoad()
        {
            // The host issues SetStep(syncPhase:false) on every async progress tick.
            // Once a blocking phase has set the latch, a later determinate tick must
            // NOT clear it, or the bar would flip back to a fill mid-freeze and read
            // as "hung". Only BeginLoad clears it.
            var state = new FuseLoadingScreenState();
            state.BeginLoad(0f);
            state.SetStep("Restoring rail cars", "1,432 cars", syncPhase: true, 1f);
            Assert.True(state.InSyncPhase);
            state.SetStep("Loading terrain", "Streaming map", syncPhase: false, 2f);
            Assert.True(state.InSyncPhase);
        }

        [Fact]
        public void SetStep_WhenInactive_IsIgnored()
        {
            var state = new FuseLoadingScreenState();
            state.SetStep("ghost", "ghost", syncPhase: true, 0f);
            Assert.Null(state.StepTitle);
            Assert.False(state.InSyncPhase);
        }

        [Theory]
        [InlineData(1.5f, 1f)]
        [InlineData(-0.3f, 0f)]
        [InlineData(0.42f, 0.42f)]
        [InlineData(float.NaN, 0f)]                 // a divide-by-zero progress must not poison the bar
        [InlineData(float.PositiveInfinity, 1f)]
        [InlineData(float.NegativeInfinity, 0f)]
        public void SetProgress_ClampsToUnitRange(float input, float expected)
        {
            var state = new FuseLoadingScreenState();
            state.BeginLoad(0f);
            state.SetProgress(input, 1f);
            Assert.Equal(expected, state.Progress);
        }

        [Fact]
        public void SetStep_SyncPhase_FlipsInSyncPhase()
        {
            var state = new FuseLoadingScreenState();
            state.BeginLoad(0f);
            state.SetProgress(0.5f, 1f);
            Assert.False(state.InSyncPhase);
            state.SetStep("Restoring rail cars", null, syncPhase: true, 2f);
            Assert.True(state.InSyncPhase);
        }

        [Fact]
        public void BeginLoad_FullyResetsForRepeatedLoads()
        {
            var state = new FuseLoadingScreenState();
            state.BeginLoad(0f);
            state.SetProgress(0.7f, 1f);
            state.SetStep("Restoring rail cars", "1,432 cars", syncPhase: true, 2f);
            state.NotifyGameScreenHidden(3f);
            state.NotifyFusePipelineComplete(4f);
            Assert.False(state.UpdateVisibility(4f)); // first load hides
            Assert.False(state.Active);

            state.BeginLoad(10f); // a second load in the same session
            Assert.True(state.Active);
            Assert.False(state.InSyncPhase);
            Assert.Equal(0f, state.Progress);
            Assert.Equal("Loading world", state.StepTitle);
            Assert.Null(state.StepDetail);
            // Gate flags must have reset too: completing only the pipeline must NOT
            // hide (proving _gameScreenHidden didn't leak true from the prior load),
            // and the watchdog clock restarted at 10f.
            Assert.False(state.GameScreenHidden);
            Assert.False(state.FusePipelineDone);
            state.NotifyFusePipelineComplete(11f);
            Assert.True(state.UpdateVisibility(12f));
            state.NotifyGameScreenHidden(13f);
            Assert.False(state.UpdateVisibility(13f));
        }
    }
}
