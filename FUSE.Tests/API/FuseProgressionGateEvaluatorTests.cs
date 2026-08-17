using FUSE.Runtime.API;
using Xunit;

namespace FUSE.Tests.API
{
    /// <summary>
    /// Pins the pure locked-feature gate decision behind the deferred-scenery fix: the
    /// post-load activation wave must not re-show a prop a locked progression feature
    /// just hid. <see cref="FuseProgressionGateEvaluator.IsHiddenByLockedGate"/> is the
    /// decision; the live <c>MapFeature</c>/<c>MapFeatureManager</c> mapping that feeds it
    /// is covered by the EditMode integration test.
    ///
    /// Cases model the real ALW East Whittier shape: the "William Elk Phase 1"
    /// (<c>ElkStage1</c>) feature gates the factory plus four <c>WoodDock50M</c> decking
    /// objects via <c>gameObjectsEnableOnUnlock</c>; while it is locked every one must
    /// report hidden, and once unlocked none may. Plain objects stand in for GameObjects
    /// so the locked-vs-unlocked filter and reference-identity match are verified without
    /// a Unity scene.
    /// </summary>
    public class FuseProgressionGateEvaluatorTests
    {
        private static FuseProgressionGateEvaluator.Gate Locked(params object[] gatedObjects) =>
            new FuseProgressionGateEvaluator.Gate(unlocked: false, gatedObjects);

        private static FuseProgressionGateEvaluator.Gate Unlocked(params object[] gatedObjects) =>
            new FuseProgressionGateEvaluator.Gate(unlocked: true, gatedObjects);

        [Fact]
        public void LockedFeatureGatingObject_IsHidden()
        {
            // The bug: a WoodDock gated by still-locked Phase 1 must stay hidden.
            var dock = new object();
            Assert.True(FuseProgressionGateEvaluator.IsHiddenByLockedGate(dock, new[] { Locked(dock) }));
        }

        [Fact]
        public void UnlockedFeatureGatingObject_IsNotHidden()
        {
            // After Phase 1 unlocks, the same dock must be allowed to show again.
            var dock = new object();
            Assert.False(FuseProgressionGateEvaluator.IsHiddenByLockedGate(dock, new[] { Unlocked(dock) }));
        }

        [Fact]
        public void ObjectGatedByNoFeature_IsNotHidden()
        {
            // The ungated East Whittier crib: absent from every gate -> always visible.
            var crib = new object();
            var dock = new object();
            Assert.False(FuseProgressionGateEvaluator.IsHiddenByLockedGate(crib, new[] { Locked(dock) }));
        }

        [Fact]
        public void OneLockedFeatureGatingManyObjects_HidesEach()
        {
            // ElkStage1 lists the factory + four WoodDock50M decks in a single feature.
            var factory = new object();
            var dockA = new object();
            var dockB = new object();
            var dockC = new object();
            var dockD = new object();
            var gates = new[] { Locked(factory, dockA, dockB, dockC, dockD) };

            Assert.True(FuseProgressionGateEvaluator.IsHiddenByLockedGate(factory, gates));
            Assert.True(FuseProgressionGateEvaluator.IsHiddenByLockedGate(dockA, gates));
            Assert.True(FuseProgressionGateEvaluator.IsHiddenByLockedGate(dockB, gates));
            Assert.True(FuseProgressionGateEvaluator.IsHiddenByLockedGate(dockC, gates));
            Assert.True(FuseProgressionGateEvaluator.IsHiddenByLockedGate(dockD, gates));
        }

        [Fact]
        public void ObjectHeldByAnyLockedFeature_IsHidden_EvenIfAnotherFeatureUnlockedIt()
        {
            // If any locked feature still holds the object it stays hidden, even when a
            // second (unlocked) feature also lists it.
            var shared = new object();
            var gates = new[] { Unlocked(shared), Locked(shared) };
            Assert.True(FuseProgressionGateEvaluator.IsHiddenByLockedGate(shared, gates));
        }

        [Fact]
        public void MixedFeatures_OnlyLockedHeldObjectsAreHidden()
        {
            var visibleProp = new object();
            var hiddenProp = new object();
            var gates = new[] { Unlocked(visibleProp), Locked(hiddenProp) };

            Assert.False(FuseProgressionGateEvaluator.IsHiddenByLockedGate(visibleProp, gates));
            Assert.True(FuseProgressionGateEvaluator.IsHiddenByLockedGate(hiddenProp, gates));
        }

        [Fact]
        public void MatchesByReferenceIdentity_NotByValueEquality()
        {
            // Two distinct instances are never interchangeable — mirrors per-GameObject
            // SetActive gating, where a different object that "looks the same" is not gated.
            var gatedDock = new object();
            var lookalike = new object();
            Assert.False(FuseProgressionGateEvaluator.IsHiddenByLockedGate(lookalike, new[] { Locked(gatedDock) }));
        }

        [Fact]
        public void NullTarget_IsNotHidden()
        {
            Assert.False(FuseProgressionGateEvaluator.IsHiddenByLockedGate(null, new[] { Locked(new object()) }));
        }

        [Fact]
        public void NullGateSet_IsNotHidden()
        {
            Assert.False(FuseProgressionGateEvaluator.IsHiddenByLockedGate(new object(), null));
        }

        [Fact]
        public void LockedGateWithNullObjectList_IsHandledAndHidesNothing()
        {
            // A freshly created MapFeature can carry a null gate array; the core must not
            // throw and must not hide anything for it.
            var prop = new object();
            var gates = new[] { new FuseProgressionGateEvaluator.Gate(unlocked: false, gatedObjects: null) };
            Assert.False(FuseProgressionGateEvaluator.IsHiddenByLockedGate(prop, gates));
        }

        [Fact]
        public void EmptyGateSet_IsNotHidden()
        {
            Assert.False(FuseProgressionGateEvaluator.IsHiddenByLockedGate(new object(), System.Array.Empty<FuseProgressionGateEvaluator.Gate>()));
        }

    }
}
