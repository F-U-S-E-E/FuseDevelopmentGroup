using System;
using System.Collections.Generic;
using System.IO;
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

        [Fact]
        public void ReferenceVersionRange_AcceptsALaterCompatibleCandidate()
        {
            var reference = new ModReference
            {
                Id = "Shared.Mod",
                NotBefore = new Version(1, 0),
                NotAfter = new Version(2, 0)
            };

            Assert.True(FuseLegacyAssemblyHost.AnyLegacyVersionMatchesReference(
                new[] { "0.9", "1.5" },
                reference));
        }

        [Fact]
        public void ReferenceVersionRange_RejectsWhenEveryCandidateIsOutsideTheRange()
        {
            var reference = new ModReference
            {
                Id = "Shared.Mod",
                NotBefore = new Version(1, 0),
                NotAfter = new Version(2, 0)
            };

            Assert.False(FuseLegacyAssemblyHost.AnyLegacyVersionMatchesReference(
                new[] { "0.9", "2.1" },
                reference));
        }

        [Fact]
        public void ReplacementCapability_DoesNotCountAsAnInstalledConflict()
        {
            var modsRoot = Path.Combine(
                Path.GetTempPath(),
                nameof(FuseLegacyAssemblyOrderingTests),
                Guid.NewGuid().ToString("N"));
            var hostFolder = Path.Combine(modsRoot, "Host.Mod");
            var manifest = Manifest("Host.Mod", "Host.Mod");
            manifest.FolderPath = hostFolder;
            var replacementReference = new ModReference { Id = "Railloader" };

            try
            {
                Directory.CreateDirectory(hostFolder);
                File.WriteAllText(
                    Path.Combine(hostFolder, "Definition.json"),
                    "{ \"id\": \"Host.Mod\", \"version\": \"1.0\" }");

                Assert.True(FuseLegacyAssemblyHost.IsLegacyReferencePresent(
                    manifest,
                    replacementReference));
                Assert.False(FuseLegacyAssemblyHost.IsLegacyReferencePresent(
                    manifest,
                    replacementReference,
                    includeReplacementCapabilities: false));
            }
            finally
            {
                if (Directory.Exists(modsRoot))
                {
                    Directory.Delete(modsRoot, recursive: true);
                }
            }
        }

        [Fact]
        public void ModReferences_ConvertToGenericReadOnlyListContract()
        {
            var converted = FuseLegacyAssemblyHost.ConvertModReferences(
                new[]
                {
                    new ModReference
                    {
                        Id = "Shared.Mod",
                        NotBefore = new Version(1, 2),
                        NotAfter = new Version(3, 4)
                    }
                },
                typeof(IReadOnlyList<ForeignModReference>));

            var references = Assert.IsAssignableFrom<IReadOnlyList<ForeignModReference>>(converted);
            var reference = Assert.Single(references);
            Assert.Equal("Shared.Mod", reference.Id);
            Assert.Equal(new Version(1, 2), reference.NotBefore);
            Assert.Equal(new Version(3, 4), reference.NotAfter);
        }

        [Fact]
        public void MixintoPath_RejectsAbsoluteAndEscapingReferences()
        {
            var modsRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "Mods");
            var packageRoot = Path.Combine(modsRoot, "Package");
            var siblingRoot = Path.Combine(modsRoot, "SharedContent");
            var outsideRoot = Path.Combine(Path.GetDirectoryName(modsRoot), "Outside");

            Assert.Equal(
                Path.GetFullPath(Path.Combine(packageRoot, "Liveries", "Blue")),
                FuseLegacyAssemblyHost.ResolvePackageFile(modsRoot, packageRoot, "Liveries/Blue"));
            Assert.Equal(
                Path.GetFullPath(Path.Combine(siblingRoot, "Blue")),
                FuseLegacyAssemblyHost.ResolvePackageFile(modsRoot, packageRoot, "../SharedContent/Blue"));
            Assert.Equal(
                string.Empty,
                FuseLegacyAssemblyHost.ResolvePackageFile(modsRoot, packageRoot, outsideRoot));
            Assert.Equal(
                string.Empty,
                FuseLegacyAssemblyHost.ResolvePackageFile(modsRoot, packageRoot, "../../Outside"));
        }

        public sealed class ForeignModReference
        {
            public string Id { get; set; }
            public Version NotBefore { get; set; }
            public Version NotAfter { get; set; }
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
