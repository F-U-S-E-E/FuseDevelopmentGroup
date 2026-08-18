using FUSE.Patches;
using Xunit;

namespace FUSE.Tests.Patches
{
    public sealed class FuseBrssModMenuGuardPatchesTests
    {
        [Theory]
        [InlineData(false, false, false)]
        [InlineData(false, true, true)]
        [InlineData(true, false, true)]
        [InlineData(true, true, true)]
        public void CreateWindow_RunsUnlessBothWindowAndSelectionAreMissing(
            bool hasWindow,
            bool hasSelectedCar,
            bool expected)
        {
            Assert.Equal(
                expected,
                FuseBrssModMenuGuardPatches.ShouldRunOriginal(hasWindow, hasSelectedCar));
        }
    }
}
