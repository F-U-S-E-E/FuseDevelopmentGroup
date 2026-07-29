using FUSE.Runtime.Lifecycle;
using Xunit;

namespace FUSE.Tests.Runtime.Lifecycle
{
    public sealed class FuseUnusedAssetReclaimerTests
    {
        [Theory]
        [InlineData(0, 8, false)]
        [InlineData(127, 6, false)]
        [InlineData(128, 6, true)]
        [InlineData(1, 7, true)]
        [InlineData(0, 7, false)]
        public void HasEnoughPressureToSweep_BatchesRoutineCleanupAndHonorsEmergencyPressure(
            int pendingEvictions,
            long textureMemoryGiB,
            bool expected)
        {
            var textureBytes = textureMemoryGiB * 1024L * 1024L * 1024L;

            Assert.Equal(
                expected,
                FuseUnusedAssetReclaimer.HasEnoughPressureToSweep(
                    pendingEvictions,
                    textureBytes));
        }
    }
}
