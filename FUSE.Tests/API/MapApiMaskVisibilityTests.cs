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
    /// need different mask handling: a building that is drawing (keep mask), one a pack has hidden
    /// at the renderer level (drop mask), and one merely streamed out or culled (keep mask). The
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
        public void AllRenderersDisabled_IsHidden()
        {
            // The canonical intentional hide: renderer.enabled cleared on every renderer.
            var renderers = new List<Sample>
            {
                new Sample(enabled: false, activeInHierarchy: true, forceRenderingOff: false),
                new Sample(enabled: false, activeInHierarchy: true, forceRenderingOff: false),
            };

            Assert.Equal(Visibility.Hidden, MapAPI.ClassifyMaskVisibility(renderers));
        }

        [Fact]
        public void AllRenderersOnInactiveObjects_IsHidden()
        {
            // Hide via a child SetActive(false): renderers stay enabled but are not in an active
            // hierarchy, so nothing draws — treat as an intentional hide.
            var renderers = new List<Sample>
            {
                new Sample(enabled: true, activeInHierarchy: false, forceRenderingOff: false),
                new Sample(enabled: true, activeInHierarchy: false, forceRenderingOff: false),
            };

            Assert.Equal(Visibility.Hidden, MapAPI.ClassifyMaskVisibility(renderers));
        }

        [Fact]
        public void CullerForceRenderingOff_ButEnabledAndActive_IsVisible_SoMaskIsKept()
        {
            // The game culler parks a resident model with forceRenderingOff = true while leaving
            // enabled/active set. That is NOT an intentional hide: the mask must stay so a
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
            // Both hide signals at once still resolves to hidden.
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

        [Fact]
        public void MaskActiveLifecycle_HiddenSurvivesStreamOut_AndCullerKeepsMask()
        {
            // Walk the watcher's decision pipeline (ClassifyMaskVisibility -> ResolveEffective...)
            // across a building's lifecycle. Visible == mask applied; Hidden == mask dropped. The
            // seed is Visible (the decouple default: applied until proven hidden).
            var last = Visibility.Visible;

            // Loads visible -> mask applied.
            last = Step(last, S(enabled: true, active: true, forceOff: false));
            Assert.Equal(Visibility.Visible, last);

            // Pack hides it (all renderers disabled) -> mask dropped.
            last = Step(last, S(enabled: false, active: true, forceOff: false));
            Assert.Equal(Visibility.Hidden, last);

            // Streams out while hidden (no renderers) -> stays dropped (regression: no re-flatten).
            last = Step(last);
            Assert.Equal(Visibility.Hidden, last);

            // Streams back in, still hidden -> still dropped.
            last = Step(last, S(enabled: false, active: true, forceOff: false));
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
