using System.Collections.Generic;
using FUSE.Runtime.API;
using Xunit;
using Sample = FUSE.Runtime.API.MapAPI.SceneryRendererVisibility;
using Visibility = FUSE.Runtime.API.MapAPI.DecoupledMaskVisibility;

namespace FUSE.Tests.API
{
    /// <summary>
    /// Pure-logic coverage for <see cref="MapAPI.ClassifyMaskVisibility"/>, the decision that
    /// drives the visibility-driven decoupled-mask lifecycle
    /// (<c>FuseDecoupledMaskVisibilityWatcher</c>). It must separate three look-alike states that
    /// need different mask handling: a building that is drawing (keep mask); one a pack has hidden
    /// via <c>SetActive(false)</c> so every holder is inactive (drop mask); and one the game culler
    /// merely streamed out or stopped drawing by clearing <c>renderer.enabled</c> while the holders
    /// stay active (keep mask — the culler owns that flag, so it is a cull, not a hide). The
    /// Unity-side gather and the SetActive toggle need a live game and are exercised in-game.
    /// </summary>
    public class MapApiMaskVisibilityTests
    {
        [Fact]
        public void NoRenderers_IsIndeterminate_SoMaskIsKept()
        {
            // Streamed out / not yet loaded: cannot tell visible from hidden, so keep the mask.
            Assert.Equal(Visibility.Indeterminate, MapAPI.ClassifyMaskVisibility(new List<Sample>()));
        }

        [Fact]
        public void NullList_IsIndeterminate()
        {
            Assert.Equal(Visibility.Indeterminate, MapAPI.ClassifyMaskVisibility(null));
        }

        [Fact]
        public void AnyEnabledActiveRenderer_IsVisible()
        {
            var renderers = new List<Sample>
            {
                new Sample(enabled: false, activeInHierarchy: true, forceRenderingOff: false),
                new Sample(enabled: true, activeInHierarchy: true, forceRenderingOff: false),
            };

            Assert.Equal(Visibility.Visible, MapAPI.ClassifyMaskVisibility(renderers));
        }

        [Fact]
        public void AllRenderersDisabledButActive_IsIndeterminate_SoCullKeepsMask()
        {
            // The game culler clears renderer.enabled on a resident-but-invisible building
            // (distance band 2) or one that is off-screen, while the holders stay ACTIVE. The
            // culler OWNS renderer.enabled, so this is a cull, NOT an intentional hide: keep the
            // decoupled mask (Indeterminate folds to the last decisive state). This is the exact
            // state that previously dropped the mask the instant a far building streamed in.
            var renderers = new List<Sample>
            {
                new Sample(enabled: false, activeInHierarchy: true, forceRenderingOff: false),
                new Sample(enabled: false, activeInHierarchy: true, forceRenderingOff: false),
            };

            Assert.Equal(Visibility.Indeterminate, MapAPI.ClassifyMaskVisibility(renderers));
        }

        [Fact]
        public void EveryHolderInactive_IsHidden_TheIntentionalHide()
        {
            // The one hide the culler never performs: a pack/progression SetActive(false) on the
            // holders (activeInHierarchy = false on every renderer). Nothing draws and it is not a
            // cull -> drop the mask so a hidden building leaves no flat patch behind.
            var renderers = new List<Sample>
            {
                new Sample(enabled: true, activeInHierarchy: false, forceRenderingOff: false),
                new Sample(enabled: true, activeInHierarchy: false, forceRenderingOff: false),
            };

            Assert.Equal(Visibility.Hidden, MapAPI.ClassifyMaskVisibility(renderers));
        }

        [Fact]
        public void SomeHolderStillActive_EvenIfAllDisabled_KeepsMask()
        {
            // As long as ANY holder is still active, the building is present and the culler merely
            // stopped drawing it -> keep the mask. Only an ALL-inactive set is the intentional hide.
            var renderers = new List<Sample>
            {
                new Sample(enabled: false, activeInHierarchy: false, forceRenderingOff: false),
                new Sample(enabled: false, activeInHierarchy: true, forceRenderingOff: false),
            };

            Assert.Equal(Visibility.Indeterminate, MapAPI.ClassifyMaskVisibility(renderers));
        }

        [Fact]
        public void CullerForceRenderingOff_ButEnabledAndActive_IsVisible_SoMaskIsKept()
        {
            // The game culler can also park a resident model with forceRenderingOff = true while
            // leaving enabled/active set. That is NOT an intentional hide: the mask must stay so a
            // culled/streamed building keeps its terrain contribution (the point of decoupling).
            var renderers = new List<Sample>
            {
                new Sample(enabled: true, activeInHierarchy: true, forceRenderingOff: true),
            };

            Assert.Equal(Visibility.Visible, MapAPI.ClassifyMaskVisibility(renderers));
        }

        [Fact]
        public void DisabledAndInactive_IsHidden()
        {
            // No active holder at all -> the intentional-hide path (drop).
            var renderers = new List<Sample>
            {
                new Sample(enabled: false, activeInHierarchy: false, forceRenderingOff: true),
            };

            Assert.Equal(Visibility.Hidden, MapAPI.ClassifyMaskVisibility(renderers));
        }

        // --- Retention: ResolveEffectiveMaskVisibility (the watcher's "hold last state" logic) ---
        // (Parameterless Facts: the internal enum can't be a parameter on a public xUnit method.)

        [Fact]
        public void ResolveEffective_DecisiveVisible_WinsOverRetainedHidden()
        {
            Assert.Equal(Visibility.Visible, MapAPI.ResolveEffectiveMaskVisibility(Visibility.Visible, Visibility.Hidden));
        }

        [Fact]
        public void ResolveEffective_DecisiveHidden_WinsOverRetainedVisible()
        {
            Assert.Equal(Visibility.Hidden, MapAPI.ResolveEffectiveMaskVisibility(Visibility.Hidden, Visibility.Visible));
        }

        [Fact]
        public void ResolveEffective_Indeterminate_RetainsHidden()
        {
            // Key regression: a hidden building that streams out (no renderers) must NOT get its mask back.
            Assert.Equal(Visibility.Hidden, MapAPI.ResolveEffectiveMaskVisibility(Visibility.Indeterminate, Visibility.Hidden));
        }

        [Fact]
        public void ResolveEffective_Indeterminate_RetainsVisible()
        {
            Assert.Equal(Visibility.Visible, MapAPI.ResolveEffectiveMaskVisibility(Visibility.Indeterminate, Visibility.Visible));
        }

        [Theory]
        [InlineData("BryShop4")]
        [InlineData("bridge-clear-variant-2")]
        [InlineData("Whittier/Depot")]
        public void PollStaggerBucket_IsStableAndWithinSchedulerRange(string sceneryId)
        {
            var first = FuseDecoupledMaskVisibilityWatcher.GetPollStaggerBucket(sceneryId);
            var second = FuseDecoupledMaskVisibilityWatcher.GetPollStaggerBucket(sceneryId);

            Assert.InRange(first, 0, 63);
            Assert.Equal(first, second);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void PollStaggerBucket_MissingIdUsesFirstBucket(string sceneryId)
        {
            Assert.Equal(0, FuseDecoupledMaskVisibilityWatcher.GetPollStaggerBucket(sceneryId));
        }

        [Fact]
        public void MaskActiveLifecycle_CullKeepsMask_IntentionalHideDropsIt()
        {
            // Walk the watcher's decision pipeline (ClassifyMaskVisibility -> ResolveEffective...)
            // across a building's lifecycle. Visible == mask applied; Hidden == mask dropped. The
            // seed is Visible (the decouple default: applied until proven hidden).
            var last = Visibility.Visible;

            // Loads visible -> mask applied.
            last = Step(last, S(enabled: true, active: true, forceOff: false));
            Assert.Equal(Visibility.Visible, last);

            // Distance-culled at load: culler clears renderer.enabled while holders stay active ->
            // KEEP. This is the bug the fix targets (a far building streaming in must not drop its
            // terrain flatten).
            last = Step(last, S(enabled: false, active: true, forceOff: false));
            Assert.Equal(Visibility.Visible, last);

            // Off-screen near the building (same culler signal) -> still KEEP.
            last = Step(last, S(enabled: false, active: true, forceOff: false));
            Assert.Equal(Visibility.Visible, last);

            // Pack hides it via SetActive(false) (every holder inactive) -> mask dropped.
            last = Step(last, S(enabled: true, active: false, forceOff: false));
            Assert.Equal(Visibility.Hidden, last);

            // Streams out while hidden (no renderers) -> stays dropped (regression: no re-flatten).
            last = Step(last);
            Assert.Equal(Visibility.Hidden, last);

            // Streams back in, still hidden (inactive) -> still dropped.
            last = Step(last, S(enabled: true, active: false, forceOff: false));
            Assert.Equal(Visibility.Hidden, last);

            // Pack shows it again -> mask restored.
            last = Step(last, S(enabled: true, active: true, forceOff: false));
            Assert.Equal(Visibility.Visible, last);

            // Game culler parks it (forceRenderingOff) -> mask STAYS applied (not an intentional hide).
            last = Step(last, S(enabled: true, active: true, forceOff: true));
            Assert.Equal(Visibility.Visible, last);

            // Streams out while visible -> mask stays applied.
            last = Step(last);
            Assert.Equal(Visibility.Visible, last);
        }

        // One decision step: classify the renderer snapshot, then fold it into the retained state,
        // exactly as FuseDecoupledMaskVisibilityWatcher.Tick does. No args => no renderers (streamed out).
        private static Visibility Step(Visibility last, params Sample[] renderers)
        {
            return MapAPI.ResolveEffectiveMaskVisibility(MapAPI.ClassifyMaskVisibility(renderers), last);
        }

        private static Sample S(bool enabled, bool active, bool forceOff)
        {
            return new Sample(enabled, active, forceOff);
        }
    }
}
