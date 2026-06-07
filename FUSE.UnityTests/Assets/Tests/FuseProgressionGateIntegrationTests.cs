using FUSE.Runtime.API;
using Game.Progression;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FUSE.UnityTests
{
    /// <summary>
    /// EditMode integration test for the deferred-scenery progression-lock guard. The
    /// pure gate decision is pinned by xUnit
    /// (<c>FUSE.Tests.API.FuseProgressionGateEvaluatorTests</c>) against fake objects;
    /// this suite verifies the live
    /// <see cref="ProgressionAPI.IsGameObjectHiddenByLockedFeature(GameObject, System.Collections.Generic.IEnumerable{MapFeature})"/>
    /// mapping reads <see cref="MapFeature.Unlocked"/> and
    /// <c>MapFeature.gameObjectsEnableOnUnlock</c> off real components and matches by
    /// GameObject identity.
    ///
    /// This is the exact shape that keeps ALW's East Whittier <c>WoodDock50M</c> decking
    /// hidden while "William Elk Phase 1" (<c>ElkStage1</c>) is locked, and lets it show
    /// once the section unlocks. Without this layer a bug in the MapFeature → gate
    /// mapping (e.g. reading the wrong flag, or comparing by name) would feed the pure
    /// core a correct-but-wrong input and pass silently.
    ///
    /// ProgressionAPI's overload is internal; FUSE.csproj grants InternalsVisibleTo to
    /// <c>FUSE.UnityTests.Tests</c>.
    /// </summary>
    public class FuseProgressionGateIntegrationTests
    {
        private GameObject _root;

        [SetUp]
        public void SetUp()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            _root = new GameObject("ProgressionGateTestRoot");
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                Object.DestroyImmediate(_root);
            }
        }

        private GameObject NewChild(string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(_root.transform, worldPositionStays: false);
            return child;
        }

        private MapFeature NewFeature(string id, bool unlocked, params GameObject[] gatedObjects)
        {
            var feature = NewChild(id).AddComponent<MapFeature>();
            feature.identifier = id;
            feature.gameObjectsEnableOnUnlock = gatedObjects;
            feature.Unlocked = unlocked;
            return feature;
        }

        [Test]
        public void LockedFeature_HidesItsGatedDeck()
        {
            var dock = NewChild("WoodDock50M");
            var feature = NewFeature("ElkStage1", unlocked: false, dock);

            Assert.IsTrue(
                ProgressionAPI.IsGameObjectHiddenByLockedFeature(dock, new[] { feature }),
                "A WoodDock gated by the still-locked William Elk Phase 1 feature must report hidden.");
        }

        [Test]
        public void UnlockedFeature_DoesNotHideItsGatedDeck()
        {
            var dock = NewChild("WoodDock50M");
            var feature = NewFeature("ElkStage1", unlocked: true, dock);

            Assert.IsFalse(
                ProgressionAPI.IsGameObjectHiddenByLockedFeature(dock, new[] { feature }),
                "Once Phase 1 unlocks, the same dock must be allowed to show.");
        }

        [Test]
        public void UngatedObject_IsNotHidden()
        {
            var crib = NewChild("EastWhittierCrib");
            var dock = NewChild("WoodDock50M");
            var feature = NewFeature("ElkStage1", unlocked: false, dock);

            Assert.IsFalse(
                ProgressionAPI.IsGameObjectHiddenByLockedFeature(crib, new[] { feature }),
                "An object no feature gates (the ungated crib) must never be reported hidden.");
        }

        [Test]
        public void LockedFeature_HidesEveryObjectItGates()
        {
            var factory = NewChild("elkphase1");
            var dockA = NewChild("WoodDock50M_A");
            var dockB = NewChild("WoodDock50M_B");
            var feature = NewFeature("ElkStage1", unlocked: false, factory, dockA, dockB);
            var features = new[] { feature };

            Assert.IsTrue(ProgressionAPI.IsGameObjectHiddenByLockedFeature(factory, features));
            Assert.IsTrue(ProgressionAPI.IsGameObjectHiddenByLockedFeature(dockA, features));
            Assert.IsTrue(ProgressionAPI.IsGameObjectHiddenByLockedFeature(dockB, features));
        }

        [Test]
        public void DifferentInstanceSharingLeafName_IsNotHidden()
        {
            var gatedDock = NewChild("WoodDock50M");
            var otherDock = NewChild("WoodDock50M"); // same leaf name, different instance
            var feature = NewFeature("ElkStage1", unlocked: false, gatedDock);

            Assert.IsFalse(
                ProgressionAPI.IsGameObjectHiddenByLockedFeature(otherDock, new[] { feature }),
                "Gating is by GameObject identity, not name — a different instance must not be hidden.");
        }

        [Test]
        public void NullGameObject_IsNotHidden()
        {
            var dock = NewChild("WoodDock50M");
            var feature = NewFeature("ElkStage1", unlocked: false, dock);

            Assert.IsFalse(ProgressionAPI.IsGameObjectHiddenByLockedFeature(null, new[] { feature }));
        }
    }
}
