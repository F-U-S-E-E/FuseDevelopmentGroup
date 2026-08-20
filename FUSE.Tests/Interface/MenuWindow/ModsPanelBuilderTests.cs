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

        [Theory]
        [InlineData("Package 'Example' requires 'Missing.Mod', but no matching package was discovered.", "Missing dependency")]
        [InlineData("Package 'Example' conflictsWith 'Other.Mod'; matching package is enabled.", "Incompatible mod installed")]
        [InlineData("manifest JSON: Unexpected character at line 12.", "Invalid package data")]
        public void ClassifyPackageStatus_distinguishes_actionable_failure_types(string fault, string expected)
        {
            var snapshot = new FusePackageManifestSnapshot
            {
                Id = "Example",
                Faults = new[] { fault }
            };

            var status = ModsPanelBuilder.ClassifyPackageStatus(snapshot);

            Assert.Equal(expected, status.Label);
            Assert.Equal("Mods Needing Attention", status.ListGroup);
        }

        [Fact]
        public void ClassifyPackageStatus_reports_successfully_applied_package()
        {
            var status = ModsPanelBuilder.ClassifyPackageStatus(new FusePackageManifestSnapshot
            {
                Id = "Example",
                AppliedToRuntime = true
            });

            Assert.Equal("Applied", status.Label);
            Assert.Equal("Applied Mods", status.ListGroup);
        }

        [Fact]
        public void ClassifyPackageStatus_keeps_optional_mixinto_skip_non_actionable()
        {
            var status = ModsPanelBuilder.ClassifyPackageStatus(new FusePackageManifestSnapshot
            {
                Id = "Example",
                AppliedToRuntime = true,
                SkipReason = "mixinto dependency missing 'Optional.Mod'"
            });

            Assert.Equal("Applied", status.Label);
            Assert.Contains("Optional content is inactive", status.Detail);
            Assert.Equal("Applied Mods", status.ListGroup);
        }

        [Fact]
        public void ClassifyPackageStatus_does_not_hide_actionable_skip_behind_optional_fragment()
        {
            var status = ModsPanelBuilder.ClassifyPackageStatus(new FusePackageManifestSnapshot
            {
                Id = "Example",
                AppliedToRuntime = true,
                SkipReasons = new[]
                {
                    "mixinto dependency missing id='Optional.Mod'",
                    "runtime apply exception"
                }
            });

            Assert.Equal("Partially applied", status.Label);
            Assert.Contains("runtime apply exception", status.Detail);
            Assert.Equal("Mods Needing Attention", status.ListGroup);
        }

        [Fact]
        public void ClassifyPackageStatus_reports_partial_apply_without_calling_whole_package_failed()
        {
            var status = ModsPanelBuilder.ClassifyPackageStatus(new FusePackageManifestSnapshot
            {
                Id = "Example",
                AppliedToRuntime = true,
                RuntimeFaults = new[] { "runtime apply: one definition failed" }
            });

            Assert.Equal("Partially applied", status.Label);
            Assert.Equal("Mods Needing Attention", status.ListGroup);
        }

        [Fact]
        public void ClassifyPackageStatus_reports_disabled_package_separately()
        {
            var status = ModsPanelBuilder.ClassifyPackageStatus(new FusePackageManifestSnapshot
            {
                Id = "Example",
                Disabled = true,
                DisabledReason = "disabled in the active profile"
            });

            Assert.Equal("Disabled", status.Label);
            Assert.Equal("disabled in the active profile", status.Detail);
            Assert.Equal("Disabled Mods", status.ListGroup);
        }
    }
}
