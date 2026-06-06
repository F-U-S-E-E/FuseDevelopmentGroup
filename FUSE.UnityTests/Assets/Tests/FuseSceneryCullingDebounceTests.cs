using FUSE.Patches;
using NUnit.Framework;
using UnityEngine;

namespace FUSE.UnityTests
{
    /// <summary>
    /// Deterministic EditMode tests for the issue #76 culling-debounce decision.
    ///
    /// The fix holds FUSE scenery resident within a distance deadband of the camera
    /// (so the game's no-hysteresis ~1500m unload boundary can't thrash load/unload),
    /// and releases it beyond the deadband so genuinely-distant scenery still culls.
    /// The decision is extracted into the pure
    /// <see cref="FuseSceneryCullingDebouncePatch.ShouldHoldResident"/> (reachable
    /// here via FUSE's InternalsVisibleTo) so it can be asserted without a live game,
    /// Harmony, a camera, or asset bundles — i.e. a fast regression guard that the
    /// fix can't silently break (e.g. an inverted comparison would re-introduce the
    /// pop, or pin everything forever).
    /// </summary>
    public class FuseSceneryCullingDebounceTests
    {
        private const float Deadband = FuseSceneryCullingDebouncePatch.UnloadDistance;

        [Test]
        public void WellInsideDeadband_HoldsResident()
        {
            Assert.IsTrue(
                FuseSceneryCullingDebouncePatch.ShouldHoldResident(Vector3.zero, new Vector3(0f, 0f, Deadband * 0.5f)),
                "Scenery within the deadband must be held resident to absorb boundary jitter (no flap).");
        }

        [Test]
        public void WellBeyondDeadband_ReleasesForUnload()
        {
            Assert.IsFalse(
                FuseSceneryCullingDebouncePatch.ShouldHoldResident(Vector3.zero, new Vector3(0f, 0f, Deadband * 2f)),
                "Genuinely-distant scenery must be released so the game unloads it (culling still works).");
        }

        [Test]
        public void JustInsideBoundary_Holds()
        {
            Assert.IsTrue(
                FuseSceneryCullingDebouncePatch.ShouldHoldResident(Vector3.zero, new Vector3(0f, 0f, Deadband - 1f)));
        }

        [Test]
        public void JustOutsideBoundary_Releases()
        {
            Assert.IsFalse(
                FuseSceneryCullingDebouncePatch.ShouldHoldResident(Vector3.zero, new Vector3(0f, 0f, Deadband + 1f)));
        }

        [Test]
        public void DecisionIsRelativeToCamera_NotAbsolutePosition()
        {
            // Distance is what matters, so the decision is floating-origin safe.
            var camera = new Vector3(123456f, 78f, -98765f);
            var near = camera + new Vector3(0f, 0f, 100f);
            var far = camera + new Vector3(0f, 0f, Deadband + 100f);

            Assert.IsTrue(FuseSceneryCullingDebouncePatch.ShouldHoldResident(camera, near));
            Assert.IsFalse(FuseSceneryCullingDebouncePatch.ShouldHoldResident(camera, far));
        }

        // Issue #76 follow-up (fix #2): the debounce holds ONLY already-loaded scenery.
        // Force-holding a not-yet-loaded object inside the deadband would clamp band
        // 3 -> 2 and stream it in, inflating the post-teleport working set to the whole
        // ~3km sphere (~8x the game's load volume) and feeding it through the throttle.
        // ShouldHold gates ShouldHoldResident on the model already being requested.

        [Test]
        public void ShouldHold_NotYetLoaded_DoesNotHold()
        {
            var near = new Vector3(0f, 0f, Deadband * 0.5f);
            Assert.IsFalse(
                FuseSceneryCullingDebouncePatch.ShouldHold(modelLoadRequested: false, Vector3.zero, near),
                "Unloaded scenery inside the deadband must not be force-held (it would force a load, inflating the set).");
        }

        [Test]
        public void ShouldHold_Loaded_WithinDeadband_Holds()
        {
            var near = new Vector3(0f, 0f, Deadband * 0.5f);
            Assert.IsTrue(
                FuseSceneryCullingDebouncePatch.ShouldHold(modelLoadRequested: true, Vector3.zero, near),
                "Already-loaded scenery inside the deadband must be held to absorb boundary jitter (anti-flap).");
        }

        [Test]
        public void ShouldHold_Loaded_BeyondDeadband_Releases()
        {
            var far = new Vector3(0f, 0f, Deadband * 2f);
            Assert.IsFalse(
                FuseSceneryCullingDebouncePatch.ShouldHold(modelLoadRequested: true, Vector3.zero, far),
                "Loaded scenery beyond the deadband must be released so the game unloads it (culling still works).");
        }
    }
}
