using FUSE.Runtime.API;
using Xunit;

namespace FUSE.Tests.API
{
    /// <summary>
    /// Pure-logic coverage for the decoupled-mask NAME format in <see cref="MapAPI"/>.
    /// <c>DecoupleAttachedMapMasks</c> names each standalone clone via
    /// <c>BuildDecoupledMaskId</c> for readability only; ownership (reuse on reload, cleanup
    /// on removal/update) is tracked by the <c>FuseDecoupledMaskMarker</c> component, which
    /// needs Unity GameObjects and is exercised in-game rather than here.
    /// </summary>
    public class MapApiDecoupledMaskTests
    {
        [Theory]
        [InlineData("BryShop4", 0, "BryShop4__mask00")]
        [InlineData("BryShop4", 7, "BryShop4__mask07")]
        [InlineData("BryShop4", 12, "BryShop4__mask12")]
        public void BuildDecoupledMaskId_FormatsIndexTwoDigits(string sceneryId, int index, string expected)
        {
            Assert.Equal(expected, MapAPI.BuildDecoupledMaskId(sceneryId, index));
        }
    }
}
