using System;
using System.IO;
using System.Text;
using FUSE.Compatibility;
using Xunit;

namespace FUSE.Tests.Compatibility
{
    public class FuseLegacyUmmRecoveryTests
    {
        [Theory]
        [InlineData("prefix Railloader.Interchange suffix")]
        [InlineData("prefix StrangeCustoms suffix")]
        public void ReferencesLegacyLoader_DetectsSupportedLegacyAssemblyNames(string content)
        {
            Assert.True(FuseLegacyUmmRecovery.ReferencesLegacyLoader(Encoding.ASCII.GetBytes(content)));
        }

        [Fact]
        public void ReferencesLegacyLoader_IgnoresUnrelatedAssemblies()
        {
            Assert.False(FuseLegacyUmmRecovery.ReferencesLegacyLoader(
                Encoding.ASCII.GetBytes("Assembly-CSharp 0Harmony UnityModManager")));
        }

        [Fact]
        public void ReferencesLegacyLoader_HandlesEmptyInput()
        {
            Assert.False(FuseLegacyUmmRecovery.ReferencesLegacyLoader(null));
            Assert.False(FuseLegacyUmmRecovery.ReferencesLegacyLoader(Array.Empty<byte>()));
        }

        [Fact]
        public void RecoveredPackageKey_NormalizesSeparatorsAndCasing()
        {
            var unique = Guid.NewGuid().ToString("N");
            var folder = Path.Combine(Path.GetTempPath(), "FuseRecoveryKeyTests", unique);
            var packageId = "AlinaNova21.AlinasUtils." + unique;

            Assert.True(FuseLegacyUmmRecovery.TryRecordRecovered(
                folder + Path.DirectorySeparatorChar,
                packageId.ToUpperInvariant()));
            Assert.True(FuseLegacyUmmRecovery.WasRecovered(
                folder,
                packageId.ToLowerInvariant()));
            Assert.True(FuseLegacyUmmRecovery.WasRecovered(
                folder + Path.AltDirectorySeparatorChar,
                packageId));
        }
    }
}
