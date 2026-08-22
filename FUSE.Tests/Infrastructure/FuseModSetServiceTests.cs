using System;
using System.Linq;
using FUSE.Infrastructure;
using FUSE.Loading;
using Xunit;

namespace FUSE.Tests.Infrastructure
{
    public sealed class FuseModSetServiceTests
    {
        [Fact]
        public void VisibleProfileMods_IncludeProfileDisabledLegacyConvertedPackage()
        {
            var packages = new[]
            {
                new FusePackageManifestSnapshot
                {
                    Id = "Katers.TurntableOfDoom.FUSE",
                    DisplayName = "Turntable of Doom and Despair",
                    Version = "1.0.0",
                    FolderName = "Turntable of Doom and Despair",
                    FolderPath = @"C:\Railroader\Mods\Turntable of Doom and Despair",
                    Disabled = true,
                    DisabledReason = "disabled by active FUSE mod set",
                    IsLegacyConverted = true
                }
            };

            var visible = FuseModSetService.MergeVisibleProfileMods(
                Array.Empty<FuseUmmModInfo>(),
                packages);
            var legacy = Assert.Single(visible);

            Assert.Equal("Katers.TurntableOfDoom.FUSE", legacy.Id);
            Assert.Equal("Turntable of Doom and Despair", legacy.DisplayName);
            Assert.True(legacy.IsFuseDataPackage);
            Assert.True(legacy.IsLegacyConverted);
            Assert.Equal("legacy converted data", legacy.ProfileSource);
        }

        [Fact]
        public void NewProfile_EnablesEveryVisibleLegacyAndUmmPackage()
        {
            var mods = new[]
            {
                new FuseUmmModInfo { Id = "UMM.Plugin", FolderName = "UMM Plugin" },
                new FuseUmmModInfo
                {
                    Id = "Legacy.Track.FUSE",
                    FolderName = "Legacy Track",
                    IsLegacyConverted = true,
                    IsFuseDataPackage = true
                }
            };

            var set = FuseModSetService.CreateSetDefinition("set-test", "Test", mods, "now");

            Assert.Equal(new[] { "Legacy.Track.FUSE", "UMM.Plugin" }, set.EnabledModIds);
            Assert.Equal(new[] { "Legacy Track", "UMM Plugin" }, set.EnabledFolderNames);
        }

        [Fact]
        public void ManifestDisabledPackage_IsNotPresentedAsProfileEnableable()
        {
            var visible = FuseModSetService.MergeVisibleProfileMods(
                Array.Empty<FuseUmmModInfo>(),
                new[]
                {
                    new FusePackageManifestSnapshot
                    {
                        Id = "Author.Disabled.FUSE",
                        FolderName = "Author Disabled",
                        Disabled = true,
                        DisabledReason = "disabled by package manifest"
                    }
                });

            Assert.Empty(visible);
        }

        [Fact]
        public void LegacyPackageToggle_PersistsBothIdAndFolderMembership()
        {
            var set = new FuseModSet { Id = "set-test", Name = "Test" };
            var legacy = new FuseUmmModInfo
            {
                Id = "Legacy.Track.FUSE",
                DisplayName = "Legacy Track",
                FolderName = "Legacy Track"
            };

            Assert.False(FuseModSetService.IsModEnabledInSet(set, legacy));
            Assert.True(FuseModSetService.ToggleModMembership(set, legacy));
            Assert.True(FuseModSetService.IsModEnabledInSet(set, legacy));
            Assert.Contains("Legacy.Track.FUSE", set.EnabledModIds);
            Assert.Contains("Legacy Track", set.EnabledFolderNames);

            Assert.False(FuseModSetService.ToggleModMembership(set, legacy));
            Assert.False(FuseModSetService.IsModEnabledInSet(set, legacy));
            Assert.Empty(set.EnabledModIds);
            Assert.Empty(set.EnabledFolderNames);
        }

        [Fact]
        public void DisabledPackage_IsRejectedByIdAndFolderAdmissionGate()
        {
            var set = new FuseModSet
            {
                EnabledModIds = new[] { "Other.Package" },
                EnabledFolderNames = new[] { "Other Folder" }
            };

            Assert.False(FuseModSetService.IsPackageEnabledInSet(
                set,
                "Legacy.Track.FUSE",
                @"C:\Railroader\Mods\Legacy Track"));

            set.EnabledFolderNames = set.EnabledFolderNames.Concat(new[] { "Legacy Track" }).ToArray();
            Assert.True(FuseModSetService.IsPackageEnabledInSet(
                set,
                "Legacy.Track.FUSE",
                @"C:\Railroader\Mods\Legacy Track"));
        }
    }
}
