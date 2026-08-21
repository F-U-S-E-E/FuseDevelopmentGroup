using FUSE.Interface.MenuWindow;
using FUSE.Authoring.Data;
using FUSE.Loading;
using FUSE.Runtime.Registry;
using Xunit;

namespace FUSE.Tests.Interface.MenuWindow
{
    [Collection("FuseRegistry")]
    public sealed class ModConflictsToolPageTests
    {
        public ModConflictsToolPageTests()
        {
            FuseRegistry.Reset();
        }

        [Fact]
        public void BuildGroups_groups_reverse_ownership_records_under_one_package_pair()
        {
            FuseRegistry.RecordPlannedConflict(
                FuseClaimKind.Segment,
                "segment-a",
                "package-z",
                "package-a",
                "later package definition won");
            FuseRegistry.RecordPlannedConflict(
                FuseClaimKind.Node,
                "node-b",
                "package-a",
                "package-z",
                "later package removal won");

            var group = Assert.Single(ModConflictsToolPage.BuildGroups(FuseRegistry.Conflicts));

            Assert.Equal("package-a", group.FirstPackageId);
            Assert.Equal("package-z", group.SecondPackageId);
            Assert.Equal(2, group.Conflicts.Length);
        }

        [Fact]
        public void BuildReport_identifies_spatial_overlap_and_lists_both_packages()
        {
            FuseRegistry.RecordPlannedConflict(
                FuseClaimKind.Segment,
                "spatial-overlap:east-whittier",
                "yard-revamp",
                "crossover",
                "spatial track overlap detected; both packages retained");

            var report = ModConflictsToolPage.BuildReport(
                ModConflictsToolPage.BuildGroups(FuseRegistry.Conflicts));

            Assert.Contains("crossover  <->  yard-revamp", report);
            Assert.Contains("Potential track-layout overlap", report);
            Assert.Contains("spatial-overlap:east-whittier", report);
        }

        [Fact]
        public void BuildGroups_collapses_definition_fragments_to_their_discovered_mod_ids()
        {
            FuseRegistry.RecordPlannedConflict(
                FuseClaimKind.Segment,
                "segment-a",
                "Author.RouteA.FUSE.graph",
                "Author.RouteB.FUSE.track",
                "later package definition won");
            FuseRegistry.RecordPlannedConflict(
                FuseClaimKind.Span,
                "span-b",
                "Author.RouteA.FUSE.operations",
                "Author.RouteB.FUSE.spans",
                "later package definition won");

            var group = Assert.Single(ModConflictsToolPage.BuildGroups(
                FuseRegistry.Conflicts,
                new[] { "Author.RouteA.FUSE", "Author.RouteB.FUSE" }));

            Assert.Equal("Author.RouteA.FUSE", group.FirstPackageId);
            Assert.Equal("Author.RouteB.FUSE", group.SecondPackageId);
            Assert.Equal(2, group.Conflicts.Length);
        }

        [Fact]
        public void DeclaredConflicts_are_reported_separately_from_runtime_ownership()
        {
            var declaring = new FusePackageManifestSnapshot
            {
                Id = "Katers.Route.FUSE",
                Version = "1.0",
                ConflictsWith = new[]
                {
                    new FuseModRequirement { Id = "Other.Route", NotBefore = "2.0" }
                }
            };
            var conflicting = new FusePackageManifestSnapshot
            {
                Id = "Other.Route.FUSE",
                Version = "2.5"
            };

            var matches = ModConflictsToolPage.BuildDeclaredConflictMatches(new[] { declaring, conflicting });
            var match = Assert.Single(matches);
            var report = ModConflictsToolPage.BuildReport(
                System.Linq.Enumerable.Empty<ModConflictsToolPage.ConflictGroup>(),
                matches);

            Assert.Equal("Katers.Route.FUSE", match.DeclaringPackage.Id);
            Assert.Contains("Author-declared incompatibilities: 1", report);
            Assert.Contains("DECLARED: Katers.Route.FUSE  X  Other.Route.FUSE", report);
            Assert.DoesNotContain("Conflict records: 1", report);
        }

        [Fact]
        public void DeclaredConflicts_ignore_disabled_declaring_package()
        {
            var declaring = new FusePackageManifestSnapshot
            {
                Id = "Katers.Route.FUSE",
                Disabled = true,
                ConflictsWith = new[]
                {
                    new FuseModRequirement { Id = "Other.Route" }
                }
            };
            var conflicting = new FusePackageManifestSnapshot
            {
                Id = "Other.Route.FUSE",
                Version = "2.5"
            };

            var matches = ModConflictsToolPage.BuildDeclaredConflictMatches(new[] { declaring, conflicting });

            Assert.Empty(matches);
        }

        [Fact]
        public void Successful_shared_industry_merge_is_informational_not_actionable()
        {
            FuseRegistry.RecordPlannedConflict(
                FuseClaimKind.Industry,
                "component:kirkland-mine.KirkOBSTrackLoad",
                "CF.AndrewsCoalPower.FUSE.kirklandcoalpatchpower-migration",
                "Katers.TuckasegeeSteelWorks.FUSE.kirklandcoal",
                "shared industry destination overlap; definitions merged into the same runtime location");

            var record = Assert.Single(FuseRegistry.Conflicts);
            Assert.True(record.IsCooperativeMerge);

            var sharedGroups = ModConflictsToolPage.BuildGroups(
                new[] { record },
                new[] { "CF.AndrewsCoalPower.FUSE", "Katers.TuckasegeeSteelWorks.FUSE" });
            var report = ModConflictsToolPage.BuildReport(
                System.Linq.Enumerable.Empty<ModConflictsToolPage.ConflictGroup>(),
                System.Linq.Enumerable.Empty<ModConflictsToolPage.DeclaredConflictMatch>(),
                sharedGroups);

            Assert.Contains("Package pairs needing attention: 0", report);
            Assert.Contains("Informational shared-extension records: 1", report);
            Assert.Contains("SHARED: CF.AndrewsCoalPower.FUSE  +  Katers.TuckasegeeSteelWorks.FUSE", report);
            Assert.Contains("no mod lost content", report);
        }
    }
}
