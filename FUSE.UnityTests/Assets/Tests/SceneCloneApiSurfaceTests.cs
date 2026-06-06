using System.Linq;
using FUSE.Runtime.API;
using FUSE.Authoring.Data;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FUSE.UnityTests
{
    /// <summary>
    /// EditMode coverage for the rest of the <see cref="SceneCloneAPI"/>
    /// public surface — Add/Update/Remove round-trips, lookup helpers,
    /// validation throws, the snapshot-restore reapply path, and the
    /// disabled-on-enable marker guard. SceneCloneApplyExecutorTests
    /// covers the apply pipeline's transform-mutation contract; this
    /// suite covers everything else that only Unity engine state can
    /// exercise.
    ///
    /// All tests share the vanilla-bind path (Source == null) so they
    /// don't need a real prefab to clone. The CloneFromSource branch
    /// has its own apply-pipeline coverage in
    /// SceneCloneApplyExecutorTests; this file deliberately stays on
    /// the annotate-existing-target side of the planner because that's
    /// where the lookup / lifecycle / validation logic lives.
    /// </summary>
    public class SceneCloneApiSurfaceTests
    {
        private const string Root = "World";
        private const string SceneryGroup = "Large Scenery";
        private const string Town = "Bryson";
        private const string Leaf = "Freight House";
        private const string SecondLeaf = "Coaling Tower";

        private GameObject _world;
        private GameObject _freightHouse;
        private GameObject _coalingTower;

        [SetUp]
        public void SetUp()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            _world = new GameObject(Root);
            var largeScenery = new GameObject(SceneryGroup);
            largeScenery.transform.SetParent(_world.transform, worldPositionStays: false);
            var bryson = new GameObject(Town);
            bryson.transform.SetParent(largeScenery.transform, worldPositionStays: false);
            bryson.transform.localPosition = new Vector3(4200f, 528f, 5200f);

            _freightHouse = new GameObject(Leaf);
            _freightHouse.transform.SetParent(bryson.transform, worldPositionStays: false);
            _freightHouse.transform.localPosition = new Vector3(202.36f, 1.0f, 210.45f);
            _freightHouse.AddComponent<MeshRenderer>();

            _coalingTower = new GameObject(SecondLeaf);
            _coalingTower.transform.SetParent(bryson.transform, worldPositionStays: false);
            _coalingTower.AddComponent<MeshRenderer>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null)
            {
                Object.DestroyImmediate(_world);
            }
        }

        private static string TargetOf(string leaf) => $"{Root}/{SceneryGroup}/{Town}/{leaf}";

        [Test]
        public void AddSceneClone_BlankId_Throws()
        {
            // Blank id must be rejected before any side effect — a
            // marker without an id would be impossible to look up or
            // remove later.
            Assert.Throws<System.ArgumentException>(() =>
                SceneCloneAPI.AddSceneClone("   ", new FuseSceneClone { TargetPath = TargetOf(Leaf), Enabled = true }));
        }

        [Test]
        public void AddSceneClone_NullDefinition_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() =>
                SceneCloneAPI.AddSceneClone("surface-test-null-definition", null));
        }

        [Test]
        public void AddSceneClone_MissingTargetPath_Throws()
        {
            // The planner is happy with a blank TargetPath, but the
            // executor's ApplyDefinition rejects it up front because
            // there's nothing to bind to.
            Assert.Throws<System.InvalidOperationException>(() =>
                SceneCloneAPI.AddSceneClone("surface-test-blank-target", new FuseSceneClone { TargetPath = "  ", Enabled = true }));
        }

        [Test]
        public void AddSceneClone_DuplicateId_Throws()
        {
            // The id is the primary key for the marker registry — a
            // duplicate must fail loudly rather than overwrite silently
            // (callers wanting update semantics use UpdateSceneClone).
            const string id = "surface-test-duplicate-id";
            SceneCloneAPI.AddSceneClone(id, new FuseSceneClone { TargetPath = TargetOf(Leaf), Enabled = true });

            Assert.Throws<System.InvalidOperationException>(() =>
                SceneCloneAPI.AddSceneClone(id, new FuseSceneClone { TargetPath = TargetOf(Leaf), Enabled = true }));
        }

        [Test]
        public void GetSceneClone_AfterAdd_ReturnsAnnotatedTarget()
        {
            const string id = "surface-test-get-roundtrip";
            SceneCloneAPI.AddSceneClone(id, new FuseSceneClone { TargetPath = TargetOf(Leaf), Enabled = true });

            var found = SceneCloneAPI.GetSceneClone(id);
            Assert.AreSame(_freightHouse, found,
                "GetSceneClone must return the vanilla GameObject the marker was attached to.");
        }

        [Test]
        public void GetSceneClone_CaseInsensitiveId_Matches()
        {
            // The marker's id comparison uses OrdinalIgnoreCase so
            // authors typing the same id with inconsistent casing
            // across files don't silently produce two phantom clones.
            const string id = "surface-test-case-insensitive";
            SceneCloneAPI.AddSceneClone(id, new FuseSceneClone { TargetPath = TargetOf(Leaf), Enabled = true });

            var found = SceneCloneAPI.GetSceneClone(id.ToUpperInvariant());
            Assert.AreSame(_freightHouse, found);
        }

        [Test]
        public void GetSceneClone_UnknownId_ReturnsNull()
        {
            Assert.IsNull(SceneCloneAPI.GetSceneClone("surface-test-no-such-id"));
            Assert.IsNull(SceneCloneAPI.GetSceneClone("   "));
            Assert.IsNull(SceneCloneAPI.GetSceneClone(null));
        }

        [Test]
        public void GetAllSceneClones_EnumeratesEveryMarker()
        {
            const string firstId = "surface-test-enum-first";
            const string secondId = "surface-test-enum-second";
            SceneCloneAPI.AddSceneClone(firstId, new FuseSceneClone { TargetPath = TargetOf(Leaf), Enabled = true });
            SceneCloneAPI.AddSceneClone(secondId, new FuseSceneClone { TargetPath = TargetOf(SecondLeaf), Enabled = true });

            var all = SceneCloneAPI.GetAllSceneClones().ToList();
            Assert.Contains(_freightHouse, all);
            Assert.Contains(_coalingTower, all);
            Assert.AreEqual(2, all.Count,
                "GetAllSceneClones must surface every marker-bearing GameObject in the scene.");
        }

        [Test]
        public void TryGetSceneCloneInfo_ForMarkerBearingObject_ReturnsIdAndTarget()
        {
            const string id = "surface-test-try-get-info";
            var target = TargetOf(Leaf);
            SceneCloneAPI.AddSceneClone(id, new FuseSceneClone { TargetPath = target, Enabled = true });

            var ok = SceneCloneAPI.TryGetSceneCloneInfo(_freightHouse, out var foundId, out var foundTarget);
            Assert.IsTrue(ok);
            Assert.AreEqual(id, foundId);
            Assert.AreEqual(target, foundTarget);
        }

        [Test]
        public void TryGetSceneCloneInfo_ForUnmarkedObject_ReturnsFalse()
        {
            // A vanilla GameObject without a FUSE marker must report
            // false so diagnostic callers don't fabricate fake ids for
            // base-game objects under the cursor.
            var ok = SceneCloneAPI.TryGetSceneCloneInfo(_coalingTower, out var id, out var target);
            Assert.IsFalse(ok);
            Assert.IsNull(id);
            Assert.IsNull(target);
        }

        [Test]
        public void TryGetSceneCloneInfo_NullGameObject_ReturnsFalse()
        {
            var ok = SceneCloneAPI.TryGetSceneCloneInfo(null, out var id, out var target);
            Assert.IsFalse(ok);
            Assert.IsNull(id);
            Assert.IsNull(target);
        }

        [Test]
        public void GetDefinition_ReflectsLiveTransformState()
        {
            // GetDefinition is the "what does the live scene currently
            // say about this clone?" reader the authoring UI uses to
            // populate edit forms. It must read the live transform,
            // not just echo the cached definition — otherwise a user
            // who nudged the object in the scene view would see the
            // pre-nudge values when reopening the form.
            const string id = "surface-test-get-definition-live";
            SceneCloneAPI.AddSceneClone(id, new FuseSceneClone { TargetPath = TargetOf(Leaf), Enabled = true });

            // Mutate live state AFTER the clone is bound.
            _freightHouse.transform.localPosition = new Vector3(11f, 22f, 33f);
            _freightHouse.transform.localScale = new Vector3(3f, 3f, 3f);

            var definition = SceneCloneAPI.GetSceneCloneDefinition(id);
            Assert.NotNull(definition);
            Assert.AreEqual(11f, definition.LocalPosition.Value.x, delta: 0.001f);
            Assert.AreEqual(33f, definition.LocalPosition.Value.z, delta: 0.001f);
            Assert.AreEqual(3f, definition.LocalScale.Value.x, delta: 0.001f);
            Assert.AreEqual(true, definition.Enabled);
            Assert.AreEqual(TargetOf(Leaf), definition.TargetPath);
        }

        [Test]
        public void UpdateSceneClone_RewritesTransformOverrides()
        {
            // Update is the API the editor calls when a user edits an
            // existing scene clone. It must NOT throw the
            // "already exists" guard that AddSceneClone enforces, and
            // it must apply the new overrides to the bound transform.
            const string id = "surface-test-update-overrides";
            SceneCloneAPI.AddSceneClone(id, new FuseSceneClone { TargetPath = TargetOf(Leaf), Enabled = true });

            SceneCloneAPI.UpdateSceneClone(id, new FuseSceneClone
            {
                TargetPath = TargetOf(Leaf),
                Enabled = true,
                LocalPosition = new Vector3(99f, 0.5f, -100f)
            });

            var afterPos = _freightHouse.transform.localPosition;
            Assert.AreEqual(99f, afterPos.x, delta: 0.001f);
            Assert.AreEqual(-100f, afterPos.z, delta: 0.001f);
        }

        [Test]
        public void TryRemoveSceneClone_OnExistingClone_ReturnsTrue_AndDropsIdFromLookup()
        {
            const string id = "surface-test-try-remove-existing";
            SceneCloneAPI.AddSceneClone(id, new FuseSceneClone { TargetPath = TargetOf(SecondLeaf), Enabled = true });

            var ok = SceneCloneAPI.TryRemoveSceneClone(id);
            Assert.IsTrue(ok);
            // Object.Destroy is deferred in EditMode, so we don't
            // assert the GameObject itself is null — instead assert
            // the public contract: GetSceneClone no longer surfaces
            // the id, because the marker is either destroyed or
            // marker.gameObject == null (Unity's fake-null on the
            // pending-destroy reference).
            Assert.IsNull(SceneCloneAPI.GetSceneClone(id));
        }

        [Test]
        public void TryRemoveSceneClone_OnMissingId_ReturnsFalse_WithoutThrowing()
        {
            // The Try-prefixed variant is the "no exception" contract
            // — the FUSE world-removal pipeline calls this once per
            // package mandela on uninstall, and a missing clone
            // (already removed, never installed) must not crash the
            // uninstall.
            var ok = SceneCloneAPI.TryRemoveSceneClone("surface-test-no-such-clone");
            Assert.IsFalse(ok);
        }

        [Test]
        public void RemoveSceneClone_OnMissingId_Throws()
        {
            // The non-Try variant is the imperative API — callers who
            // believe a clone exists want a loud failure if it
            // doesn't, not a silent no-op.
            Assert.Throws<System.InvalidOperationException>(() =>
                SceneCloneAPI.RemoveSceneClone("surface-test-no-such-clone-imperative"));
        }

        [Test]
        public void ReapplyEnabledFromCache_RestoresDisabledState_WhenCacheDivergesFromLive()
        {
            // The snapshot-restore path: a savegame load reactivates
            // every GameObject in the world, but FUSE-disabled clones
            // must come back disabled. ReapplyEnabledFromCache walks
            // every marker, consults the cached definition, and
            // re-applies the desired active state.
            //
            // To exercise the divergence-correction without fighting
            // the marker's OnEnable guard (which has its own test
            // below), we set up a clone that's currently active and
            // then poke the cache directly to say it SHOULD be
            // disabled. Reapply must reconcile to the cache.
            const string id = "surface-test-reapply-cache-wins";
            SceneCloneAPI.AddSceneClone(id, new FuseSceneClone { TargetPath = TargetOf(Leaf), Enabled = true });
            Assert.IsTrue(_freightHouse.activeSelf,
                "Sanity: AddSceneClone with Enabled=true leaves the target active.");

            // Rewrite the cache behind the marker's back to model a
            // saved-state snapshot whose recorded mandela is
            // disabled, even though the live scene came up active.
            FuseRuntimeDefinitionCache.Store(FuseDefinitionKind.SceneClone, id,
                new FuseSceneClone { TargetPath = TargetOf(Leaf), Enabled = false });

            var reapplied = SceneCloneAPI.ReapplyEnabledFromCache("unit-test");
            Assert.That(reapplied, Is.GreaterThanOrEqualTo(1),
                "ReapplyEnabledFromCache must report at least one clone whose live state was flipped.");
            Assert.IsFalse(_freightHouse.activeSelf,
                "After reapply, the cached Enabled=false must dominate the live active state.");
        }

        [Test]
        public void ReapplyEnabledFromCache_LeavesCloneAlone_WhenCachedEnabledIsNull()
        {
            // The early-return contract: a definition with Enabled
            // null is a "don't touch active state" mandela. Reapply
            // must skip those entirely — neither flipping the live
            // state nor counting it as reapplied.
            const string id = "surface-test-reapply-null-enabled-noop";
            SceneCloneAPI.AddSceneClone(id, new FuseSceneClone { TargetPath = TargetOf(Leaf) /* Enabled deliberately null */ });
            var wasActive = _freightHouse.activeSelf;

            var reapplied = SceneCloneAPI.ReapplyEnabledFromCache("unit-test-noop");
            Assert.AreEqual(0, reapplied,
                "Clones with cached Enabled=null must not be counted as reapplied.");
            Assert.AreEqual(wasActive, _freightHouse.activeSelf,
                "Clones with cached Enabled=null must keep their pre-reapply active state.");
        }

        [Test]
        public void MarkerGuard_AutoRedisables_AfterManualReactivate()
        {
            // The FuseSceneCloneMarker.OnEnable guard is the in-scene
            // failsafe that catches a SetActive(true) from any source
            // (savegame restore, SetActive call from another mod, etc.)
            // on a clone the package author wanted hidden. The cached
            // DesiredEnabled = false on the marker drives the guard;
            // we test it by adding a disabled clone, force-activating
            // the bound GameObject, and asserting the guard bounces it
            // back to inactive synchronously.
            const string id = "surface-test-marker-guard";
            SceneCloneAPI.AddSceneClone(id, new FuseSceneClone { TargetPath = TargetOf(Leaf), Enabled = false });
            Assert.IsFalse(_freightHouse.activeSelf);

            _freightHouse.SetActive(true);

            Assert.IsFalse(_freightHouse.activeSelf,
                "Marker's OnEnable guard must re-deactivate a clone the author disabled.");
        }
    }
}
