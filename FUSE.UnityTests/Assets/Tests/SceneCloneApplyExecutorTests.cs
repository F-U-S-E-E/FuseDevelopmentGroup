using FUSE.Runtime.API;
using FUSE.Authoring.Data;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FUSE.UnityTests
{
    /// <summary>
    /// EditMode tests that drive <see cref="SceneCloneAPI"/> against
    /// real <see cref="Transform"/> trees. These complement the
    /// xUnit suite in <c>FUSE.Tests/</c>: that suite proves the
    /// planner computes the right plan, this suite proves the
    /// executor actually walks the plan against live Unity APIs.
    ///
    /// The Bryson Freight House regression we shipped — a vanilla
    /// scenery wrapper at local (202.36, 1.0, 210.45) being silently
    /// zeroed by an enabled-only mandela — is the headline test here.
    /// If this case ever turns red again, the executor or its
    /// abstractions have regressed.
    /// </summary>
    public class SceneCloneApplyExecutorTests
    {
        // We build the same scene-path skeleton vanilla Railroader
        // ships: World/Large Scenery/Bryson/Freight House. The
        // production scene path resolver walks SceneManager root
        // GameObjects by name, so the test fixture has to literally
        // create that hierarchy under a fresh EditMode scene.
        private const string Root = "World";
        private const string SceneryGroup = "Large Scenery";
        private const string Town = "Bryson";
        private const string Leaf = "Freight House";

        private GameObject _world;
        private GameObject _largeScenery;
        private GameObject _bryson;
        private GameObject _freightHouse;

        [SetUp]
        public void SetUp()
        {
            // Fresh empty scene per test so leftover GameObjects from a
            // previous test cannot influence FUSE's path resolver.
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            _world = new GameObject(Root);
            _largeScenery = new GameObject(SceneryGroup);
            _largeScenery.transform.SetParent(_world.transform, worldPositionStays: false);
            _bryson = new GameObject(Town);
            _bryson.transform.SetParent(_largeScenery.transform, worldPositionStays: false);
            // The Bryson container's vanilla world position. Children's
            // local positions are interpreted relative to this, so
            // setting it here lets us reason about world-space exactly
            // the way the production code does.
            _bryson.transform.localPosition = new Vector3(4200f, 528f, 5200f);

            _freightHouse = new GameObject(Leaf);
            _freightHouse.transform.SetParent(_bryson.transform, worldPositionStays: false);
            // The exact vanilla local position from level6 (pathID=2868)
            // that the Bryson Freight House ships with. Production
            // tests inspecting world position should see (4402.36,
            // 529.0, 5410.45) here when local + parent compose.
            _freightHouse.transform.localPosition = new Vector3(202.36f, 1.0f, 210.45f);
            // A renderer marks the wrapper as content-bearing so the
            // FindChild disambiguation prefers it over any empty
            // sibling — though this fixture only has one child named
            // "Freight House", so disambiguation is exercised
            // explicitly in FindChildIntegrationTests.cs.
            _freightHouse.AddComponent<MeshRenderer>();
        }

        [TearDown]
        public void TearDown()
        {
            // EditMode scenes are not auto-cleaned between tests; do it
            // by hand so artifacts from a failed test don't haunt the
            // next one.
            if (_world != null)
            {
                Object.DestroyImmediate(_world);
            }
        }

        /// <summary>
        /// The headline regression: <c>{ "enabled": true }</c> on a
        /// vanilla scenery wrapper must NOT move the wrapper. We
        /// shipped the opposite of this on 2026-05-24.
        /// </summary>
        [Test]
        public void EnabledOnlyMandela_OnVanillaWrapper_LeavesLocalPositionUntouched()
        {
            // The wrapper's vanilla local position before FUSE touches it.
            var beforePos = _freightHouse.transform.localPosition;
            Assert.AreEqual(202.36f, beforePos.x, delta: 0.001f);
            Assert.AreEqual(210.45f, beforePos.z, delta: 0.001f);

            SceneCloneAPI.AddSceneClone("test-freight-house-enabled-only", new FuseSceneClone
            {
                TargetPath = $"{Root}/{SceneryGroup}/{Town}/{Leaf}",
                Enabled = true
                // LocalPosition deliberately omitted — this is the
                // regression case.
            });

            // The wrapper's local position MUST be identical to what we
            // set in SetUp. If FUSE zeroed it, this test catches the
            // regression we shipped.
            var afterPos = _freightHouse.transform.localPosition;
            Assert.AreEqual(beforePos.x, afterPos.x, delta: 0.001f,
                "enabled-only mandela must not modify localPosition.x");
            Assert.AreEqual(beforePos.y, afterPos.y, delta: 0.001f,
                "enabled-only mandela must not modify localPosition.y");
            Assert.AreEqual(beforePos.z, afterPos.z, delta: 0.001f,
                "enabled-only mandela must not modify localPosition.z");
        }

        [Test]
        public void EnabledOnlyMandela_KeepsTargetActive()
        {
            // The mandela's intent: target stays visible / active.
            // Sanity check that we're not over-correcting and breaking
            // the activeness contract too.
            SceneCloneAPI.AddSceneClone("test-freight-house-active", new FuseSceneClone
            {
                TargetPath = $"{Root}/{SceneryGroup}/{Town}/{Leaf}",
                Enabled = true
            });

            Assert.IsTrue(_freightHouse.activeSelf,
                "enabled:true mandela must leave the target active");
        }

        [Test]
        public void EnabledFalseMandela_DeactivatesTarget()
        {
            // Counterpart: enabled:false must hide it.
            SceneCloneAPI.AddSceneClone("test-freight-house-inactive", new FuseSceneClone
            {
                TargetPath = $"{Root}/{SceneryGroup}/{Town}/{Leaf}",
                Enabled = false
            });

            Assert.IsFalse(_freightHouse.activeSelf,
                "enabled:false mandela must SetActive(false) the target");
        }

        [Test]
        public void EnabledFalseMandela_StillDoesNotModifyTransform()
        {
            // Hiding a target must not move it either — a future
            // re-enable should restore it at the vanilla position.
            var beforePos = _freightHouse.transform.localPosition;

            SceneCloneAPI.AddSceneClone("test-freight-house-hide-no-move", new FuseSceneClone
            {
                TargetPath = $"{Root}/{SceneryGroup}/{Town}/{Leaf}",
                Enabled = false
            });

            var afterPos = _freightHouse.transform.localPosition;
            Assert.AreEqual(beforePos.x, afterPos.x, delta: 0.001f);
            Assert.AreEqual(beforePos.y, afterPos.y, delta: 0.001f);
            Assert.AreEqual(beforePos.z, afterPos.z, delta: 0.001f);
        }

        [Test]
        public void MandelaWithLocalPosition_OverridesTargetLocalPosition()
        {
            // Author explicitly authors a position — executor must
            // honour it.
            var authored = new Vector3(123.4f, 56.7f, -89f);

            SceneCloneAPI.AddSceneClone("test-freight-house-moved", new FuseSceneClone
            {
                TargetPath = $"{Root}/{SceneryGroup}/{Town}/{Leaf}",
                Enabled = true,
                LocalPosition = authored
            });

            var afterPos = _freightHouse.transform.localPosition;
            Assert.AreEqual(authored.x, afterPos.x, delta: 0.001f);
            Assert.AreEqual(authored.y, afterPos.y, delta: 0.001f);
            Assert.AreEqual(authored.z, afterPos.z, delta: 0.001f);
        }

        [Test]
        public void MandelaWithLocalScale_OverridesOnlyScale_NotPosition()
        {
            // Partial-override: scale only. The vanilla local position
            // must survive — that's the regression the planner unit
            // tests cover, but only at the planner level. This locks
            // the executor's behaviour against the same partial-set.
            var beforePos = _freightHouse.transform.localPosition;
            var authoredScale = new Vector3(2f, 2f, 2f);

            SceneCloneAPI.AddSceneClone("test-freight-house-scaled", new FuseSceneClone
            {
                TargetPath = $"{Root}/{SceneryGroup}/{Town}/{Leaf}",
                Enabled = true,
                LocalScale = authoredScale
            });

            // Scale applied.
            var afterScale = _freightHouse.transform.localScale;
            Assert.AreEqual(2f, afterScale.x, delta: 0.001f);
            // Position untouched.
            var afterPos = _freightHouse.transform.localPosition;
            Assert.AreEqual(beforePos.x, afterPos.x, delta: 0.001f);
            Assert.AreEqual(beforePos.z, afterPos.z, delta: 0.001f);
        }

        [Test]
        public void EnabledUnsetMandela_LeavesBothPositionAndActiveStateUntouched()
        {
            // Omitting "enabled" altogether: planner emits
            // SetActiveState.HasValue == false. Executor must NOT
            // call SetActive — the target stays at whatever state
            // (active or not) it was before.
            var wasActive = _freightHouse.activeSelf;
            var beforePos = _freightHouse.transform.localPosition;

            SceneCloneAPI.AddSceneClone("test-freight-house-touchless", new FuseSceneClone
            {
                TargetPath = $"{Root}/{SceneryGroup}/{Town}/{Leaf}",
                // Enabled deliberately null.
            });

            Assert.AreEqual(wasActive, _freightHouse.activeSelf);
            Assert.AreEqual(beforePos.x, _freightHouse.transform.localPosition.x, delta: 0.001f);
            Assert.AreEqual(beforePos.z, _freightHouse.transform.localPosition.z, delta: 0.001f);
        }
    }
}
