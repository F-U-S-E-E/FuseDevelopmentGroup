using FUSE.Patches;
using Xunit;

namespace FUSE.Tests.Patches
{
    public class FuseAssetLoaderReplacementCompatibilityTests
    {
        [Theory]
        [InlineData("AssetLoader", true)]
        [InlineData("assetloader", true)]
        [InlineData("FUSE", false)]
        [InlineData("SomeAssetPack", false)]
        [InlineData(null, false)]
        public void AssemblyMatch_IsExactAndCaseInsensitive(string assemblyName, bool expected)
        {
            Assert.Equal(expected,
                FuseAssetLoaderReplacementCompatibility.IsTargetAssemblyName(assemblyName));
        }
    }
}
