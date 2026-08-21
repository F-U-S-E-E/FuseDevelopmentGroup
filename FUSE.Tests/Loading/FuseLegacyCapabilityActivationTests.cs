using System;
using FUSE.Loading;
using Xunit;

namespace FUSE.Tests.Loading
{
    public sealed class FuseLegacyCapabilityActivationTests
    {
        [Fact]
        public void BuildRequestedIds_includes_enabled_package_and_dependency_ids()
        {
            var requested = FuseLegacyCapabilityActivation.BuildRequestedIds(new[]
            {
                new FusePackageManifestSnapshot
                {
                    Id = "Consumer.FUSE",
                    RequiredPackageIds = new[] { "Zamu.SomeKindOfMadness.FUSE" },
                },
            });

            Assert.Contains("Consumer", requested);
            Assert.Contains("Zamu.SomeKindOfMadness", requested);
        }

        [Fact]
        public void BuildRequestedIds_excludes_disabled_packages_and_their_dependencies()
        {
            var requested = FuseLegacyCapabilityActivation.BuildRequestedIds(new[]
            {
                new FusePackageManifestSnapshot
                {
                    Id = "Disabled.Consumer",
                    Disabled = true,
                    RequiredPackageIds = new[] { "Zamu.AbsoluteMadness" },
                },
            });

            Assert.Empty(requested);
        }

        [Fact]
        public void BuildRequestedIds_accepts_a_null_snapshot_sequence()
        {
            Assert.Empty(FuseLegacyCapabilityActivation.BuildRequestedIds(null));
        }
    }
}
