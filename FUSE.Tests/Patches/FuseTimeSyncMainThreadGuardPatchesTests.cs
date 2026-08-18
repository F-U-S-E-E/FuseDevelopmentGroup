using FUSE.Patches;
using Xunit;

namespace FUSE.Tests.Patches
{
    public sealed class FuseTimeSyncMainThreadGuardPatchesTests
    {
        [Theory]
        [InlineData(12, 0, true)]
        [InlineData(12, 7, true)]
        [InlineData(7, 7, false)]
        public void SyncTimes_DefersUntilTheCallbackIsOnTheRecordedMainThread(
            int currentThreadId,
            int mainThreadId,
            bool expected)
        {
            Assert.Equal(
                expected,
                FuseTimeSyncMainThreadGuardPatches.ShouldDefer(
                    currentThreadId,
                    mainThreadId));
        }
    }
}
