using System.Collections.Generic;
using System.Linq;
using FUSE.Patches;
using Xunit;

namespace FUSE.Tests.Patches
{
    /// <summary>
    /// Tests for the pure catalog-vs-bundle diff behind the asset pack bundle
    /// audit (visible via InternalsVisibleTo). Shapes mirror the field case: a
    /// pack whose Catalog.json declared 'aspenbridgeclear' while its bundle
    /// contained no such asset, so every streaming pass near the referencing
    /// scenery burst-failed with nothing naming the pack.
    /// </summary>
    public class FuseAssetPackBundleAuditTests
    {
        private static KeyValuePair<string, string> Declared(string identifier, string filename)
        {
            return new KeyValuePair<string, string>(identifier, filename);
        }

        [Fact]
        public void MissingDeclaredAsset_IsReported()
        {
            var declared = new[] { Declared("aspenbridgeclear", "aspenbridgeclear.prefab") };
            var bundle = new[] { "assets/prefabs/aspensawmill.prefab", "assets/prefabs/aspencape.prefab" };

            var missing = FuseAssetPackBundleAuditPatch.FindMissingDeclaredAssets(declared, bundle);

            var entry = Assert.Single(missing);
            Assert.Equal("aspenbridgeclear", entry.Key);
            Assert.Equal("aspenbridgeclear.prefab", entry.Value);
        }

        [Fact]
        public void AssetPresentByFilename_IsNotReported()
        {
            var declared = new[] { Declared("bridge-clear", "aspenbridgeclear.prefab") };
            var bundle = new[] { "assets/deep/nested/folder/aspenbridgeclear.prefab" };

            Assert.Empty(FuseAssetPackBundleAuditPatch.FindMissingDeclaredAssets(declared, bundle));
        }

        [Fact]
        public void AssetPresentByIdentifierWithoutExtension_IsNotReported()
        {
            // The loader requests assets by identifier and the engine matches by
            // path-less name, so a filename mismatch alone is not a failure.
            var declared = new[] { Declared("aspenbridgeclear", "some-other-file.prefab") };
            var bundle = new[] { "assets/prefabs/aspenbridgeclear.prefab" };

            Assert.Empty(FuseAssetPackBundleAuditPatch.FindMissingDeclaredAssets(declared, bundle));
        }

        [Fact]
        public void Matching_IsCaseInsensitive()
        {
            // Unity lowercases bundle asset paths; catalogs are hand-written.
            var declared = new[] { Declared("AspenBridgeClear", "AspenBridgeClear.PREFAB") };
            var bundle = new[] { "assets/prefabs/aspenbridgeclear.prefab" };

            Assert.Empty(FuseAssetPackBundleAuditPatch.FindMissingDeclaredAssets(declared, bundle));
        }

        [Fact]
        public void BlankIdentifiers_AreSkipped()
        {
            var declared = new[] { Declared("", "orphan.prefab"), Declared("   ", "other.prefab") };
            var bundle = new string[0];

            Assert.Empty(FuseAssetPackBundleAuditPatch.FindMissingDeclaredAssets(declared, bundle));
        }

        [Fact]
        public void BlankFilename_StillMatchesByIdentifier()
        {
            var declared = new[] { Declared("aspenbridgeclear", ""), Declared("gone", null) };
            var bundle = new[] { "assets/prefabs/aspenbridgeclear.prefab" };

            var missing = FuseAssetPackBundleAuditPatch.FindMissingDeclaredAssets(declared, bundle);

            var entry = Assert.Single(missing);
            Assert.Equal("gone", entry.Key);
        }

        [Fact]
        public void EmptyOrNullInputs_ReportNothing()
        {
            Assert.Empty(FuseAssetPackBundleAuditPatch.FindMissingDeclaredAssets(
                new List<KeyValuePair<string, string>>(), new[] { "assets/a.prefab" }));
            Assert.Empty(FuseAssetPackBundleAuditPatch.FindMissingDeclaredAssets(null, new[] { "assets/a.prefab" }));
            var declared = new[] { Declared("a", "a.prefab") };
            Assert.Single(FuseAssetPackBundleAuditPatch.FindMissingDeclaredAssets(declared, null));
        }

        [Fact]
        public void MixedPack_ReportsOnlyTheMissingEntries_InDeclarationOrder()
        {
            var declared = new[]
            {
                Declared("present-by-file", "present.prefab"),
                Declared("gone-one", "gone-one.prefab"),
                Declared("present-by-name", "renamed.prefab"),
                Declared("gone-two", "gone-two.prefab")
            };
            var bundle = new[]
            {
                "assets/x/present.prefab",
                "assets/x/present-by-name.prefab"
            };

            var missing = FuseAssetPackBundleAuditPatch.FindMissingDeclaredAssets(declared, bundle);

            Assert.Equal(new[] { "gone-one", "gone-two" }, missing.Select(entry => entry.Key).ToArray());
        }
    }
}
