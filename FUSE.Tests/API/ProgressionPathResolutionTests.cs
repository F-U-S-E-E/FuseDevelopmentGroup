using FUSE.Runtime.API;
using Xunit;

namespace FUSE.Tests.API
{
    public sealed class ProgressionPathResolutionTests
    {
        [Theory]
        [InlineData("Zacm_qalf", true)]
        [InlineData("World/Large Scenery/Bryson", false)]
        [InlineData(@"World\Large Scenery\Bryson", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsLeafOnlyPath_DistinguishesAuthoredIdsFromScenePaths(
            string value,
            bool expected)
        {
            Assert.Equal(expected, ProgressionAPI.IsLeafOnlyPath(value));
        }
    }
}
