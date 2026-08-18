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
            Assert.True(FuseLegacyLoaderReferenceScanner.ReferencesLegacyLoader(Encoding.ASCII.GetBytes(content)));
        }

        [Fact]
        public void ReferencesLegacyLoader_IgnoresUnrelatedAssemblies()
        {
            Assert.False(FuseLegacyLoaderReferenceScanner.ReferencesLegacyLoader(
                Encoding.ASCII.GetBytes("Assembly-CSharp 0Harmony UnityModManager")));
        }

        [Fact]
        public void ReferencesLegacyLoader_HandlesEmptyInput()
        {
            Assert.False(FuseLegacyLoaderReferenceScanner.ReferencesLegacyLoader(null));
            Assert.False(FuseLegacyLoaderReferenceScanner.ReferencesLegacyLoader(Array.Empty<byte>()));
        }

        [Fact]
        public void RecoveredPackageKey_NormalizesSeparatorsAndCasing()
        {
            FuseRecoveredPackageRegistry.Reset();
            var unique = Guid.NewGuid().ToString("N");
            var folder = Path.Combine(Path.GetTempPath(), "FuseRecoveryKeyTests", unique);
            var packageId = "AlinaNova21.AlinasUtils." + unique;

            Assert.True(FuseRecoveredPackageRegistry.TryRecord(
                folder + Path.DirectorySeparatorChar,
                packageId.ToUpperInvariant()));
            Assert.True(FuseRecoveredPackageRegistry.WasRecovered(
                folder.ToUpperInvariant(),
                packageId.ToLowerInvariant()));
            Assert.True(FuseRecoveredPackageRegistry.WasRecovered(
                folder + Path.AltDirectorySeparatorChar,
                packageId));
            Assert.True(FuseRecoveredPackageRegistry.WasRecovered(
                folder,
                "different-legacy-definition-id"));
            Assert.True(FuseRecoveredPackageRegistry.WasRecovered(
                Path.Combine(Path.GetTempPath(), "relocated", unique),
                packageId.ToLowerInvariant()));
        }

        [Fact]
        public void RecoveredPackageKey_NormalizesRelativePathsAndResets()
        {
            FuseRecoveredPackageRegistry.Reset();
            var unique = "FuseRecoveryKeyTests-" + Guid.NewGuid().ToString("N");
            var relative = Path.Combine(".", unique);
            var absolute = Path.Combine(Directory.GetCurrentDirectory(), unique);

            Assert.True(FuseRecoveredPackageRegistry.TryRecord(relative, "Package.Id"));
            Assert.True(FuseRecoveredPackageRegistry.WasRecovered(
                absolute + Path.DirectorySeparatorChar,
                "other-id"));

            FuseRecoveredPackageRegistry.Reset();
            Assert.False(FuseRecoveredPackageRegistry.WasRecovered(absolute, "Package.Id"));
        }

        [Fact]
        public void RecoveredPackageKey_UsesIdAliasWhenFolderIsUnavailable()
        {
            FuseRecoveredPackageRegistry.Reset();

            Assert.True(FuseRecoveredPackageRegistry.TryRecord(null, "AlinaNova21.AlinasUtils"));
            Assert.True(FuseRecoveredPackageRegistry.WasRecovered(
                null,
                "alinanova21.alinasutils"));
        }
    }
}
