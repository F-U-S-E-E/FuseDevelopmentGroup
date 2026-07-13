using System;
using System.Collections.Generic;
using System.IO;
using FUSE.Loading;
using Xunit;

namespace FUSE.Tests.Loading
{
    public sealed class FuseAssetPackStoreReuseTests
    {
        [Fact]
        public void NormalizePhysicalPath_CollapsesRelativeSegmentsAndTrailingSeparators()
        {
            var root = TestPath("Normalize");
            var canonical = Path.Combine(root, "Pack");
            var equivalent = Path.Combine(root, "Nested", "..", "Pack") + Path.DirectorySeparatorChar;

            Assert.Equal(
                FuseAssetPackRegistry.NormalizeAssetPackPhysicalPath(canonical),
                FuseAssetPackRegistry.NormalizeAssetPackPhysicalPath(equivalent),
                StringComparer.OrdinalIgnoreCase);
        }

        [Fact]
        public void NormalizePhysicalPath_PreservesFilesystemRootAndRejectsInvalidInput()
        {
            var root = Path.GetPathRoot(Path.GetTempPath());

            Assert.Equal(Path.GetFullPath(root), FuseAssetPackRegistry.NormalizeAssetPackPhysicalPath(root));
            Assert.Null(FuseAssetPackRegistry.NormalizeAssetPackPhysicalPath(null));
            Assert.Null(FuseAssetPackRegistry.NormalizeAssetPackPhysicalPath("   "));
            Assert.Null(FuseAssetPackRegistry.NormalizeAssetPackPhysicalPath("bad\0path"));
        }

        [Fact]
        public void PhysicalPathIndex_FirstRegistrationForFolderWins()
        {
            var folder = TestPath("SharedPack");
            var index = new FuseAssetPackRegistry.AssetPackStoreRegistrationIndex();

            Assert.True(index.Observe("AssetLoader/SharedPack", folder));
            Assert.False(index.Observe("fuseasset://duplicate", folder + Path.DirectorySeparatorChar));

            Assert.Equal(
                "AssetLoader/SharedPack",
                FuseAssetPackRegistry.SelectStoreIdentifierForPhysicalPath(
                    folder,
                    index.ReusableIdentifiersByNormalizedPath,
                    "fuseasset://fallback"));
        }

        [Fact]
        public void DuplicateExactIdentifier_OnDifferentPath_DoesNotReuseLaterPath()
        {
            var first = TestPath("DuplicateIdFirst");
            var later = TestPath("DuplicateIdLater");
            var index = new FuseAssetPackRegistry.AssetPackStoreRegistrationIndex();

            Assert.True(index.Observe("Owner/Child", first));
            Assert.False(index.Observe("Owner/Child", later));

            var plan = FuseAssetPackRegistry.PlanStoreRegistration(
                later,
                index,
                "fuseasset://later");
            Assert.Equal(FuseAssetPackRegistry.AssetPackStoreRegistrationAction.AddDirect, plan.Action);
            Assert.Equal("fuseasset://later", plan.SelectedIdentifier);
        }

        [Fact]
        public void UnknownFirstPath_BlocksLaterStoreWithSameExactIdentifier()
        {
            var later = TestPath("UnknownFirstLater");
            var index = new FuseAssetPackRegistry.AssetPackStoreRegistrationIndex();

            Assert.False(index.Observe("Owner/Child", null));
            Assert.False(index.Observe("Owner/Child", later));

            var plan = FuseAssetPackRegistry.PlanStoreRegistration(
                later,
                index,
                "fuseasset://later");
            Assert.Equal(FuseAssetPackRegistry.AssetPackStoreRegistrationAction.AddDirect, plan.Action);
        }

        [Fact]
        public void DuplicateExactIdentifier_OnSamePath_RemainsReusable()
        {
            var folder = TestPath("SameIdSamePath");
            var index = new FuseAssetPackRegistry.AssetPackStoreRegistrationIndex();

            Assert.True(index.Observe("Owner/Child", folder));
            Assert.False(index.Observe("Owner/Child", folder + Path.DirectorySeparatorChar));

            var plan = FuseAssetPackRegistry.PlanStoreRegistration(
                folder,
                index,
                "fuseasset://fallback");
            Assert.Equal(FuseAssetPackRegistry.AssetPackStoreRegistrationAction.ReuseExisting, plan.Action);
            Assert.Equal("Owner/Child", plan.SelectedIdentifier);
        }

        [Fact]
        public void LaterUniqueIdentifier_CanSafelyOwnDuplicateIdentifiersLaterPath()
        {
            var first = TestPath("DuplicateFirst");
            var later = TestPath("DuplicateLaterUnique");
            var index = new FuseAssetPackRegistry.AssetPackStoreRegistrationIndex();

            Assert.True(index.Observe("Duplicated/Id", first));
            Assert.False(index.Observe("Duplicated/Id", later));
            Assert.True(index.Observe("Unique/Id", later));

            var plan = FuseAssetPackRegistry.PlanStoreRegistration(
                later,
                index,
                "fuseasset://fallback");
            Assert.Equal(FuseAssetPackRegistry.AssetPackStoreRegistrationAction.ReuseExisting, plan.Action);
            Assert.Equal("Unique/Id", plan.SelectedIdentifier);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void RegistrationPlan_RejectsDirectIdentifierOwnedByAnotherStore(bool unresolvedOwnerPath)
        {
            var directIdentifier = "fuseasset://occupied";
            var index = new FuseAssetPackRegistry.AssetPackStoreRegistrationIndex();
            index.Observe(
                directIdentifier,
                unresolvedOwnerPath ? null : TestPath("ExistingDirectStore"));

            var plan = FuseAssetPackRegistry.PlanStoreRegistration(
                TestPath("CandidateDirectStore"),
                index,
                directIdentifier);

            Assert.Equal(FuseAssetPackRegistry.AssetPackStoreRegistrationAction.IdentifierConflict, plan.Action);
            Assert.Equal(directIdentifier, plan.SelectedIdentifier);
        }

        [Fact]
        public void IdentifierOwnership_IsCaseSensitiveWhilePathsAreCaseInsensitive()
        {
            var upperPath = TestPath("UpperIdentifier");
            var lowerPath = TestPath("LowerIdentifier");
            var index = new FuseAssetPackRegistry.AssetPackStoreRegistrationIndex();

            Assert.True(index.Observe("Owner/Child", upperPath));
            Assert.True(index.Observe("owner/child", lowerPath));

            Assert.Equal(
                "Owner/Child",
                FuseAssetPackRegistry.SelectStoreIdentifierForPhysicalPath(
                    upperPath.ToUpperInvariant(),
                    index.ReusableIdentifiersByNormalizedPath,
                    "fallback"));
            Assert.Equal(
                "owner/child",
                FuseAssetPackRegistry.SelectStoreIdentifierForPhysicalPath(
                    lowerPath.ToLowerInvariant(),
                    index.ReusableIdentifiersByNormalizedPath,
                    "fallback"));
        }

        [Fact]
        public void ExistingIdentifier_IsPreservedExactlyWhileAliasKeyNormalizesBackslashes()
        {
            var folder = TestPath("BackslashIdentifier");
            var index = new FuseAssetPackRegistry.AssetPackStoreRegistrationIndex();

            Assert.True(index.Observe(@"Owner\Child", folder));
            var selected = FuseAssetPackRegistry.SelectStoreIdentifierForPhysicalPath(
                folder,
                index.ReusableIdentifiersByNormalizedPath,
                "fallback");

            Assert.Equal(@"Owner\Child", selected);
            Assert.Equal("Owner/Child", FuseAssetPackRegistry.NormalizeLegacyAssetPackIdentifier(selected));
        }

        [Fact]
        public void RegistrationPlan_AddsToSecondPrefabStoreEvenWhenIdentifierWasHistoricallyTracked()
        {
            var folder = TestPath("SecondPrefabStore");
            var directIdentifier = "fuseasset://historically-tracked";
            var historicallyTracked = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                directIdentifier
            };

            Assert.Contains(directIdentifier, historicallyTracked);
            var plan = FuseAssetPackRegistry.PlanStoreRegistration(
                folder,
                new FuseAssetPackRegistry.AssetPackStoreRegistrationIndex(),
                directIdentifier);

            Assert.Equal(FuseAssetPackRegistry.AssetPackStoreRegistrationAction.AddDirect, plan.Action);
            Assert.Equal(directIdentifier, plan.SelectedIdentifier);
        }

        [Fact]
        public void RegistrationPlan_ReusesCurrentPrefabStoresExactIdentifier()
        {
            var folder = TestPath("CurrentPrefabStore");
            var index = new FuseAssetPackRegistry.AssetPackStoreRegistrationIndex();
            index.Observe(@"AssetLoader.Owner\Child", folder);

            var plan = FuseAssetPackRegistry.PlanStoreRegistration(
                folder + Path.DirectorySeparatorChar,
                index,
                "fuseasset://fallback");

            Assert.Equal(FuseAssetPackRegistry.AssetPackStoreRegistrationAction.ReuseExisting, plan.Action);
            Assert.Equal(@"AssetLoader.Owner\Child", plan.SelectedIdentifier);
        }

        [Fact]
        public void RegistrationPlan_AddsNestedPackMissingFromCurrentStores()
        {
            var nestedFolder = Path.Combine(TestPath("NestedMod"), "SCAssetPacks", "NestedPack");

            var plan = FuseAssetPackRegistry.PlanStoreRegistration(
                nestedFolder,
                new FuseAssetPackRegistry.AssetPackStoreRegistrationIndex(),
                "fuseasset://nested-pack");

            Assert.Equal(FuseAssetPackRegistry.AssetPackStoreRegistrationAction.AddDirect, plan.Action);
            Assert.Equal("fuseasset://nested-pack", plan.SelectedIdentifier);
        }

        [Fact]
        public void AliasSelection_IgnoresBlankExistingIdentifier()
        {
            var folder = TestPath("BlankIdentifierPack");
            var normalized = FuseAssetPackRegistry.NormalizeAssetPackPhysicalPath(folder);
            var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [normalized] = "  "
            };

            Assert.Equal(
                "fuseasset://fallback",
                FuseAssetPackRegistry.SelectStoreIdentifierForPhysicalPath(
                    folder,
                    index,
                    "fuseasset://fallback"));
        }

        private static string TestPath(string leaf)
        {
            return Path.Combine(
                Path.GetTempPath(),
                "FuseStoreReuse",
                Guid.NewGuid().ToString("N"),
                leaf);
        }
    }
}
