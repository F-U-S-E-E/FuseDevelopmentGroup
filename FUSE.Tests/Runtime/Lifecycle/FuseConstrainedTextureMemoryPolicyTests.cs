using FUSE.Runtime.Lifecycle;
using Xunit;

namespace FUSE.Tests.Runtime.Lifecycle
{
    public sealed class FuseConstrainedTextureMemoryPolicyTests
    {
        [Theory]
        [InlineData(0, 1)]
        [InlineData(1, 1)]
        [InlineData(2, 2)]
        public void EffectiveMipmapLimit_NeverRaisesTextureResolution(
            int currentLimit,
            int expected)
        {
            Assert.Equal(
                expected,
                FuseConstrainedTextureMemoryPolicy.EffectiveMipmapLimit(currentLimit));
        }

        [Theory]
        [InlineData(8192, false, true)]
        [InlineData(16384, false, false)]
        [InlineData(16384, true, true)]
        [InlineData(0, false, false)]
        public void ShouldConstrainTextures_MatchesVramPolicy(
            int graphicsMemoryMb,
            bool forceConstrained,
            bool expected)
        {
            Assert.Equal(
                expected,
                FuseConstrainedTextureMemoryPolicy.ShouldConstrainTextures(
                    graphicsMemoryMb,
                    forceConstrained));
        }
    }
}
