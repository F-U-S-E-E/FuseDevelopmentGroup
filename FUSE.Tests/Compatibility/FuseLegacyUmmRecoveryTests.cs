using System;
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
    }
}
