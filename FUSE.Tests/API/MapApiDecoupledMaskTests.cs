using FUSE.Runtime.API;
using Xunit;

namespace FUSE.Tests.API
{
    /// <summary>
    /// Pure-logic coverage for the decoupled-mask naming/ownership helpers in
    /// <see cref="MapAPI"/>. These are the contract that ties a scenery to the standalone
    /// terrain masks <c>DecoupleAttachedMapMasks</c> creates for it: the cleanup path
    /// (<c>RemoveDecoupledMasksFor</c> via <c>SceneryAPI.TryRemoveScenery</c>/<c>UpdateScenery</c>)
    /// must find exactly that scenery's masks and nothing else. The clone itself uses Unity
    /// mask components and is exercised in-game, not here.
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

        [Fact]
        public void IsDecoupledMaskOf_MatchesIdsItBuilt()
        {
            for (var index = 0; index < 15; index++)
            {
                var built = MapAPI.BuildDecoupledMaskId("BryShop4", index);
                Assert.True(MapAPI.IsDecoupledMaskOf(built, "BryShop4"));
            }
        }

        [Theory]
        // A scenery whose id is a PREFIX of the mask owner must NOT match — the "__mask"
        // separator guarantees it (Shop4 vs Shop4X, Shop4 vs Shop40).
        [InlineData("BryShop4X__mask00", "BryShop4")]
        [InlineData("BryShop40__mask00", "BryShop4")]
        [InlineData("OtherShop__mask00", "BryShop4")]
        // The plain scenery id (no decoupled-mask infix) and unrelated names never match.
        [InlineData("BryShop4", "BryShop4")]
        [InlineData("some-user-authored-mask", "BryShop4")]
        [InlineData("", "BryShop4")]
        [InlineData(null, "BryShop4")]
        public void IsDecoupledMaskOf_RejectsNonOwnedNames(string maskName, string sceneryId)
        {
            Assert.False(MapAPI.IsDecoupledMaskOf(maskName, sceneryId));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void IsDecoupledMaskOf_NullOrEmptySceneryId_IsFalse(string sceneryId)
        {
            Assert.False(MapAPI.IsDecoupledMaskOf("anything__mask00", sceneryId));
        }
    }
}
