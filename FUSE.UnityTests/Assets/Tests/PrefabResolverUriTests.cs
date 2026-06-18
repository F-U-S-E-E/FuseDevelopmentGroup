using System;
using FUSE.Runtime.API;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FUSE.UnityTests
{
    /// <summary>
    /// EditMode coverage for <see cref="FusePrefabResolver.Resolve(string)"/>
    /// — the URI entry point that dispatches by scheme to the inner
    /// path / scenery / vanilla resolvers. FindChildIntegrationTests
    /// covers the deep <c>ResolveScenePath</c> walk; this suite covers
    /// the scheme parser, argument validation, and the
    /// <c>FindRootObject</c> case-insensitive fallback that
    /// ResolveScenePath uses for root-level lookups.
    ///
    /// FusePrefabResolver is internal; FUSE.csproj's
    /// InternalsVisibleTo to <c>FUSE.UnityTests.Tests</c> lets the
    /// asmdef call the static entry points directly.
    /// </summary>
    public class PrefabResolverUriTests
    {
        private GameObject _world;

        [SetUp]
        public void SetUp()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            _world = new GameObject("World");
            var largeScenery = new GameObject("Large Scenery");
            largeScenery.transform.SetParent(_world.transform, worldPositionStays: false);
            var bryson = new GameObject("Bryson");
            bryson.transform.SetParent(largeScenery.transform, worldPositionStays: false);
            var depot = new GameObject("Depot");
            depot.transform.SetParent(bryson.transform, worldPositionStays: false);
            depot.AddComponent<MeshRenderer>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null)
            {
                UnityEngine.Object.DestroyImmediate(_world);
            }
        }

        [Test]
        public void Resolve_EmptyScheme_ReturnsBrandNewGameObject()
        {
            // The "empty://" scheme is FUSE's escape hatch for
            // scene-clone sources that want an empty container without
            // referencing any actual prefab. It must always return a
            // brand-new GameObject, not null.
            var result = FusePrefabResolver.Resolve("empty://whatever-the-path-is");
            try
            {
                Assert.NotNull(result);
                Assert.AreEqual("empty", result.name);
                Assert.IsNull(result.transform.parent,
                    "empty:// must return an unparented GameObject — callers control reparenting.");
            }
            finally
            {
                if (result != null)
                {
                    UnityEngine.Object.DestroyImmediate(result);
                }
            }
        }

        [Test]
        public void Resolve_EmptyScheme_IsCaseInsensitive()
        {
            // Authors writing manifest YAML may capitalise the scheme
            // differently; case-insensitive scheme parsing matches the
            // implementation's StringComparison.OrdinalIgnoreCase.
            var result = FusePrefabResolver.Resolve("EMPTY://anything");
            try
            {
                Assert.NotNull(result);
            }
            finally
            {
                if (result != null)
                {
                    UnityEngine.Object.DestroyImmediate(result);
                }
            }
        }

        [Test]
        public void Resolve_PathScheme_ReturnsResolvedScenePath()
        {
            // path:// goes through ResolveScenePath against the
            // current scene's root GameObjects. Asserting equality
            // with our fixture's Depot proves the dispatch reached
            // the underlying walker.
            var depot = GameObject.Find("World/Large Scenery/Bryson/Depot");
            Assert.NotNull(depot, "Sanity: fixture must contain Depot.");

            var resolved = FusePrefabResolver.Resolve("path://World/Large Scenery/Bryson/Depot");
            Assert.AreSame(depot, resolved);
        }

        [Test]
        public void Resolve_PathScheme_StripsScenePrefix()
        {
            // The "scene/" prefix on path:// URIs is optional —
            // both "path://World/..." and "path://scene/World/..."
            // must resolve to the same GameObject. This is the
            // contract some manifest authors rely on for clarity.
            var depot = GameObject.Find("World/Large Scenery/Bryson/Depot");
            Assert.NotNull(depot);

            var withoutPrefix = FusePrefabResolver.Resolve("path://World/Large Scenery/Bryson/Depot");
            var withPrefix = FusePrefabResolver.Resolve("path://scene/World/Large Scenery/Bryson/Depot");
            Assert.AreSame(depot, withoutPrefix);
            Assert.AreSame(depot, withPrefix);
        }

        [Test]
        public void Resolve_PathScheme_NonexistentPath_ReturnsNull()
        {
            var resolved = FusePrefabResolver.Resolve("path://World/Large Scenery/Bryson/NoSuchObject");
            Assert.IsNull(resolved);
        }

        [Test]
        public void Resolve_UnknownScheme_Throws()
        {
            // An unrecognised scheme must throw rather than silently
            // returning null — a typoed scheme like "scennery://" or
            // "vanilaa://" otherwise produces a phantom missing-target
            // failure later in the pipeline with no diagnostic.
            Assert.Throws<ArgumentException>(() =>
                FusePrefabResolver.Resolve("not-a-scheme://foo"));
        }

        [Test]
        public void Resolve_MissingSchemeSeparator_Throws()
        {
            // No "://" separator at all means we can't parse the
            // string as a URI — fail loudly.
            Assert.Throws<ArgumentException>(() =>
                FusePrefabResolver.Resolve("just-a-name"));
        }

        [Test]
        public void Resolve_NullOrBlank_Throws()
        {
            Assert.Throws<ArgumentException>(() => FusePrefabResolver.Resolve(null));
            Assert.Throws<ArgumentException>(() => FusePrefabResolver.Resolve(string.Empty));
            Assert.Throws<ArgumentException>(() => FusePrefabResolver.Resolve("   "));
        }

        [Test]
        public void ResolveScenePath_BlankInput_ReturnsNull()
        {
            // The deep walker has its own null/blank guard — it must
            // return null (not throw) because callers like
            // FindRemovableSceneClone use it as a probe.
            Assert.IsNull(FusePrefabResolver.ResolveScenePath(null));
            Assert.IsNull(FusePrefabResolver.ResolveScenePath(string.Empty));
            Assert.IsNull(FusePrefabResolver.ResolveScenePath("   "));
        }

        [Test]
        public void ResolveScenePath_RootOnly_ReturnsRootGameObject()
        {
            // A path that's only a root segment must return the root
            // GameObject directly without trying to walk children.
            var resolved = FusePrefabResolver.ResolveScenePath("World");
            Assert.AreSame(_world, resolved);
        }

        [Test]
        public void ResolveScenePath_RootName_PrefersExactCaseMatch()
        {
            // Two scene roots that differ only in case: the resolver
            // must return the exact-case match (matches the
            // FindRootObject contract that an exact name wins over a
            // case-insensitive fallback).
            var lowercaseWorld = new GameObject("world");
            try
            {
                var resolved = FusePrefabResolver.ResolveScenePath("world");
                Assert.AreSame(lowercaseWorld, resolved,
                    "Exact-case root match must win over the case-different sibling.");
            }
            finally
            {
                if (lowercaseWorld != null)
                {
                    UnityEngine.Object.DestroyImmediate(lowercaseWorld);
                }
            }
        }

        [Test]
        public void ResolveScenePath_RootName_FallsBackToCaseInsensitive_WhenNoExact()
        {
            // If no root has the exact name, the resolver falls back
            // to a case-insensitive root match. This is the
            // tolerance that lets authors type "world" when the scene
            // has "World".
            var resolved = FusePrefabResolver.ResolveScenePath("WORLD");
            Assert.AreSame(_world, resolved,
                "Case-insensitive root fallback must surface 'World' for the input 'WORLD'.");
        }
    }
}
