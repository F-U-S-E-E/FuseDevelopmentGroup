using FUSE.API;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FUSE.UnityTests
{
    /// <summary>
    /// Integration tests for the duplicate-named-sibling
    /// disambiguation in <see cref="FUSE.API.FusePrefabResolver"/>.
    /// The pure resolver in <see cref="FuseFindChildResolver"/> is
    /// already pinned by xUnit tests against a fake candidate list;
    /// this suite verifies the live <see cref="Transform"/>-walking
    /// wrapper builds the right candidate list and routes through the
    /// resolver correctly.
    ///
    /// Without this layer, a bug in the wrapper's
    /// <see cref="Transform.GetChild"/> walk / name comparison /
    /// HasSceneContent classification would silently produce a wrong
    /// candidate list — the resolver would happily pick from it and
    /// return a wrong-but-internally-consistent answer.
    ///
    /// FusePrefabResolver itself is internal; FUSE.csproj grants
    /// InternalsVisibleTo to <c>FUSE.UnityTests.Tests</c> so the
    /// asmdef can call the static entry point directly.
    /// </summary>
    public class FindChildIntegrationTests
    {
        private GameObject _root;

        [SetUp]
        public void SetUp()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            _root = new GameObject("World");
            var largeScenery = new GameObject("Large Scenery");
            largeScenery.transform.SetParent(_root.transform, worldPositionStays: false);
            var bryson = new GameObject("Bryson");
            bryson.transform.SetParent(largeScenery.transform, worldPositionStays: false);
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                Object.DestroyImmediate(_root);
            }
        }

        [Test]
        public void DuplicateNamedSiblings_PicksTheOneWithRenderer()
        {
            // The exact runtime shape that bit us with the Bryson
            // Freight House before the FindChild content-aware fix:
            // two siblings sharing a leaf name, only one of them
            // carrying scenery content. Production resolver MUST
            // prefer the content-bearing one.
            var bryson = GameObject.Find("World/Large Scenery/Bryson").transform;

            // Empty placeholder added first — Unity's vanilla
            // Transform.Find would land on it.
            var empty = new GameObject("Freight House");
            empty.transform.SetParent(bryson, worldPositionStays: false);
            // Content-bearing sibling added second.
            var withContent = new GameObject("Freight House");
            withContent.transform.SetParent(bryson, worldPositionStays: false);
            withContent.AddComponent<MeshRenderer>();

            var resolved = FusePrefabResolver.ResolveScenePath("World/Large Scenery/Bryson/Freight House");

            Assert.AreSame(withContent, resolved,
                "Resolver must pick the renderer-bearing sibling over the empty placeholder.");
        }

        [Test]
        public void DuplicateNamedSiblings_BothEmpty_PicksFirst()
        {
            // No content to disambiguate by — fall back to sibling
            // order (matches Unity's prior Transform.Find contract).
            var bryson = GameObject.Find("World/Large Scenery/Bryson").transform;

            var first = new GameObject("Freight House");
            first.transform.SetParent(bryson, worldPositionStays: false);
            var second = new GameObject("Freight House");
            second.transform.SetParent(bryson, worldPositionStays: false);

            var resolved = FusePrefabResolver.ResolveScenePath("World/Large Scenery/Bryson/Freight House");
            Assert.AreSame(first, resolved, "Tie-break must favour earlier siblings.");
        }

        [Test]
        public void SingleSiblingWithoutContent_StillResolves()
        {
            // The "intermediate empty container" case — e.g.
            // World/Large Scenery itself is an empty Transform-only
            // node. Resolver must not refuse to walk it just because
            // it has no content.
            var bryson = GameObject.Find("World/Large Scenery/Bryson").transform;
            var only = new GameObject("Freight House");
            only.transform.SetParent(bryson, worldPositionStays: false);

            var resolved = FusePrefabResolver.ResolveScenePath("World/Large Scenery/Bryson/Freight House");
            Assert.AreSame(only, resolved);
        }

        [Test]
        public void NoMatchingChild_ReturnsNull()
        {
            var resolved = FusePrefabResolver.ResolveScenePath("World/Large Scenery/Bryson/Nonexistent");
            Assert.IsNull(resolved);
        }

        [Test]
        public void CaseInsensitiveMatch_UsedOnlyAsFallback()
        {
            // Two siblings: one with a case-insensitive-only match,
            // one with no match at all. The resolver should pick the
            // case-insensitive one rather than returning null.
            var bryson = GameObject.Find("World/Large Scenery/Bryson").transform;
            var caseDifferent = new GameObject("freight house");
            caseDifferent.transform.SetParent(bryson, worldPositionStays: false);

            var resolved = FusePrefabResolver.ResolveScenePath("World/Large Scenery/Bryson/Freight House");
            Assert.AreSame(caseDifferent, resolved,
                "When no exact match exists, the resolver must fall back to a case-insensitive match.");
        }

        [Test]
        public void ExactMatchBeatsCaseInsensitive_EvenWhenExactIsEmpty()
        {
            // Exact match wins over case-insensitive match
            // regardless of content. This honours the contract that
            // an author's literal scene path takes priority over the
            // case-tolerance fallback.
            var bryson = GameObject.Find("World/Large Scenery/Bryson").transform;
            var caseDifferentWithContent = new GameObject("freight house");
            caseDifferentWithContent.transform.SetParent(bryson, worldPositionStays: false);
            caseDifferentWithContent.AddComponent<MeshRenderer>();
            var exactEmpty = new GameObject("Freight House");
            exactEmpty.transform.SetParent(bryson, worldPositionStays: false);

            var resolved = FusePrefabResolver.ResolveScenePath("World/Large Scenery/Bryson/Freight House");
            Assert.AreSame(exactEmpty, resolved,
                "An exact-name match — even an empty placeholder — must outrank a case-insensitive match with content.");
        }
    }
}
