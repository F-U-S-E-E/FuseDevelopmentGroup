using System;
using System.Linq;
using FUSE.Loading;
using Railloader;
using Xunit;

namespace FUSE.Tests.Loading
{
    public sealed class FuseLegacyAssemblyOrderingTests
    {
        [Fact]
        public void LoadAfter_orders_hosted_plugin_after_referenced_package()
        {
            var extension = Manifest("Extension.Mod", "A-Extension");
            extension.LoadAfter = new[] { new ModReference { Id = "Base.Mod" } };
            var @base = Manifest("Base.Mod", "Z-Base");

            var ordered = FuseLegacyAssemblyHost.OrderLegacyManifestsForHosting(new[] { extension, @base });

            Assert.Equal(new[] { "Base.Mod", "Extension.Mod" }, ordered.Select(item => item.Id));
        }

        [Fact]
        public void LoadBefore_orders_hosted_plugin_before_referenced_package_alias()
        {
            var extension = Manifest("Extension.Mod.FUSE", "A-Extension");
            var @base = Manifest("Base.Mod", "Z-Base");
            @base.LoadBefore = new[] { "Extension.Mod" };

            var ordered = FuseLegacyAssemblyHost.OrderLegacyManifestsForHosting(new[] { extension, @base });

            Assert.Equal(new[] { "Base.Mod", "Extension.Mod.FUSE" }, ordered.Select(item => item.Id));
        }

        [Fact]
        public void Required_reference_also_orders_dependency_before_hosted_plugin()
        {
            var extension = Manifest("Extension.Mod", "A-Extension");
            extension.RequiredReferences = new[] { new ModReference { Id = "Base.Mod" } };
            var @base = Manifest("Base.Mod", "Z-Base");

            var ordered = FuseLegacyAssemblyHost.OrderLegacyManifestsForHosting(new[] { extension, @base });

            Assert.Equal(new[] { "Base.Mod", "Extension.Mod" }, ordered.Select(item => item.Id));
        }

        [Fact]
        public void Missing_advisory_reference_does_not_drop_package()
        {
            var package = Manifest("Only.Mod", "Only");
            package.LoadAfter = new[] { new ModReference { Id = "Not.Installed" } };

            Assert.Same(package, Assert.Single(FuseLegacyAssemblyHost.OrderLegacyManifestsForHosting(new[] { package })));
        }

        [Fact]
        public void Load_order_cycle_keeps_every_package_in_deterministic_folder_order()
        {
            var first = Manifest("First.Mod", "A-First");
            var second = Manifest("Second.Mod", "B-Second");
            first.LoadAfter = new[] { new ModReference { Id = second.Id } };
            second.LoadAfter = new[] { new ModReference { Id = first.Id } };

            var ordered = FuseLegacyAssemblyHost.OrderLegacyManifestsForHosting(new[] { second, first });

            Assert.Equal(new[] { "First.Mod", "Second.Mod" }, ordered.Select(item => item.Id));
        }

        private static FuseLegacyAssemblyManifest Manifest(string id, string folder)
        {
            return new FuseLegacyAssemblyManifest
            {
                Id = id,
                Name = id,
                Version = "1.0",
                FolderPath = "C:\\Railroader\\Mods\\" + folder,
                Assemblies = Array.Empty<string>()
            };
        }
    }
}
