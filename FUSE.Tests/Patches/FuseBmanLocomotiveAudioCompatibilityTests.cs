using FUSE.Patches;
using Xunit;

namespace FUSE.Tests.Patches
{
    public sealed class FuseBmanLocomotiveAudioCompatibilityTests
    {
        [Theory]
        [InlineData(null, false)]
        [InlineData("", false)]
        [InlineData("   ", false)]
        [InlineData("EMD 16-567C Audio\\Audio", true)]
        [InlineData("Alco 12-244A Audio\\Audio", true)]
        public void CustomSelection_RequiresNonBlankAudioPackPath(string selection, bool expected)
        {
            Assert.Equal(
                expected,
                FusePrimeMoverAudioSelectionCompatibilityPatch.HasCustomSelection(selection));
        }
    }
}
