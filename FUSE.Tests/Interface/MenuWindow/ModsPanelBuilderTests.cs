using System.IO;
using FUSE.Interface.MenuWindow;
using FUSE.Loading;
using Xunit;

namespace FUSE.Tests.Interface.MenuWindow
{
    public sealed class ModsPanelBuilderTests
    {
        [Fact]
        public void MergeHostedLegacyPackageSnapshots_adds_code_only_hosted_package()
        {
            var folder = Path.Combine("C:\\Railroader\\Mods", "NotEnoughRosters");
            var hosted = new FuseLegacyAssemblyManifest
            {
                Id = "Joo.NotEnoughRosters",
                Name = "NotEnoughRosters",
                Version = "1.1.0",
                FolderPath = folder,
                Assemblies = new[] { "NotEnoughRosters" }
            };

            var result = ModsPanelBuilder.MergeHostedLegacyPackageSnapshots(
                dataPackageSnapshots: null,
                hostedLegacyManifests: new[] { hosted });

            var snapshot = Assert.Single(result);
            Assert.Equal("Joo.NotEnoughRosters", snapshot.Id);
            Assert.Equal("NotEnoughRosters", snapshot.DisplayName);
            Assert.Equal("1.1.0", snapshot.Version);
            Assert.Equal(folder, snapshot.FolderPath);
            Assert.Equal("NotEnoughRosters", snapshot.FolderName);
            Assert.True(snapshot.IsLegacyHosted);
        }

        [Fact]
        public void MergeHostedLegacyPackageSnapshots_preserves_data_snapshot_on_folder_collision()
        {
            var folder = Path.Combine("C:\\Railroader\\Mods", "SignalsEverywhere");
            var dataSnapshot = new FusePackageManifestSnapshot
            {
                Id = "Joo.SignalsEverywhere.FUSE",
                DisplayName = "Signals Everywhere",
                FolderPath = folder + Path.DirectorySeparatorChar,
                IsLegacyConverted = true
            };
            var hosted = new FuseLegacyAssemblyManifest
            {
                Id = "Joo.SignalsEverywhere",
                Name = "SignalsEverywhere",
                FolderPath = folder
            };

            var result = ModsPanelBuilder.MergeHostedLegacyPackageSnapshots(
                new[] { dataSnapshot },
                new[] { hosted });

            Assert.Same(dataSnapshot, Assert.Single(result));
        }

        [Fact]
        public void MergeHostedLegacyPackageSnapshots_deduplicates_hosted_instances_by_folder()
        {
            var folder = Path.Combine("C:\\Railroader\\Mods", "NotEnoughRosters");
            var first = new FuseLegacyAssemblyManifest
            {
                Id = "Joo.NotEnoughRosters",
                Name = "NotEnoughRosters",
                FolderPath = folder
            };
            var second = new FuseLegacyAssemblyManifest
            {
                Id = "Joo.NotEnoughRosters.SecondPlugin",
                Name = "NotEnoughRosters Helper",
                FolderPath = folder + Path.DirectorySeparatorChar
            };

            var result = ModsPanelBuilder.MergeHostedLegacyPackageSnapshots(
                dataPackageSnapshots: null,
                hostedLegacyManifests: new[] { first, second });

            Assert.Equal("Joo.NotEnoughRosters", Assert.Single(result).Id);
        }

        [Fact]
        public void MergeHostedLegacyPackageSnapshots_deduplicates_hosted_instances_by_id_after_folder()
        {
            var hosted = new[]
            {
                new FuseLegacyAssemblyManifest
                {
                    Id = "Joo.NotEnoughRosters",
                    Name = "NotEnoughRosters",
                    FolderPath = Path.Combine("C:\\Railroader\\Mods", "NotEnoughRosters")
                },
                new FuseLegacyAssemblyManifest
                {
                    Id = "joo.notenoughrosters",
                    Name = "Duplicate NotEnoughRosters",
                    FolderPath = Path.Combine("D:\\Railroader\\Mods", "NotEnoughRosters")
                }
            };

            var result = ModsPanelBuilder.MergeHostedLegacyPackageSnapshots(
                dataPackageSnapshots: null,
                hostedLegacyManifests: hosted);

            Assert.Equal("NotEnoughRosters", Assert.Single(result).DisplayName);
        }
    }
}
