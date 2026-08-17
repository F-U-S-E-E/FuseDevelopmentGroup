using System;
using System.Collections.Generic;
using System.Linq;
using FUSE.Patches;
using Newtonsoft.Json;
using Xunit;

namespace FUSE.Tests.Patches
{
    public class FuseSceneryAssetManagerEditorMenuPatchTests
    {
        [Fact]
        public void FilterDirectOnlyIdentifiersForEditorMenu_RemovesOnlyDirectOnlyEntries()
        {
            var identifiers = new[] { "local-low-shed", "mod-folder-tree", "local-low-crossing" };
            var directOnly = new HashSet<string> { "mod-folder-tree" };

            var filtered = FuseSceneryAssetManagerEditorMenuPatch.FilterDirectOnlyIdentifiersForEditorMenu(
                identifiers,
                directOnly);

            Assert.Equal(new[] { "local-low-shed", "local-low-crossing" }, filtered);
        }

        [Fact]
        public void FilterDirectOnlyIdentifiersForEditorMenu_PreservesOrderWhenNothingIsDirectOnly()
        {
            var identifiers = new[] { "a", "b", "c" };

            var filtered = FuseSceneryAssetManagerEditorMenuPatch.FilterDirectOnlyIdentifiersForEditorMenu(
                identifiers,
                new HashSet<string>());

            Assert.Equal(identifiers, filtered);
        }

        [Fact]
        public void CollectSceneryIdentifiersSafely_SkipsMalformedStoreAndContinues()
        {
            var stores = new[] { "already-quarantined", "malformed", "later-valid" };
            var probed = new List<string>();
            var quarantined = new List<string>();

            var identifiers =
                FuseSceneryAssetManagerEditorMenuPatch.CollectSceneryIdentifiersSafely(
                    stores,
                    store => store != "already-quarantined",
                    store =>
                    {
                        probed.Add(store);
                        if (store == "malformed")
                        {
                            throw new JsonReaderException("invalid definitions");
                        }

                        return new[] { "z-scenery", "a-scenery" };
                    },
                    (store, _) => quarantined.Add(store));

            Assert.Equal(new[] { "a-scenery", "z-scenery" }, identifiers);
            Assert.Equal(new[] { "malformed", "later-valid" }, probed);
            Assert.Equal(new[] { "malformed" }, quarantined);
        }

        [Fact]
        public void CollectSceneryIdentifiersSafely_PropagatesNonJsonFailure()
        {
            Assert.Throws<InvalidOperationException>(() =>
                FuseSceneryAssetManagerEditorMenuPatch.CollectSceneryIdentifiersSafely(
                    new[] { "opaque" },
                    _ => true,
                    _ => throw new InvalidOperationException("opaque failure"),
                    (_, __) => { }));
        }

        [Fact]
        public void CollectSceneryIdentifiersSafely_MatchesStockDeduplicationAndSorting()
        {
            string[] sourceIdentifiers =
            {
                "duplicate",
                null,
                string.Empty,
                "Duplicate",
                "duplicate",
                "z-scenery",
                "a-scenery"
            };

            var identifiers =
                FuseSceneryAssetManagerEditorMenuPatch.CollectSceneryIdentifiersSafely(
                    new[] { "store" },
                    _ => true,
                    _ => sourceIdentifiers,
                    (_, __) => { });

            var expected = new HashSet<string>(sourceIdentifiers, StringComparer.Ordinal)
                .OrderBy(identifier => identifier)
                .ToList();
            Assert.Equal(expected, identifiers);
            Assert.Single(identifiers, identifier => identifier == "duplicate");
            Assert.Contains("Duplicate", identifiers);
            Assert.Contains(null, identifiers);
            Assert.Contains(string.Empty, identifiers);
        }
    }
}
