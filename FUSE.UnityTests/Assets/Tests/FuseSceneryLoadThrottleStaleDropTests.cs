using FUSE.Patches;
using NUnit.Framework;
using UnityEngine;

namespace FUSE.UnityTests
{
    /// <summary>
    /// Pins the load-throttle pump's stale-drop decision
    /// <see cref="FuseSceneryLoadThrottlePatch.ShouldDropStale"/>: a queued load is
    /// dropped only when its object is beyond
    /// <see cref="FuseSceneryLoadThrottlePatch.StaleLoadDropDistance"/> from the
    /// camera. The comparison direction and the constant both carry a stranding
    /// invariant — an object dropped while still inside the game's outermost
    /// (~1500&#160;m) scenery band never receives another CullingGroup event and stays
    /// invisible until a teleport — so an inverted comparison or a lowered constant
    /// must fail here, not ship.
    /// </summary>
    public class FuseSceneryLoadThrottleStaleDropTests
    {
        private const float Threshold = FuseSceneryLoadThrottlePatch.StaleLoadDropDistance;

        [Test]
        public void WellInsideThreshold_IsNotDropped()
        {
            Assert.IsFalse(
                FuseSceneryLoadThrottlePatch.ShouldDropStale(Vector3.zero, new Vector3(0f, 0f, Threshold * 0.5f)),
                "A queued load half a threshold away must be released, not dropped.");
        }

        [Test]
        public void JustInsideBoundary_IsNotDropped()
        {
            Assert.IsFalse(
                FuseSceneryLoadThrottlePatch.ShouldDropStale(Vector3.zero, new Vector3(0f, 0f, Threshold - 1f)));
        }

        [Test]
        public void ExactlyAtBoundary_IsDropped()
        {
            // The decision is >= threshold, so an object exactly at the threshold
            // drops. Pinned explicitly so the boundary semantics can't silently flip.
            Assert.IsTrue(
                FuseSceneryLoadThrottlePatch.ShouldDropStale(Vector3.zero, new Vector3(0f, 0f, Threshold)));
        }

        [Test]
        public void JustOutsideBoundary_IsDropped()
        {
            Assert.IsTrue(
                FuseSceneryLoadThrottlePatch.ShouldDropStale(Vector3.zero, new Vector3(0f, 0f, Threshold + 1f)));
        }

        [Test]
        public void FarOutsideThreshold_IsDropped()
        {
            Assert.IsTrue(
                FuseSceneryLoadThrottlePatch.ShouldDropStale(Vector3.zero, new Vector3(0f, 0f, Threshold * 2f)));
        }

        [Test]
        public void DistanceIsRelative_NotOriginBased()
        {
            // Both points far from the origin (floating-origin-shifted space); only
            // the camera-to-object delta may matter.
            var camera = new Vector3(12000f, 550f, 7000f);
            var near = camera + new Vector3(100f, 0f, 0f);
            var far = camera + new Vector3(Threshold + 100f, 0f, 0f);
            Assert.IsFalse(FuseSceneryLoadThrottlePatch.ShouldDropStale(camera, near));
            Assert.IsTrue(FuseSceneryLoadThrottlePatch.ShouldDropStale(camera, far));
        }

        [Test]
        public void Threshold_StaysBeyondTheGamesOutermostSceneryBand()
        {
            // The stranding invariant itself: dropping is only safe for objects the
            // culler has in band 3 (beyond ~1500m), with margin for large culling
            // spheres. See the constant's doc comment.
            Assert.Greater(Threshold, 1500f,
                "StaleLoadDropDistance at or below the game's outermost scenery band strands " +
                "dropped scenery invisible until a teleport re-evaluates it.");
        }
    }
}
