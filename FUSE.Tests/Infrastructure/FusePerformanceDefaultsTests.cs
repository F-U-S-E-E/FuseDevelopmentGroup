using FUSE.Infrastructure;
using Xunit;

namespace FUSE.Tests.Infrastructure
{
    public class FusePerformanceDefaultsTests
    {
        [Fact]
        public void SceneryCullingDiagnostics_AreSessionOnly()
        {
            Assert.True(FuseSettings.IsSessionOnlyUserSetting(
                nameof(FuseSettings.EnableSceneryCullingDiagnostics)));
        }

        [Fact]
        public void LegacyUmmRows_AreOptInByDefault()
        {
            Assert.False(FuseSettings.DefaultShowLegacyModsInUmm);
        }

        [Fact]
        public void SyntheticUmmEntries_AreMetadataOnlyAndInactive()
        {
            Assert.False(FuseUmmInjector.SyntheticEntriesAreActive);
            Assert.True(FuseUmmInjector.SyntheticLegacyPackagesAreEnabled);
        }
    }
}
