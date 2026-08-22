using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FUSE.Interface.MenuWindow;
using FUSE.Loading;
using Xunit;

namespace FUSE.Tests.Loading
{
    public sealed class FuseInstalledDependencyCatalogTests
    {
        [Fact]
        public void UmmEquipmentRequirement_ParsesPackageIdAndMinimumVersion()
        {
            using (var fixture = new CatalogFixture())
            {
                fixture.Write(
                    "GP38ATSF/Info.json",
                    "{\"Id\":\"GP38ATSF\",\"DisplayName\":\"ATSF GP38\",\"Version\":\"4.4.5\",\"Requirements\":[\"GP38SoundMod-4.4.1\"]}");
                fixture.Write(
                    "GP38ATSF/GP38/Definitions.json",
                    "{\"objects\":[{\"definition\":{\"kind\":\"DieselLocomotive\"}}]}");
                fixture.Write(
                    "GP38SoundMod/Info.json",
                    "{\"Id\":\"GP38SoundMod\",\"Version\":\"4.4.7\",\"EntryMethod\":\"Scripts.Load\"}");

                var packages = FuseInstalledDependencyCatalog.DiscoverInstalledPackages(
                    fixture.Root,
                    Array.Empty<FusePackageManifestSnapshot>());
                var equipment = Assert.Single(packages, package => package.Id == "GP38ATSF");
                var requirement = Assert.Single(equipment.Requirements);

                Assert.Equal("Equipment", equipment.Category);
                Assert.True(equipment.HasAssetLoaderMetadata);
                Assert.Equal("GP38SoundMod", requirement.Id);
                Assert.Equal("4.4.1", requirement.NotBefore);
                Assert.Equal("UMM Info.json", requirement.Source);
                Assert.Contains(
                    "READY 4.4.7",
                    DependencyGraphPage.FormatDependencyEdge(requirement, packages, advisory: false));
            }
        }

        [Fact]
        public void LegacyObjectRequirement_PreservesVersionBounds()
        {
            using (var fixture = new CatalogFixture())
            {
                fixture.Write(
                    "LegacyCar/Definition.json",
                    "{\"manifestVersion\":7,\"id\":\"Legacy.Car\",\"requires\":[{\"id\":\"Truck.Library\",\"notBefore\":\"2.1.0\",\"notAfter\":\"3.0.0\"}]}");
                fixture.Write("TruckLibrary/Info.json", "{\"Id\":\"Truck.Library\",\"Version\":\"2.5.0\"}");

                var packages = FuseInstalledDependencyCatalog.DiscoverInstalledPackages(
                    fixture.Root,
                    Array.Empty<FusePackageManifestSnapshot>());
                var package = Assert.Single(packages, candidate => candidate.Id == "Legacy.Car");
                var requirement = Assert.Single(package.Requirements);

                Assert.Equal("2.1.0", requirement.NotBefore);
                Assert.Equal("3.0.0", requirement.NotAfter);
                Assert.Contains(
                    "(2.1.0 to 3.0.0) | <color=\"green\">READY 2.5.0",
                    DependencyGraphPage.FormatDependencyEdge(requirement, packages, advisory: false));
            }
        }

        [Fact]
        public void NexusCache_FillsManifestGap_WithoutInventingInstalledProvider()
        {
            using (var fixture = new CatalogFixture())
            {
                fixture.Write("FreightCar/Info.json", "{\"Id\":\"Freight.Car\",\"Version\":\"1.0.0\"}");
                fixture.Write(
                    ".fuse-metadata/dependencies.json",
                    "{\"schemaVersion\":1,\"packages\":[" +
                    "{\"folder\":\"FreightCar\",\"id\":\"Freight.Car\",\"requirements\":[{\"id\":\"Shared.Script\",\"minimumVersion\":\"2.0.0\",\"source\":\"nexus\"}]}," +
                    "{\"folder\":\"RemovedCar\",\"id\":\"Removed.Car\",\"requirements\":[{\"id\":\"Ghost.Dependency\"}]}]}");

                var packages = FuseInstalledDependencyCatalog.DiscoverInstalledPackages(
                    fixture.Root,
                    Array.Empty<FusePackageManifestSnapshot>());
                var package = Assert.Single(packages);
                var requirement = Assert.Single(package.Requirements);

                Assert.Equal("Shared.Script", requirement.Id);
                Assert.Equal("2.0.0", requirement.NotBefore);
                Assert.Equal("Nexus API cache", requirement.Source);
                Assert.Contains(
                    "MISSING",
                    DependencyGraphPage.FormatDependencyEdge(requirement, packages, advisory: false));
                Assert.DoesNotContain(packages, candidate => candidate.Id == "Removed.Car");
            }
        }

        [Fact]
        public void ExplicitLocalRequirement_WinsOverCachedNexusDuplicate()
        {
            using (var fixture = new CatalogFixture())
            {
                fixture.Write(
                    "Car/Info.json",
                    "{\"Id\":\"Car\",\"Requirements\":[\"Local.Library-3.0.0\"]}");
                fixture.Write(
                    ".fuse-metadata/dependencies.json",
                    "{\"schemaVersion\":1,\"packages\":[{\"folder\":\"Car\",\"id\":\"Car\",\"requirements\":[{\"id\":\"Local.Library\",\"minimumVersion\":\"1.0.0\",\"source\":\"nexus\"}]}]}");

                var packages = FuseInstalledDependencyCatalog.DiscoverInstalledPackages(
                    fixture.Root,
                    Array.Empty<FusePackageManifestSnapshot>());
                var requirement = Assert.Single(Assert.Single(packages).Requirements);

                Assert.Equal("3.0.0", requirement.NotBefore);
                Assert.Equal("UMM Info.json", requirement.Source);
            }
        }

        [Fact]
        public void ExplicitLocalRequirements_IgnoreStaleCachedNexusEdges()
        {
            using (var fixture = new CatalogFixture())
            {
                fixture.Write(
                    "Car/Info.json",
                    "{\"Id\":\"Car\",\"Requirements\":[\"Current.Library-3.0.0\"]}");
                fixture.Write(
                    ".fuse-metadata/dependencies.json",
                    "{\"schemaVersion\":1,\"packages\":[{\"folder\":\"Car\",\"id\":\"Car\",\"source\":{\"kind\":\"nexus\"},\"requirements\":[{\"id\":\"Retired.Library\",\"minimumVersion\":\"1.0.0\",\"source\":\"nexus\"}]}]}");

                var packages = FuseInstalledDependencyCatalog.DiscoverInstalledPackages(
                    fixture.Root,
                    Array.Empty<FusePackageManifestSnapshot>());
                var requirement = Assert.Single(Assert.Single(packages).Requirements);

                Assert.Equal("Current.Library", requirement.Id);
                Assert.Equal("UMM Info.json", requirement.Source);
            }
        }

        [Fact]
        public void NexusIdentity_MatchesInstalledProviderByHomepageInsteadOfDisplayName()
        {
            using (var fixture = new CatalogFixture())
            {
                fixture.Write("Car/Info.json", "{\"Id\":\"Car\"}");
                fixture.Write(
                    "Scripts/Info.json",
                    "{\"Id\":\"Different.Local.Id\",\"Version\":\"2.3.0\",\"Homepage\":\"https://www.nexusmods.com/railroader/mods/712\"}");
                fixture.Write(
                    ".fuse-metadata/dependencies.json",
                    "{\"schemaVersion\":1,\"packages\":[{\"folder\":\"Car\",\"id\":\"Car\",\"requirements\":[" +
                    "{\"id\":\"nexus:railroader:712\",\"displayName\":\"Shared Scripts\",\"minimumVersion\":\"2.2.0\",\"nexusModId\":\"712\",\"source\":\"nexus\"}]}]}");

                var packages = FuseInstalledDependencyCatalog.DiscoverInstalledPackages(
                    fixture.Root,
                    Array.Empty<FusePackageManifestSnapshot>());
                var requirement = Assert.Single(Assert.Single(packages, item => item.Id == "Car").Requirements);
                var formatted = DependencyGraphPage.FormatDependencyEdge(requirement, packages, advisory: false);

                Assert.Contains("Shared Scripts", formatted);
                Assert.Contains("READY 2.3.0", formatted);
            }
        }

        private sealed class CatalogFixture : IDisposable
        {
            public CatalogFixture()
            {
                Root = Path.Combine(Path.GetTempPath(), "fuse-dependency-catalog-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Root);
            }

            public string Root { get; }

            public void Write(string relativePath, string contents)
            {
                var path = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, contents);
            }

            public void Dispose()
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, recursive: true);
                }
            }
        }
    }
}
