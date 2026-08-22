using FUSE.Compatibility;
using Xunit;

namespace FUSE.Tests.Compatibility
{
    public sealed class FuseLegacyInstallDetectorTests
    {
        [Theory]
        [InlineData("Railloader")]
        [InlineData("Railloader.Injector")]
        [InlineData("Railloader.Interchange")]
        [InlineData("StrangeCustoms")]
        public void IsLegacyLoaderAssemblyName_RecognizesEveryReplacedAssembly(string assemblyName)
        {
            Assert.True(FuseLegacyInstallDetector.IsLegacyLoaderAssemblyName(assemblyName));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("FUSE")]
        [InlineData("UnityModManager")]
        public void IsLegacyLoaderAssemblyName_DoesNotFlagCurrentRuntimeAssemblies(string assemblyName)
        {
            Assert.False(FuseLegacyInstallDetector.IsLegacyLoaderAssemblyName(assemblyName));
        }
    }
}
