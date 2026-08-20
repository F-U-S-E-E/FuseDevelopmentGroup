using FUSE.Authoring.Data;
using FUSE.Loading;
using Xunit;

namespace FUSE.Tests.Loading
{
    public sealed class FuseDeclaredPackageRelationshipTests
    {
        [Fact]
        public void Requires_marks_later_override_as_expected_when_resolved_order_agrees()
        {
            var existing = Snapshot(1, "Katers.SylvaInterchange.FUSE");
            var later = Snapshot(2, "Katers.SylvaInterchangeHYSpans.FUSE");
            later.RequiredPackageIds = new[] { "Katers.SylvaInterchange" };

            Assert.True(FuseDeclaredPackageRelationship.IsExpectedLaterOverride(existing, later));
        }

        [Fact]
        public void LoadAfter_object_id_alias_marks_later_override_as_expected()
        {
            var existing = Snapshot(3, "Katers.SylvaInterchange.FUSE");
            var later = Snapshot(4, "Katers.SylvaInterchangeHYSpans.FUSE");
            later.LoadAfter = new[] { "Katers.SylvaInterchange.FUSE" };

            Assert.True(FuseDeclaredPackageRelationship.IsExpectedLaterOverride(existing, later));
        }

        [Fact]
        public void LoadBefore_on_base_marks_later_override_as_expected()
        {
            var existing = Snapshot(1, "base.route");
            var later = Snapshot(2, "extension.route");
            existing.LoadBefore = new[] { "extension.route" };

            Assert.True(FuseDeclaredPackageRelationship.IsExpectedLaterOverride(existing, later));
        }

        [Fact]
        public void Conditional_mixinto_requirement_marks_later_override_as_expected()
        {
            var existing = Snapshot(1, "base.route");
            var later = Snapshot(2, "extension.route");
            var definition = new FuseModDefinition
            {
                Mixinto = new FuseMixintoDefinition
                {
                    Requires = new[] { new FuseModRequirement { Id = "base.route" } }
                }
            };

            Assert.True(FuseDeclaredPackageRelationship.IsExpectedLaterOverride(
                existing,
                later,
                laterDefinition: definition));
        }

        [Fact]
        public void Declaration_does_not_hide_conflict_when_resolved_order_contradicts_it()
        {
            var existing = Snapshot(4, "base.route");
            var later = Snapshot(2, "extension.route");
            later.LoadAfter = new[] { "base.route" };

            Assert.False(FuseDeclaredPackageRelationship.IsExpectedLaterOverride(existing, later));
        }

        [Fact]
        public void Unrelated_packages_remain_conflicts()
        {
            Assert.False(FuseDeclaredPackageRelationship.IsExpectedLaterOverride(
                Snapshot(1, "yard-a"),
                Snapshot(2, "yard-b")));
        }

        private static FusePackageManifestSnapshot Snapshot(int order, string id)
        {
            return new FusePackageManifestSnapshot { Order = order, Id = id };
        }
    }
}
